import CoreImage
import CoreImage.CIFilterBuiltins
import Foundation

/// One slider in the Edit Photo window.
struct Adjustment: Identifiable, Hashable {
    enum Key: String, CaseIterable {
        case exposure, brightness, contrast, highlights, shadows, whites, blacks, gamma
        case temperature, tint, saturation, vibrance, hue
        case clarity, dehaze, sharpness, noiseReduction
        case vignette, grain, fade
    }

    let key: Key
    let title: String
    let range: ClosedRange<Double>
    let unit: String

    var id: Key { key }

    static let sections: [(title: String, items: [Adjustment])] = [
        ("Light", [
            Adjustment(key: .exposure, title: L10n.t("Exposure"), range: -3...3, unit: " EV"),
            Adjustment(key: .brightness, title: L10n.t("Brightness"), range: -100...100, unit: ""),
            Adjustment(key: .contrast, title: L10n.t("Contrast"), range: -100...100, unit: ""),
            Adjustment(key: .highlights, title: L10n.t("Highlights"), range: -100...100, unit: ""),
            Adjustment(key: .shadows, title: L10n.t("Shadows"), range: -100...100, unit: ""),
            Adjustment(key: .whites, title: L10n.t("Whites"), range: -100...100, unit: ""),
            Adjustment(key: .blacks, title: L10n.t("Blacks"), range: -100...100, unit: ""),
            Adjustment(key: .gamma, title: L10n.t("Gamma"), range: -100...100, unit: ""),
        ]),
        ("Color", [
            Adjustment(key: .temperature, title: L10n.t("Temperature"), range: -100...100, unit: ""),
            Adjustment(key: .tint, title: L10n.t("Tint"), range: -100...100, unit: ""),
            Adjustment(key: .saturation, title: L10n.t("Saturation"), range: -100...100, unit: ""),
            Adjustment(key: .vibrance, title: L10n.t("Vibrance"), range: -100...100, unit: ""),
            Adjustment(key: .hue, title: L10n.t("Hue"), range: -180...180, unit: "°"),
        ]),
        ("Detail", [
            Adjustment(key: .clarity, title: L10n.t("Clarity"), range: -100...100, unit: ""),
            Adjustment(key: .dehaze, title: L10n.t("Dehaze"), range: -100...100, unit: ""),
            Adjustment(key: .sharpness, title: L10n.t("Sharpness"), range: 0...100, unit: ""),
            Adjustment(key: .noiseReduction, title: L10n.t("Noise Reduction"), range: 0...100, unit: ""),
        ]),
        ("Effects", [
            Adjustment(key: .vignette, title: L10n.t("Vignette"), range: -100...100, unit: ""),
            Adjustment(key: .grain, title: L10n.t("Grain"), range: 0...100, unit: ""),
            Adjustment(key: .fade, title: L10n.t("Fade"), range: 0...100, unit: ""),
        ]),
    ]
}

/// Slider values keyed by adjustment; missing keys are at their neutral 0.
struct PhotoAdjustments: Equatable {
    var values: [Adjustment.Key: Double] = [:]

    subscript(_ key: Adjustment.Key) -> Double {
        get { values[key] ?? 0 }
        set { values[key] = abs(newValue) < 0.0001 ? nil : newValue }
    }

    var isNeutral: Bool { values.isEmpty }

