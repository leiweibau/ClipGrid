import AppKit
import Foundation
import SwiftUI
import UniformTypeIdentifiers

@MainActor
final class AppViewModel: ObservableObject {
    private static let maxConcurrentImports = 4
    private static let supportedVideoExtensions = [
        "mp4", "mov", "m4v", "avi", "mkv", "webm"
    ]

    @Published var videos: [VideoItem] = []
    @Published var selectedVideoID: UUID?
    @Published var isExporting = false
    @Published var isRendering = false
    @Published var isImporting = false
    @Published var importCompletedCount = 0
    @Published var importTotalCount = 0
    @Published var isShowingSettings = false
    @Published var isCheckingForUpdates = false
    @Published var lastError: String?

    let settings = AppSettings()
    private var cachedPlaceholderPreviewKey: String?
    private var cachedPlaceholderPreviewImage: NSImage?

    private let fileSizeFormatter: ByteCountFormatter = {
        let formatter = ByteCountFormatter()
        formatter.countStyle = .file
        return formatter
    }()

    private let durationFormatter: DateComponentsFormatter = {
        let formatter = DateComponentsFormatter()
        formatter.allowedUnits = [.hour, .minute, .second]
        formatter.unitsStyle = .positional
        formatter.zeroFormattingBehavior = [.pad]
        return formatter
    }()

    let supportedDropTypeIdentifiers: [String] = {
        var identifiers = [
            UTType.fileURL.identifier,
            UTType.movie.identifier,
            UTType.mpeg4Movie.identifier,
            UTType.quickTimeMovie.identifier
        ]

        identifiers.append(
            contentsOf: supportedVideoExtensions.compactMap { UTType(filenameExtension: $0)?.identifier }
        )

        return Array(Set(identifiers))
    }()

    var selectedVideo: VideoItem? {
        videos.first(where: { $0.id == selectedVideoID }) ?? videos.first
    }

    var importProgress: Double {
        guard importTotalCount > 0 else { return 0 }
        return Double(importCompletedCount) / Double(importTotalCount)
    }

    func chooseVideos() {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.allowedContentTypes = supportedVideoContentTypes
        panel.allowedFileTypes = Self.supportedVideoExtensions

        guard panel.runModal() == .OK else { return }
        Task {
            await addVideos(from: panel.urls)
        }
    }

    func removeSelected() {
        guard let selectedVideoID,
              let selectedIndex = videos.firstIndex(where: { $0.id == selectedVideoID }) else { return }

        videos.remove(at: selectedIndex)

        if videos.indices.contains(selectedIndex) {
            self.selectedVideoID = videos[selectedIndex].id
        } else {
            self.selectedVideoID = videos.last?.id
        }
    }

    func clearAll() {
        videos.removeAll()
        selectedVideoID = nil
    }

    func addVideos(from urls: [URL]) async {
        let existingURLs = Set(videos.map(\.url))
        let newURLs = urls.filter { !existingURLs.contains($0) }

        guard !newURLs.isEmpty else { return }

        isImporting = true
        importCompletedCount = 0
        importTotalCount = newURLs.count
        await Task.yield()
        defer {
            isImporting = false
            importCompletedCount = 0
            importTotalCount = 0
        }

        var loadedItems: [(Int, Result<VideoMetadata, Error>)] = []
        loadedItems.reserveCapacity(newURLs.count)

        var startIndex = 0
        while startIndex < newURLs.count {
            let endIndex = min(startIndex + Self.maxConcurrentImports, newURLs.count)
            let batch = Array(newURLs[startIndex..<endIndex].enumerated()).map { offset, url in
                (index: startIndex + offset, url: url)
            }

            let batchResults = await withTaskGroup(of: (Int, Result<VideoMetadata, Error>).self) { group in
                for entry in batch {
                    group.addTask {
                        do {
                            let metadata = try await VideoProcessingService.loadMetadata(for: entry.url)
                            return (entry.index, .success(metadata))
                        } catch {
                            return (entry.index, .failure(error))
                        }
                    }
                }

                var results: [(Int, Result<VideoMetadata, Error>)] = []
                for await result in group {
                    results.append(result)
                    await MainActor.run {
                        self.importCompletedCount += 1
                    }
                }
                return results
            }

            loadedItems.append(contentsOf: batchResults)
            startIndex = endIndex
        }

        loadedItems.sort { $0.0 < $1.0 }

        for (index, result) in loadedItems {
            switch result {
            case .success(let metadata):
                let item = VideoItem(
                    url: newURLs[index],
                    duration: 0,
                    fileSize: metadata.fileSize
                )
                videos.append(item)
                selectedVideoID = selectedVideoID ?? item.id
            case .failure(let error):
                let fileName = newURLs[index].lastPathComponent
                lastError = AppStrings.fileError(fileName, error.localizedDescription)
            }
        }
    }

