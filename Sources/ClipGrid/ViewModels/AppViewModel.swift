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
    @Published var lastError: String?

    let settings = AppSettings()

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

    func invalidatePreviews() {
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

        return ContactSheetRenderer.renderPlaceholder(
            title: item?.fileName ?? AppStrings.previewTitle,
            durationText: item.map(formattedDuration(for:)) ?? "00:00",
            resolutionText: item.map(formattedResolution(for:)) ?? "0 × 0 px",
            fileSizeText: item.map(formattedSize(for:)) ?? "0 KB",
            options: renderOptions(for: item)
        )
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

        for item in videos {
            await generatePreview(for: item)
            if item.previewImage != nil {
                await export(item: item, to: directory)
            }
        }
    }

    private func export(item: VideoItem, to directory: URL) async {
        item.status = .exporting

        do {
            let image: NSImage
            if let previewImage = item.previewImage {
                image = previewImage
            } else {
                let thumbnails = try await VideoProcessingService.generateThumbnails(
                    for: item.url,
                    count: settings.columns * settings.rows,
                    maxSize: settings.resolvedThumbnailSize(for: item.resolution)
                )
                image = ContactSheetRenderer.render(
                    title: item.fileName,
                    durationText: formattedDuration(for: item),
                    resolutionText: formattedResolution(for: item),
                    fileSizeText: formattedSize(for: item),
                    thumbnails: thumbnails,
                    options: renderOptions(for: item)
                )
            }

            let targetURL = directory.appendingPathComponent(outputFileName(for: item))
            try write(image: image, to: targetURL, format: settings.exportFormat)
            item.status = .exported(targetURL)
        } catch {
            item.status = .failed(error.localizedDescription)
        }
    }

    private func renderOptions(for item: VideoItem?) -> ContactSheetRenderOptions {
        ContactSheetRenderOptions(
            columns: settings.columns,
            rows: settings.rows,
            spacing: settings.thumbnailSpacing,
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
