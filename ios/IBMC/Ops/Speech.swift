import Speech
import AVFoundation

/// Speech to text with Apple's recognizer: a .txt of the words and a timed .srt for subtitles.
enum SpeechOps {
    static func transcribe(_ files: [URL], locale: String, report: Report) async throws -> [URL] {
        let status = await withCheckedContinuation { (done: CheckedContinuation<SFSpeechRecognizerAuthorizationStatus, Never>) in
            SFSpeechRecognizer.requestAuthorization { done.resume(returning: $0) }
        }
        guard status == .authorized else {
            throw OpError("Allow speech recognition for IBMC in Settings", "اسمح لـ IBMC بالتعرف على الكلام من الإعدادات")
        }
        guard let recognizer = SFSpeechRecognizer(locale: Locale(identifier: locale)), recognizer.isAvailable else {
            throw OpError("Speech recognition for this language isn't available right now", "التعرف على هاي اللغة مو متوفر هسه")
        }
        var outputs: [URL] = []
        for (index, file) in files.enumerated() {
            report.status(L.t("Writing down the speech \(index + 1) of \(files.count)", "كتابة الكلام \(index + 1) من \(files.count)"))
            report.progress(Double(index) / Double(files.count))
            // Videos first become sound only; the recognizer reads audio files.
            var audio = file
            var temporary: URL?
            if file.kind == .video {
                audio = try await MediaOps.extractAudio(file) { _ in }
                temporary = audio
            }
            defer { if let temporary { try? FileManager.default.removeItem(at: temporary) } }
            let request = SFSpeechURLRecognitionRequest(url: audio)
            request.shouldReportPartialResults = false
            request.addsPunctuation = true
            if recognizer.supportsOnDeviceRecognition { request.requiresOnDeviceRecognition = true }
            let result: SFSpeechRecognitionResult = try await withCheckedThrowingContinuation { done in
                var finished = false
                recognizer.recognitionTask(with: request) { result, error in
                    guard !finished else { return }
                    if let error { finished = true; done.resume(throwing: error); return }
                    if let result, result.isFinal { finished = true; done.resume(returning: result) }
                }
            }
            let transcription = result.bestTranscription
            guard !transcription.formattedString.isEmpty else { throw OpError("No speech was found", "ما لكيت كلام") }
            let txt = Outputs.path(for: file, ext: "txt", suffix: "")
            try transcription.formattedString.write(to: txt, atomically: true, encoding: .utf8)
            let srt = Outputs.path(for: file, ext: "srt", suffix: "")
            try subtitles(transcription.segments).write(to: srt, atomically: true, encoding: .utf8)
            outputs += [txt, srt]
        }
        return outputs
    }

    /// Words grouped into short lines (at most seven words, a new line after a pause).
    private static func subtitles(_ segments: [SFTranscriptionSegment]) -> String {
        var cues: [(start: Double, end: Double, text: [String])] = []
        for segment in segments {
            let start = segment.timestamp, end = segment.timestamp + segment.duration
            if var last = cues.last, last.text.count < 7, start - last.end < 0.8 {
                last.text.append(segment.substring)
                last.end = end
                cues[cues.count - 1] = last
            } else {
                cues.append((start, end, [segment.substring]))
            }
        }
        func time(_ s: Double) -> String {
            let ms = Int((s * 1000).rounded())
            return String(format: "%02d:%02d:%02d,%03d", ms / 3_600_000, ms / 60_000 % 60, ms / 1000 % 60, ms % 1000)
        }
        return cues.enumerated().map { index, cue in
            "\(index + 1)\r\n\(time(cue.start)) --> \(time(max(cue.end, cue.start + 0.5)))\r\n\(cue.text.joined(separator: " "))\r\n"
        }.joined(separator: "\r\n")
    }
}
