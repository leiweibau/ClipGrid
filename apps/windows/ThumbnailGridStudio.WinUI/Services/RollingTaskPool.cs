namespace ThumbnailGridStudio.WinUI.Services;

internal static class RollingTaskPool
{
    public static async Task<TResult[]> MapAsync<TInput, TResult>(
        IReadOnlyList<TInput> inputs,
        int maxConcurrent,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        Action<TResult>? onComplete = null,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        var results = new TResult[inputs.Count];
        var nextIndex = -1;
        var workerCount = Math.Min(Math.Max(maxConcurrent, 1), inputs.Count);
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(async () =>
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var index = Interlocked.Increment(ref nextIndex);
                    if (index >= inputs.Count)
                    {
                        return;
                    }

                    var result = await operation(inputs[index], cancellationToken).ConfigureAwait(false);
                    results[index] = result;
                    onComplete?.Invoke(result);
                }
            }, cancellationToken))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
        return results;
    }
}
