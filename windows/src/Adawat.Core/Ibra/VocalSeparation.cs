using System.Numerics;
using Adawat.Core.Media;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Adawat.Core.Ibra;

/// <summary>
/// Splits a song into the singer's voice and the music with MDX-Net ("UVR-MDX-NET-Voc_FT" from Ultimate Vocal Remover, MIT),
/// run with ONNX Runtime: on the graphics card through DirectML on Windows when it can, otherwise on the CPU.
/// The model predicts the voice; the music is the original minus the voice. The 66 MB model downloads on first use.
/// </summary>
public static class VocalSeparation
{
    private const string ModelUrl = "https://github.com/TRvlvr/model_repo/releases/download/all_public_uvr_models/UVR-MDX-NET-Voc_FT.onnx";
    private const string ModelName = "UVR-MDX-NET-Voc_FT.onnx";

    // The model's own settings (from UVR's model data for Voc_FT).
    private const int Rate = 44100, NFft = 7680, Hop = 1024, DimF = 3072, DimT = 256;
    private const float Compensate = 1.021f;
    private const int Chunk = Hop * (DimT - 1), Trim = NFft / 2, Gen = Chunk - 2 * Trim;

    private static readonly SemaphoreSlim Gate = new(1);
    private static InferenceSession? _session;
    private static bool _gpu;

    private static string ModelPath => Path.Combine(IbraTools.Models, ModelName);

    public enum Part { Vocals, Music }

    public static Task<IReadOnlyList<string>> Vocals(IReadOnlyList<string> files, OperationContext context) => Run(files, Part.Vocals, context);

    public static Task<IReadOnlyList<string>> Music(IReadOnlyList<string> files, OperationContext context) => Run(files, Part.Music, context);

    public static async Task<IReadOnlyList<string>> Run(IReadOnlyList<string> files, Part part, OperationContext context)
    {
        await Gate.WaitAsync(context.Cancellation);
        try
        {
            if (!File.Exists(ModelPath))
            {
                context.Report("Downloading the voice separation model (one time only)");
                await IbraTools.Fetch(ModelUrl, ModelPath, context.Progress, context.Cancellation);
            }
            _session ??= Open(out _gpu);
            var outputs = new List<string>();
            for (var index = 0; index < files.Count; index++)
            {
                var file = files[index];
                var step = context.ProgressFor(index, files.Count);
                context.Report(part == Part.Vocals ? $"Separating the voice {index + 1} of {files.Count}" : $"Removing the voice {index + 1} of {files.Count}");
                var info = await MediaProbe.Info(file);
                var raw = await FFmpeg.Capture(["-i", file, "-vn", "-f", "f32le", "-ac", "2", "-ar", Rate.ToString(), "-"], context.Cancellation);
                var count = raw.Length / 8;
                if (count < Rate / 10) throw new AdawatException("This file has no sound to separate");
                var left = new float[count];
                var right = new float[count];
                for (var i = 0; i < count; i++)
                {
                    left[i] = BitConverter.ToSingle(raw, i * 8);
                    right[i] = BitConverter.ToSingle(raw, i * 8 + 4);
                }
                raw = [];
                var (vocalLeft, vocalRight) = await Task.Run(() => Separate(left, right, p => step(p * 0.9), context.Cancellation), context.Cancellation);
                if (part == Part.Music)
                    for (var i = 0; i < count; i++)
                    {
                        vocalLeft[i] = left[i] - vocalLeft[i];
                        vocalRight[i] = right[i] - vocalRight[i];
                    }
                context.Report(part == Part.Vocals ? "Saving the voice" : "Saving the music");
                outputs.Add(await Save(file, info, vocalLeft, vocalRight, part, p => step(0.9 + p * 0.1), context));
            }
            context.Finish(part == Part.Vocals
                ? (files.Count == 1 ? "The singer's voice is ready" : $"Separated the voice from {files.Count} files")
                : (files.Count == 1 ? "The music without the voice is ready" : $"Removed the voice from {files.Count} files"));
            return outputs;
        }
        finally { Gate.Release(); }
    }

    // MARK: Model

    private static InferenceSession Open(out bool gpu)
    {
#if WINDOWS
        try
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                EnableMemoryPattern = false,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            };
            options.AppendExecutionProvider_DML(0);
            var session = new InferenceSession(ModelPath, options);
            gpu = true;
            return session;
        }
        catch (Exception e) { Log("DirectML unavailable, using the CPU: " + e.Message); }