    /// The Core Image chain for these values. `scale` is the preview's size relative to the full image,
    /// so radius-based effects look the same in the preview as in the saved photo.
    func apply(to input: CIImage, scale: Double = 1) -> CIImage {
        guard !isNeutral else { return input }
        let extent = input.extent
        var image = input

        if self[.exposure] != 0 {
            let filter = CIFilter.exposureAdjust()
            filter.inputImage = image
            filter.ev = Float(self[.exposure])
            image = filter.outputImage ?? image
        }
        if self[.highlights] != 0 || self[.shadows] != 0 {
            let filter = CIFilter.highlightShadowAdjust()
            filter.inputImage = image
            filter.highlightAmount = Float(1 - max(0, -self[.highlights]) / 100 + max(0, self[.highlights]) / 300)
            filter.shadowAmount = Float(self[.shadows] / 100)
            image = filter.outputImage ?? image
        }
        if self[.whites] != 0 || self[.blacks] != 0 || self[.fade] != 0 {
            // Move the curve's end points: blacks lift or crush the floor, whites the ceiling, fade lifts the floor.
            let blacks = self[.blacks] / 100 * 0.15 + self[.fade] / 100 * 0.18
            let whites = self[.whites] / 100 * 0.15
            let filter = CIFilter.toneCurve()
            filter.inputImage = image
            filter.point0 = blacks >= 0 ? CGPoint(x: 0, y: blacks) : CGPoint(x: -blacks, y: 0)
            filter.point1 = CGPoint(x: 0.25, y: 0.25 + blacks * 0.5)
            filter.point2 = CGPoint(x: 0.5, y: 0.5 + (blacks + whites) * 0.25)
            filter.point3 = CGPoint(x: 0.75, y: 0.75 + whites * 0.5)
            filter.point4 = whites <= 0 ? CGPoint(x: 1, y: 1 + whites) : CGPoint(x: 1 - whites, y: 1)
            image = filter.outputImage ?? image
        }
        if self[.gamma] != 0 {
            let filter = CIFilter.gammaAdjust()
            filter.inputImage = image
            filter.power = Float(pow(2, -self[.gamma] / 100))
            image = filter.outputImage ?? image
        }
        let dehaze = self[.dehaze] / 100
        if self[.brightness] != 0 || self[.contrast] != 0 || self[.saturation] != 0 || dehaze != 0 {
            let filter = CIFilter.colorControls()
            filter.inputImage = image
            filter.brightness = Float(self[.brightness] / 100 * 0.25 - dehaze * 0.04)
            filter.contrast = Float(1 + self[.contrast] / 100 * 0.5 + dehaze * 0.25)
            filter.saturation = Float(1 + self[.saturation] / 100 + dehaze * 0.15)
            image = filter.outputImage ?? image
        }
        if self[.temperature] != 0 || self[.tint] != 0 {
            let filter = CIFilter.temperatureAndTint()
            filter.inputImage = image
            filter.neutral = CIVector(x: 6500, y: 0)
            filter.targetNeutral = CIVector(x: 6500 - self[.temperature] * 30, y: -self[.tint])
            image = filter.outputImage ?? image
        }
        if self[.vibrance] != 0 {
            let filter = CIFilter.vibrance()
            filter.inputImage = image
            filter.amount = Float(self[.vibrance] / 100)
            image = filter.outputImage ?? image
        }
        if self[.hue] != 0 {
            let filter = CIFilter.hueAdjust()
            filter.inputImage = image
            filter.angle = Float(self[.hue] * .pi / 180)
            image = filter.outputImage ?? image
        }
        if self[.clarity] != 0 {
            // Local contrast: an unsharp mask with a wide radius.
            let filter = CIFilter.unsharpMask()
            filter.inputImage = image.clampedToExtent()
            filter.radius = Float(max(4, min(extent.width, extent.height) * 0.02))
            filter.intensity = Float(self[.clarity] / 100 * 0.9)
            image = filter.outputImage?.cropped(to: extent) ?? image
        }
        if self[.noiseReduction] > 0 {
            let filter = CIFilter.noiseReduction()
            filter.inputImage = image
            filter.noiseLevel = Float(self[.noiseReduction] / 100 * 0.06)
            filter.sharpness = 0.4
            image = filter.outputImage ?? image
        }
        if self[.sharpness] > 0 {
            let filter = CIFilter.sharpenLuminance()
            filter.inputImage = image
            filter.sharpness = Float(self[.sharpness] / 100 * 1.5)
            filter.radius = Float(1.6 * scale)
            image = filter.outputImage ?? image
        }
        if self[.vignette] != 0 {
            let filter = CIFilter.vignette()
            filter.inputImage = image
            filter.intensity = Float(self[.vignette] / 100 * 1.5)
            filter.radius = Float(max(extent.width, extent.height) / 160)
            image = filter.outputImage ?? image
        }
        if self[.grain] > 0, let noise = CIFilter.randomGenerator().outputImage {
            // Monochrome noise around mid-gray, blended with soft light so it textures without tinting.
            let amount = self[.grain] / 100
            let mono = noise
                .transformed(by: CGAffineTransform(scaleX: max(0.5, scale * 1.4), y: max(0.5, scale * 1.4)))
                .applyingFilter("CIColorMatrix", parameters: [
                    "inputRVector": CIVector(x: 0.6 * amount, y: 0, z: 0, w: 0),
                    "inputGVector": CIVector(x: 0.6 * amount, y: 0, z: 0, w: 0),
                    "inputBVector": CIVector(x: 0.6 * amount, y: 0, z: 0, w: 0),
                    "inputAVector": CIVector(x: 0, y: 0, z: 0, w: 1),
                    "inputBiasVector": CIVector(x: 0.5 - 0.3 * amount, y: 0.5 - 0.3 * amount, z: 0.5 - 0.3 * amount, w: 0),
                ])
                .cropped(to: extent)
            let blend = CIFilter.softLightBlendMode()
            blend.inputImage = mono
            blend.backgroundImage = image
            image = blend.outputImage ?? image
        }
        return image.cropped(to: extent)
    }
}