    func handleDrop(providers: [NSItemProvider]) -> Bool {
        let matchingProviders = providers.filter { provider in
            supportedDropTypeIdentifiers.contains { provider.hasItemConformingToTypeIdentifier($0) }
        }

        guard !matchingProviders.isEmpty else {
            return false
        }

        Task {
            let droppedURLs = await loadDroppedURLs(from: matchingProviders)
            let videoURLs = droppedURLs.filter(Self.isSupportedVideoURL(_:))
            guard !videoURLs.isEmpty else { return }
            await addVideos(from: videoURLs)
        }

        return true
    }

    func startRendering() async {
        guard !videos.isEmpty else { return }
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.prompt = AppStrings.startPrompt

        guard panel.runModal() == .OK, let directory = panel.url else { return }
        await renderAndExportAll(to: directory)
    }

    func checkForUpdates() async {
        guard !isCheckingForUpdates else { return }
        isCheckingForUpdates = true
        defer { isCheckingForUpdates = false }

        do {
            let release = try await UpdateService.fetchLatestRelease()
            let localVersion = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"

            if UpdateService.isRemoteVersionNewer(remoteTag: release.tagName, localVersion: localVersion) {
                showUpdateAvailableDialog(release: release, localVersion: localVersion)
            } else {
                showUpToDateDialog(localVersion: localVersion)
            }
        } catch {
            showUpdateErrorDialog(error: error)
        }
    }

    func invalidatePreviews() {
        cachedPlaceholderPreviewKey = nil
        cachedPlaceholderPreviewImage = nil

        for item in videos {
            item.previewImage = nil
            if case .exporting = item.status {
                item.status = .idle
            } else if case .exported = item.status {
                item.status = .idle
            } else if case .ready = item.status {
                item.status = .idle
            } else if case .failed = item.status {
                item.status = .idle
            }
        }
    }

    func formattedDuration(for item: VideoItem) -> String {
        durationFormatter.string(from: item.duration) ?? "00:00"
    }

    func formattedSize(for item: VideoItem) -> String {
        fileSizeFormatter.string(fromByteCount: item.fileSize)
    }

    func formattedResolution(for item: VideoItem) -> String {
        let width = Int(item.resolution.width.rounded())
        let height = Int(item.resolution.height.rounded())
        guard width > 0, height > 0 else { return AppStrings.unknownResolution }
        return "\(width) × \(height) px"
    }

    func backgroundColorBinding() -> Binding<Color> {
        Binding(
            get: { self.settings.backgroundColor },
            set: { newValue in
                self.settings.updateBackgroundColor(NSColor(newValue))
            }
        )
    }

    func metadataTextColorBinding() -> Binding<Color> {
        Binding(
            get: { self.settings.metadataTextColor },
            set: { newValue in
                self.settings.updateMetadataTextColor(NSColor(newValue))
            }
        )
    }

    func previewImage(for item: VideoItem?) -> NSImage {
        if let item, let image = item.previewImage {
            return image
        }

        let cacheKey = placeholderPreviewCacheKey(for: item)
        if cacheKey == cachedPlaceholderPreviewKey, let cachedPlaceholderPreviewImage {
            return cachedPlaceholderPreviewImage
        }

        let image = ContactSheetRenderer.renderPlaceholder(
            title: item?.fileName ?? AppStrings.previewTitle,
            durationText: item.map(formattedDuration(for:)) ?? "00:00",
            resolutionText: item.map(formattedResolution(for:)) ?? "0 × 0 px",
            fileSizeText: item.map(formattedSize(for:)) ?? "0 KB",
            options: renderOptions(for: item)
        )
        cachedPlaceholderPreviewKey = cacheKey
        cachedPlaceholderPreviewImage = image
        return image
    }

