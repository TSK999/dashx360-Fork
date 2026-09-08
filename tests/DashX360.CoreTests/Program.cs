using System.Text;
using System.Text.Json;
using XboxMetroLauncher.Utilities;

var failures = new List<string>();
var root = Path.Combine(Path.GetTempPath(), "dashx360-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    await Check("Reject traversal and Windows reserved paths", () =>
    {
        foreach (var name in new[] { ".", "..", "../outside", "..\\outside", "C:\\outside", "/outside", "con", "NUL.png", "LPT9", "theme.", "theme ", "file:stream", "a//b" })
            Throws<InvalidDataException>(() => SafePaths.Within(root, name));
        Assert(SafePaths.Within(root, "Themes/My Theme/home.png").StartsWith(root));
        return Task.CompletedTask;
    });
    await Check("Process identity requires exact executable or a folder boundary", () =>
    {
        Assert(SafePaths.MatchesExecutable("C:\\Games\\One\\game.exe", null, "C:\\Games\\One"));
        Assert(!SafePaths.MatchesExecutable("C:\\Games\\OneOther\\game.exe", null, "C:\\Games\\One"));
        Assert(!SafePaths.MatchesExecutable("C:\\Windows\\notepad.exe", "C:\\Games\\One\\game.exe", null));
        Assert(!SafePaths.MatchesExecutable(null, "C:\\Games\\One\\game.exe", "C:\\Games\\One"));
        return Task.CompletedTask;
    });
    await Check("Partial backups preserve absent sections and reject future/null data", () =>
    {
        using var partial = JsonDocument.Parse("{\"customthemes\":[]}");
        var sections = BackupSchema.Sections(partial.RootElement);
        Assert(sections.Contains("CustomThemes") && !sections.Contains("Library"));
        foreach (var json in new[] { "{\"ExportVersion\":\"999\"}", "{\"Library\":null}", "{\"CustomThemes\":{}}", "{\"Settings\":{},\"settings\":{}}" })
        { using var document = JsonDocument.Parse(json); Throws<InvalidDataException>(() => BackupSchema.Sections(document.RootElement)); }
        return Task.CompletedTask;
    });
    await Check("Atomic replacement preserves the previous file on cancellation", async () =>
    {
        var path = Path.Combine(root, "atomic.json");
        await AtomicFile.WriteJsonAsync(path, new { Value = "before" });
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => AtomicFile.WriteJsonAsync(path, new { Value = "after" }, token: cancelled.Token));
        Assert(File.ReadAllText(path).Contains("before"));
        Assert(!Directory.EnumerateFiles(root, "*.tmp").Any());
    });
    await Check("Concurrent readers only see complete JSON", async () =>
    {
        var path = Path.Combine(root, "readers.json");
        await AtomicFile.WriteJsonAsync(path, new { Value = new string('x', 20000) });
        var reader = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++)
            { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); using var doc = await JsonDocument.ParseAsync(stream); Assert(doc.RootElement.GetProperty("Value").GetString()!.Length == 20000); }
        });
        for (var i = 0; i < 30; i++) await AtomicFile.WriteJsonAsync(path, new { Value = new string('y', 20000) });
        await reader;
    });
    await Check("Import failure rolls back replaced files and removes new assets", async () =>
    {
        var data = Path.Combine(root, "transaction"); Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), "old settings");
        await File.WriteAllTextAsync(Path.Combine(data, "library.json"), "old library");
        using var transaction = new DataTransaction(data);
        await File.WriteAllTextAsync(Path.Combine(transaction.StagingRoot, "settings.json"), "new settings");
        await File.WriteAllTextAsync(Path.Combine(transaction.StagingRoot, "library.json"), "new library");
        await File.WriteAllTextAsync(Path.Combine(transaction.StagingRoot, "image.png"), "new image");
        await ThrowsAsync<IOException>(() => transaction.CommitAsync(afterPublish: index => { if (index == 2) throw new IOException("simulated failure"); }));
        Assert(File.ReadAllText(Path.Combine(data, "settings.json")) == "old settings");
        Assert(File.ReadAllText(Path.Combine(data, "library.json")) == "old library");
        Assert(!File.Exists(Path.Combine(data, "image.png")));
    });
    await Check("Startup recovers a persisted interrupted import journal", async () =>
    {
        var data = Path.Combine(root, "recovery");
        var work = Path.Combine(data, ".import-transaction");
        Directory.CreateDirectory(Path.Combine(work, "original"));
        await File.WriteAllTextAsync(Path.Combine(work, "original", "settings.json"), "old");
        await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), "new");
        await AtomicFile.WriteJsonAsync(Path.Combine(work, "journal.json"), new[] { new DataTransaction.Entry("settings.json", true) });
        DataTransaction.Recover(data);
        Assert(File.ReadAllText(Path.Combine(data, "settings.json")) == "old");
        Assert(!Directory.Exists(work));
    });
    await Check("Storage transaction serializes competing writers and allows nested reads", async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var competingRan = false;
        var first = StorageLock.RunAsync(root, async () => { await StorageLock.RunAsync(root, () => Task.CompletedTask); entered.SetResult(); await release.Task; });
        await entered.Task;
        var second = StorageLock.RunAsync(root, () => { competingRan = true; return Task.CompletedTask; });
        Assert(!competingRan); release.SetResult(); await Task.WhenAll(first, second); Assert(competingRan);
    });
    await Check("Large app libraries have distinct reachable cells", () =>
    {
        foreach (var count in new[] { 0, 5, 6, 10, 11, 25, 100 })
        {
            var cells = Enumerable.Range(0, count).Select(AppLibraryLayout.Position).ToList();
            Assert(cells.Distinct().Count() == count);
            Assert(cells.All(p => p.X + 198 <= AppLibraryLayout.Width(count)));
        }
        return Task.CompletedTask;
    });
    await Check("Recursive scan handles mixed-case executables and cancellation", () =>
    {
        var data = Path.Combine(root, "scan", "nested"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "Game.EXE"), "");
        Assert(LibraryScan.FindExecutables(Path.GetDirectoryName(data)!, default).Count == 1);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Throws<OperationCanceledException>(() => LibraryScan.FindExecutables(root, cancelled.Token));
        return Task.CompletedTask;
    });
    await Check("Audio restart waits for prior worker cleanup", async () =>
    {
        using var began = new ManualResetEventSlim();
        using var cleanup = new ManualResetEventSlim();
        var active = 0; var maximum = 0; var starts = 0;
        using var worker = new CaptureWorker(token =>
        {
            var now = Interlocked.Increment(ref active); maximum = Math.Max(maximum, now);
            Interlocked.Increment(ref starts); began.Set();
            token.WaitHandle.WaitOne(); cleanup.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Decrement(ref active);
        }, ex => throw new Exception("Unexpected worker error", ex));
        worker.Start(); Assert(began.Wait(TimeSpan.FromSeconds(3)));
        worker.Stop(); worker.Start(); await Task.Delay(30); Assert(starts == 1);
        cleanup.Set();
        Assert(SpinWait.SpinUntil(() => Volatile.Read(ref starts) == 2, TimeSpan.FromSeconds(3)));
        worker.Dispose(); Assert(SpinWait.SpinUntil(() => !worker.IsRunning, TimeSpan.FromSeconds(3)));
        Assert(maximum == 1);
    });
}
finally { Directory.Delete(root, recursive: true); }
Console.WriteLine($"{11 - failures.Count}/11 regression checks passed.");
return failures.Count == 0 ? 0 : 1;

async Task Check(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add(name); Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
