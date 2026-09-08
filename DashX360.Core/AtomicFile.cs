using System.Text.Json;

namespace XboxMetroLauncher.Utilities;

public static class AtomicFile
{
    public static async Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken token = default)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            // A rename on the same volume publishes the complete file in one operation.
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Task WriteJsonAsync<T>(string path, T value, JsonSerializerOptions? options = null, CancellationToken token = default) =>
        WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, options), token);
}