#endif
        gpu = false;
        return Cpu();
    }

    private static InferenceSession Cpu() => new(ModelPath, new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL });

    private static void Log(string line)
    {
        try { File.AppendAllLines(Path.Combine(Settings.Folder, "vocals.log"), [$"{DateTime.Now:yyyy-MM-dd HH:mm} {line}"]); } catch (IOException) { }
    }

    private static Tensor<float> Infer(DenseTensor<float> input)
    {
        var name = _session!.InputMetadata.Keys.First();
        try
        {
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(name, input)]);
            var output = results.First().AsTensor<float>();
            return output.Clone();
        }
        catch (OnnxRuntimeException e) when (_gpu)
        {
            // The graphics card gave up (out of memory, old driver): carry on with the CPU.
            Log("DirectML failed, switching to the CPU: " + e.Message);
            _session.Dispose();
            _session = Cpu();
            _gpu = false;
            return Infer(input);
        }
    }

    // MARK: Separation

    /// <summary>The voice, as predicted by the model, for each channel.</summary>
    private static (float[] Left, float[] Right) Separate(float[] left, float[] right, Action<double> progress, CancellationToken cancellation)
    {
        var n = left.Length;
        var pad = Gen + Trim - n % Gen;
        var total = Trim + n + pad;
        var mixL = new float[total];
        var mixR = new float[total];
        Array.Copy(left, 0, mixL, Trim, n);
        Array.Copy(right, 0, mixR, Trim, n);
        var outL = new float[n];
        var outR = new float[n];
        var window = new double[NFft];
        for (var i = 0; i < NFft; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / NFft);
        var envelope = Envelope(window);
        var chunks = (total - Chunk) / Gen + 1;

        for (var chunk = 0; chunk < chunks; chunk++)
        {
            cancellation.ThrowIfCancellationRequested();
            var start = chunk * Gen;
            var input = new DenseTensor<float>([1, 4, DimF, DimT]);
            Parallel.For(0, DimT, new ParallelOptions { CancellationToken = cancellation }, () => new Fft(NFft), (t, _, fft) =>
            {
                Forward(fft, mixL, mixR, start, t, window, input);
                return fft;
            }, _ => { });

            var spec = Infer(input);
            if (_gpu)
            {
                // UVR's "denoise": average with the negated prediction of the negated input, which cancels the model's own hiss.
                var negative = new DenseTensor<float>([1, 4, DimF, DimT]);
                var src = input.Buffer.Span;
                var dst = negative.Buffer.Span;
                for (var i = 0; i < src.Length; i++) dst[i] = -src[i];
                var second = Infer(negative);
                var a = ((DenseTensor<float>)spec).Buffer.Span;
                var b = ((DenseTensor<float>)second).Buffer.Span;
                for (var i = 0; i < a.Length; i++) a[i] = (a[i] - b[i]) * 0.5f;
            }

            var waveL = new double[Chunk + NFft];
            var waveR = new double[Chunk + NFft];
            var frames = new (double[] L, double[] R)[DimT];
            Parallel.For(0, DimT, new ParallelOptions { CancellationToken = cancellation }, () => new Fft(NFft), (t, _, fft) =>
            {
                frames[t] = Inverse(fft, (DenseTensor<float>)spec, t, window);
                return fft;
            }, _ => { });
            for (var t = 0; t < DimT; t++)
            {
                var offset = t * Hop;
                var (fl, fr) = frames[t];
                for (var i = 0; i < NFft; i++)
                {
                    waveL[offset + i] += fl[i];
                    waveR[offset + i] += fr[i];
                }
            }
            // Drop the centring pad and the trimmed edges; keep the middle Gen samples of the chunk.
            for (var i = 0; i < Gen; i++)
            {
                var target = start + i;
                if (target >= n) break;
                var at = NFft / 2 + Trim + i;
                var norm = Math.Max(envelope[at], 1e-8);
                outL[target] = (float)(waveL[at] / norm * Compensate);
                outR[target] = (float)(waveR[at] / norm * Compensate);
            }
            progress((double)(chunk + 1) / chunks);
        }
        return (outL, outR);
    }

    /// <summary>Sum of the squared windows over one chunk, which the overlap-add divides by.</summary>
    private static double[] Envelope(double[] window)
    {
        var envelope = new double[Chunk + NFft];
        for (var t = 0; t < DimT; t++)
            for (var i = 0; i < NFft; i++) envelope[t * Hop + i] += window[i] * window[i];
        return envelope;
    }

    /// <summary>Sample <paramref name="index"/> of a chunk with torch's centred "reflect" padding.</summary>
    private static float Reflect(float[] mix, int start, int index)
    {
        if (index < 0) index = -index;
        else if (index >= Chunk) index = 2 * (Chunk - 1) - index;
        return mix[start + index];
    }

    /// <summary>One STFT frame of both channels, written as left real, left imaginary, right real, right imaginary.</summary>
    private static void Forward(Fft fft, float[] mixL, float[] mixR, int start, int t, double[] window, DenseTensor<float> input)
    {
        var buffer = fft.Buffer;
        var origin = t * Hop - NFft / 2;
        for (var i = 0; i < NFft; i++)
            buffer[i] = new Complex(Reflect(mixL, start, origin + i) * window[i], Reflect(mixR, start, origin + i) * window[i]);
        fft.Forward();
        // Two real signals packed in one transform: L = (Z[k] + conj Z[N-k]) / 2, R = (Z[k] - conj Z[N-k]) / 2i.
        for (var k = 0; k < DimF; k++)
        {
            var z = buffer[k];
            var mirror = Complex.Conjugate(buffer[(NFft - k) % NFft]);
            var l = (z + mirror) * 0.5;
            var r = (z - mirror) * new Complex(0, -0.5);
            input[0, 0, k, t] = (float)l.Real;
            input[0, 1, k, t] = (float)l.Imaginary;
            input[0, 2, k, t] = (float)r.Real;
            input[0, 3, k, t] = (float)r.Imaginary;
        }
    }

    /// <summary>One windowed frame back in time for both channels, from the predicted spectrum.</summary>
    private static (double[] L, double[] R) Inverse(Fft fft, DenseTensor<float> spec, int t, double[] window)
    {
        var buffer = fft.Buffer;
        Array.Clear(buffer);
        for (var k = 0; k < DimF; k++)
        {
            var l = new Complex(spec[0, 0, k, t], k == 0 ? 0 : spec[0, 1, k, t]);
            var r = new Complex(spec[0, 2, k, t], k == 0 ? 0 : spec[0, 3, k, t]);
            // Both spectra are Hermitian; packing L + iR gives one transform whose real part is L and imaginary part R.
            buffer[k] += l + Complex.ImaginaryOne * r;
            if (k > 0) buffer[NFft - k] += Complex.Conjugate(l) + Complex.ImaginaryOne * Complex.Conjugate(r);
        }
        fft.Inverse();
        var outL = new double[NFft];
        var outR = new double[NFft];
        for (var i = 0; i < NFft; i++)
        {
            outL[i] = buffer[i].Real * window[i];
            outR[i] = buffer[i].Imaginary * window[i];
        }
        return (outL, outR);
    }

    // MARK: Saving

    private static async Task<string> Save(string file, MediaInfo info, float[] left, float[] right, Part part, Action<double> progress, OperationContext context)
    {
        using var temp = Output.Temp();
        var pcm = temp.File("stem.f32");
        await using (var stream = File.Create(pcm))
        {
            var bytes = new byte[left.Length * 8];
            for (var i = 0; i < left.Length; i++)
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), left[i]);
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 8 + 4), right[i]);
            }
            await stream.WriteAsync(bytes, context.Cancellation);
        }
        string[] rawInput = ["-f", "f32le", "-ar", Rate.ToString(), "-ac", "2", "-i", pcm];
        var ext = FileKinds.Ext(file);
        var suffix = part == Part.Vocals ? "vocals" : "music";
        var duration = (double)left.Length / Rate;

        // Music from a video stays a video: the picture is copied as it is and only the sound is replaced.
        if (part == Part.Music && FileKinds.Of(file) == FileKind.Video && info.VideoCodec != null)
        {
            var (container, audio) = ext switch
            {
                "mp4" or "m4v" or "mov" => (ext, new[] { "-c:a", "aac", "-b:a", "256k" }),
                "webm" => ("webm", new[] { "-c:a", "libopus", "-b:a", "192k" }),
                _ => ("mkv", new[] { "-c:a", "aac", "-b:a", "256k" }),
            };
            var video = Output.PathFor(file, container, suffix);
            try
            {
                await FFmpeg.Run(["-i", file, .. rawInput, "-map", "0:v:0", "-map", "1:a", "-c:v", "copy", .. audio, "-shortest", video],
                                 duration, progress, context.Cancellation);
            }
            catch { MediaOps.TryDelete(video); throw; }
            return video;
        }

        var (outExt, codec) = FileKinds.Of(file) == FileKind.Audio ? AudioCodec(ext) : ("mp3", AudioCodec("mp3").Codec);
        var output = Output.PathFor(file, outExt, suffix);
        try { await FFmpeg.Run([.. rawInput, .. codec, output], duration, progress, context.Cancellation); }
        catch { MediaOps.TryDelete(output); throw; }
        return output;
    }

    /// <summary>The same kind of file as the original, at a high quality.</summary>
    private static (string Ext, string[] Codec) AudioCodec(string ext) => ext switch
    {
        "m4a" or "aac" => ("m4a", ["-c:a", "aac", "-b:a", "256k"]),
        "wav" => ("wav", ["-c:a", "pcm_s16le"]),
        "flac" => ("flac", ["-c:a", "flac"]),
        "aiff" or "aif" => ("aiff", ["-c:a", "pcm_s16be"]),
        "ogg" => ("ogg", ["-c:a", "libvorbis", "-q:a", "6"]),
        "opus" => ("opus", ["-c:a", "libopus", "-b:a", "192k"]),
        "wma" => ("wma", ["-c:a", "wmav2", "-b:a", "256k"]),
        _ => ("mp3", ["-c:a", "libmp3lame", "-b:a", "320k"]),
    };

    /// <summary>Mixed-radix FFT (factors 2, 3, 4, 5) for the model's 7680-point frames, after KissFFT's layout.</summary>
    private sealed class Fft
    {
        private readonly int _n;
        private readonly Complex[] _twiddles;
        private readonly int[] _factors;
        private readonly Complex[] _work;
        private readonly Complex[] _scratch = new Complex[8];
        public readonly Complex[] Buffer;

        public Fft(int n)
        {
            _n = n;
            _twiddles = new Complex[n];
            for (var i = 0; i < n; i++) _twiddles[i] = Complex.FromPolarCoordinates(1, -2 * Math.PI * i / n);
            var factors = new List<int>();
            var rest = n;
            foreach (var p in new[] { 4, 2, 3, 5 })
                while (rest % p == 0) { factors.Add(p); rest /= p; }
            if (rest != 1) throw new ArgumentException("FFT size must factor into 2, 3 and 5");
            _factors = [.. factors];
            _work = new Complex[n];
            Buffer = new Complex[n];
        }

        public void Forward()
        {
            Array.Copy(Buffer, _work, _n);
            Transform(Buffer, 0, _work, 0, 1, 0);
        }

        /// <summary>Inverse transform, scaled by 1/N.</summary>
        public void Inverse()
        {
            for (var i = 0; i < _n; i++) _work[i] = Complex.Conjugate(Buffer[i]);
            Transform(Buffer, 0, _work, 0, 1, 0);
            var scale = 1.0 / _n;
            for (var i = 0; i < _n; i++) Buffer[i] = Complex.Conjugate(Buffer[i]) * scale;
        }

        private void Transform(Complex[] output, int outAt, Complex[] input, int inAt, int stride, int level)
        {
            var p = _factors[level];
            var m = _n / (stride * p);
            if (m == 1)
                for (var i = 0; i < p; i++) output[outAt + i] = input[inAt + i * stride];
            else
                for (var q = 0; q < p; q++) Transform(output, outAt + q * m, input, inAt + q * stride, stride * p, level + 1);

            var scratch = _scratch;
            for (var u = 0; u < m; u++)
            {
                for (var q = 0; q < p; q++) scratch[q] = output[outAt + u + q * m];
                for (var q1 = 0; q1 < p; q1++)
                {
                    var k = u + q1 * m;
                    var sum = scratch[0];
                    var twiddle = 0;
                    for (var q = 1; q < p; q++)
                    {
                        twiddle += stride * k;
                        if (twiddle >= _n) twiddle %= _n;
                        sum += scratch[q] * _twiddles[twiddle];
                    }
                    output[outAt + k] = sum;
                }
            }
        }
    }
}