    private func generatePreview(for item: VideoItem) async {
        item.status = .generating
        do {
            if item.resolution == .zero || item.duration == 0 {
                let renderMetadata = try await VideoProcessingService.loadRenderMetadata(for: item.url)
                item.duration = renderMetadata.duration
                item.resolution = renderMetadata.resolution
            }

            let thumbnailSize = settings.resolvedThumbnailSize(for: item.resolution)
            let thumbnails = try await VideoProcessingService.generateThumbnails(
                for: item.url,
                count: settings.columns * settings.rows,
                maxSize: thumbnailSize
            )

            let image = ContactSheetRenderer.render(
                title: item.fileName,
                durationText: formattedDuration(for: item),
                resolutionText: formattedResolution(for: item),
                fileSizeText: formattedSize(for: item),
                thumbnails: thumbnails,
                options: renderOptions(for: item)
            )

            item.previewImage = image
            item.status = .ready
        } catch {
            item.status = .failed(error.localizedDescription)
        }
    }

    private func renderAndExportAll(to directory: URL) async {
        isRendering = true
        isExporting = true
        defer {
            isRendering = false
            isExporting = false
        }

        let configuration = RenderConfiguration(settings: settings)
        let inputs = videos.map {
            RenderJobInput(
                id: $0.id,
                url: $0.url,
                fileName: $0.fileName,
                duration: $0.duration,
                fileSize: $0.fileSize,
                resolution: $0.resolution
            )
        }

        var startIndex = 0
        while startIndex < inputs.count {
            let endIndex = min(startIndex + max(settings.renderConcurrency, 1), inputs.count)
            let batch = Array(inputs[startIndex..<endIndex])

            for input in batch {
                if let item = videos.first(where: { $0.id == input.id }) {
                    item.status = .generating
                }
            }

            let results = await withTaskGroup(of: RenderJobResult.self) { group in
                for input in batch {
                    group.addTask {
                        await Self.processRenderJob(input: input, directory: directory, configuration: configuration)
                    }
                }

                var completed: [RenderJobResult] = []
                for await result in group {
                    completed.append(result)
                }
                return completed
            }

            for result in results {
                guard let item = videos.first(where: { $0.id == result.id }) else { continue }

                switch result.outcome {
                case .success(let image, let duration, let resolution, let targetURL):
                    item.duration = duration
                    item.resolution = resolution
                    item.previewImage = image
                    item.status = .exported(targetURL)
                case .failure(let message):
                    item.status = .failed(message)
                }
            }

            startIndex = endIndex
        }
    }

    private func placeholderPreviewCacheKey(for item: VideoItem?) -> String {
        let itemKey = item.map {
            [
                $0.id.uuidString,
                $0.fileName,
                formattedDuration(for: $0),
                formattedResolution(for: $0),
                formattedSize(for: $0)
            ].joined(separator: "|")
        } ?? "preview"

        return "\(itemKey)#\(settings.renderKey)#\(appearanceCacheKey)"
    }

