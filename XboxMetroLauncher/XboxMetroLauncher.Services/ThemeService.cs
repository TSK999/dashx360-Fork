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

	private readonly string _themesRoot;

	public ThemeService()
	{
		_themesRoot = AppPaths.WritableFolder(Path.Combine("Assets", "Custom Files", "Themes"));
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
		if ((await LoadThemesAsync(cancellationToken).ConfigureAwait(false)).Any(theme => string.Equals(theme.Name, safeName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A theme with this name already exists. Choose another name.");
        string path = "theme-" + Guid.NewGuid().ToString("N");
		string folderPath = SafePaths.Within(_themesRoot, path);
		Directory.CreateDirectory(folderPath);
		DashboardThemeManifest manifest = new DashboardThemeManifest
		{
			Name = safeName
		};
		await SaveThemeImageAsync(homeImagePath, Path.Combine(folderPath, manifest.HomeImage), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await SaveThemeImageAsync(gamesImagePath, Path.Combine(folderPath, manifest.GamesImage), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await SaveThemeImageAsync(settingsImagePath, Path.Combine(folderPath, manifest.SettingsImage), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await SaveThemeImageAsync(appsImagePath, Path.Combine(folderPath, manifest.AppsImage), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await AtomicFile.WriteJsonAsync(Path.Combine(folderPath, "theme.json"), manifest, JsonOptions, cancellationToken);
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
		string path = Path.Combine(folderPath, "theme.json");
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
		string path = SafePaths.Within(folderPath, fileName);
		if (!File.Exists(path))
		{
			return string.Empty;
		}
		return path;
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

}
