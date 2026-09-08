using System.Text.Json;

namespace XboxMetroLauncher.Utilities;

/// <summary>Stages an import and journals original files before publishing any changes.
/// The caller holds StorageLock for the data root. Startup recovers interrupted imports.</summary>
public sealed class DataTransaction : IDisposable
{
    private readonly string root;
    private readonly string work;
    public string StagingRoot { get; }
    public sealed record Entry(string Path, bool Existed);

    public DataTransaction(string root)
    {
        this.root = Path.GetFullPath(root);
        Recover(root);
        work = Path.Combine(root, ".import-transaction");
        StagingRoot = Path.Combine(work, "stage");
        Directory.CreateDirectory(StagingRoot);
    }

    public async Task CommitAsync(CancellationToken token = default, Action<int>? afterPublish = null)
    {
        var entries = new List<Entry>();
        foreach (var staged in Directory.EnumerateFiles(StagingRoot, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(StagingRoot, staged);
            var destination = SafePaths.Within(root, relative);
            var existed = File.Exists(destination);
            entries.Add(new(relative, existed));
            if (existed) await AtomicFile.WriteAsync(SafePaths.Within(Path.Combine(work, "original"), relative), await File.ReadAllBytesAsync(destination, token), token);
        }
        await AtomicFile.WriteJsonAsync(Path.Combine(work, "journal.json"), entries, token: token);
        try
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                await AtomicFile.WriteAsync(SafePaths.Within(root, entry.Path), await File.ReadAllBytesAsync(SafePaths.Within(StagingRoot, entry.Path), token), token);
                afterPublish?.Invoke(index);
            }
            await AtomicFile.WriteAsync(Path.Combine(work, "committed"), Array.Empty<byte>(), token);
        }
        catch { Recover(root); throw; }
        Directory.Delete(work, recursive: true);
    }

    public static void Recover(string root)
    {
        var work = Path.Combine(root, ".import-transaction");
        if (!Directory.Exists(work)) return;
        var journal = Path.Combine(work, "journal.json");
        if (!File.Exists(Path.Combine(work, "committed")) && File.Exists(journal))
        {
            var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(journal)) ?? throw new InvalidDataException("The import recovery journal is unreadable.");
            foreach (var entry in entries)
            {
                var destination = SafePaths.Within(root, entry.Path);
                if (entry.Existed)
                    AtomicFile.WriteAsync(destination, File.ReadAllBytes(SafePaths.Within(Path.Combine(work, "original"), entry.Path))).GetAwaiter().GetResult();
                else if (File.Exists(destination)) File.Delete(destination);
            }
        }
        Directory.Delete(work, recursive: true);
    }

    public void Dispose()
    {
        // Retain a journal if rollback failed; startup will try recovery again.
        if (Directory.Exists(work) && !File.Exists(Path.Combine(work, "journal.json"))) Directory.Delete(work, true);
    }
}
