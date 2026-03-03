import AppKit
import Foundation
import SwiftUI

enum ExportFormat: String, CaseIterable, Identifiable {
    case jpg
    case png

    var id: String { rawValue }

    var fileExtension: String { rawValue }

    var displayName: String {
        switch self {
        case .jpg: AppStrings.exportJPEG
        case .png: AppStrings.exportPNG
        }
    }
}

@MainActor
final class AppSettings: ObservableObject {
    static let defaultThumbnailWidth: CGFloat = 320
    static let defaultThumbnailHeight: CGFloat = 180
    static let defaultFileNameFontSize: CGFloat = 26
    static let defaultDurationFontSize: CGFloat = 14
    static let defaultFileSizeFontSize: CGFloat = 14
    static let defaultResolutionFontSize: CGFloat = 14
    static let defaultTimestampFontSize: CGFloat = 12

    @Published var columns: Int { didSet { defaults.set(columns, forKey: Keys.columns) } }
    @Published var rows: Int { didSet { defaults.set(rows, forKey: Keys.rows) } }
    @Published var thumbnailSpacing: Double { didSet { defaults.set(thumbnailSpacing, forKey: Keys.thumbnailSpacing) } }
    @Published var thumbnailWidthText: String { didSet { defaults.set(thumbnailWidthText, forKey: Keys.thumbnailWidthText) } }
    @Published var thumbnailHeightText: String { didSet { defaults.set(thumbnailHeightText, forKey: Keys.thumbnailHeightText) } }
    @Published var backgroundRed: Double { didSet { defaults.set(backgroundRed, forKey: Keys.backgroundRed) } }
    @Published var backgroundGreen: Double { didSet { defaults.set(backgroundGreen, forKey: Keys.backgroundGreen) } }
    @Published var backgroundBlue: Double { didSet { defaults.set(backgroundBlue, forKey: Keys.backgroundBlue) } }
    @Published var metadataTextRed: Double { didSet { defaults.set(metadataTextRed, forKey: Keys.metadataTextRed) } }
    @Published var metadataTextGreen: Double { didSet { defaults.set(metadataTextGreen, forKey: Keys.metadataTextGreen) } }
    @Published var metadataTextBlue: Double { didSet { defaults.set(metadataTextBlue, forKey: Keys.metadataTextBlue) } }
    @Published var exportFormat: ExportFormat { didSet { defaults.set(exportFormat.rawValue, forKey: Keys.exportFormat) } }
    @Published var showFileName: Bool { didSet { defaults.set(showFileName, forKey: Keys.showFileName) } }
    @Published var showDuration: Bool { didSet { defaults.set(showDuration, forKey: Keys.showDuration) } }
    @Published var showFileSize: Bool { didSet { defaults.set(showFileSize, forKey: Keys.showFileSize) } }
    @Published var showResolution: Bool { didSet { defaults.set(showResolution, forKey: Keys.showResolution) } }
    @Published var showTimestamp: Bool { didSet { defaults.set(showTimestamp, forKey: Keys.showTimestamp) } }
    @Published var fileNameFontSizeText: String { didSet { defaults.set(fileNameFontSizeText, forKey: Keys.fileNameFontSizeText) } }
    @Published var durationFontSizeText: String { didSet { defaults.set(durationFontSizeText, forKey: Keys.durationFontSizeText) } }
    @Published var fileSizeFontSizeText: String { didSet { defaults.set(fileSizeFontSizeText, forKey: Keys.fileSizeFontSizeText) } }
    @Published var resolutionFontSizeText: String { didSet { defaults.set(resolutionFontSizeText, forKey: Keys.resolutionFontSizeText) } }
    @Published var timestampFontSizeText: String { didSet { defaults.set(timestampFontSizeText, forKey: Keys.timestampFontSizeText) } }

    private let defaults: UserDefaults

