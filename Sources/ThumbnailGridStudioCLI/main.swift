import AppKit
import Foundation

private enum CLIError: LocalizedError {
    case invalidArgument(String)
    case missingValue(String)
    case missingRequired(String)
    case toolNotFound(String)
    case executionFailed(String)
    case executionTimedOut(String)

    var errorDescription: String? {
        switch self {
        case .invalidArgument(let message),
             .missingValue(let message),
             .missingRequired(let message),
             .toolNotFound(let message),
             .executionFailed(let message),
             .executionTimedOut(let message):
            return message
        }
    }
}

private struct CLIOptions {
    var inputFiles: [String] = []
    var outputDirectory: String?
    var columns: Int?
    var rows: Int?
    var format: String?
    var width: Int?
    var height: Int?
    var spacing: Int?
    var backgroundHex: String?
    var metadataTextHex: String?
    var fileNameFontSize: CGFloat?
    var durationFontSize: CGFloat?
    var fileSizeFontSize: CGFloat?
    var resolutionFontSize: CGFloat?
    var timestampFontSize: CGFloat?
    var showHelp = false
}

private struct GUISettingsFallback {
    var columns: Int?
    var rows: Int?
    var exportFormat: String?
    var thumbnailWidthText: String?
    var thumbnailHeightText: String?
    var spacing: Int?
    var backgroundHex: String?
    var metadataTextHex: String?
    var fileNameFontSize: CGFloat?
    var durationFontSize: CGFloat?
    var fileSizeFontSize: CGFloat?
    var resolutionFontSize: CGFloat?
    var timestampFontSize: CGFloat?
}

private struct VideoInfo {
    let duration: Double
    let width: Int
    let height: Int
    let fileSizeBytes: Int64
}

private struct ResolvedRenderOptions {
    let columns: Int
    let rows: Int
    let format: String
    let thumbnailWidth: Int
    let thumbnailHeight: Int
    let spacing: Int
    let backgroundHex: String
    let metadataTextHex: String
    let fileNameFontSize: CGFloat
    let durationFontSize: CGFloat
    let fileSizeFontSize: CGFloat
    let resolutionFontSize: CGFloat
    let timestampFontSize: CGFloat
}

private struct ThumbnailFrame {
    let image: NSImage
    let timestamp: Double
}

private enum CLIRunner {
    static func run() throws {
        setenv("LSUIElement", "1", 1)

        let options = try parseArguments(Array(CommandLine.arguments.dropFirst()))
        if options.showHelp {
            printHelp()
            return
        }

        guard !options.inputFiles.isEmpty else {
            throw CLIError.missingRequired("Missing required option: --input <video-file> (can be repeated).")
        }
        guard let outputDirectory = options.outputDirectory else {
            throw CLIError.missingRequired("Missing required option: --output-dir <directory>.")
        }

        let fallback = loadGUISettingsFallback()
        let outputDirectoryURL = URL(fileURLWithPath: outputDirectory, isDirectory: true)
        try FileManager.default.createDirectory(at: outputDirectoryURL, withIntermediateDirectories: true)

        for inputFile in options.inputFiles {
            let inputURL = URL(fileURLWithPath: inputFile)
            guard FileManager.default.fileExists(atPath: inputURL.path) else {
                throw CLIError.invalidArgument("Input file does not exist: \(inputURL.path)")
            }
            try renderContactSheet(for: inputURL, options: options, fallback: fallback, outputDirectory: outputDirectoryURL)
        }
    }

