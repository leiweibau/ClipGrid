import AppKit
import Foundation
import XCTest
@testable import ThumbnailGridStudio

final class VideoFormatTests: XCTestCase {
    func testAVIWithXvidAndDivxAndRawM4V() async throws {
        let tools = try bundledDevelopmentTools()
        setenv("THUMBNAIL_GRID_STUDIO_FFMPEG", tools.ffmpeg.path, 1)
        setenv("THUMBNAIL_GRID_STUDIO_FFPROBE", tools.ffprobe.path, 1)

        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("ThumbnailGridStudioFormatTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }

        let fixtures = [
            Fixture(name: "xvid.avi", extraArguments: ["-vtag", "XVID"]),
            Fixture(name: "divx.avi", extraArguments: ["-vtag", "DIVX"]),
            Fixture(name: "mpeg4-part2.m4v", extraArguments: ["-f", "m4v"])
        ]

        for fixture in fixtures {
            let url = directory.appendingPathComponent(fixture.name)
            try run(
                tools.ffmpeg,
                arguments: [
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-f", "lavfi", "-i", "testsrc=size=160x90:rate=10",
                    "-t", "1", "-c:v", "mpeg4"
                ] + fixture.extraArguments + [url.path]
            )

            let metadata = try await VideoProcessingService.loadMetadata(for: url)
            XCTAssertEqual(metadata.resolution, CGSize(width: 160, height: 90), fixture.name)
            XCTAssertGreaterThan(metadata.duration, 0, fixture.name)
            XCTAssertEqual(metadata.videoCodec, "mpeg4", fixture.name)

            let frames = try await VideoProcessingService.generateThumbnails(
                for: url,
                duration: metadata.duration,
                count: 4,
                maxSize: CGSize(width: 80, height: 45)
            )
            XCTAssertGreaterThanOrEqual(frames.count, 3, fixture.name)
            XCTAssertLessThanOrEqual(frames.count, 4, fixture.name)
            XCTAssertEqual(frames.map(\.timestamp), frames.map(\.timestamp).sorted(), fixture.name)

            let exportedURL = directory.appendingPathComponent("\(fixture.name).png")
            guard let representation = NSBitmapImageRep(data: frames[0].image.tiffRepresentation ?? Data()),
                  let png = representation.representation(using: .png, properties: [:]) else {
                XCTFail("Could not encode \(fixture.name)")
                continue
            }
            try png.write(to: exportedURL)
            XCTAssertGreaterThan(try exportedURL.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0, 0)

            if fixture.name == "xvid.avi" {
                let collector = FullResolutionFrameCollector()
                let scaledFrames = try await VideoProcessingService.generateThumbnails(
                    for: url,
                    duration: metadata.duration,
                    count: 4,
                    maxSize: CGSize(width: 80, height: 45),
                    fullResolutionFrameHandler: { index, frame in
                        await collector.record(index: index, size: frame.image.size)
                    }
                )
                let collected = await collector.frames
                XCTAssertEqual(collected.map(\.index), collected.map(\.index).sorted())
                XCTAssertEqual(collected.count, scaledFrames.count)
                XCTAssertTrue(collected.allSatisfy { $0.size.width == 160 && $0.size.height == 90 })
                XCTAssertTrue(scaledFrames.allSatisfy { $0.image.size.width <= 80 && $0.image.size.height <= 45 })
            }
        }
    }

    private func bundledDevelopmentTools() throws -> (ffmpeg: URL, ffprobe: URL) {
        let packageRoot = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
        #if arch(arm64)
        let architecture = "arm64"
        #else
        let architecture = "x86_64"
        #endif
        let binaryDirectory = packageRoot
            .appendingPathComponent(".cache/ffmpeg-install", isDirectory: true)
            .appendingPathComponent(architecture, isDirectory: true)
            .appendingPathComponent("bin", isDirectory: true)
        let ffmpeg = binaryDirectory.appendingPathComponent("ffmpeg")
        let ffprobe = binaryDirectory.appendingPathComponent("ffprobe")
        guard FileManager.default.isExecutableFile(atPath: ffmpeg.path),
              FileManager.default.isExecutableFile(atPath: ffprobe.path) else {
            throw XCTSkip("Bundled FFmpeg development tools are unavailable")
        }
        return (ffmpeg, ffprobe)
    }

    private func run(_ executable: URL, arguments: [String]) throws {
        let process = Process()
        process.executableURL = executable
        process.arguments = arguments
        let errorPipe = Pipe()
        process.standardError = errorPipe
        try process.run()
        process.waitUntilExit()
        let error = errorPipe.fileHandleForReading.readDataToEndOfFile()
        XCTAssertEqual(
            process.terminationStatus,
            0,
            String(data: error, encoding: .utf8) ?? "Fixture generation failed"
        )
    }
}

private struct Fixture {
    let name: String
    let extraArguments: [String]
}

private actor FullResolutionFrameCollector {
    private(set) var frames: [(index: Int, size: CGSize)] = []

    func record(index: Int, size: CGSize) {
        frames.append((index, size))
        frames.sort { $0.index < $1.index }
    }
}