    private var appearanceCacheKey: String {
        guard let bestMatch = NSApp.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) else {
            return "light"
        }
        return bestMatch == .darkAqua ? "dark" : "light"
    }

    private func showUpdateAvailableDialog(release: ReleaseInfo, localVersion: String) {
        let alert = NSAlert()
        alert.alertStyle = .informational
        alert.messageText = AppStrings.updateAvailableTitle

        let notes = release.notes.trimmingCharacters(in: .whitespacesAndNewlines)
        let notesText = notes.isEmpty ? AppStrings.updateNoReleaseNotes : notes
        alert.informativeText = AppStrings.updateAvailableMessage(
            localVersion,
            release.tagName,
            release.name,
            notesText
        )

        alert.addButton(withTitle: AppStrings.updateOpenReleasePage)
        alert.addButton(withTitle: AppStrings.updateLater)
        let result = alert.runModal()
        if result == .alertFirstButtonReturn {
            NSWorkspace.shared.open(release.url)
        }
    }

    private func showUpToDateDialog(localVersion: String) {
        let alert = NSAlert()
        alert.alertStyle = .informational
        alert.messageText = AppStrings.updateUpToDateTitle
        alert.informativeText = AppStrings.updateUpToDateMessage(localVersion)
        alert.addButton(withTitle: AppStrings.ok)
        alert.runModal()
    }

    private func showUpdateErrorDialog(error: Error) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = AppStrings.updateErrorTitle
        alert.informativeText = AppStrings.updateErrorMessage(error.localizedDescription)
        alert.addButton(withTitle: AppStrings.ok)
        alert.runModal()
    }

    private func renderOptions(for item: VideoItem?) -> ContactSheetRenderOptions {
        ContactSheetRenderOptions(
            columns: settings.columns,
            rows: settings.rows,
            spacing: settings.resolvedThumbnailSpacing,
            thumbnailSize: settings.resolvedThumbnailSize(for: item?.resolution ?? .zero),
            backgroundColor: settings.backgroundNSColor,
            metadataTextColor: settings.metadataTextNSColor,
            fileNameFontSize: settings.resolvedFileNameFontSize,
            durationFontSize: settings.resolvedDurationFontSize,
            fileSizeFontSize: settings.resolvedFileSizeFontSize,
            resolutionFontSize: settings.resolvedResolutionFontSize,
            timestampFontSize: settings.resolvedTimestampFontSize,
            metadataVisibility: ContactSheetMetadataVisibility(
                showFileName: settings.showFileName,
                showDuration: settings.showDuration,
                showFileSize: settings.showFileSize,
                showResolution: settings.showResolution,
                showTimestamp: settings.showTimestamp
            )
        )
    }

    private func outputFileName(for item: VideoItem) -> String {
        let stem = URL(fileURLWithPath: item.fileName).deletingPathExtension().lastPathComponent
        return "\(stem).\(settings.exportFormat.fileExtension)"
    }

    private func write(image: NSImage, to url: URL, format: ExportFormat) throws {
        switch format {
        case .jpg:
            guard let data = image.jpegData(compressionFactor: 0.92) else {
                throw CocoaError(.fileWriteUnknown)
            }
            try data.write(to: url)
        case .png:
            guard let data = image.pngData() else {
                throw CocoaError(.fileWriteUnknown)
            }
            try data.write(to: url)
        }
    }

    private func loadDroppedURLs(from providers: [NSItemProvider]) async -> [URL] {
        var urls: [URL] = []
        for provider in providers {
            if let url = try? await provider.loadVideoFileURL() {
                urls.append(url)
            }
        }
        return urls
    }

    private static func isSupportedVideoURL(_ url: URL) -> Bool {
        let fileExtension = url.pathExtension.lowercased()
        if supportedVideoExtensions.contains(fileExtension) {
            return true
        }

        guard let type = UTType(filenameExtension: url.pathExtension) else {
            return false
        }
        return type.conforms(to: .movie) || type.conforms(to: .video)
    }

    private var supportedVideoContentTypes: [UTType] {
        var contentTypes: [UTType] = [.movie, .mpeg4Movie, .quickTimeMovie]
        contentTypes.append(contentsOf: Self.supportedVideoExtensions.compactMap { UTType(filenameExtension: $0) })
        return Array(Set(contentTypes))
    }

    private static func processRenderJob(
        input: RenderJobInput,
        directory: URL,
        configuration: RenderConfiguration
    ) async -> RenderJobResult {
        do {
            var duration = input.duration
            var resolution = input.resolution

            if resolution == .zero || duration == 0 {
                let renderMetadata = try await VideoProcessingService.loadRenderMetadata(for: input.url)
                duration = renderMetadata.duration
                resolution = renderMetadata.resolution
            }

            let thumbnailSize = configuration.resolvedThumbnailSize(for: resolution)
            let thumbnails = try await VideoProcessingService.generateThumbnails(
                for: input.url,
                count: configuration.columns * configuration.rows,
                maxSize: thumbnailSize
            )

            let image = ContactSheetRenderer.render(
                title: input.fileName,
                durationText: formatDuration(duration),
                resolutionText: formatResolution(resolution),
                fileSizeText: formatFileSize(input.fileSize),
                thumbnails: thumbnails,
                options: configuration.renderOptions(for: resolution)
            )

            let targetURL = directory.appendingPathComponent(outputFileName(fileName: input.fileName, exportFormat: configuration.exportFormat))
            try writeImage(image, to: targetURL, format: configuration.exportFormat)

            if configuration.exportSeparateThumbnails {
                let fullResolutionThumbnails = try await VideoProcessingService.generateThumbnails(
                    for: input.url,
                    count: configuration.columns * configuration.rows,
                    maxSize: resolution
                )
                try exportSeparateThumbnails(
                    fullResolutionThumbnails,
                    for: input.fileName,
                    in: directory,
                    format: configuration.exportFormat
                )
            }

            return RenderJobResult(
                id: input.id,
                outcome: .success(
                    image: image,
                    duration: duration,
                    resolution: resolution,
                    targetURL: targetURL
                )
            )
        } catch {
            return RenderJobResult(id: input.id, outcome: .failure(error.localizedDescription))
        }
    }

    private static func outputFileName(fileName: String, exportFormat: ExportFormat) -> String {
        let stem = URL(fileURLWithPath: fileName).deletingPathExtension().lastPathComponent
        return "\(stem).\(exportFormat.fileExtension)"
    }

    private static func exportSeparateThumbnails(
        _ thumbnails: [ThumbnailFrame],
        for fileName: String,
        in directory: URL,
        format: ExportFormat
    ) throws {
        let stem = URL(fileURLWithPath: fileName).deletingPathExtension().lastPathComponent
        let targetFolder = directory.appendingPathComponent(stem, isDirectory: true)
        try FileManager.default.createDirectory(at: targetFolder, withIntermediateDirectories: true)

        for (index, thumbnail) in thumbnails.enumerated() {
            let timePart = timestampForFileName(thumbnail.timestamp)
            let itemName = String(format: "%03d_%@.%@", index + 1, timePart, format.fileExtension)
            let targetURL = targetFolder.appendingPathComponent(itemName)
            try writeImage(thumbnail.image, to: targetURL, format: format)
        }
    }

    private static func timestampForFileName(_ seconds: TimeInterval) -> String {
        let totalMilliseconds = max(Int((seconds * 1000).rounded()), 0)
        let hours = totalMilliseconds / 3_600_000
        let minutes = (totalMilliseconds % 3_600_000) / 60_000
        let secs = (totalMilliseconds % 60_000) / 1000
        let millis = totalMilliseconds % 1000
        return String(format: "%02d-%02d-%02d_%03d", hours, minutes, secs, millis)
    }

    private static func writeImage(_ image: NSImage, to url: URL, format: ExportFormat) throws {
        switch format {
        case .jpg:
            guard let data = image.jpegData(compressionFactor: 0.92) else {
                throw CocoaError(.fileWriteUnknown)
            }
            try data.write(to: url)
        case .png:
            guard let data = image.pngData() else {
                throw CocoaError(.fileWriteUnknown)
            }
            try data.write(to: url)
        }
    }

    private static func formatDuration(_ duration: TimeInterval) -> String {
        let formatter = DateComponentsFormatter()
        formatter.allowedUnits = [.hour, .minute, .second]
        formatter.unitsStyle = .positional
        formatter.zeroFormattingBehavior = [.pad]
        return formatter.string(from: duration) ?? "00:00"
    }

    private static func formatFileSize(_ fileSize: Int64) -> String {
        let formatter = ByteCountFormatter()
        formatter.countStyle = .file
        return formatter.string(fromByteCount: fileSize)
    }

    private static func formatResolution(_ resolution: CGSize) -> String {
        let width = Int(resolution.width.rounded())
        let height = Int(resolution.height.rounded())
        guard width > 0, height > 0 else { return AppStrings.unknownResolution }
        return "\(width) × \(height) px"
    }
}

