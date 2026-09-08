namespace XboxMetroLauncher.Utilities;

public sealed record LibraryScanProgress(int Completed, string Location);

public static class LibraryScan
{
    public static IReadOnlyList<string> FindExecutables(string root, CancellationToken token, IProgress<LibraryScanProgress>? progress = null)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        var visited = 0;
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = false };
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new(++visited, directory));
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) result.Add(path);
                }
                foreach (var child in Directory.EnumerateDirectories(directory, "*", options))
                { token.ThrowIfCancellationRequested(); pending.Push(child); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A disappearing or protected directory does not discard the scan. */ }
        }
        return result;
    }
}
