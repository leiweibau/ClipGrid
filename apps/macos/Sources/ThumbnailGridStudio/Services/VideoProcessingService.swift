import AVFoundation
import AppKit
import Darwin
import Foundation

struct VideoMetadata: Sendable {
    let fileSize: Int64
    let duration: TimeInterval
    let resolution: CGSize
    let bitrateBitsPerSecond: Int64
    let videoCodec: String
    let audioCodecs: [String]
}

struct VideoRenderMetadata: Sendable {
    let duration: TimeInterval
    let resolution: CGSize
    let bitrateBitsPerSecond: Int64
    let videoCodec: String
    let audioCodecs: [String]
}

struct ThumbnailFrame: @unchecked Sendable {
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
    private static let ffmpegFrameLimiter = AsyncSemaphore(
        limit: min(4, max(1, ProcessInfo.processInfo.activeProcessorCount / 2))
    )
    private static let bundledToolFolderName = "Tools"
    private static let toolSearchPaths = [
        "/usr/local/bin",
        "/opt/homebrew/bin",
        "/usr/bin"
    ]

    static func loadMetadata(for url: URL) async throws -> VideoMetadata {
        let values = try url.resourceValues(forKeys: [.fileSizeKey])
        let bytes = Int64(values.fileSize ?? 0)
        let renderMetadata = try await loadRenderMetadataWithFFmpeg(for: url)
        return VideoMetadata(
            fileSize: bytes,
            duration: renderMetadata.duration,
            resolution: renderMetadata.resolution,
            bitrateBitsPerSecond: renderMetadata.bitrateBitsPerSecond,
            videoCodec: renderMetadata.videoCodec,
            audioCodecs: renderMetadata.audioCodecs
        )
    }

    static func loadRenderMetadata(for url: URL) async throws -> VideoRenderMetadata {
        if prefersFFmpeg(for: url) {
            return try await loadRenderMetadataWithFFmpeg(for: url)
        }

        do {
            let asset = AVAsset(url: url)
            let duration = try await asset.load(.duration)
            let tracks = try await asset.loadTracks(withMediaType: .video)
            let resolution = try await resolvedVideoSize(from: tracks.first)
            let avFoundationMetadata = VideoRenderMetadata(
                duration: duration.seconds.isFinite ? duration.seconds : 0,
                resolution: resolution,
                bitrateBitsPerSecond: 0,
                videoCodec: "",
                audioCodecs: []
            )

            // AVFoundation is fine for duration/resolution, but codec and bitrate
            // metadata still need ffprobe for parity with the Windows app.
            if let ffmpegMetadata = try? await loadRenderMetadataWithFFmpeg(for: url) {
                return VideoRenderMetadata(
                    duration: avFoundationMetadata.duration > 0 ? avFoundationMetadata.duration : ffmpegMetadata.duration,
                    resolution: avFoundationMetadata.resolution == .zero ? ffmpegMetadata.resolution : avFoundationMetadata.resolution,
                    bitrateBitsPerSecond: ffmpegMetadata.bitrateBitsPerSecond,
                    videoCodec: ffmpegMetadata.videoCodec,
                    audioCodecs: ffmpegMetadata.audioCodecs
                )
            }

            return avFoundationMetadata
        } catch {
            if error is CancellationError {
                throw error
            }
            return try await loadRenderMetadataWithFFmpeg(for: url)
        }
    }

