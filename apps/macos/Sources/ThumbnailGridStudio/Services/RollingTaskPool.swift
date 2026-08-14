import Foundation

enum RollingTaskPool {
    static func map<Input: Sendable, Output: Sendable>(
        _ inputs: [Input],
        maxConcurrent: Int,
        onComplete: @escaping @Sendable (Output) async -> Void = { _ in },
        operation: @escaping @Sendable (Input) async -> Output
    ) async -> [Output] {
        guard !inputs.isEmpty else { return [] }

        let limit = min(max(maxConcurrent, 1), inputs.count)
        return await withTaskGroup(of: IndexedOutput<Output>.self) { group in
            var nextIndex = 0
            var completed: [IndexedOutput<Output>] = []
            completed.reserveCapacity(inputs.count)

            while nextIndex < limit {
                let index = nextIndex
                group.addTask {
                    IndexedOutput(index: index, value: await operation(inputs[index]))
                }
                nextIndex += 1
            }

            while let result = await group.next() {
                completed.append(result)
                await onComplete(result.value)

                if nextIndex < inputs.count {
                    let index = nextIndex
                    group.addTask {
                        IndexedOutput(index: index, value: await operation(inputs[index]))
                    }
                    nextIndex += 1
                }
            }

            return completed.sorted { $0.index < $1.index }.map(\.value)
        }
    }
}

private struct IndexedOutput<Value: Sendable>: Sendable {
    let index: Int
    let value: Value
}
