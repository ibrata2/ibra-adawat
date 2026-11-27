import Foundation
import AVFoundation
import Accelerate
#if canImport(OnnxRuntimeBindings)
import OnnxRuntimeBindings
#elseif canImport(onnxruntime_objc)
import onnxruntime_objc
#endif

/// Splits a song into the singer's voice and the music with MDX-Net ("UVR-MDX-NET-Voc_FT" from Ultimate Vocal Remover, MIT),
/// the same model and steps as IBMC for Windows. The model predicts the voice; the music is the original minus the voice.
/// The 66 MB model downloads once, then everything runs on the phone.
enum VocalSeparation {
    enum Part { case vocals, music }

    private static let modelURL = URL(string: "https://github.com/TRvlvr/model_repo/releases/download/all_public_uvr_models/UVR-MDX-NET-Voc_FT.onnx")!
    private static let rate = 44100.0
    private static let nFft = 7680, hop = 1024, dimF = 3072, dimT = 256
    private static let compensate: Float = 1.021
    private static let chunk = hop * (dimT - 1), trim = nFft / 2, gen = chunk - 2 * (nFft / 2)

    private static var modelPath: URL {
        let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("models", isDirectory: true)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder.appendingPathComponent("UVR-MDX-NET-Voc_FT.onnx")
    }

    static func run(_ files: [URL], part: Part, report: Report) async throws -> [URL] {
        try await Model.shared.prepare(report: report)
        var outputs: [URL] = []
        for (index, file) in files.enumerated() {
            let base = Double(index) / Double(files.count)
            let share = 1 / Double(files.count)
            report.status(part == .vocals
                ? L.t("Separating the voice \(index + 1) of \(files.count)", "جاري فصل صوت المغني \(index + 1) من \(files.count)")
                : L.t("Removing the voice \(index + 1) of \(files.count)", "جاري شيل صوت المغني \(index + 1) من \(files.count)"))
            var (left, right) = try await decode(file)
            guard left.count > Int(rate) / 10 else { throw OpError("This file has no sound to separate", "هذا الملف ما بيه صوت حتى ينفصل") }
            var (vocalL, vocalR) = try await Model.shared.separate(left, right) { report.progress(base + $0 * 0.9 * share) }
            if part == .music {
                for i in 0..<vocalL.count {
                    vocalL[i] = left[i] - vocalL[i]
                    vocalR[i] = right[i] - vocalR[i]
                }
            }
            left = []; right = []
            report.status(part == .vocals ? L.t("Saving the voice", "جاري حفظ صوت المغني") : L.t("Saving the music", "جاري حفظ الموسيقى"))
            outputs.append(try await save(file, vocalL, vocalR, part: part))
            report.progress(base + share)
        }
        report.finish = part == .vocals
            ? (files.count == 1 ? L.t("The singer's voice is ready", "صوت المغني جاهز") : L.t("Separated the voice from \(files.count) files", "انفصل صوت المغني من \(files.count) ملفات"))
            : (files.count == 1 ? L.t("The music without the voice is ready", "الموسيقى بدون صوت جاهزة") : L.t("Removed the voice from \(files.count) files", "انشال صوت المغني من \(files.count) ملفات"))
        return outputs
    }

    // MARK: Model