    private enum Keys {
        static let columns = "settings.columns"
        static let rows = "settings.rows"
        static let thumbnailSpacing = "settings.thumbnailSpacing"
        static let thumbnailWidthText = "settings.thumbnailWidthText"
        static let thumbnailHeightText = "settings.thumbnailHeightText"
        static let backgroundRed = "settings.backgroundRed"
        static let backgroundGreen = "settings.backgroundGreen"
        static let backgroundBlue = "settings.backgroundBlue"
        static let metadataTextRed = "settings.metadataTextRed"
        static let metadataTextGreen = "settings.metadataTextGreen"
        static let metadataTextBlue = "settings.metadataTextBlue"
        static let exportFormat = "settings.exportFormat"
        static let showFileName = "settings.showFileName"
        static let showDuration = "settings.showDuration"
        static let showFileSize = "settings.showFileSize"
        static let showResolution = "settings.showResolution"
        static let showTimestamp = "settings.showTimestamp"
        static let fileNameFontSizeText = "settings.fileNameFontSizeText"
        static let durationFontSizeText = "settings.durationFontSizeText"
        static let fileSizeFontSizeText = "settings.fileSizeFontSizeText"
        static let resolutionFontSizeText = "settings.resolutionFontSizeText"
        static let timestampFontSizeText = "settings.timestampFontSizeText"
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        self.columns = defaults.object(forKey: Keys.columns) as? Int ?? 4
        self.rows = defaults.object(forKey: Keys.rows) as? Int ?? 4
        self.thumbnailSpacing = defaults.object(forKey: Keys.thumbnailSpacing) as? Double ?? 16
        self.thumbnailWidthText = defaults.string(forKey: Keys.thumbnailWidthText) ?? "\(Int(Self.defaultThumbnailWidth))"
        self.thumbnailHeightText = defaults.string(forKey: Keys.thumbnailHeightText) ?? "\(Int(Self.defaultThumbnailHeight))"
        self.backgroundRed = defaults.object(forKey: Keys.backgroundRed) as? Double ?? 0.12
        self.backgroundGreen = defaults.object(forKey: Keys.backgroundGreen) as? Double ?? 0.13
        self.backgroundBlue = defaults.object(forKey: Keys.backgroundBlue) as? Double ?? 0.15
        self.metadataTextRed = defaults.object(forKey: Keys.metadataTextRed) as? Double ?? 1
        self.metadataTextGreen = defaults.object(forKey: Keys.metadataTextGreen) as? Double ?? 1
        self.metadataTextBlue = defaults.object(forKey: Keys.metadataTextBlue) as? Double ?? 1
        self.exportFormat = ExportFormat(rawValue: defaults.string(forKey: Keys.exportFormat) ?? "") ?? .jpg
        self.showFileName = defaults.object(forKey: Keys.showFileName) as? Bool ?? true
        self.showDuration = defaults.object(forKey: Keys.showDuration) as? Bool ?? true
        self.showFileSize = defaults.object(forKey: Keys.showFileSize) as? Bool ?? true
        self.showResolution = defaults.object(forKey: Keys.showResolution) as? Bool ?? true
        self.showTimestamp = defaults.object(forKey: Keys.showTimestamp) as? Bool ?? true
        self.fileNameFontSizeText = defaults.string(forKey: Keys.fileNameFontSizeText) ?? "\(Int(Self.defaultFileNameFontSize))"
        self.durationFontSizeText = defaults.string(forKey: Keys.durationFontSizeText) ?? "\(Int(Self.defaultDurationFontSize))"
        self.fileSizeFontSizeText = defaults.string(forKey: Keys.fileSizeFontSizeText) ?? "\(Int(Self.defaultFileSizeFontSize))"
        self.resolutionFontSizeText = defaults.string(forKey: Keys.resolutionFontSizeText) ?? "\(Int(Self.defaultResolutionFontSize))"
        self.timestampFontSizeText = defaults.string(forKey: Keys.timestampFontSizeText) ?? "\(Int(Self.defaultTimestampFontSize))"
    }

    var backgroundColor: Color {
        Color(nsColor: backgroundNSColor)
    }

    var backgroundNSColor: NSColor {
        NSColor(
            calibratedRed: backgroundRed,
            green: backgroundGreen,
            blue: backgroundBlue,
            alpha: 1
        )
    }

    var metadataTextColor: Color {
        Color(nsColor: metadataTextNSColor)
    }

