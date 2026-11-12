import UIKit
import PDFKit

enum PdfOps {
    static func merge(_ files: [URL]) throws -> URL {
        let merged = PDFDocument()
        for file in files {
            guard let document = PDFDocument(url: file) else { throw OpError("A PDF can't be opened", "ما أگدر أفتح ملف PDF") }
            for index in 0..<document.pageCount {
                if let page = document.page(at: index) { merged.insert(page, at: merged.pageCount) }
            }
        }
        let output = Outputs.path(for: files[0], ext: "pdf", suffix: "merged")
        guard merged.write(to: output) else { throw OpError("Saving failed", "فشل الحفظ") }
        return output
    }

    static func pagesToImages(_ file: URL) throws -> [URL] {
        guard let document = PDFDocument(url: file) else { throw OpError("This PDF can't be opened", "ما أگدر أفتح هذا الـ PDF") }
        var outputs: [URL] = []
        for index in 0..<document.pageCount {
            guard let page = document.page(at: index) else { continue }
            let bounds = page.bounds(for: .mediaBox)
            let image = page.thumbnail(of: CGSize(width: bounds.width * 2, height: bounds.height * 2), for: .mediaBox)
            guard let data = image.pngData() else { continue }
            let output = Outputs.path(for: file, ext: "png", suffix: "page \(index + 1)")
            try data.write(to: output)
            outputs.append(output)
        }
        return outputs
    }

    static func text(_ file: URL) throws -> URL {
        guard let document = PDFDocument(url: file) else { throw OpError("This PDF can't be opened", "ما أگدر أفتح هذا الـ PDF") }
        let output = Outputs.path(for: file, ext: "txt", suffix: "")
        try (document.string ?? "").write(to: output, atomically: true, encoding: .utf8)
        return output
    }

    /// One page per picture, each page the picture's own shape at 72 points per 2 pixels.
    static func fromImages(_ files: [URL]) throws -> URL {
        let images = try files.map { UIImage(cgImage: try ImageOps.upright($0)) }
        let output = Outputs.path(for: files[0], ext: "pdf", suffix: "")
        let first = images.first?.size ?? CGSize(width: 595, height: 842)
        let renderer = UIGraphicsPDFRenderer(bounds: CGRect(origin: .zero, size: first))
        try renderer.writePDF(to: output) { context in
            for image in images {
                let scale = min(1, 1200 / max(image.size.width, image.size.height))
                let rect = CGRect(origin: .zero, size: CGSize(width: image.size.width * scale, height: image.size.height * scale))
                context.beginPage(withBounds: rect, pageInfo: [:])
                image.draw(in: rect)
            }
        }
        return output
    }
}
