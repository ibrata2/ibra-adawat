import UIKit
import ImageIO
import UniformTypeIdentifiers
import Vision
import CoreImage
import CoreImage.CIFilterBuiltins

/// Pictures with ImageIO, Core Image and Vision.
enum ImageOps {
    /// The picture turned upright, at full size. Reading it this way leaves every tag of the original behind.
    static func upright(_ url: URL) throws -> CGImage {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { throw OpError("This picture can't be opened", "ما أگدر أفتح هاي الصورة") }
        let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any] ?? [:]
        let width = properties[kCGImagePropertyPixelWidth] as? Int ?? 4096
        let height = properties[kCGImagePropertyPixelHeight] as? Int ?? 4096
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: max(width, height),
            kCGImageSourceShouldCacheImmediately: true,
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else {
            throw OpError("This picture can't be opened", "ما أگدر أفتح هاي الصورة")
        }
        return image
    }

    /// Writes a picture with no metadata except its colour profile, which lives in the image itself.
    static func write(_ image: CGImage, to url: URL, type: UTType, quality: Double = 0.95) throws -> URL {
        guard let destination = CGImageDestinationCreateWithURL(url as CFURL, type.identifier as CFString, 1, nil) else {
            throw OpError("This format can't be written", "ما أگدر أحفظ بهاي الصيغة")
        }
        let options: [CFString: Any] = [kCGImageDestinationLossyCompressionQuality: quality]
        CGImageDestinationAddImage(destination, image, options as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { throw OpError("Saving failed", "فشل الحفظ") }
        return url
    }

    static func convert(_ url: URL, to type: UTType, ext: String) throws -> URL {
        try write(try upright(url), to: Outputs.path(for: url, ext: ext, suffix: ""), type: type)
    }

    /// At most 2048 pixels on the long side, JPEG at 75%.
    static func compress(_ url: URL) throws -> URL {
        var image = try upright(url)
        let longest = max(image.width, image.height)
        if longest > 2048, let smaller = resize(image, scale: 2048 / Double(longest)) { image = smaller }
        return try write(image, to: Outputs.path(for: url, ext: "jpg", suffix: "small"), type: .jpeg, quality: 0.75)
    }

    private static func resize(_ image: CGImage, scale: Double) -> CGImage? {
        let ci = CIImage(cgImage: image).transformed(by: CGAffineTransform(scaleX: scale, y: scale))
        return CIContext().createCGImage(ci, from: ci.extent)
    }

    /// The picture fitted inside the frame; the space around it filled with a blurred, darker copy of itself.
    static func social(_ url: URL, size: CGSize, tag: String) throws -> URL {
        let image = CIImage(cgImage: try upright(url))
        let extent = image.extent
        let fill = max(size.width / extent.width, size.height / extent.height)
        let fit = min(size.width / extent.width, size.height / extent.height)
        let background = image.transformed(by: CGAffineTransform(scaleX: fill, y: fill))
        let blurred = background.clampedToExtent().applyingGaussianBlur(sigma: 40)
            .applyingFilter("CIColorControls", parameters: [kCIInputBrightnessKey: -0.08])
        let bx = (background.extent.width - size.width) / 2, by = (background.extent.height - size.height) / 2
        let bg = blurred.transformed(by: CGAffineTransform(translationX: -bx, y: -by)).cropped(to: CGRect(origin: .zero, size: size))
        let front = image.transformed(by: CGAffineTransform(scaleX: fit, y: fit))
        let fx = (size.width - front.extent.width) / 2, fy = (size.height - front.extent.height) / 2
        let composed = front.transformed(by: CGAffineTransform(translationX: fx, y: fy)).composited(over: bg)
        guard let out = CIContext().createCGImage(composed, from: CGRect(origin: .zero, size: size)) else { throw OpError("Saving failed", "فشل الحفظ") }
        return try write(out, to: Outputs.path(for: url, ext: "jpg", suffix: tag), type: .jpeg, quality: 0.92)
    }

    /// Keeps the main subject on a transparent background (Vision, on the phone).
    static func removeBackground(_ url: URL) throws -> URL {
        let image = try upright(url)
        let request = VNGenerateForegroundInstanceMaskRequest()
        let handler = VNImageRequestHandler(cgImage: image)
        try handler.perform([request])
        guard let result = request.results?.first else { throw OpError("No subject was found in this picture", "ما لكيت شي واضح بالصورة") }
        let buffer = try result.generateMaskedImage(ofInstances: result.allInstances, from: handler, croppedToInstancesExtent: false)
        let ci = CIImage(cvPixelBuffer: buffer)
        guard let out = CIContext().createCGImage(ci, from: ci.extent) else { throw OpError("Saving failed", "فشل الحفظ") }
        return try write(out, to: Outputs.path(for: url, ext: "png", suffix: "no-bg"), type: .png)
    }

    /// Reads the text in a picture into a .txt file (Arabic and English where the phone supports them).
    static func recognizeText(_ url: URL) throws -> URL {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        let supported = (try? request.supportedRecognitionLanguages()) ?? []
        let wanted = ["ar-SA", "ar", "en-US"].filter { supported.contains($0) }
        if !wanted.isEmpty { request.recognitionLanguages = wanted }
        request.automaticallyDetectsLanguage = true
        try VNImageRequestHandler(cgImage: try upright(url)).perform([request])
        let lines = (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }
        guard !lines.isEmpty else { throw OpError("No text was found", "ما لكيت نص بالصورة") }
        let output = Outputs.path(for: url, ext: "txt", suffix: "text")
        try lines.joined(separator: "\n").write(to: output, atomically: true, encoding: .utf8)
        UIPasteboard.general.string = lines.joined(separator: "\n")
        return output
    }
}