    /// One loaded model for the whole app; separations run one at a time.
    private actor Model {
        static let shared = Model()
        private var env: ORTEnv?
        private var session: ORTSession?
        private var fast = false

        func prepare(report: Report) async throws {
            if session != nil { return }
            if !FileManager.default.fileExists(atPath: modelPath.path) {
                report.status(L.t("Downloading the voice separation model (one time only)", "جاري تنزيل نموذج فصل الصوت (مرة وحدة بس)"))
                let (temporary, response) = try await URLSession.shared.download(from: modelURL)
                guard (response as? HTTPURLResponse)?.statusCode == 200 else {
                    throw OpError("Download failed. Check the internet connection.", "فشل التنزيل. تأكد من الإنترنت.")
                }
                try? FileManager.default.removeItem(at: modelPath)
                try FileManager.default.moveItem(at: temporary, to: modelPath)
            }
            let env = try ORTEnv(loggingLevel: .warning)
            self.env = env
            // Core ML (the Neural Engine and graphics) when it takes the model; the CPU otherwise.
            if let options = try? ORTSessionOptions() {
                _ = try? options.setGraphOptimizationLevel(.all)
                if (try? options.appendCoreMLExecutionProvider(with: ORTCoreMLExecutionProviderOptions())) != nil,
                   let accelerated = try? ORTSession(env: env, modelPath: modelPath.path, sessionOptions: options) {
                    session = accelerated
                    fast = true
                    return
                }
            }
            session = try cpu(env)
        }

        private func cpu(_ env: ORTEnv) throws -> ORTSession {
            let options = try ORTSessionOptions()
            try options.setGraphOptimizationLevel(.all)
            try options.setIntraOpNumThreads(Int32(max(ProcessInfo.processInfo.activeProcessorCount, 1)))
            fast = false
            return try ORTSession(env: env, modelPath: modelPath.path, sessionOptions: options)
        }

        private func infer(_ input: inout [Float]) throws -> [Float] {
            let shape: [NSNumber] = [1, 4, NSNumber(value: dimF), NSNumber(value: dimT)]
            let data = input.withUnsafeMutableBytes { NSMutableData(bytes: $0.baseAddress!, length: $0.count) }
            let value = try ORTValue(tensorData: data, elementType: .float, shape: shape)
            do {
                let results = try session!.run(withInputs: ["input": value], outputNames: ["output"], runOptions: nil)
                guard let output = results["output"] else { throw OpError("The voice model gave no result", "نموذج الصوت ما رجع نتيجة") }
                let bytes = try output.tensorData() as Data
                return bytes.withUnsafeBytes { Array($0.bindMemory(to: Float.self)) }
            } catch where fast {
                // Core ML gave up on this model: carry on with the CPU.
                session = try cpu(env!)
                return try infer(&input)
            }
        }

        func separate(_ left: [Float], _ right: [Float], progress: @escaping (Double) -> Void) throws -> ([Float], [Float]) {
            let n = left.count
            let pad = gen + trim - n % gen
            var mixL = [Float](repeating: 0, count: trim + n + pad)
            var mixR = mixL
            mixL.replaceSubrange(trim..<(trim + n), with: left)
            mixR.replaceSubrange(trim..<(trim + n), with: right)
            var outL = [Float](repeating: 0, count: n)
            var outR = [Float](repeating: 0, count: n)
            let window = (0..<nFft).map { 0.5 - 0.5 * cos(2 * Double.pi * Double($0) / Double(nFft)) }
            var envelope = [Double](repeating: 0, count: chunk + nFft)
            for t in 0..<dimT { for i in 0..<nFft { envelope[t * hop + i] += window[i] * window[i] } }
            guard let forward = vDSP_DFT_zop_CreateSetupD(nil, vDSP_Length(nFft), .FORWARD),
                  let inverse = vDSP_DFT_zop_CreateSetupD(forward, vDSP_Length(nFft), .INVERSE) else {
                throw OpError("This phone can't run the voice separation", "هذا الموبايل ما يگدر يشغل فصل الصوت")
            }
            defer { vDSP_DFT_DestroySetupD(forward); vDSP_DFT_DestroySetupD(inverse) }
            let chunks = (mixL.count - chunk) / gen + 1
            let plane = dimF * dimT

            for c in 0..<chunks {
                try Task.checkCancellation()
                let start = c * gen
                var input = [Float](repeating: 0, count: 4 * plane)
                input.withUnsafeMutableBufferPointer { tensor in
                    mixL.withUnsafeBufferPointer { ml in
                        mixR.withUnsafeBufferPointer { mr in
                            let tensor = tensor, ml = ml, mr = mr
                            DispatchQueue.concurrentPerform(iterations: dimT) { t in
                                var inR = [Double](repeating: 0, count: nFft), inI = inR, outRe = inR, outIm = inR
                                let origin = t * hop - nFft / 2
                                for i in 0..<nFft {
                                    // torch's centred "reflect" padding at the chunk edges.
                                    var at = origin + i
                                    if at < 0 { at = -at } else if at >= chunk { at = 2 * (chunk - 1) - at }
                                    inR[i] = Double(ml[start + at]) * window[i]
                                    inI[i] = Double(mr[start + at]) * window[i]
                                }
                                vDSP_DFT_ExecuteD(forward, inR, inI, &outRe, &outIm)
                                // Two real signals in one transform: L = (Z[k] + conj Z[N-k]) / 2, R = (Z[k] - conj Z[N-k]) / 2i.
                                for k in 0..<dimF {
                                    let m = (nFft - k) % nFft
                                    let zr = outRe[k], zi = outIm[k], mr2 = outRe[m], mi = -outIm[m]
                                    let at = k * dimT + t
                                    tensor[at] = Float((zr + mr2) * 0.5)
                                    tensor[plane + at] = Float((zi + mi) * 0.5)
                                    tensor[2 * plane + at] = Float((zi - mi) * 0.5)
                                    tensor[3 * plane + at] = Float(-(zr - mr2) * 0.5)
                                }
                            }
                        }
                    }
                }

                var spec = try infer(&input)
                if fast {
                    // UVR's "denoise": average with the negated prediction of the negated input.
                    var negative = input.map { -$0 }
                    let second = try infer(&negative)
                    for i in 0..<spec.count { spec[i] = (spec[i] - second[i]) * 0.5 }
                }

                var frames = [[Double]](repeating: [], count: dimT)
                spec.withUnsafeBufferPointer { s in
                    frames.withUnsafeMutableBufferPointer { f in
                        let s = s, f = f
                        DispatchQueue.concurrentPerform(iterations: dimT) { t in
                            var inR = [Double](repeating: 0, count: nFft), inI = inR, outRe = inR, outIm = inR
                            for k in 0..<dimF {
                                let at = k * dimT + t
                                let lr = Double(s[at]), li = k == 0 ? 0 : Double(s[plane + at])
                                let rr = Double(s[2 * plane + at]), ri = k == 0 ? 0 : Double(s[3 * plane + at])
                                // L + iR, both Hermitian: the inverse's real part is L and its imaginary part R.
                                inR[k] += lr - ri
                                inI[k] += li + rr
                                if k > 0 {
                                    inR[nFft - k] += lr + ri
                                    inI[nFft - k] += -li + rr
                                }
                            }
                            vDSP_DFT_ExecuteD(inverse, inR, inI, &outRe, &outIm)
                            var frame = [Double](repeating: 0, count: 2 * nFft)
                            let scale = 1 / Double(nFft)
                            for i in 0..<nFft {
                                frame[i] = outRe[i] * scale * window[i]
                                frame[nFft + i] = outIm[i] * scale * window[i]
                            }
                            f[t] = frame
                        }
                    }
                }
                var waveL = [Double](repeating: 0, count: chunk + nFft)
                var waveR = waveL
                for t in 0..<dimT {
                    let offset = t * hop
                    let frame = frames[t]
                    for i in 0..<nFft {
                        waveL[offset + i] += frame[i]
                        waveR[offset + i] += frame[nFft + i]
                    }
                }
                for i in 0..<gen {
                    let target = start + i
                    if target >= n { break }
                    let at = nFft / 2 + trim + i
                    let norm = max(envelope[at], 1e-8)
                    outL[target] = Float(waveL[at] / norm) * compensate
                    outR[target] = Float(waveR[at] / norm) * compensate
                }
                progress(Double(c + 1) / Double(chunks))
            }
            return (outL, outR)
        }
    }

