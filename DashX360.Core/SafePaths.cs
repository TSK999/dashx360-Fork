namespace XboxMetroLauncher.Utilities;

public static class SafePaths
{
    public static string FileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value != value.Trim() || value.EndsWith('.') ||
            value.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)))
            throw new InvalidDataException("An asset has an unsafe file or folder name.");
        var stem = value.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "CLOCK$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789".Contains(stem[3])))
            throw new InvalidDataException("An asset uses a reserved Windows name.");
        return value;
    }

    public static string Within(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("An asset path must be relative to its data folder.");
        var parts = relative.Replace('\\', '/').Split('/');
        foreach (var part in parts) FileName(part);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(parts)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An asset path leaves its data folder.");
        for (var current = path; current != null && current.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked asset folders are not supported.");
        return path;
    }

    public static bool MatchesExecutable(string? processPath, string? executable, string? installFolder)
    {
        if (string.IsNullOrWhiteSpace(processPath)) return false;
        static string Normalize(string path) => Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar)).TrimEnd(Path.DirectorySeparatorChar);
        try
        {
            var path = Normalize(processPath);
            if (!string.IsNullOrWhiteSpace(executable) && string.Equals(path, Normalize(executable), StringComparison.OrdinalIgnoreCase)) return true;
            return !string.IsNullOrWhiteSpace(installFolder) && path.StartsWith(Normalize(installFolder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }
}
