using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Adawat.Core.Images;

/// <summary>Finds faces with Ultra-Light-Fast-Generic-Face-Detector (RFB-640, MIT), a 1.6 MB model that ships in
/// models/face-detector.onnx next to the app and runs on the CPU with ONNX Runtime.</summary>
internal static class FaceDetector
{
    private const int InputWidth = 640, InputHeight = 480;
    private const float Threshold = 0.7f, Overlap = 0.3f;

    private static readonly Lazy<InferenceSession> Session = new(() =>
    {
        var model = Path.Combine(AppContext.BaseDirectory, "models", "face-detector.onnx");
        if (!File.Exists(model)) throw new AdawatException("The face detector is missing from this installation.");
        return new InferenceSession(model, new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL });
    });

    public static IReadOnlyList<(double X, double Y, double Width, double Height)> Detect(MagickImage image)
    {
        using var copy = (MagickImage)image.Clone();
        copy.BackgroundColor = MagickColors.White;
        copy.Alpha(AlphaAction.Remove);
        // The model sees the whole picture stretched to 640×480; its boxes come back as fractions of that frame.
        copy.Resize(new MagickGeometry(InputWidth, InputHeight) { IgnoreAspectRatio = true });
        using var pixels = copy.GetPixels();
        var rgb = pixels.ToByteArray(PixelMapping.RGB)!;
        var input = new DenseTensor<float>([1, 3, InputHeight, InputWidth]);
        for (var y = 0; y < InputHeight; y++)
        for (var x = 0; x < InputWidth; x++)
        {
            var i = (y * InputWidth + x) * 3;
            for (var c = 0; c < 3; c++) input[0, c, y, x] = (rgb[i + c] - 127f) / 128f;
        }
        var session = Session.Value;
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        var scores = results.First(r => r.Name == "scores").AsTensor<float>();
        var boxes = results.First(r => r.Name == "boxes").AsTensor<float>();
        var candidates = new List<(float Score, float X1, float Y1, float X2, float Y2)>();
        for (var i = 0; i < scores.Dimensions[1]; i++)
        {
            var score = scores[0, i, 1];
            if (score < Threshold) continue;
            candidates.Add((score, boxes[0, i, 0], boxes[0, i, 1], boxes[0, i, 2], boxes[0, i, 3]));
        }
        var kept = new List<(float Score, float X1, float Y1, float X2, float Y2)>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
            if (kept.All(k => IoU(k, candidate) < Overlap)) kept.Add(candidate);
        double w = image.Width, h = image.Height;
        return kept.Select(b =>
        {
            double x1 = Math.Clamp(b.X1, 0, 1) * w, y1 = Math.Clamp(b.Y1, 0, 1) * h, x2 = Math.Clamp(b.X2, 0, 1) * w, y2 = Math.Clamp(b.Y2, 0, 1) * h;
            return (x1, y1, x2 - x1, y2 - y1);
        }).Where(b => b.Item3 > 2 && b.Item4 > 2).ToList();
    }

    private static float IoU((float S, float X1, float Y1, float X2, float Y2) a, (float S, float X1, float Y1, float X2, float Y2) b)
    {
        var w = Math.Max(0, Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1));
        var h = Math.Max(0, Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1));
        var inter = w * h;
        var union = (a.X2 - a.X1) * (a.Y2 - a.Y1) + (b.X2 - b.X1) * (b.Y2 - b.Y1) - inter;
        return union <= 0 ? 0 : inter / union;
    }
}
