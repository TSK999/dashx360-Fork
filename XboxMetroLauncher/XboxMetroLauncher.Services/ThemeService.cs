using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class ThemeService : IThemeService
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private static readonly HashSet<string> ReservedWindowsFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"CON",
		"PRN",
		"AUX",
		"NUL",
		"COM1",
		"COM2",
		"COM3",
		"COM4",
		"COM5",
		"COM6",
		"COM7",
		"COM8",
		"COM9",
		"LPT1",
		"LPT2",
		"LPT3",
		"LPT4",
		"LPT5",
		"LPT6",
		"LPT7",
		"LPT8",
		"LPT9"
	};

	private readonly string _themesRoot;

	public ThemeService()
	{
		_themesRoot = Path.GetFullPath(AppPaths.FindFolder(Path.Combine("Assets", "Custom Files", "Themes")));
		Directory.CreateDirectory(_themesRoot);
	}

	public async Task<IReadOnlyList<DashboardTheme>> LoadThemesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		List<DashboardTheme> themes = new List<DashboardTheme>
		{
			new DashboardTheme
			{
				Name = "Xbox 360",
				IsBuiltIn = true
			}
		};
		foreach (string item in Directory.EnumerateDirectories(_themesRoot).OrderBy<string, string>((string path) => path, StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				DirectoryInfo directoryInfo = new DirectoryInfo(item);
				if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
				{
					continue;
				}
				DashboardTheme dashboardTheme = await LoadThemeAsync(item, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				if (dashboardTheme != null)
				{
					themes.Add(dashboardTheme);
				}
			}
			catch
			{
			}
		}
		return (from @group in themes.GroupBy<DashboardTheme, string>((DashboardTheme theme) => theme.Name, StringComparer.OrdinalIgnoreCase)
			select @group.First() into theme
			orderby (!theme.IsBuiltIn) ? 1 : 0
			select theme).ThenBy<DashboardTheme, string>((DashboardTheme theme) => theme.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public async Task<DashboardTheme> CreateThemeAsync(string themeName, string? homeImagePath, string? gamesImagePath, string? settingsImagePath, string? appsImagePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		string safeName = (string.IsNullOrWhiteSpace(themeName) ? "Custom Theme" : themeName.Trim());
		string path = CreateSafeFolderName(safeName);
		string folderPath = GetContainedPath(_themesRoot, path, "theme folder");
		Directory.CreateDirectory(folderPath);
		DashboardThemeManifest manifest = new DashboardThemeManifest
		{
			Name = safeName
		};
		await SaveThemeImageAsync(homeImagePath, GetContainedPath(folderPath, manifest.HomeImage, "theme image"), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await SaveThemeImageAsync(gamesImagePath, GetContainedPath(folderPath, manifest.GamesImage, "theme image"), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await SaveThemeImageAsync(settingsImagePath, GetContainedPath(folderPath, manifest.SettingsImage, "theme image"), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await SaveThemeImageAsync(appsImagePath, GetContainedPath(folderPath, manifest.AppsImage, "theme image"), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await using (FileStream stream = File.Create(GetContainedPath(folderPath, "theme.json", "theme manifest")))
		{
			await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		return new DashboardTheme
		{
			Name = safeName,
			FolderPath = folderPath,
			HomeBackgroundPath = GetThemeImagePath(folderPath, manifest.HomeImage),
			GamesBackgroundPath = GetThemeImagePath(folderPath, manifest.GamesImage),
			SettingsBackgroundPath = GetThemeImagePath(folderPath, manifest.SettingsImage),
			AppsBackgroundPath = GetThemeImagePath(folderPath, manifest.AppsImage)
		};
	}

	private static async Task<DashboardTheme?> LoadThemeAsync(string folderPath, CancellationToken cancellationToken)
	{
		folderPath = Path.GetFullPath(folderPath);
		string path = GetContainedPath(folderPath, "theme.json", "theme manifest");
		DashboardThemeManifest manifest;
		if (File.Exists(path))
		{
			await using FileStream stream = File.OpenRead(path);
			manifest = (await JsonSerializer.DeserializeAsync<DashboardThemeManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)) ?? new DashboardThemeManifest();
		}
		else
		{
			manifest = new DashboardThemeManifest
			{
				Name = Path.GetFileName(folderPath)
			};
		}
		string text = (string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileName(folderPath) : manifest.Name.Trim());
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		return new DashboardTheme
		{
			Name = text,
			FolderPath = folderPath,
			HomeBackgroundPath = GetThemeImagePath(folderPath, manifest.HomeImage),
			GamesBackgroundPath = GetThemeImagePath(folderPath, manifest.GamesImage),
			SettingsBackgroundPath = GetThemeImagePath(folderPath, manifest.SettingsImage),
			AppsBackgroundPath = GetThemeImagePath(folderPath, manifest.AppsImage)
		};
	}

	private static string GetThemeImagePath(string folderPath, string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return string.Empty;
		}
		string path;
		try
		{
			path = GetContainedPath(folderPath, fileName, "theme image");
		}
		catch (InvalidDataException)
		{
			return string.Empty;
		}
		if (!File.Exists(path))
		{
			return string.Empty;
		}
		return Path.GetRelativePath(AppPaths.AppFolder, path);
	}

	private static async Task SaveThemeImageAsync(string? sourcePath, string destinationPath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
		{
			DeleteIfExists(destinationPath);
			return;
		}
		await Task.Run(delegate
		{
			using FileStream bitmapStream = File.OpenRead(sourcePath);
			BitmapFrame bitmapFrame = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.FirstOrDefault();
			if (bitmapFrame == null)
			{
				throw new InvalidOperationException("Theme image could not be loaded.");
			}
			PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder
			{
				Frames = { bitmapFrame }
			};
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
			using FileStream stream = File.Create(destinationPath);
			pngBitmapEncoder.Save(stream);
		}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static void DeleteIfExists(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static string GetContainedPath(string rootPath, string relativePath, string description)
	{
		if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
		{
			throw new InvalidDataException($"Invalid {description} path.");
		}
		string fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
		string rootPrefix = fullRoot + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException($"The {description} path escapes its allowed folder.");
		}
		return fullPath;
	}

	private static string CreateSafeFolderName(string themeName)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string text = new string((from ch in themeName
			where !invalid.Contains(ch)
			select (!char.IsWhiteSpace(ch)) ? ch : '_').ToArray()).Trim(' ', '.', '_');
		if (text.Length > 80)
		{
			text = text.Substring(0, 80).TrimEnd(' ', '.', '_');
		}
		string reservedCandidate = text.Split('.')[0];
		if (string.IsNullOrWhiteSpace(text) || text == "." || text == ".." || ReservedWindowsFileNames.Contains(reservedCandidate))
		{
			text = "Custom_Theme";
		}
		return text;
	}
}
