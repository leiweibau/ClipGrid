import AVFoundation
import AppKit
import Darwin
import Foundation

struct VideoMetadata {
    let fileSize: Int64
}

struct VideoRenderMetadata {
    let duration: TimeInterval
    let resolution: CGSize
}

struct ThumbnailFrame {
    let image: NSImage
    let timestamp: TimeInterval
}

enum VideoProcessingError: LocalizedError {
    case unreadableVideo
    case noFramesGenerated

    var errorDescription: String? {
        switch self {
        case .unreadableVideo:
            return AppStrings.unreadableVideo
        case .noFramesGenerated:
            return AppStrings.noThumbnails
        }
    }
}

enum VideoProcessingService {
    private static let ffmpegPreferredExtensions: Set<String> = ["mkv", "avi", "webm"]
    private static let bundledToolFolderName = "Tools"
    private static let toolSearchPaths = [
        "/usr/local/bin",
        "/opt/homebrew/bin",
        "/usr/bin"
    ]

    static func loadMetadata(for url: URL) async throws -> VideoMetadata {
        let values = try url.resourceValues(forKeys: [.fileSizeKey])
        let bytes = Int64(values.fileSize ?? 0)
        return VideoMetadata(
            fileSize: bytes
        )
    }

    static func loadRenderMetadata(for url: URL) async throws -> VideoRenderMetadata {
        if prefersFFmpeg(for: url) {
            return try loadRenderMetadataWithFFmpeg(for: url)
        }

        do {
            let asset = AVAsset(url: url)
            let duration = try await asset.load(.duration)
            let tracks = try await asset.loadTracks(withMediaType: .video)
            let resolution = try await resolvedVideoSize(from: tracks.first)
            return VideoRenderMetadata(
                duration: duration.seconds.isFinite ? duration.seconds : 0,
                resolution: resolution
            )
        } catch {
            return try loadRenderMetadataWithFFmpeg(for: url)
        }
    }

    static func generateThumbnails(
        for url: URL,
        count: Int,
        maxSize: CGSize
    ) async throws -> [ThumbnailFrame] {
        if prefersFFmpeg(for: url) {
            return try generateThumbnailsWithFFmpeg(for: url, count: count, maxSize: maxSize)
        }

        do {
            let asset = AVAsset(url: url)
            let duration = try await asset.load(.duration)
            let seconds = max(duration.seconds, 0.1)
            let generator = AVAssetImageGenerator(asset: asset)
            generator.maximumSize = maxSize
            generator.appliesPreferredTrackTransform = true
            let toleranceSeconds = min(max(seconds / Double(max(count, 1)) / 3, 0.1), 2.0)
            let tolerance = CMTime(seconds: toleranceSeconds, preferredTimescale: 600)
            generator.requestedTimeToleranceBefore = tolerance
            generator.requestedTimeToleranceAfter = tolerance

            let timestamps = frameTimes(duration: seconds, count: count)
            var thumbnails: [ThumbnailFrame] = []
            thumbnails.reserveCapacity(timestamps.count)

            for second in timestamps {
                let time = CMTime(seconds: second, preferredTimescale: 600)
                do {
                    let image = try generator.copyCGImage(at: time, actualTime: nil)
                    thumbnails.append(
                        ThumbnailFrame(
                            image: NSImage(cgImage: image, size: .zero),
                            timestamp: second
                        )
                    )
                } catch {
                    continue
                }
            }

            guard !thumbnails.isEmpty else {
                throw VideoProcessingError.noFramesGenerated
            }

            return thumbnails
        } catch {
            return try generateThumbnailsWithFFmpeg(for: url, count: count, maxSize: maxSize)
        }
    }

    private static func frameTimes(duration: TimeInterval, count: Int) -> [TimeInterval] {
        guard count > 0 else { return [] }
        if count == 1 {
            return [duration / 2]
        }

        let usableDuration = max(duration, 0.1)
        let start = usableDuration * 0.05
        let end = usableDuration * 0.95
        let step = (end - start) / Double(count - 1)
        return (0..<count).map { start + Double($0) * step }
    }

    private static func resolvedVideoSize(from track: AVAssetTrack?) async throws -> CGSize {
        guard let track else { return .zero }
        let naturalSize = try await track.load(.naturalSize)
        let transform = try await track.load(.preferredTransform)
        return naturalSize.applying(transform).absoluteSize
    }

    private static func prefersFFmpeg(for url: URL) -> Bool {
        ffmpegPreferredExtensions.contains(url.pathExtension.lowercased())
    }

