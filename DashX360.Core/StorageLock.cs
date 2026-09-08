using System.Collections.Concurrent;

namespace XboxMetroLauncher.Utilities;

public static class StorageLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AsyncLocal<string?> HeldRoot = new();

    public static async Task<T> RunAsync<T>(string root, Func<Task<T>> action, CancellationToken token = default)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(HeldRoot.Value, root, StringComparison.OrdinalIgnoreCase)) return await action().ConfigureAwait(false);
        var gate = Gates.GetOrAdd(root, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        var previous = HeldRoot.Value;
        try { HeldRoot.Value = root; return await action().ConfigureAwait(false); }
        finally { HeldRoot.Value = previous; gate.Release(); }
    }

    public static Task RunAsync(string root, Func<Task> action, CancellationToken token = default) =>
        RunAsync(root, async () => { await action().ConfigureAwait(false); return true; }, token);
}