    // MARK: Sound in and out

    /// The whole sound as 44.1 kHz stereo floats.
    private static func decode(_ url: URL) async throws -> ([Float], [Float]) {
        let asset = AVURLAsset(url: url)
        guard let track = try await asset.loadTracks(withMediaType: .audio).first else { throw OpError("This file has no sound", "هذا الملف ما بيه صوت") }
        let reader = try AVAssetReader(asset: asset)
        let output = AVAssetReaderTrackOutput(track: track, outputSettings: [
            AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: rate, AVNumberOfChannelsKey: 2,
            AVLinearPCMBitDepthKey: 32, AVLinearPCMIsFloatKey: true, AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false,
        ])
        reader.add(output)
        guard reader.startReading() else { throw reader.error ?? OpError("This file can't be read", "ما أگدر أقرا هذا الملف") }
        var left: [Float] = [], right: [Float] = []
        while let buffer = output.copyNextSampleBuffer() {
            guard let block = CMSampleBufferGetDataBuffer(buffer) else { continue }
            let length = CMBlockBufferGetDataLength(block)
            let count = length / 8
            guard count > 0 else { continue }
            var samples = [Float](repeating: 0, count: count * 2)
            let status = samples.withUnsafeMutableBytes { CMBlockBufferCopyDataBytes(block, atOffset: 0, dataLength: count * 8, destination: $0.baseAddress!) }
            guard status == noErr else { continue }
            for i in 0..<count {
                left.append(samples[2 * i])
                right.append(samples[2 * i + 1])
            }
        }
        if reader.status == .failed { throw reader.error ?? OpError("This file can't be read", "ما أگدر أقرا هذا الملف") }
        return (left, right)
    }