    private static func parseArguments(_ args: [String]) throws -> CLIOptions {
        var options = CLIOptions()
        var index = 0

        while index < args.count {
            let argument = args[index]
            switch argument {
            case "--h", "-h", "--help":
                options.showHelp = true
                index += 1
            case "--input":
                options.inputFiles.append(try argumentValue(args, index: &index, name: "--input"))
            case "--output-dir":
                options.outputDirectory = try argumentValue(args, index: &index, name: "--output-dir")
            case "--columns":
                options.columns = try parsePositiveInt(try argumentValue(args, index: &index, name: "--columns"), name: "--columns")
            case "--rows":
                options.rows = try parsePositiveInt(try argumentValue(args, index: &index, name: "--rows"), name: "--rows")
            case "--format":
                let value = try argumentValue(args, index: &index, name: "--format").lowercased()
                guard value == "jpg" || value == "png" else {
                    throw CLIError.invalidArgument("Invalid value for --format: \(value). Supported: jpg, png.")
                }
                options.format = value
            case "--width":
                options.width = try parsePositiveInt(try argumentValue(args, index: &index, name: "--width"), name: "--width")
            case "--height":
                options.height = try parsePositiveInt(try argumentValue(args, index: &index, name: "--height"), name: "--height")
            case "--spacing":
                options.spacing = try parseNonNegativeInt(try argumentValue(args, index: &index, name: "--spacing"), name: "--spacing")
            case "--background":
                options.backgroundHex = try parseHex(try argumentValue(args, index: &index, name: "--background"), name: "--background")
            case "--metadata-color":
                options.metadataTextHex = try parseHex(try argumentValue(args, index: &index, name: "--metadata-color"), name: "--metadata-color")
            case "--file-name-font-size":
                options.fileNameFontSize = try parsePositiveCGFloat(try argumentValue(args, index: &index, name: "--file-name-font-size"), name: "--file-name-font-size")
            case "--duration-font-size":
                options.durationFontSize = try parsePositiveCGFloat(try argumentValue(args, index: &index, name: "--duration-font-size"), name: "--duration-font-size")
            case "--file-size-font-size":
                options.fileSizeFontSize = try parsePositiveCGFloat(try argumentValue(args, index: &index, name: "--file-size-font-size"), name: "--file-size-font-size")
            case "--resolution-font-size":
                options.resolutionFontSize = try parsePositiveCGFloat(try argumentValue(args, index: &index, name: "--resolution-font-size"), name: "--resolution-font-size")
            case "--timestamp-font-size":
                options.timestampFontSize = try parsePositiveCGFloat(try argumentValue(args, index: &index, name: "--timestamp-font-size"), name: "--timestamp-font-size")
            default:
                if argument.hasPrefix("-") {
                    throw CLIError.invalidArgument("Unknown option: \(argument). Use --h for help.")
                }
                options.inputFiles.append(argument)
                index += 1
            }
        }

        return options
    }

    private static func argumentValue(_ args: [String], index: inout Int, name: String) throws -> String {
        let nextIndex = index + 1
        guard nextIndex < args.count else {
            throw CLIError.missingValue("Missing value for \(name).")
        }
        index += 2
        return args[nextIndex]
    }

    private static func parsePositiveInt(_ raw: String, name: String) throws -> Int {
        guard let value = Int(raw), value > 0 else {
            throw CLIError.invalidArgument("Invalid value for \(name): \(raw). Expected an integer > 0.")
        }
        return value
    }

    private static func parseNonNegativeInt(_ raw: String, name: String) throws -> Int {
        guard let value = Int(raw), value >= 0 else {
            throw CLIError.invalidArgument("Invalid value for \(name): \(raw). Expected an integer >= 0.")
        }
        return value
    }

    private static func parsePositiveCGFloat(_ raw: String, name: String) throws -> CGFloat {
        guard let value = Double(raw), value > 0 else {
            throw CLIError.invalidArgument("Invalid value for \(name): \(raw). Expected a number > 0.")
        }
        return CGFloat(value)
    }

    private static func parseHex(_ raw: String, name: String) throws -> String {
        let value = raw.replacingOccurrences(of: "#", with: "").uppercased()
        guard value.range(of: "^[0-9A-F]{6}$", options: .regularExpression) != nil else {
            throw CLIError.invalidArgument("Invalid value for \(name): \(value). Use 6-digit hex, e.g. 1F2126.")
        }
        return value
    }

