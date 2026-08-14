import Foundation
import XCTest
@testable import ThumbnailGridStudio

final class DemoBenchmarkTests: XCTestCase {
    func testDemoVideoThroughput() async throws {
        guard ProcessInfo.processInfo.environment["TGS_RUN_BENCHMARKS"] == "1" else {
            throw XCTSkip("Set TGS_RUN_BENCHMARKS=1 to run demo performance measurements")
        }

        let packageRoot = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
        let demoDirectory = packageRoot
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .appendingPathComponent("demo", isDirectory: true)
        let videos = try FileManager.default.contentsOfDirectory(
            at: demoDirectory,
            includingPropertiesForKeys: nil
        ).filter { ["mp4", "mkv", "mov"].contains($0.pathExtension.lowercased()) }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }
        XCTAssertEqual(videos.count, 3)

        let sequentialStart = ContinuousClock.now
        var sequentialResults: [DemoBenchmarkResult] = []
        for video in videos {
            sequentialResults.append(await Self.process(video))
        }
        let sequentialDuration = sequentialStart.duration(to: .now)

        let rollingStart = ContinuousClock.now
        let rollingResults = await RollingTaskPool.map(videos, maxConcurrent: 2) {
            await Self.process($0)
        }
        let rollingDuration = rollingStart.duration(to: .now)

        XCTAssertTrue(sequentialResults.allSatisfy(\.succeeded))
        XCTAssertTrue(rollingResults.allSatisfy(\.succeeded))
        print("TGS benchmark sequential=\(sequentialDuration) rolling2=\(rollingDuration)")
    }

    private static func process(_ url: URL) async -> DemoBenchmarkResult {
        do {
            let metadata = try await VideoProcessingService.loadMetadata(for: url)
            let frames = try await VideoProcessingService.generateThumbnails(
                for: url,
                duration: metadata.duration,
                count: 16,
                maxSize: CGSize(width: 320, height: 180)
            )
            return DemoBenchmarkResult(succeeded: !frames.isEmpty)
        } catch {
            return DemoBenchmarkResult(succeeded: false)
        }
    }
}

private struct DemoBenchmarkResult: Sendable {
    let succeeded: Bool
}
