import XCTest
@testable import ThumbnailGridStudio

final class RollingTaskPoolTests: XCTestCase {
    func testPreservesInputOrderAndConcurrencyLimit() async {
        let tracker = ConcurrencyTracker()
        let inputs = Array(0..<12)

        let output = await RollingTaskPool.map(inputs, maxConcurrent: 3) { value in
            await tracker.started()
            try? await Task.sleep(for: .milliseconds((12 - value) * 2))
            await tracker.finished()
            return value * 2
        }

        XCTAssertEqual(output, inputs.map { $0 * 2 })
        let peak = await tracker.peak
        XCTAssertEqual(peak, 3)
    }

    func testClampsInvalidConcurrencyToOne() async {
        let tracker = ConcurrencyTracker()

        _ = await RollingTaskPool.map(Array(0..<4), maxConcurrent: 0) { value in
            await tracker.started()
            try? await Task.sleep(for: .milliseconds(2))
            await tracker.finished()
            return value
        }

        let peak = await tracker.peak
        XCTAssertEqual(peak, 1)
    }
}

private actor ConcurrencyTracker {
    private var active = 0
    private(set) var peak = 0

    func started() {
        active += 1
        peak = max(peak, active)
    }

    func finished() {
        active -= 1
    }
}