    var metadataTextNSColor: NSColor {
        NSColor(
            calibratedRed: metadataTextRed,
            green: metadataTextGreen,
            blue: metadataTextBlue,
            alpha: 1
        )
    }

    var renderKey: String {
        [
            "\(columns)",
            "\(rows)",
            "\(Int(thumbnailSpacing))",
            thumbnailWidthText,
            thumbnailHeightText,
            "\(Int(backgroundRed * 1000))",
            "\(Int(backgroundGreen * 1000))",
            "\(Int(backgroundBlue * 1000))",
            "\(Int(metadataTextRed * 1000))",
            "\(Int(metadataTextGreen * 1000))",
            "\(Int(metadataTextBlue * 1000))",
            "\(exportFormat.rawValue.hashValue)",
            showFileName ? "1" : "0",
            showDuration ? "1" : "0",
            showFileSize ? "1" : "0",
            showResolution ? "1" : "0",
            showTimestamp ? "1" : "0",
            fileNameFontSizeText,
            durationFontSizeText,
            fileSizeFontSizeText,
            resolutionFontSizeText,
            timestampFontSizeText
        ].joined(separator: "-")
    }

    func updateBackgroundColor(_ color: NSColor) {
        guard let rgbColor = color.usingColorSpace(.deviceRGB) else { return }
        backgroundRed = Double(rgbColor.redComponent)
        backgroundGreen = Double(rgbColor.greenComponent)
        backgroundBlue = Double(rgbColor.blueComponent)
    }

    func updateMetadataTextColor(_ color: NSColor) {
        guard let rgbColor = color.usingColorSpace(.deviceRGB) else { return }
        metadataTextRed = Double(rgbColor.redComponent)
        metadataTextGreen = Double(rgbColor.greenComponent)
        metadataTextBlue = Double(rgbColor.blueComponent)
    }

    func resolvedThumbnailSize(for resolution: CGSize) -> CGSize {
        let width = parsedPositiveCGFloat(from: thumbnailWidthText)
        let height = parsedPositiveCGFloat(from: thumbnailHeightText)
        let aspectRatio = resolvedAspectRatio(for: resolution)

        switch (width, height) {
        case let (.some(width), .some(height)):
            return CGSize(width: width, height: height)
        case let (.some(width), nil):
            return CGSize(width: width, height: width / aspectRatio)
        case let (nil, .some(height)):
            return CGSize(width: height * aspectRatio, height: height)
        case (nil, nil):
            return CGSize(width: Self.defaultThumbnailWidth, height: Self.defaultThumbnailHeight)
        }
    }

    var resolvedFileNameFontSize: CGFloat {
        resolvedFontSize(from: fileNameFontSizeText, defaultValue: Self.defaultFileNameFontSize)
    }

    var resolvedDurationFontSize: CGFloat {
        resolvedFontSize(from: durationFontSizeText, defaultValue: Self.defaultDurationFontSize)
    }

    var resolvedFileSizeFontSize: CGFloat {
        resolvedFontSize(from: fileSizeFontSizeText, defaultValue: Self.defaultFileSizeFontSize)
    }

    var resolvedResolutionFontSize: CGFloat {
        resolvedFontSize(from: resolutionFontSizeText, defaultValue: Self.defaultResolutionFontSize)
    }

    var resolvedTimestampFontSize: CGFloat {
        resolvedFontSize(from: timestampFontSizeText, defaultValue: Self.defaultTimestampFontSize)
    }

    private func resolvedFontSize(from text: String, defaultValue: CGFloat) -> CGFloat {
        parsedPositiveCGFloat(from: text) ?? defaultValue
    }

    private func parsedPositiveCGFloat(from text: String) -> CGFloat? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, let value = Double(trimmed), value > 0 else { return nil }
        return CGFloat(value)
    }

    private func resolvedAspectRatio(for resolution: CGSize) -> CGFloat {
        guard resolution.width > 0, resolution.height > 0 else {
            return Self.defaultThumbnailWidth / Self.defaultThumbnailHeight
        }
        return resolution.width / resolution.height
    }
}