    static func generateThumbnails(
        for url: URL,
        duration knownDuration: TimeInterval? = nil,
        count: Int,
        maxSize: CGSize,
        fullResolutionFrameHandler: (@Sendable (Int, ThumbnailFrame) async throws -> Void)? = nil
    ) async throws -> [ThumbnailFrame] {
        if prefersFFmpeg(for: url) {
            return try await generateThumbnailsWithFFmpeg(
                for: url,
                duration: knownDuration,
                count: count,
                maxSize: maxSize,
                fullResolutionFrameHandler: fullResolutionFrameHandler
            )
        }

        let asset = AVAsset(url: url)
        let generator = AVAssetImageGenerator(asset: asset)
        var didHandleFullResolutionFrame = false
        do {
            let duration = try await asset.load(.duration)
            let seconds = max(duration.seconds, 0.1)
            if fullResolutionFrameHandler == nil {
                generator.maximumSize = maxSize
            }
            generator.appliesPreferredTrackTransform = true
            let toleranceSeconds = min(max(seconds / Double(max(count, 1)) / 3, 0.1), 2.0)
            let tolerance = CMTime(seconds: toleranceSeconds, preferredTimescale: 600)
            generator.requestedTimeToleranceBefore = tolerance
            generator.requestedTimeToleranceAfter = tolerance

            let timestamps = frameTimes(duration: seconds, count: count)
            let requestedTimes = timestamps.map {
                CMTime(seconds: $0, preferredTimescale: 600)
            }
            var thumbnails = [ThumbnailFrame?](repeating: nil, count: requestedTimes.count)

            for await result in generator.images(for: requestedTimes) {
                try Task.checkCancellation()
                switch result {
                case .success(let requestedTime, let image, _):
                    guard let index = requestedTimes.firstIndex(where: {
                        CMTimeCompare($0, requestedTime) == 0
                    }) else { continue }
                    let fullResolutionFrame = ThumbnailFrame(
                        image: NSImage(cgImage: image, size: .zero),
                        timestamp: timestamps[index]
                    )
                    if let fullResolutionFrameHandler {
                        try await fullResolutionFrameHandler(index, fullResolutionFrame)
                        didHandleFullResolutionFrame = true
                        thumbnails[index] = ThumbnailFrame(
                            image: resizedImage(fullResolutionFrame.image, maximumSize: maxSize),
                            timestamp: fullResolutionFrame.timestamp
                        )
                    } else {
                        thumbnails[index] = fullResolutionFrame
                    }
                case .failure:
                    continue
                }
            }

            let generated = thumbnails.compactMap { $0 }

            guard !generated.isEmpty else {
                throw VideoProcessingError.noFramesGenerated
            }

            return generated
        } catch {
            generator.cancelAllCGImageGeneration()
            if error is CancellationError {
                throw error
            }
            if didHandleFullResolutionFrame {
                throw error
            }
            return try await generateThumbnailsWithFFmpeg(
                for: url,
                duration: knownDuration,
                count: count,
                maxSize: maxSize,
                fullResolutionFrameHandler: fullResolutionFrameHandler
            )
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

    private static func loadRenderMetadataWithFFmpeg(for url: URL) async throws -> VideoRenderMetadata {
        let data = try await runTool("ffprobe", arguments: [
            "-v", "error",
            "-show_entries", "stream=codec_type,codec_name,width,height,duration,bit_rate,avg_frame_rate,nb_frames:stream_tags=language:format=duration,format_name,bit_rate",
            "-of", "json",
            url.path
        ])

        let response = try JSONDecoder().decode(FFprobeResponse.self, from: data)
        guard let stream = response.streams.first(where: { $0.codecType == "video" }) else {
            throw VideoProcessingError.unreadableVideo
        }

        let width = stream.width ?? 0
        let height = stream.height ?? 0
        guard width > 0, height > 0 else {
            throw VideoProcessingError.unreadableVideo
        }

        let formatName = response.format?.formatName?.lowercased() ?? ""
        if formatName.contains("image2") || formatName.hasSuffix("_pipe") {
            throw VideoProcessingError.unreadableVideo
        }

        var duration = max(
            Double(response.format?.duration ?? "") ?? 0,
            Double(stream.duration ?? "") ?? 0
        )
        if duration <= 0 {
            duration = estimatedDuration(frameCount: stream.frameCount, frameRate: stream.averageFrameRate)
        }
        if duration <= 0 {
            duration = try await measuredElementaryStreamDuration(for: url)
        }
        guard duration > 0 else {
            throw VideoProcessingError.unreadableVideo
        }

        let bitrate = max(
            parseBitrateBitsPerSecond(response.format?.bitRate),
            parseBitrateBitsPerSecond(stream.bitRate)
        )
        let videoCodec = normalizedCodecName(stream.codecName)
        let audioCodecs = response.streams
            .filter { $0.codecType == "audio" }
            .map { formatAudioCodecEntry(codec: $0.codecName, language: $0.tags?.language, bitrateBitsPerSecond: parseBitrateBitsPerSecond($0.bitRate)) }
            .filter { !$0.isEmpty }

        return VideoRenderMetadata(
            duration: duration,
            resolution: CGSize(width: width, height: height),
            bitrateBitsPerSecond: bitrate,
            videoCodec: videoCodec,
            audioCodecs: audioCodecs
        )
    }

    private static func measuredElementaryStreamDuration(for url: URL) async throws -> TimeInterval {
        let data = try await runTool("ffprobe", arguments: [
            "-v", "error",
            "-count_frames",
            "-select_streams", "v:0",
            "-show_entries", "stream=nb_read_frames,avg_frame_rate",
            "-of", "json",
            url.path
        ])
        let response = try JSONDecoder().decode(FFprobeFrameCountResponse.self, from: data)
        guard let stream = response.streams.first else { return 0 }
        return estimatedDuration(frameCount: stream.frameCount, frameRate: stream.averageFrameRate)
    }

    private static func estimatedDuration(frameCount: String?, frameRate: String?) -> TimeInterval {
        guard let frameCount,
              let frames = Double(frameCount),
              frames > 0,
              let frameRate,
              let framesPerSecond = parseFraction(frameRate),
              framesPerSecond > 0 else {
            return 0
        }
        return frames / framesPerSecond
    }

    private static func parseFraction(_ value: String) -> Double? {
        let components = value.split(separator: "/", maxSplits: 1).compactMap { Double($0) }
        if components.count == 2, components[1] != 0 {
            return components[0] / components[1]
        }
        return Double(value)
    }

    private static func generateThumbnailsWithFFmpeg(
        for url: URL,
        duration knownDuration: TimeInterval?,
        count: Int,
        maxSize: CGSize,
        fullResolutionFrameHandler: (@Sendable (Int, ThumbnailFrame) async throws -> Void)?
    ) async throws -> [ThumbnailFrame] {
        let duration: TimeInterval
        if let knownDuration, knownDuration > 0 {
            duration = knownDuration
        } else {
            duration = try await loadRenderMetadataWithFFmpeg(for: url).duration
        }
        let timestamps = frameTimes(duration: max(duration, 0.1), count: count)
        let tempDirectory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: tempDirectory, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(at: tempDirectory)
        }

        let ffmpegScaleArguments: [String]
        if fullResolutionFrameHandler == nil {
            let width = Int(maxSize.width.rounded())
            let height = Int(maxSize.height.rounded())
            ffmpegScaleArguments = [
                "-vf", "scale=w=\(width):h=\(height):force_original_aspect_ratio=decrease"
            ]
        } else {
            ffmpegScaleArguments = []
        }

        let indexedFrames = try await withThrowingTaskGroup(
            of: IndexedThumbnailFrame.self,
            returning: [IndexedThumbnailFrame].self
        ) { group in
            for (index, timestamp) in timestamps.enumerated() {
                group.addTask {
                    try Task.checkCancellation()
                    let outputURL = tempDirectory.appendingPathComponent("thumb-\(index).bmp")

                    await ffmpegFrameLimiter.acquire()
                    do {
                        try Task.checkCancellation()
                        _ = try await runTool("ffmpeg", arguments: [
                            "-y",
                            "-loglevel", "error",
                            "-nostdin",
                            "-ss", String(
                                format: "%.3f",
                                locale: Locale(identifier: "en_US_POSIX"),
                                timestamp
                            ),
                            "-i", url.path,
                            "-frames:v", "1"
                        ] + ffmpegScaleArguments + [
                            "-c:v", "bmp",
                            outputURL.path
                        ])
                        await ffmpegFrameLimiter.release()
                    } catch {
                        await ffmpegFrameLimiter.release()
                        throw error
                    }

                    let frame: ThumbnailFrame?
                    if let image = NSImage(contentsOf: outputURL) {
                        let fullResolutionFrame = ThumbnailFrame(image: image, timestamp: timestamp)
                        if let fullResolutionFrameHandler {
                            try await fullResolutionFrameHandler(index, fullResolutionFrame)
                            frame = ThumbnailFrame(
                                image: resizedImage(image, maximumSize: maxSize),
                                timestamp: timestamp
                            )
                        } else {
                            frame = fullResolutionFrame
                        }
                    } else {
                        frame = nil
                    }
                    return IndexedThumbnailFrame(index: index, frame: frame)
                }
            }

            var generated: [IndexedThumbnailFrame] = []
            generated.reserveCapacity(timestamps.count)
            for try await frame in group {
                generated.append(frame)
            }
            return generated
        }

        let thumbnails = indexedFrames
            .sorted { $0.index < $1.index }
            .compactMap(\.frame)

        guard !thumbnails.isEmpty else {
            throw VideoProcessingError.noFramesGenerated
        }

        return thumbnails
    }

    private static func resizedImage(_ image: NSImage, maximumSize: CGSize) -> NSImage {
        guard maximumSize.width > 0,
              maximumSize.height > 0,
              let source = image.cgImage(forProposedRect: nil, context: nil, hints: nil),
              source.width > 0,
              source.height > 0 else {
            return image
        }

        let sourceSize = CGSize(width: source.width, height: source.height)
        let scale = min(maximumSize.width / sourceSize.width, maximumSize.height / sourceSize.height)
        let destinationSize = CGSize(
            width: max(Int((sourceSize.width * scale).rounded()), 1),
            height: max(Int((sourceSize.height * scale).rounded()), 1)
        )
        guard let context = CGContext(
            data: nil,
            width: Int(destinationSize.width),
            height: Int(destinationSize.height),
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: source.colorSpace ?? CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else {
            return image
        }

        context.interpolationQuality = .high
        context.draw(source, in: CGRect(origin: .zero, size: destinationSize))
        guard let resized = context.makeImage() else { return image }
        return NSImage(cgImage: resized, size: .zero)
    }

    @discardableResult
    private static func runTool(_ launchPath: String, arguments: [String]) async throws -> Data {
        let captureDirectory = FileManager.default.temporaryDirectory
            .appendingPathComponent("ThumbnailGridStudio-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: captureDirectory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: captureDirectory) }

        let outputURL = captureDirectory.appendingPathComponent("stdout")
        let errorURL = captureDirectory.appendingPathComponent("stderr")
        _ = FileManager.default.createFile(atPath: outputURL.path, contents: nil)
        _ = FileManager.default.createFile(atPath: errorURL.path, contents: nil)
        let outputHandle = try FileHandle(forWritingTo: outputURL)
        let errorHandle = try FileHandle(forWritingTo: errorURL)
        defer {
            try? outputHandle.close()
            try? errorHandle.close()
        }

        let process = Process()
        process.executableURL = try resolvedToolURL(named: launchPath)
        process.arguments = arguments
        process.standardOutput = outputHandle
        process.standardError = errorHandle

        let runner = AsyncProcess(process: process)
        let terminationStatus = try await withTaskCancellationHandler {
            try await runner.run()
        } onCancel: {
            runner.cancel()
        }
        try Task.checkCancellation()

        try outputHandle.close()
        try errorHandle.close()
        let outputData = try Data(contentsOf: outputURL)
        let errorData = try Data(contentsOf: errorURL)

        guard terminationStatus == 0 else {
            throw NSError(
                domain: "ThumbnailGridStudio.FFmpeg",
                code: Int(terminationStatus),
                userInfo: [
                    NSLocalizedDescriptionKey: String(data: errorData, encoding: .utf8) ?? AppStrings.unreadableVideo
                ]
            )
        }

        return outputData
    }

    private static func resolvedToolURL(named toolName: String) throws -> URL {
        let fileManager = FileManager.default

        let environmentKey = "THUMBNAIL_GRID_STUDIO_\(toolName.uppercased())"
        if let explicitPath = ProcessInfo.processInfo.environment[environmentKey],
           fileManager.isExecutableFile(atPath: explicitPath) {
            return URL(fileURLWithPath: explicitPath)
        }

        if let bundledToolURL = bundledToolURL(named: toolName), fileManager.isExecutableFile(atPath: bundledToolURL.path) {
            return bundledToolURL
        }

        for basePath in toolSearchPaths {
            let candidate = URL(fileURLWithPath: basePath).appendingPathComponent(toolName)
            if fileManager.isExecutableFile(atPath: candidate.path) {
                return candidate
            }
        }

        let developmentCandidate = URL(fileURLWithPath: fileManager.currentDirectoryPath)
            .appendingPathComponent(".cache/ffmpeg-install", isDirectory: true)
            .appendingPathComponent(currentArchitectureFolderName, isDirectory: true)
            .appendingPathComponent("bin", isDirectory: true)
            .appendingPathComponent(toolName)
        if fileManager.isExecutableFile(atPath: developmentCandidate.path) {
            return developmentCandidate
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

    private static func parseBitrateBitsPerSecond(_ value: String?) -> Int64 {
        guard let value, let parsed = Int64(value), parsed > 0 else { return 0 }
        return parsed
    }

    private static func normalizedCodecName(_ value: String?) -> String {
        value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    }

    private static func normalizedLanguageCode(_ value: String?) -> String {
        guard let value else { return "" }
        let normalized = value.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !normalized.isEmpty, normalized != "und" else { return "" }
        return normalized
    }

    private static func formatCompactBitrate(_ bitrateBitsPerSecond: Int64) -> String {
        guard bitrateBitsPerSecond > 0 else { return "" }
        let kbps = Double(bitrateBitsPerSecond) / 1000
        return kbps >= 1000 ? String(format: "%.2f Mbps", kbps / 1000) : String(format: "%.0f kbps", kbps)
    }

    private static func formatAudioCodecEntry(codec: String?, language: String?, bitrateBitsPerSecond: Int64) -> String {
        let normalizedCodec = normalizedCodecName(codec)
        let normalizedLanguage = normalizedLanguageCode(language)
        let normalizedBitrate = formatCompactBitrate(bitrateBitsPerSecond)
        guard !normalizedCodec.isEmpty else { return "" }
        let details = [normalizedLanguage, normalizedBitrate].filter { !$0.isEmpty }
        guard !details.isEmpty else { return normalizedCodec }
        return "\(normalizedCodec) (\(details.joined(separator: ", ")))"
    }
}

private struct IndexedThumbnailFrame: @unchecked Sendable {
    let index: Int
    let frame: ThumbnailFrame?
}

private actor AsyncSemaphore {
    private var availablePermits: Int
    private var waiters: [CheckedContinuation<Void, Never>] = []

    init(limit: Int) {
        availablePermits = max(limit, 1)
    }

    func acquire() async {
        if availablePermits > 0 {
            availablePermits -= 1
            return
        }

        await withCheckedContinuation { continuation in
            waiters.append(continuation)
        }
    }

    func release() {
        if waiters.isEmpty {
            availablePermits += 1
        } else {
            waiters.removeFirst().resume()
        }
    }
}

private final class AsyncProcess: @unchecked Sendable {
    private let process: Process
    private let lock = NSLock()
    private var continuation: CheckedContinuation<Int32, Never>?
    private var completedStatus: Int32?
    private var isCancelled = false

    init(process: Process) {
        self.process = process
    }

    func run() async throws -> Int32 {
        process.terminationHandler = { [weak self] process in
            self?.didTerminate(with: process.terminationStatus)
        }

        let shouldStart = lock.withLock { !isCancelled }
        guard shouldStart else { throw CancellationError() }

        try process.run()

        let shouldTerminate = lock.withLock { isCancelled }
        if shouldTerminate, process.isRunning {
            process.terminate()
        }

        return await withCheckedContinuation { continuation in
            lock.lock()
            if let completedStatus {
                lock.unlock()
                continuation.resume(returning: completedStatus)
            } else {
                self.continuation = continuation
                lock.unlock()
            }
        }
    }

    func cancel() {
        lock.lock()
        isCancelled = true
        let shouldTerminate = process.isRunning
        lock.unlock()

        if shouldTerminate {
            process.terminate()
        }
    }

    private func didTerminate(with status: Int32) {
        lock.lock()
        completedStatus = status
        let continuation = continuation
        self.continuation = nil
        lock.unlock()
        continuation?.resume(returning: status)
    }
}

private struct FFprobeResponse: Decodable {
    struct Stream: Decodable {
        let codecType: String?
        let codecName: String?
        let width: Double?
        let height: Double?
        let duration: String?
        let bitRate: String?
        let averageFrameRate: String?
        let frameCount: String?
        let tags: Tags?

        struct Tags: Decodable {
            let language: String?
        }

        enum CodingKeys: String, CodingKey {
            case codecType = "codec_type"
            case codecName = "codec_name"
            case width
            case height
            case duration
            case bitRate = "bit_rate"
            case averageFrameRate = "avg_frame_rate"
            case frameCount = "nb_frames"
            case tags
        }
    }

    struct Format: Decodable {
        let duration: String?
        let formatName: String?
        let bitRate: String?

        enum CodingKeys: String, CodingKey {
            case duration
            case formatName = "format_name"
            case bitRate = "bit_rate"
        }
    }

    let streams: [Stream]
    let format: Format?
}

private struct FFprobeFrameCountResponse: Decodable {
    struct Stream: Decodable {
        let frameCount: String?
        let averageFrameRate: String?

        enum CodingKeys: String, CodingKey {
            case frameCount = "nb_read_frames"
            case averageFrameRate = "avg_frame_rate"
        }
    }

    let streams: [Stream]
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