private struct RenderJobInput: Sendable {
    let id: UUID
    let url: URL
    let fileName: String
    let duration: TimeInterval
    let fileSize: Int64
    let resolution: CGSize
}

private struct RenderJobResult {
    enum Outcome {
        case success(image: NSImage, duration: TimeInterval, resolution: CGSize, targetURL: URL)
        case failure(String)
    }

    let id: UUID
    let outcome: Outcome
}

private struct RenderConfiguration: Sendable {
    private static let defaultThumbnailWidth: CGFloat = 320
    private static let defaultThumbnailHeight: CGFloat = 180

    let columns: Int
    let rows: Int
    let spacing: CGFloat
    let thumbnailWidthText: String
    let thumbnailHeightText: String
    let backgroundColorComponents: (Double, Double, Double)
    let metadataTextColorComponents: (Double, Double, Double)
    let fileNameFontSize: CGFloat
    let durationFontSize: CGFloat
    let fileSizeFontSize: CGFloat
    let resolutionFontSize: CGFloat
    let timestampFontSize: CGFloat
    let exportFormat: ExportFormat
    let exportSeparateThumbnails: Bool
    let metadataVisibility: ContactSheetMetadataVisibility

    @MainActor
    init(settings: AppSettings) {
        columns = settings.columns
        rows = settings.rows
        spacing = settings.resolvedThumbnailSpacing
        thumbnailWidthText = settings.thumbnailWidthText
        thumbnailHeightText = settings.thumbnailHeightText
        backgroundColorComponents = (settings.backgroundRed, settings.backgroundGreen, settings.backgroundBlue)
        metadataTextColorComponents = (settings.metadataTextRed, settings.metadataTextGreen, settings.metadataTextBlue)
        fileNameFontSize = settings.resolvedFileNameFontSize
        durationFontSize = settings.resolvedDurationFontSize
        fileSizeFontSize = settings.resolvedFileSizeFontSize
        resolutionFontSize = settings.resolvedResolutionFontSize
        timestampFontSize = settings.resolvedTimestampFontSize
        exportFormat = settings.exportFormat
        exportSeparateThumbnails = settings.exportSeparateThumbnails
        metadataVisibility = ContactSheetMetadataVisibility(
            showFileName: settings.showFileName,
            showDuration: settings.showDuration,
            showFileSize: settings.showFileSize,
            showResolution: settings.showResolution,
            showTimestamp: settings.showTimestamp
        )
    }

