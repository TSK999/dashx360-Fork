namespace XboxMetroLauncher.Utilities;

public static class DataDirectoryLease
{
    public static FileStream Acquire(string root)
    {
        Directory.CreateDirectory(root);
        // File sharing also protects a shared override folder across Windows sessions.
        return new FileStream(Path.Combine(root, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
