using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace XboxMetroLauncher.Utilities;

internal static class AppPaths
{
	private static readonly string[] LegacyUserDataFolders = new string[3]
	{
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DashX360"),
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DashX360"),
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "DashX360Data")
	};

	public static string AppFolder
	{
		get
		{
			string text = Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(text))
			{
				try
				{
					text = Process.GetCurrentProcess().MainModule?.FileName;
				}
				catch
				{
					text = null;
				}
			}
			string text2 = (string.IsNullOrWhiteSpace(text) ? AppContext.BaseDirectory : Path.GetDirectoryName(text));
			return Path.TrimEndingDirectorySeparator(string.IsNullOrWhiteSpace(text2) ? AppContext.BaseDirectory : text2);
		}
	}

	public static string UserDataFolder => EnsureFolder(ResolveUserDataFolder());

	public static string LogsFolder => Path.Combine(ResolveUserDataFolder(), "Logs");

	public static bool IsPartyLinkTestInstance => HasCommandLineSwitch("--party-link-test") || string.Equals(Environment.GetEnvironmentVariable("DASHX360_PARTY_LINK_TEST"), "1", StringComparison.OrdinalIgnoreCase);

	public static IReadOnlyList<string> LegacyDataRoots()
	{
		return new[] { Path.Combine(AppFolder, "UserData") }.Concat(LegacyUserDataFolders).Where((string path) => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	public static IReadOnlyList<string> CandidateRoots()
	{
		List<string> list = new List<string>();
		AddRoot(list, AppFolder);
		AddRoot(list, Environment.CurrentDirectory);
		AddRoot(list, AppContext.BaseDirectory);
		return list;
	}

	public static string FindFolder(string relativePath, Func<string, bool>? predicate = null)
	{
		foreach (string item in CandidateRoots())
		{
			string text = Path.Combine(item, relativePath);
			if (Directory.Exists(text) && (predicate == null || predicate(text)))
			{
				return text;
			}
		}
		return Path.Combine(AppFolder, relativePath);
	}

	public static string FindFile(string relativePath)
	{
		foreach (string item in CandidateRoots())
		{
			string text = Path.Combine(item, relativePath);
			if (File.Exists(text))
			{
				return text;
			}
		}
		return Path.Combine(AppFolder, relativePath);
	}

	public static string ResolvePath(string path)
	{
		if (!Path.IsPathRooted(path))
		{
			var writable = Path.Combine(UserDataFolder, path);
            return File.Exists(writable) ? writable : Path.Combine(AppFolder, path);
		}
		return path;
	}

    public static string WritableFolder(string relativePath)
    {
        var destination = SafePaths.Within(UserDataFolder, relativePath);
        Directory.CreateDirectory(destination);
        return destination;
    }

    public static void MigrateMutableAssets()
    {
        foreach (var relative in new[] { Path.Combine("Assets", "Custom Files"), Path.Combine("Assets", "GameArt", "Steam") })
        {
            var source = Path.Combine(AppFolder, relative);
            var destination = SafePaths.Within(UserDataFolder, relative);
            if (!Directory.Exists(source) || Directory.Exists(destination)) continue;
            foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                var target = SafePaths.Within(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }
    }

	private static void AddRoot(List<string> roots, string? path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
			if (!roots.Any((string root) => string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase)))
			{
				roots.Add(fullPath);
			}
		}
	}

	private static string ResolveUserDataFolder()
	{
		string text = Environment.GetEnvironmentVariable("DASHX360_USER_DATA_FOLDER");
		if (!string.IsNullOrWhiteSpace(text))
		{
			return Path.GetFullPath(text);
		}
		string text2 = GetCommandLineOption("--userdata") ?? GetCommandLineOption("--user-data");
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return Path.GetFullPath(text2);
		}
		return HasCommandLineSwitch("--portable")
            ? Path.Combine(AppFolder, "UserData")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DashX360", "UserData");
	}

	private static string? GetCommandLineOption(string optionName)
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		for (int i = 0; i < commandLineArgs.Length - 1; i++)
		{
			if (string.Equals(commandLineArgs[i], optionName, StringComparison.OrdinalIgnoreCase))
			{
				return commandLineArgs[i + 1];
			}
		}
		return null;
	}

	private static bool HasCommandLineSwitch(string switchName)
	{
		return Environment.GetCommandLineArgs().Any((string argument) => string.Equals(argument, switchName, StringComparison.OrdinalIgnoreCase));
	}

	private static string EnsureFolder(string path)
	{
		Directory.CreateDirectory(path);
		return path;
	}
}
