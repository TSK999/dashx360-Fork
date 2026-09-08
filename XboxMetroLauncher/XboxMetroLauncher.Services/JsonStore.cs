using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class JsonStore : IJsonStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    private readonly string _rootPath;
    public JsonStore(string rootPath) { _rootPath = Path.GetFullPath(rootPath); Directory.CreateDirectory(_rootPath); }

    public Task<T?> ReadAsync<T>(string fileName, CancellationToken cancellationToken = default) =>
        StorageLock.RunAsync(_rootPath, async () =>
        {
            var path = SafePaths.Within(_rootPath, fileName);
            if (!File.Exists(path)) return default(T);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task WriteAsync<T>(string fileName, T value, CancellationToken cancellationToken = default)
    {
        // Snapshot on the caller's context before another UI edit can mutate collections.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        return StorageLock.RunAsync(_rootPath, () => AtomicFile.WriteAsync(SafePaths.Within(_rootPath, fileName), bytes, cancellationToken), cancellationToken);
    }
}