    private static func loadRenderMetadataWithFFmpeg(for url: URL) throws -> VideoRenderMetadata {
        let data = try runTool("ffprobe", arguments: [
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=width,height:format=duration",
            "-of", "json",
            url.path
        ])

        let response = try JSONDecoder().decode(FFprobeResponse.self, from: data)
        let duration = Double(response.format?.duration ?? "") ?? 0
        let stream = response.streams.first
        return VideoRenderMetadata(
            duration: duration,
            resolution: CGSize(width: stream?.width ?? 0, height: stream?.height ?? 0)
        )
    }

    private static func generateThumbnailsWithFFmpeg(
        for url: URL,
        count: Int,
        maxSize: CGSize
    ) throws -> [ThumbnailFrame] {
        let metadata = try loadRenderMetadataWithFFmpeg(for: url)
        let timestamps = frameTimes(duration: max(metadata.duration, 0.1), count: count)
        let tempDirectory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: tempDirectory, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(at: tempDirectory)
        }

        var thumbnails: [ThumbnailFrame] = []
        thumbnails.reserveCapacity(timestamps.count)

        let width = Int(maxSize.width.rounded())
        let height = Int(maxSize.height.rounded())
        let scaleFilter = "scale=w=\(width):h=\(height):force_original_aspect_ratio=decrease"

        for (index, timestamp) in timestamps.enumerated() {
            let outputURL = tempDirectory.appendingPathComponent("thumb-\(index).png")
            _ = try runTool("ffmpeg", arguments: [
                "-y",
                "-loglevel", "error",
                "-ss", String(format: "%.3f", timestamp),
                "-i", url.path,
                "-frames:v", "1",
                "-vf", scaleFilter,
                outputURL.path
            ])

            guard let image = NSImage(contentsOf: outputURL) else { continue }
            thumbnails.append(ThumbnailFrame(image: image, timestamp: timestamp))
        }

        guard !thumbnails.isEmpty else {
            throw VideoProcessingError.noFramesGenerated
        }

        return thumbnails
    }

    @discardableResult
    private static func runTool(_ launchPath: String, arguments: [String]) throws -> Data {
        let process = Process()
        process.executableURL = try resolvedToolURL(named: launchPath)
        process.arguments = arguments

        let outputPipe = Pipe()
        let errorPipe = Pipe()
        process.standardOutput = outputPipe
        process.standardError = errorPipe

        try process.run()
        process.waitUntilExit()

        let outputData = outputPipe.fileHandleForReading.readDataToEndOfFile()
        let errorData = errorPipe.fileHandleForReading.readDataToEndOfFile()

        guard process.terminationStatus == 0 else {
            throw NSError(
                domain: "ThumbnailGridStudio.FFmpeg",
                code: Int(process.terminationStatus),
                userInfo: [
                    NSLocalizedDescriptionKey: String(data: errorData, encoding: .utf8) ?? AppStrings.unreadableVideo
                ]
            )
        }

        return outputData
    }

    private static func resolvedToolURL(named toolName: String) throws -> URL {
        let fileManager = FileManager.default

        if let bundledToolURL = bundledToolURL(named: toolName), fileManager.isExecutableFile(atPath: bundledToolURL.path) {
            return bundledToolURL
        }

        for basePath in toolSearchPaths {
            let candidate = URL(fileURLWithPath: basePath).appendingPathComponent(toolName)
            if fileManager.isExecutableFile(atPath: candidate.path) {
                return candidate
            }
        }

        throw NSError(
            domain: "ThumbnailGridStudio.FFmpeg",
            code: 127,
            userInfo: [
                NSLocalizedDescriptionKey: "\(toolName) not found"
            ]
        )
    }

    private static func bundledToolURL(named toolName: String) -> URL? {
        guard let resourceURL = Bundle.main.resourceURL else {
            return nil
        }

        return resourceURL
            .appendingPathComponent(bundledToolFolderName, isDirectory: true)
            .appendingPathComponent(currentArchitectureFolderName, isDirectory: true)
            .appendingPathComponent(toolName, isDirectory: false)
    }

    private static var currentArchitectureFolderName: String {
        #if arch(arm64)
        return "arm64"
        #elseif arch(x86_64)
        return "x86_64"
        #else
        return ProcessInfo.processInfo.machineArchitectureName
        #endif
    }
}

private struct FFprobeResponse: Decodable {
    struct Stream: Decodable {
        let width: Double?
        let height: Double?
    }

    struct Format: Decodable {
        let duration: String?
    }

    let streams: [Stream]
    let format: Format?
}

private extension CGSize {
    var absoluteSize: CGSize {
        CGSize(width: abs(width), height: abs(height))
    }
}

private extension ProcessInfo {
    var machineArchitectureName: String {
        var systemInfo = utsname()
        uname(&systemInfo)
        let machine = systemInfo.machine
        return withUnsafeBytes(of: machine) { rawBuffer in
            let bytes = rawBuffer.prefix { $0 != 0 }
            return String(decoding: bytes, as: UTF8.self)
        }
    }
}