    /// AAC in an .m4a for sound; for the music of a video, the same picture with the new sound.
    private static func save(_ source: URL, _ left: [Float], _ right: [Float], part: Part) async throws -> URL {
        let suffix = part == .vocals ? "vocals" : "music"
        let video = part == .music && source.kind == .video
        let audioURL = video ? FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".m4a")
                             : Outputs.path(for: source, ext: "m4a", suffix: suffix)
        try write(left, right, to: audioURL)
        guard video else { return audioURL }
        defer { try? FileManager.default.removeItem(at: audioURL) }

        let original = AVURLAsset(url: source)
        let music = AVURLAsset(url: audioURL)
        let composition = AVMutableComposition()
        let duration = try await original.load(.duration)
        for track in try await original.loadTracks(withMediaType: .video) {
            let target = composition.addMutableTrack(withMediaType: .video, preferredTrackID: kCMPersistentTrackID_Invalid)
            try target?.insertTimeRange(CMTimeRange(start: .zero, duration: duration), of: track, at: .zero)
            target?.preferredTransform = try await track.load(.preferredTransform)
        }
        if let sound = try await music.loadTracks(withMediaType: .audio).first {
            let soundLength = try await music.load(.duration)
            let target = composition.addMutableTrack(withMediaType: .audio, preferredTrackID: kCMPersistentTrackID_Invalid)
            try target?.insertTimeRange(CMTimeRange(start: .zero, duration: CMTimeMinimum(duration, soundLength)), of: sound, at: .zero)
        }
        let output = Outputs.path(for: source, ext: "mp4", suffix: suffix)
        let passthrough = await AVAssetExportSession.compatibility(ofExportPreset: AVAssetExportPresetPassthrough, with: composition, outputFileType: .mp4)
        guard let session = AVAssetExportSession(asset: composition, presetName: passthrough ? AVAssetExportPresetPassthrough : AVAssetExportPresetHighestQuality) else {
            throw OpError("This file can't be converted here", "ما أگدر أحول هذا الملف هنا")
        }
        return try await MediaOps.export(session, to: output, type: .mp4, metadata: nil) { _ in }
    }

    private static func write(_ left: [Float], _ right: [Float], to url: URL) throws {
        let settings: [String: Any] = [AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: rate, AVNumberOfChannelsKey: 2, AVEncoderBitRateKey: 256_000]
        let file = try AVAudioFile(forWriting: url, settings: settings, commonFormat: .pcmFormatFloat32, interleaved: false)
        guard let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: rate, channels: 2, interleaved: false) else { return }
        let block = 1 << 16
        var at = 0
        while at < left.count {
            let count = min(block, left.count - at)
            guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(count)), let channels = buffer.floatChannelData else { break }
            buffer.frameLength = AVAudioFrameCount(count)
            left.withUnsafeBufferPointer { channels[0].update(from: $0.baseAddress! + at, count: count) }
            right.withUnsafeBufferPointer { channels[1].update(from: $0.baseAddress! + at, count: count) }
            try file.write(from: buffer)
            at += count
        }
    }
}
