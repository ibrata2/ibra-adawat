import AVFoundation

/// Video and audio with AVFoundation: everything stays on the phone.
enum MediaOps {
    /// Runs an export session, passing its progress along, and returns the written file.
    static func export(_ session: AVAssetExportSession, to output: URL, type: AVFileType, metadata: [AVMetadataItem]? = [],
                       progress: @escaping @Sendable (Double) -> Void) async throws -> URL {
        session.outputURL = output
        session.outputFileType = type
        session.shouldOptimizeForNetworkUse = true
        // An empty list writes no title, comment, software or location tags at all.
        if let metadata { session.metadata = metadata }
        session.metadataItemFilter = AVMetadataItemFilter.forSharing()
        let ticker = Task {
            while !Task.isCancelled {
                progress(Double(session.progress))
                try? await Task.sleep(nanoseconds: 200_000_000)
            }
        }
        defer { ticker.cancel() }
        await withCheckedContinuation { (done: CheckedContinuation<Void, Never>) in
            session.exportAsynchronously { done.resume() }
        }
        if session.status != .completed {
            try? FileManager.default.removeItem(at: output)
            throw session.error ?? OpError("The export did not finish", "ما كمل الحفظ")
        }
        return output
    }

    private static func session(_ asset: AVAsset, _ preset: String) throws -> AVAssetExportSession {
        guard let session = AVAssetExportSession(asset: asset, presetName: preset) else {
            throw OpError("This file can't be converted here", "ما أگدر أحول هذا الملف هنا")
        }
        return session
    }

    /// Same picture and sound in another container when possible, re-encoded at high quality otherwise.
    static func convert(_ url: URL, to type: AVFileType, ext: String, progress: @escaping @Sendable (Double) -> Void) async throws -> URL {
        let asset = AVURLAsset(url: url)
        let output = Outputs.path(for: url, ext: ext, suffix: "")
        let compatible = await AVAssetExportSession.compatibility(ofExportPreset: AVAssetExportPresetPassthrough, with: asset, outputFileType: type)
        let preset = compatible ? AVAssetExportPresetPassthrough : AVAssetExportPresetHighestQuality
        return try await export(try session(asset, preset), to: output, type: type, metadata: nil, progress: progress)
    }

    static func compress(_ url: URL, preset: String, tag: String, progress: @escaping @Sendable (Double) -> Void) async throws -> URL {
        let output = Outputs.path(for: url, ext: "mp4", suffix: tag)
        return try await export(try session(AVURLAsset(url: url), preset), to: output, type: .mp4, metadata: nil, progress: progress)
    }

    static func extractAudio(_ url: URL, progress: @escaping @Sendable (Double) -> Void) async throws -> URL {
        let asset = AVURLAsset(url: url)
        guard try await !asset.loadTracks(withMediaType: .audio).isEmpty else { throw OpError("This file has no sound", "هذا الملف ما بيه صوت") }
        let output = Outputs.path(for: url, ext: "m4a", suffix: "")
        return try await export(try session(asset, AVAssetExportPresetAppleM4A), to: output, type: .m4a, metadata: nil, progress: progress)
    }

    static func mute(_ url: URL, progress: @escaping @Sendable (Double) -> Void) async throws -> URL {
        let asset = AVURLAsset(url: url)
        let composition = AVMutableComposition()
        let duration = try await asset.load(.duration)
        for track in try await asset.loadTracks(withMediaType: .video) {
            let target = composition.addMutableTrack(withMediaType: .video, preferredTrackID: kCMPersistentTrackID_Invalid)
            try target?.insertTimeRange(CMTimeRange(start: .zero, duration: duration), of: track, at: .zero)
            target?.preferredTransform = try await track.load(.preferredTransform)
        }
        let output = Outputs.path(for: url, ext: "mp4", suffix: "muted")
        return try await export(try session(composition, AVAssetExportPresetHighestQuality), to: output, type: .mp4, progress: progress)
    }

    /// 16-bit PCM WAV, decoded with an asset reader.
    static func toWav(_ url: URL, progress: @escaping @Sendable (Double) -> Void) async throws -> URL {
        let asset = AVURLAsset(url: url)
        guard let track = try await asset.loadTracks(withMediaType: .audio).first else { throw OpError("This file has no sound", "هذا الملف ما بيه صوت") }
        let duration = try await asset.load(.duration).seconds
        let output = Outputs.path(for: url, ext: "wav", suffix: "")
        let reader = try AVAssetReader(asset: asset)
        let settings: [String: Any] = [AVFormatIDKey: kAudioFormatLinearPCM, AVLinearPCMBitDepthKey: 16, AVLinearPCMIsFloatKey: false,
                                       AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false]
        let out = AVAssetReaderTrackOutput(track: track, outputSettings: settings)
        reader.add(out)
        let writer = try AVAssetWriter(outputURL: output, fileType: .wav)
        let descriptions = try await track.load(.formatDescriptions)
        var rate = 44100.0, channels = 2
        if let first = descriptions.first, let basic = CMAudioFormatDescriptionGetStreamBasicDescription(first)?.pointee {
            rate = basic.mSampleRate
            channels = Int(basic.mChannelsPerFrame)
        }
        let input = AVAssetWriterInput(mediaType: .audio, outputSettings: [
            AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: rate, AVNumberOfChannelsKey: channels, AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false, AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false,
        ])
        writer.add(input)
        reader.startReading()
        writer.startWriting()
        writer.startSession(atSourceTime: .zero)
        while reader.status == .reading {
            if input.isReadyForMoreMediaData {
                guard let buffer = out.copyNextSampleBuffer() else { break }
                let time = CMSampleBufferGetPresentationTimeStamp(buffer).seconds
                if duration > 0 { progress(time / duration) }
                input.append(buffer)
            } else {
                try await Task.sleep(nanoseconds: 5_000_000)
            }
        }
        input.markAsFinished()
        await writer.finishWriting()
        if writer.status != .completed { throw writer.error ?? OpError("The export did not finish", "ما كمل الحفظ") }
        return output
    }

    /// The same streams in a fresh container with no tags at all (MP3 becomes M4A, which iPhone can write).
    static func stripped(_ url: URL, progress: @escaping @Sendable (Double) -> Void) async throws -> URL {
        let asset = AVURLAsset(url: url)
        let ext = url.pathExtension.lowercased()
        let (type, outExt): (AVFileType, String) = url.kind == .audio
            ? (ext == "m4a" ? (.m4a, "m4a") : ext == "wav" ? (.wav, "wav") : (.m4a, "m4a"))
            : (ext == "mov" ? (.mov, "mov") : (.mp4, "mp4"))
        let output = Outputs.path(for: url, ext: outExt, suffix: "clean")
        let passthrough = await AVAssetExportSession.compatibility(ofExportPreset: AVAssetExportPresetPassthrough, with: asset, outputFileType: type)
        let preset = passthrough ? AVAssetExportPresetPassthrough : (url.kind == .audio ? AVAssetExportPresetAppleM4A : AVAssetExportPresetHighestQuality)
        return try await export(try session(asset, preset), to: output, type: type, metadata: [], progress: progress)
    }
}
