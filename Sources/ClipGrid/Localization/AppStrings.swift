import Foundation

enum AppStrings {
    static var exportJPEG: String { text("export.jpeg") }
    static var exportPNG: String { text("export.png") }

    static var statusReady: String { text("status.ready") }
    static var statusGenerating: String { text("status.generating") }
    static var statusReadyDone: String { text("status.preview_ready") }
    static var statusExporting: String { text("status.exporting") }
    static var statusExported: String { text("status.exported") }

    static var errorTitle: String { text("error.title") }
    static var ok: String { text("common.ok") }
    static var unreadableVideo: String { text("error.unreadable_video") }
    static var noThumbnails: String { text("error.no_thumbnails") }
    static func fileError(_ fileName: String, _ message: String) -> String {
        formatted("error.file", fileName, message)
    }

    static var unknownResolution: String { text("metadata.unknown_resolution") }
    static var previewTitle: String { text("preview.placeholder_title") }

    static var settingsTitle: String { text("settings.title") }
    static func columns(_ value: Int) -> String { formatted("settings.columns", value) }
    static func rows(_ value: Int) -> String { formatted("settings.rows", value) }
    static var thumbnailWidth: String { text("settings.thumbnail_width") }
    static var thumbnailHeight: String { text("settings.thumbnail_height") }
    static func spacing(_ value: Int) -> String { formatted("settings.spacing", value) }
    static var exportFormat: String { text("settings.export_format") }
    static var background: String { text("settings.background") }
    static var metadataTextColor: String { text("settings.metadata_text_color") }
    static var metadata: String { text("settings.metadata") }
    static var fontSizes: String { text("settings.font_sizes") }
    static var fileNameFontSize: String { text("settings.file_name_font_size") }
    static var durationFontSize: String { text("settings.duration_font_size") }
    static var fileSizeFontSize: String { text("settings.file_size_font_size") }
    static var resolutionFontSize: String { text("settings.resolution_font_size") }
    static var timestampFontSize: String { text("settings.timestamp_font_size") }
    static func pixelPlaceholder(_ value: Int) -> String { formatted("settings.pixel_placeholder", value) }
    static var auto: String { text("settings.auto") }
    static var showFileName: String { text("settings.show_file_name") }
    static var showDuration: String { text("settings.show_duration") }
    static var showFileSize: String { text("settings.show_file_size") }
    static var showResolution: String { text("settings.show_resolution") }
    static var showTimestamp: String { text("settings.show_timestamp") }
    static var close: String { text("common.close") }

    static var sidebarVideos: String { text("sidebar.videos") }
    static var importingVideos: String { text("sidebar.importing") }
    static var addVideosHelp: String { text("help.add_videos") }
    static var removeSelectionHelp: String { text("help.remove_selection") }
    static var settingsHelp: String { text("help.settings") }
    static var dropVideosHere: String { text("drop.videos_here") }
    static var clearAllHelp: String { text("help.clear_all") }
    static var startHelp: String { text("help.start") }
    static var generatingPreview: String { text("preview.generating") }
    static var startPrompt: String { text("action.start") }

    private static func text(_ key: String) -> String {
        Bundle.module.localizedString(forKey: key, value: nil, table: "Localizable")
    }

    private static func formatted(_ key: String, _ arguments: CVarArg...) -> String {
        String(format: text(key), locale: Locale.current, arguments: arguments)
    }
}