    private static func renderContactSheet(
        for inputURL: URL,
        options: CLIOptions,
        fallback: GUISettingsFallback,
        outputDirectory: URL
    ) throws {
        print("Processing: \(inputURL.lastPathComponent)")
        fflush(stdout)

        let ffprobePath = try resolveToolPath(explicitEnvName: "THUMBNAIL_GRID_STUDIO_FFPROBE", toolName: "ffprobe")
        let ffmpegPath = try resolveToolPath(explicitEnvName: "THUMBNAIL_GRID_STUDIO_FFMPEG", toolName: "ffmpeg")

        print("Running ffprobe...")
        fflush(stdout)
        let info = try readVideoInfo(inputURL: inputURL, ffprobePath: ffprobePath)
        let resolved = resolveRenderOptions(cli: options, fallback: fallback, video: info)

        let baseName = inputURL.deletingPathExtension().lastPathComponent
        let outputURL = outputDirectory.appendingPathComponent("\(baseName).\(resolved.format)")

        let timestamps = frameTimes(duration: max(info.duration, 0.1), count: max(resolved.columns * resolved.rows, 1))
        let tempDirectory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: tempDirectory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: tempDirectory) }

        var frames: [ThumbnailFrame] = []
        frames.reserveCapacity(timestamps.count)

        print("Running ffmpeg...")
        fflush(stdout)
        for (index, timestamp) in timestamps.enumerated() {
            let frameURL = tempDirectory.appendingPathComponent("thumb-\(index).bmp")
            let filter =
                "scale=w=\(resolved.thumbnailWidth):h=\(resolved.thumbnailHeight):force_original_aspect_ratio=decrease," +
                "pad=\(resolved.thumbnailWidth):\(resolved.thumbnailHeight):(ow-iw)/2:(oh-ih)/2:color=black"

            let (_, _, stderr) = try runProcess(
                executable: ffmpegPath,
                arguments: [
                    "-y",
                    "-hide_banner",
                    "-loglevel", "error",
                    "-nostdin",
                    "-ss", String(format: "%.3f", locale: Locale(identifier: "en_US_POSIX"), timestamp),
                    "-i", inputURL.path,
                    "-frames:v", "1",
                    "-vf", filter,
                    "-c:v", "bmp",
                    frameURL.path
                ],
                timeoutSeconds: 60
            )

            let trimmed = stderr.trimmingCharacters(in: .whitespacesAndNewlines)
            if !trimmed.isEmpty {
                print(trimmed)
            }

            guard let image = NSImage(contentsOf: frameURL) else { continue }
            frames.append(ThumbnailFrame(image: image, timestamp: timestamp))
        }

        guard !frames.isEmpty else {
            throw CLIError.executionFailed("No thumbnails could be generated.")
        }

        try composeFinalImage(
            thumbnails: frames,
            outputURL: outputURL,
            options: resolved,
            inputURL: inputURL,
            info: info
        )

        print("Rendered: \(outputURL.path)")
    }

    private static func resolveRenderOptions(
        cli: CLIOptions,
        fallback: GUISettingsFallback,
        video: VideoInfo
    ) -> ResolvedRenderOptions {
        let columns = cli.columns ?? fallback.columns ?? 4
        let rows = cli.rows ?? fallback.rows ?? 4
        let format = (cli.format ?? fallback.exportFormat ?? "jpg").lowercased()
        let spacing = cli.spacing ?? fallback.spacing ?? 16

        let aspect = (video.width > 0 && video.height > 0)
            ? Double(video.width) / Double(video.height)
            : (320.0 / 180.0)

        let explicitWidth = cli.width
        let explicitHeight = cli.height
        let fallbackWidth = positiveInt(fromString: fallback.thumbnailWidthText)
        let fallbackHeight = positiveInt(fromString: fallback.thumbnailHeightText)

        let resolvedSize: (Int, Int)
        if explicitWidth != nil || explicitHeight != nil {
            resolvedSize = resolveSize(width: explicitWidth, height: explicitHeight, aspectRatio: aspect)
        } else {
            resolvedSize = resolveSize(width: fallbackWidth, height: fallbackHeight, aspectRatio: aspect)
        }

        return ResolvedRenderOptions(
            columns: max(1, columns),
            rows: max(1, rows),
            format: (format == "png" ? "png" : "jpg"),
            thumbnailWidth: max(1, resolvedSize.0),
            thumbnailHeight: max(1, resolvedSize.1),
            spacing: max(0, spacing),
            backgroundHex: cli.backgroundHex ?? fallback.backgroundHex ?? "1F2126",
            metadataTextHex: cli.metadataTextHex ?? fallback.metadataTextHex ?? "FFFFFF",
            fileNameFontSize: cli.fileNameFontSize ?? fallback.fileNameFontSize ?? 26,
            durationFontSize: cli.durationFontSize ?? fallback.durationFontSize ?? 14,
            fileSizeFontSize: cli.fileSizeFontSize ?? fallback.fileSizeFontSize ?? 14,
            resolutionFontSize: cli.resolutionFontSize ?? fallback.resolutionFontSize ?? 14,
            timestampFontSize: cli.timestampFontSize ?? fallback.timestampFontSize ?? 12
        )
    }

    private static func resolveSize(width: Int?, height: Int?, aspectRatio: Double) -> (Int, Int) {
        switch (width, height) {
        case let (.some(w), .some(h)):
            return (w, h)
        case let (.some(w), nil):
            return (w, max(1, Int((Double(w) / aspectRatio).rounded())))
        case let (nil, .some(h)):
            return (max(1, Int((Double(h) * aspectRatio).rounded())), h)
        case (nil, nil):
            return (320, 180)
        }
    }

    private static func positiveInt(fromString value: String?) -> Int? {
        guard let value else { return nil }
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let parsed = Int(trimmed), parsed > 0 else { return nil }
        return parsed
    }

    private static func loadGUISettingsFallback() -> GUISettingsFallback {
        let domainNames = ["local.thumbnailgridstudio", "local.clipgrid"]
        let defaults = UserDefaults.standard
        var domain: [String: Any] = [:]

        for name in domainNames {
            if let candidate = defaults.persistentDomain(forName: name), !candidate.isEmpty {
                domain = candidate
                break
            }
        }

        if domain.isEmpty {
            domain = defaults.dictionaryRepresentation()
        }

        let spacing: Int? = {
            if let text = stringValue(domain["settings.thumbnailSpacingText"]), let value = Int(text), value >= 0 { return value }
            if let number = numberValue(domain["settings.thumbnailSpacing"]), number >= 0 { return Int(number.rounded()) }
            return nil
        }()

        return GUISettingsFallback(
            columns: intValue(domain["settings.columns"]),
            rows: intValue(domain["settings.rows"]),
            exportFormat: stringValue(domain["settings.exportFormat"])?.lowercased(),
            thumbnailWidthText: stringValue(domain["settings.thumbnailWidthText"]),
            thumbnailHeightText: stringValue(domain["settings.thumbnailHeightText"]),
            spacing: spacing,
            backgroundHex: hexFromRGB(
                red: numberValue(domain["settings.backgroundRed"]),
                green: numberValue(domain["settings.backgroundGreen"]),
                blue: numberValue(domain["settings.backgroundBlue"])
            ),
            metadataTextHex: hexFromRGB(
                red: numberValue(domain["settings.metadataTextRed"]),
                green: numberValue(domain["settings.metadataTextGreen"]),
                blue: numberValue(domain["settings.metadataTextBlue"])
            ),
            fileNameFontSize: positiveCGFloat(from: domain["settings.fileNameFontSizeText"]),
            durationFontSize: positiveCGFloat(from: domain["settings.durationFontSizeText"]),
            fileSizeFontSize: positiveCGFloat(from: domain["settings.fileSizeFontSizeText"]),
            resolutionFontSize: positiveCGFloat(from: domain["settings.resolutionFontSizeText"]),
            timestampFontSize: positiveCGFloat(from: domain["settings.timestampFontSizeText"])
        )
    }

    private static func intValue(_ value: Any?) -> Int? {
        if let number = value as? NSNumber { return number.intValue }
        if let string = value as? String { return Int(string) }
        return nil
    }

    private static func numberValue(_ value: Any?) -> Double? {
        if let number = value as? NSNumber { return number.doubleValue }
        if let string = value as? String { return Double(string) }
        return nil
    }

    private static func stringValue(_ value: Any?) -> String? {
        if let string = value as? String { return string }
        if let number = value as? NSNumber { return number.stringValue }
        return nil
    }

    private static func positiveCGFloat(from value: Any?) -> CGFloat? {
        guard let number = numberValue(value), number > 0 else { return nil }
        return CGFloat(number)
    }

    private static func hexFromRGB(red: Double?, green: Double?, blue: Double?) -> String? {
        guard let red, let green, let blue else { return nil }
        let r = Int((max(0, min(1, red)) * 255).rounded())
        let g = Int((max(0, min(1, green)) * 255).rounded())
        let b = Int((max(0, min(1, blue)) * 255).rounded())
        return String(format: "%02X%02X%02X", r, g, b)
    }

    private static func readVideoInfo(inputURL: URL, ffprobePath: String) throws -> VideoInfo {
        let values = try inputURL.resourceValues(forKeys: [.fileSizeKey])
        let fileSizeBytes = Int64(values.fileSize ?? 0)

        let (_, stdout, stderr) = try runProcess(
            executable: ffprobePath,
            arguments: [
                "-v", "error",
                "-select_streams", "v:0",
                "-show_entries", "stream=width,height:format=duration",
                "-of", "json",
                inputURL.path
            ],
            timeoutSeconds: 30
        )

        let errorMessage = stderr.trimmingCharacters(in: .whitespacesAndNewlines)
        if !errorMessage.isEmpty {
            throw CLIError.executionFailed("ffprobe failed for \(inputURL.lastPathComponent): \(errorMessage)")
        }

        let data = Data(stdout.utf8)
        let parsed = try JSONDecoder().decode(FFprobeResponse.self, from: data)
        let duration = Double(parsed.format?.duration ?? "") ?? 0
        let width = Int(parsed.streams.first?.width ?? 0)
        let height = Int(parsed.streams.first?.height ?? 0)

        return VideoInfo(
            duration: duration.isFinite && duration > 0 ? duration : 1,
            width: width,
            height: height,
            fileSizeBytes: fileSizeBytes
        )
    }

    private static func composeFinalImage(
        thumbnails: [ThumbnailFrame],
        outputURL: URL,
        options: ResolvedRenderOptions,
        inputURL: URL,
        info: VideoInfo
    ) throws {
        let horizontalPadding: CGFloat = 28
        let verticalPadding: CGFloat = 28

        let thumbnailSize = CGSize(width: CGFloat(options.thumbnailWidth), height: CGFloat(options.thumbnailHeight))
        let spacing = CGFloat(options.spacing)
        let gridWidth = CGFloat(options.columns) * thumbnailSize.width + CGFloat(max(options.columns - 1, 0)) * spacing
        let gridHeight = CGFloat(options.rows) * thumbnailSize.height + CGFloat(max(options.rows - 1, 0)) * spacing
        let headerHeight = calculatedHeaderHeight(options: options)

        let canvasSize = CGSize(
            width: horizontalPadding * 2 + gridWidth,
            height: verticalPadding * 2 + headerHeight + gridHeight
        )

        let image = NSImage(size: canvasSize)
        image.lockFocus()
        color(fromHex: options.backgroundHex).setFill()
        NSBezierPath(rect: NSRect(origin: .zero, size: canvasSize)).fill()

        let metadataColor = color(fromHex: options.metadataTextHex)
        let titleAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: options.fileNameFontSize, weight: .semibold),
            .foregroundColor: NSColor.white
        ]
        let durationAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: options.durationFontSize, weight: .medium),
            .foregroundColor: metadataColor.withAlphaComponent(0.9)
        ]
        let fileSizeAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: options.fileSizeFontSize, weight: .medium),
            .foregroundColor: metadataColor.withAlphaComponent(0.9)
        ]
        let resolutionAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: options.resolutionFontSize, weight: .medium),
            .foregroundColor: metadataColor.withAlphaComponent(0.9)
        ]

        let title = inputURL.lastPathComponent
        let durationText = timestampText(for: info.duration)
        let sizeText = ByteCountFormatter.string(fromByteCount: info.fileSizeBytes, countStyle: .file)
        let resolutionText = "\(max(info.width, 0)) x \(max(info.height, 0)) px"

        var currentY = canvasSize.height - verticalPadding
        let titleLineHeight = lineHeight(for: titleAttributes)
        title.draw(
            in: NSRect(x: horizontalPadding, y: currentY - titleLineHeight, width: canvasSize.width - horizontalPadding * 2, height: titleLineHeight),
            withAttributes: titleAttributes
        )
        currentY -= titleLineHeight + 6

        let metadataLineHeight = max(lineHeight(for: durationAttributes), lineHeight(for: fileSizeAttributes))
        let metadataLine = NSMutableAttributedString(string: durationText, attributes: durationAttributes)
        metadataLine.append(NSAttributedString(string: "  •  ", attributes: durationAttributes))
        metadataLine.append(NSAttributedString(string: sizeText, attributes: fileSizeAttributes))
        metadataLine.draw(
            in: NSRect(x: horizontalPadding, y: currentY - metadataLineHeight, width: canvasSize.width - horizontalPadding * 2, height: metadataLineHeight)
        )
        currentY -= metadataLineHeight + 4

        let resolutionLineHeight = lineHeight(for: resolutionAttributes)
        resolutionText.draw(
            in: NSRect(x: horizontalPadding, y: currentY - resolutionLineHeight, width: canvasSize.width - horizontalPadding * 2, height: resolutionLineHeight),
            withAttributes: resolutionAttributes
        )

        for row in 0..<options.rows {
            for column in 0..<options.columns {
                let index = row * options.columns + column
                let x = horizontalPadding + CGFloat(column) * (thumbnailSize.width + spacing)
                let y = verticalPadding + CGFloat(options.rows - 1 - row) * (thumbnailSize.height + spacing)
                let frame = NSRect(origin: CGPoint(x: x, y: y), size: thumbnailSize)

                let placeholder = NSBezierPath(roundedRect: frame, xRadius: 10, yRadius: 10)
                NSColor.white.withAlphaComponent(0.08).setFill()
                placeholder.fill()

                guard index < thumbnails.count else { continue }
                drawThumbnail(
                    thumbnails[index],
                    in: frame,
                    timestampFontSize: options.timestampFontSize
                )
            }
        }

        image.unlockFocus()
        try write(image: image, to: outputURL, format: options.format)
    }

    private static func calculatedHeaderHeight(options: ResolvedRenderOptions) -> CGFloat {
        let titleAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: options.fileNameFontSize, weight: .semibold)
        ]
        let durationAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: options.durationFontSize, weight: .medium)
        ]
        let fileSizeAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: options.fileSizeFontSize, weight: .medium)
        ]
        let resolutionAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: options.resolutionFontSize, weight: .medium)
        ]

        var height: CGFloat = 18
        height += lineHeight(for: titleAttributes) + 10
        height += max(lineHeight(for: durationAttributes), lineHeight(for: fileSizeAttributes)) + 8
        height += lineHeight(for: resolutionAttributes) + 6
        return max(height, 18)
    }

    private static func lineHeight(for attributes: [NSAttributedString.Key: Any]) -> CGFloat {
        ceil(("Ag" as NSString).size(withAttributes: attributes).height)
    }

    private static func drawThumbnail(_ thumbnail: ThumbnailFrame, in frame: NSRect, timestampFontSize: CGFloat) {
        let image = thumbnail.image
        let sourceSize = image.size
        guard sourceSize.width > 0, sourceSize.height > 0 else { return }

        let scale = max(frame.width / sourceSize.width, frame.height / sourceSize.height)
        let drawSize = CGSize(width: sourceSize.width * scale, height: sourceSize.height * scale)
        let drawOrigin = CGPoint(x: frame.midX - drawSize.width / 2, y: frame.midY - drawSize.height / 2)
        let drawRect = NSRect(origin: drawOrigin, size: drawSize)

        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current?.imageInterpolation = .high
        let clipPath = NSBezierPath(roundedRect: frame, xRadius: 10, yRadius: 10)
        clipPath.addClip()
        image.draw(in: drawRect)
        drawTimestamp(timestampText(for: thumbnail.timestamp), in: frame, fontSize: timestampFontSize)
        NSGraphicsContext.restoreGraphicsState()
    }

    private static func drawTimestamp(_ text: String, in frame: NSRect, fontSize: CGFloat) {
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedDigitSystemFont(ofSize: fontSize, weight: .semibold),
            .foregroundColor: NSColor.white
        ]
        let textSize = text.size(withAttributes: attributes)
        let badgeRect = NSRect(
            x: frame.maxX - textSize.width - 18,
            y: frame.minY + 10,
            width: textSize.width + 12,
            height: textSize.height + 6
        )

        NSColor.black.withAlphaComponent(0.6).setFill()
        NSBezierPath(roundedRect: badgeRect, xRadius: 7, yRadius: 7).fill()
        text.draw(
            in: NSRect(x: badgeRect.minX + 6, y: badgeRect.minY + 3, width: textSize.width, height: textSize.height),
            withAttributes: attributes
        )
    }

    private static func frameTimes(duration: Double, count: Int) -> [Double] {
        guard count > 0 else { return [] }
        if count == 1 { return [duration / 2] }
        let start = duration * 0.05
        let end = duration * 0.95
        let step = (end - start) / Double(count - 1)
        return (0..<count).map { start + Double($0) * step }
    }

    private static func timestampText(for seconds: Double) -> String {
        let totalSeconds = Int(seconds.rounded())
        let hours = totalSeconds / 3600
        let minutes = (totalSeconds % 3600) / 60
        let secs = totalSeconds % 60
        if hours > 0 {
            return String(format: "%d:%02d:%02d", hours, minutes, secs)
        }
        return String(format: "%02d:%02d", minutes, secs)
    }

    private static func color(fromHex hex: String) -> NSColor {
        var value: UInt64 = 0
        guard Scanner(string: hex).scanHexInt64(&value) else {
            return NSColor(calibratedRed: 0.12, green: 0.13, blue: 0.15, alpha: 1)
        }
        return NSColor(
            calibratedRed: CGFloat((value & 0xFF0000) >> 16) / 255,
            green: CGFloat((value & 0x00FF00) >> 8) / 255,
            blue: CGFloat(value & 0x0000FF) / 255,
            alpha: 1
        )
    }

    private static func write(image: NSImage, to url: URL, format: String) throws {
        guard let bitmapRep = srgbBitmapRepresentation(from: image) else {
            throw CLIError.executionFailed("Could not encode output image.")
        }

        let data: Data?
        if format == "png" {
            data = bitmapRep.representation(
                using: .png,
                properties: [
                    .compressionFactor: 1.0,
                    .interlaced: false
                ]
            )
        } else {
            data = bitmapRep.representation(using: .jpeg, properties: [.compressionFactor: 0.92])
        }

        guard let outputData = data else {
            throw CLIError.executionFailed("Could not create \(format.uppercased()) data.")
        }
        try outputData.write(to: url)
    }

    private static func srgbBitmapRepresentation(from image: NSImage) -> NSBitmapImageRep? {
        guard let sourceCGImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
            guard let tiff = image.tiffRepresentation else { return nil }
            return NSBitmapImageRep(data: tiff)
        }
        let targetWidth = max(Int(image.size.width.rounded()), 1)
        let targetHeight = max(Int(image.size.height.rounded()), 1)
        guard let colorSpace = CGColorSpace(name: CGColorSpace.sRGB) else {
            guard let tiff = image.tiffRepresentation else { return nil }
            return NSBitmapImageRep(data: tiff)
        }
        guard let context = CGContext(
            data: nil,
            width: targetWidth,
            height: targetHeight,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: colorSpace,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else {
            guard let tiff = image.tiffRepresentation else { return nil }
            return NSBitmapImageRep(data: tiff)
        }

        context.interpolationQuality = .high
        context.draw(
            sourceCGImage,
            in: CGRect(x: 0, y: 0, width: targetWidth, height: targetHeight)
        )

        guard let converted = context.makeImage() else {
            guard let tiff = image.tiffRepresentation else { return nil }
            return NSBitmapImageRep(data: tiff)
        }
        return NSBitmapImageRep(cgImage: converted)
    }

    private static func runProcess(
        executable: String,
        arguments: [String],
        timeoutSeconds: TimeInterval = 120
    ) throws -> (Int32, String, String) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments

        let tempDirectory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: tempDirectory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: tempDirectory) }

        let stdoutURL = tempDirectory.appendingPathComponent("stdout.txt")
        let stderrURL = tempDirectory.appendingPathComponent("stderr.txt")
        FileManager.default.createFile(atPath: stdoutURL.path, contents: nil)
        FileManager.default.createFile(atPath: stderrURL.path, contents: nil)

        let stdoutHandle = try FileHandle(forWritingTo: stdoutURL)
        let stderrHandle = try FileHandle(forWritingTo: stderrURL)
        process.standardOutput = stdoutHandle
        process.standardError = stderrHandle

        try process.run()
        let deadline = Date().addingTimeInterval(timeoutSeconds)
        while process.isRunning && Date() < deadline {
            Thread.sleep(forTimeInterval: 0.1)
        }
        if process.isRunning {
            process.terminate()
            throw CLIError.executionTimedOut("Command timed out after \(Int(timeoutSeconds))s: \(URL(fileURLWithPath: executable).lastPathComponent)")
        }
        try stdoutHandle.close()
        try stderrHandle.close()

        let stdoutData = (try? Data(contentsOf: stdoutURL)) ?? Data()
        let stderrData = (try? Data(contentsOf: stderrURL)) ?? Data()
        let stdout = String(data: stdoutData, encoding: .utf8) ?? ""
        let stderr = String(data: stderrData, encoding: .utf8) ?? ""

        let exitCode = process.terminationStatus
        if exitCode != 0 {
            let message = stderr.trimmingCharacters(in: .whitespacesAndNewlines)
            throw CLIError.executionFailed(
                message.isEmpty
                    ? "Command failed (\(executable)) with exit code \(exitCode)."
                    : message
            )
        }

        return (exitCode, stdout, stderr)
    }

    private static func resolveToolPath(explicitEnvName: String, toolName: String) throws -> String {
        if let explicit = ProcessInfo.processInfo.environment[explicitEnvName], !explicit.isEmpty,
           FileManager.default.isExecutableFile(atPath: explicit) {
            return explicit
        }

        if let bundledToolPath = bundledToolPath(toolName: toolName),
           FileManager.default.isExecutableFile(atPath: bundledToolPath) {
            return bundledToolPath
        }

        let projectToolPath = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
            .appendingPathComponent(".cache/ffmpeg-install", isDirectory: true)
            .appendingPathComponent(currentArchitectureFolderName, isDirectory: true)
            .appendingPathComponent("bin", isDirectory: true)
            .appendingPathComponent(toolName, isDirectory: false)
            .path

        if FileManager.default.isExecutableFile(atPath: projectToolPath) {
            return projectToolPath
        }

        let path = ProcessInfo.processInfo.environment["PATH"] ?? ""
        for component in path.split(separator: ":") {
            let candidate = URL(fileURLWithPath: String(component)).appendingPathComponent(toolName).path
            if FileManager.default.isExecutableFile(atPath: candidate) {
                return candidate
            }
        }

        throw CLIError.toolNotFound("\(toolName) not found. Build bundled tools with Scripts/build-ffmpeg.sh.")
    }

    private static func bundledToolPath(toolName: String) -> String? {
        let executableURL = URL(fileURLWithPath: CommandLine.arguments[0]).standardizedFileURL
        let macOSDirectory = executableURL.deletingLastPathComponent()
        let contentsDirectory = macOSDirectory.deletingLastPathComponent()
        return contentsDirectory
            .appendingPathComponent("Resources", isDirectory: true)
            .appendingPathComponent("Tools", isDirectory: true)
            .appendingPathComponent(currentArchitectureFolderName, isDirectory: true)
            .appendingPathComponent(toolName, isDirectory: false)
            .path
    }

    private static var currentArchitectureFolderName: String {
        #if arch(arm64)
        return "arm64"
        #elseif arch(x86_64)
        return "x86_64"
        #else
        return "unknown"
        #endif
    }

    private static func printHelp() {
        print(
            """
            Thumbnail Grid Studio CLI

            Usage:
              thumbnail-grid-studio-cli --input <video> [--input <video> ...] --output-dir <directory> [options]
              thumbnail-grid-studio-cli <video1> <video2> ... --output-dir <directory> [options]

            Options:
              --h, -h, --help         Show this help text.
              --input <file>          Input video file (repeatable).
              --output-dir <dir>      Output directory for generated images. (required)
              --columns <int>         Grid columns (fallback: GUI setting).
              --rows <int>            Grid rows (fallback: GUI setting).
              --format <jpg|png>      Output image format (fallback: GUI setting).
              --width <px>            Thumbnail width in pixels (fallback: GUI setting/auto).
              --height <px>           Thumbnail height in pixels (fallback: GUI setting/auto).
              --spacing <px>          Spacing in pixels (fallback: GUI setting).
              --background <RRGGBB>   Grid background color as hex (fallback: GUI setting).
              --metadata-color <RRGGBB> Metadata text color as hex (fallback: GUI setting).
              --file-name-font-size <n> Header filename font size (fallback: GUI setting).
              --duration-font-size <n> Header duration font size (fallback: GUI setting).
              --file-size-font-size <n> Header file size font size (fallback: GUI setting).
              --resolution-font-size <n> Header resolution font size (fallback: GUI setting).
              --timestamp-font-size <n> Thumbnail timestamp font size (fallback: GUI setting).
            """
        )
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

do {
    try CLIRunner.run()
} catch {
    fputs("Error: \(error.localizedDescription)\n", stderr)
    fputs("Use --h for help.\n", stderr)
    exit(1)
}