    func renderOptions(for resolution: CGSize) -> ContactSheetRenderOptions {
        ContactSheetRenderOptions(
            columns: columns,
            rows: rows,
            spacing: spacing,
            thumbnailSize: resolvedThumbnailSize(for: resolution),
            backgroundColor: NSColor(
                calibratedRed: backgroundColorComponents.0,
                green: backgroundColorComponents.1,
                blue: backgroundColorComponents.2,
                alpha: 1
            ),
            metadataTextColor: NSColor(
                calibratedRed: metadataTextColorComponents.0,
                green: metadataTextColorComponents.1,
                blue: metadataTextColorComponents.2,
                alpha: 1
            ),
            fileNameFontSize: fileNameFontSize,
            durationFontSize: durationFontSize,
            fileSizeFontSize: fileSizeFontSize,
            resolutionFontSize: resolutionFontSize,
            timestampFontSize: timestampFontSize,
            metadataVisibility: metadataVisibility
        )
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

private extension NSImage {
    func bitmapRepresentation() -> NSBitmapImageRep? {
        guard let tiffRepresentation else { return nil }
        return NSBitmapImageRep(data: tiffRepresentation)
    }

    func pngData() -> Data? {
        bitmapRepresentation()?.representation(using: .png, properties: [:])
    }

    func jpegData(compressionFactor: CGFloat) -> Data? {
        bitmapRepresentation()?.representation(using: .jpeg, properties: [.compressionFactor: compressionFactor])
    }
}

private extension NSItemProvider {
    @MainActor
    func loadVideoFileURL() async throws -> URL? {
        if canLoadObject(ofClass: NSURL.self) {
            return try await loadURLObject()
        }

        if hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) {
            return try await loadSimpleFileURL()
        }

        for typeIdentifier in preferredVideoTypeIdentifiers {
            if let url = try await loadInPlaceVideoURL(forTypeIdentifier: typeIdentifier) {
                return url
            }
        }

        return nil
    }

    private var preferredVideoTypeIdentifiers: [String] {
        let knownTypes = [
            UTType.movie.identifier,
            UTType.mpeg4Movie.identifier,
            UTType.quickTimeMovie.identifier,
            UTType.video.identifier
        ]
        return knownTypes.filter { hasItemConformingToTypeIdentifier($0) }
    }

    @MainActor
    func loadSimpleFileURL() async throws -> URL? {
        return try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<URL?, Error>) in
            loadItem(forTypeIdentifier: UTType.fileURL.identifier, options: nil) { item, error in
                if let error {
                    continuation.resume(throwing: error)
                    return
                }

                switch item {
                case let url as URL:
                    continuation.resume(returning: url)
                case let data as Data:
                    continuation.resume(returning: URL(dataRepresentation: data, relativeTo: nil))
                case let string as String:
                    continuation.resume(returning: URL(string: string))
                case let string as NSString:
                    continuation.resume(returning: URL(string: string as String))
                default:
                    continuation.resume(returning: nil)
                }
            }
        }
    }

    @MainActor
    func loadURLObject() async throws -> URL? {
        return try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<URL?, Error>) in
            loadObject(ofClass: NSURL.self) { object, error in
                if let error {
                    continuation.resume(throwing: error)
                    return
                }

                continuation.resume(returning: object as? URL)
            }
        }
    }

    @MainActor
    func loadInPlaceVideoURL(forTypeIdentifier typeIdentifier: String) async throws -> URL? {
        return try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<URL?, Error>) in
            loadInPlaceFileRepresentation(forTypeIdentifier: typeIdentifier) { url, _, error in
                if let error {
                    continuation.resume(throwing: error)
                    return
                }

                continuation.resume(returning: url)
            }
        }
    }
}
