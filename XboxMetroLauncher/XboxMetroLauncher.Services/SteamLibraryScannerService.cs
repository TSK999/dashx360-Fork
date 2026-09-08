using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class SteamLibraryScannerService : ISteamLibraryScannerService
{
	private const double DefaultSteamCoverZoom = 1.06;

	private sealed record SteamManifestEntry(string AppId, string Name, string InstallDir);

	private sealed record SteamArtworkResult(SteamArtworkFileResult Cover, SteamArtworkFileResult Header, SteamArtworkFileResult Hero, SteamArtworkFileResult Logo);

	private sealed record SteamArtworkFileResult(string? FinalAssetPath, string? LocalSourcePath, string? DownloadedPath);

	private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

	private static readonly string SteamArtworkDebugLogPath = Path.Combine(AppPaths.LogsFolder, "steam-art-debug.log");

	private static readonly string[] FallbackSteamPaths = new string[2] { "C:\\Program Files (x86)\\Steam", "C:\\Program Files\\Steam" };

	public Task<SteamGameScanResult> ScanAsync(GameLibrary library, CancellationToken cancellationToken = default, IProgress<LibraryScanProgress>? progress = null) => Task.Run(() => ScanCoreAsync(library, cancellationToken, progress), cancellationToken);

    private async Task<SteamGameScanResult> ScanCoreAsync(GameLibrary library, CancellationToken cancellationToken, IProgress<LibraryScanProgress>? progress)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string steamPath = FindSteamPath();
		if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
		{
			return new SteamGameScanResult
			{
				Message = "Steam does not appear to be installed."
			};
		}
		List<string> list = (from path in GetSteamLibraryPaths(steamPath)
			select Path.Combine(path, "steamapps")).Where(Directory.Exists).SelectMany((string steamAppsPath) => SafeEnumerateFiles(steamAppsPath, "appmanifest_*.acf")).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (list.Count == 0)
		{
			return new SteamGameScanResult
			{
				Message = "No installed Steam games were found."
			};
		}
		int added = 0;
		int updated = 0;
		int skipped = 0;
		StringBuilder debugReport = new StringBuilder();
		StringBuilder stringBuilder = debugReport;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder);
		handler.AppendLiteral("[STEAM ART] ");
		handler.AppendFormatted(DateTime.Now, "yyyy-MM-dd HH:mm:ss");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = debugReport;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder);
		handler.AppendLiteral("steam path: ");
		handler.AppendFormatted(steamPath);
		stringBuilder3.AppendLine(ref handler);
		int completed = 0;
        foreach (string item in list)
		{
            progress?.Report(new LibraryScanProgress(++completed, Path.GetFileName(item)));
			cancellationToken.ThrowIfCancellationRequested();
			SteamManifestEntry entry;
			try
			{
				entry = ParseManifest(item);
			}
			catch
			{
				skipped++;
				continue;
			}
			if ((object)entry == null || string.IsNullOrWhiteSpace(entry.AppId) || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.InstallDir))
			{
				skipped++;
				continue;
			}
			if (!uint.TryParse(entry.AppId, out _)) { skipped++; continue; }
            string installPath;
            try { installPath = SafePaths.Within(Path.Combine(Path.GetDirectoryName(item)!, "common"), entry.InstallDir); }
            catch (InvalidDataException) { skipped++; continue; }
			if (!Directory.Exists(installPath) || !await IsInstalledSteamGameAsync(entry, cancellationToken))
			{
				skipped++;
				continue;
			}
			SteamArtworkResult artwork = await ResolveArtworkAsync(steamPath, entry.AppId, cancellationToken);
			GameMetadata gameMetadata = library.Games.FirstOrDefault((GameMetadata game) => string.Equals(game.SteamAppId, entry.AppId, StringComparison.OrdinalIgnoreCase));
			if (gameMetadata != null)
			{
				gameMetadata.Title = entry.Name;
				gameMetadata.Platform = "Steam";
				gameMetadata.Genre = "Imported";
				gameMetadata.SteamAppId = entry.AppId;
				gameMetadata.InstallPath = installPath;
				gameMetadata.LaunchType = "Steam";
				gameMetadata.LaunchCommand = "steam://rungameid/" + entry.AppId;
				gameMetadata.WorkingDirectory = installPath;
				ApplySteamArtwork(gameMetadata, artwork);
				ApplyDefaultSteamCoverZoom(gameMetadata);
				AppendArtworkDebug(debugReport, entry.AppId, entry.Name, artwork, gameMetadata.CoverArtPath);
				updated++;
			}
			else if (library.Games.FirstOrDefault((GameMetadata game) => !string.Equals(game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(game.Title) && string.Equals(game.Title, entry.Name, StringComparison.OrdinalIgnoreCase)) != null)
			{
				skipped++;
			}
			else
			{
				GameMetadata gameMetadata2 = new GameMetadata
				{
					Title = entry.Name,
					Platform = "Steam",
					Genre = "Imported",
					LaunchType = "Steam",
					SteamAppId = entry.AppId,
					InstallPath = installPath,
					LaunchCommand = "steam://rungameid/" + entry.AppId,
					WorkingDirectory = installPath
				};
				ApplySteamArtwork(gameMetadata2, artwork);
				ApplyDefaultSteamCoverZoom(gameMetadata2);
				library.Games.Add(gameMetadata2);
				AppendArtworkDebug(debugReport, entry.AppId, entry.Name, artwork, gameMetadata2.CoverArtPath);
				added++;
			}
		}
		WriteArtworkDebugReport(debugReport);
		string message = ((added == 0 && updated == 0) ? "No Steam games were imported." : $"Steam scan complete. Added: {added}, Updated: {updated}, Skipped: {skipped}.");
		return new SteamGameScanResult
		{
			Added = added,
			Updated = updated,
			Skipped = skipped,
			Message = message
		};
	}

	private static string? FindSteamPath()
	{
		string text = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Valve\\Steam", "SteamPath", null) as string;
		if (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text))
		{
			return NormalizeDirectoryPath(text);
		}
		return FallbackSteamPaths.FirstOrDefault(Directory.Exists);
	}

	private static IReadOnlyList<string> GetSteamLibraryPaths(string steamPath)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizeDirectoryPath(steamPath) };
		string path = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
		if (!File.Exists(path))
		{
			return hashSet.ToList();
		}
		foreach (string item in File.ReadLines(path))
		{
			string text = item.Trim();
			if (!text.Contains("\"path\"", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			List<string> list = ExtractQuotedTokens(text);
			if (list.Count >= 2)
			{
				string path2 = list[list.Count - 1].Replace("\\\\", "\\");
				if (Directory.Exists(path2))
				{
					hashSet.Add(NormalizeDirectoryPath(path2));
				}
			}
		}
		return hashSet.ToList();
	}

	private static SteamManifestEntry? ParseManifest(string manifestPath)
	{
		string text = null;
		string text2 = null;
		string text3 = null;
		foreach (string item in File.ReadLines(manifestPath))
		{
			List<string> list = ExtractQuotedTokens(item);
			if (list.Count >= 2)
			{
				string text4 = list[0];
				string text5 = list[1];
				if (text4.Equals("appid", StringComparison.OrdinalIgnoreCase))
				{
					text = text5;
				}
				else if (text4.Equals("name", StringComparison.OrdinalIgnoreCase))
				{
					text2 = text5;
				}
				else if (text4.Equals("installdir", StringComparison.OrdinalIgnoreCase))
				{
					text3 = text5;
				}
			}
		}
		if (text != null || text2 != null || text3 != null)
		{
			return new SteamManifestEntry(text ?? string.Empty, text2 ?? string.Empty, text3 ?? string.Empty);
		}
		return null;
	}

	private static async Task<bool> IsInstalledSteamGameAsync(SteamManifestEntry entry, CancellationToken cancellationToken)
	{
		if (LooksLikeNonGameSteamApp(entry.Name))
		{
			return false;
		}
		try
		{
			using HttpResponseMessage response = await HttpClient.GetAsync("https://store.steampowered.com/api/appdetails?appids=" + Uri.EscapeDataString(entry.AppId) + "&filters=basic", cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return true;
			}
			string text = await response.Content.ReadAsStringAsync(cancellationToken);
			if (text.Contains("\"success\":false", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (text.Contains("\"type\":\"game\"", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (text.Contains("\"type\":\"software\"", StringComparison.OrdinalIgnoreCase) || text.Contains("\"type\":\"tool\"", StringComparison.OrdinalIgnoreCase) || text.Contains("\"type\":\"demo\"", StringComparison.OrdinalIgnoreCase) || text.Contains("\"type\":\"dlc\"", StringComparison.OrdinalIgnoreCase) || text.Contains("\"type\":\"music\"", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
		{
		}
		return true;
	}

	private static bool LooksLikeNonGameSteamApp(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return true;
		}
		string[] array = new string[14]
		{
			"Steamworks Common Redistributables",
			"Steam Linux Runtime",
			"SteamVR",
			"Proton",
			"Dedicated Server",
			"SDK",
			"Tool",
			"Editor",
			"Benchmark",
			"Soundtrack",
			"OpenXR",
			"Runtime",
			"Redistributable",
			"Software"
		};
		return array.Any((string token) => name.Contains(token, StringComparison.OrdinalIgnoreCase));
	}

	private static List<string> ExtractQuotedTokens(string line)
	{
		List<string> list = new List<string>();
		bool flag = false;
		int num = 0;
		for (int i = 0; i < line.Length; i++)
		{
			if (line[i] == '"')
			{
				if (!flag)
				{
					flag = true;
					num = i + 1;
				}
				else
				{
					int num2 = num;
					list.Add(line.Substring(num2, i - num2));
					flag = false;
				}
			}
		}
		return list;
	}

	private static IEnumerable<string> SafeEnumerateFiles(string folderPath, string searchPattern)
	{
		try
		{
			return Directory.EnumerateFiles(folderPath, searchPattern, SearchOption.TopDirectoryOnly);
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static string NormalizeDirectoryPath(string path)
	{
		return Path.GetFullPath(path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
	}

    private static async Task<SteamArtworkResult> ResolveArtworkAsync(string steamPath, string appId, CancellationToken cancellationToken)
    {
        var localCacheRoot = Path.Combine(steamPath, "appcache", "librarycache");
        var appCacheRoot = AppPaths.WritableFolder(Path.Combine("Assets", "GameArt", "Steam", appId));
        var cdn = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId;
        // Four independent assets per game; keep concurrency bounded at four requests.
        var cover = ResolveArtworkFileAsync(localCacheRoot, appCacheRoot, appId, "cover", new[] { appId + "_library_600x900.jpg", appId + "_library_600x900.png" }, new[] { cdn + "/library_600x900.jpg", cdn + "/library_600x900.png" }, cancellationToken);
        var header = ResolveArtworkFileAsync(localCacheRoot, appCacheRoot, appId, "header", new[] { appId + "_header.jpg", appId + "_header.png" }, new[] { cdn + "/header.jpg" }, cancellationToken);
        var hero = ResolveArtworkFileAsync(localCacheRoot, appCacheRoot, appId, "hero", new[] { appId + "_library_hero.jpg", appId + "_library_hero.png" }, new[] { cdn + "/library_hero.jpg" }, cancellationToken);
        var logo = ResolveArtworkFileAsync(localCacheRoot, appCacheRoot, appId, "logo", new[] { appId + "_logo.png" }, new[] { cdn + "/logo.png" }, cancellationToken);
        await Task.WhenAll(cover, header, hero, logo).ConfigureAwait(false);
        return new SteamArtworkResult(await cover, await header, await hero, await logo);
    }

	private static async Task<SteamArtworkFileResult> ResolveArtworkFileAsync(string localCacheRoot, string appCacheRoot, string appId, string destinationName, IReadOnlyList<string> localFileNames, IReadOnlyList<string> downloadUrls, CancellationToken cancellationToken)
	{
		string text = null;
		if (Directory.Exists(localCacheRoot))
		{
			text = localFileNames.Select((string fileName) => Path.Combine(localCacheRoot, fileName)).FirstOrDefault(IsValidArtworkFile);
		}
		if (text != null)
		{
			string text2 = Path.Combine(appCacheRoot, destinationName + Path.GetExtension(text));
			if (!IsValidArtworkFile(text2))
			{
				File.Copy(text, text2, overwrite: true);
			}
			return new SteamArtworkFileResult(MakePortableAssetPath(appId, Path.GetFileName(text2)), text, null);
		}
		foreach (string downloadUrl in downloadUrls)
		{
			string extension = Path.GetExtension(new Uri(downloadUrl).AbsolutePath);
			string destinationPath = Path.Combine(appCacheRoot, destinationName + extension);
			if (IsValidArtworkFile(destinationPath))
			{
				return new SteamArtworkFileResult(MakePortableAssetPath(appId, Path.GetFileName(destinationPath)), null, null);
			}
			try
			{
				using HttpResponseMessage response = await HttpClient.GetAsync(downloadUrl, cancellationToken);
				if (!response.IsSuccessStatusCode)
				{
					continue;
				}
				string text3 = response.Content.Headers.ContentType?.MediaType;
				if (!string.IsNullOrWhiteSpace(text3) && !text3.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				byte[] array = await response.Content.ReadAsByteArrayAsync(cancellationToken);
				if (array.Length >= 128)
				{
					await AtomicFile.WriteAsync(destinationPath, array, cancellationToken);
					if (IsValidArtworkFile(destinationPath))
					{
						return new SteamArtworkFileResult(MakePortableAssetPath(appId, Path.GetFileName(destinationPath)), null, destinationPath);
					}
					File.Delete(destinationPath);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
			catch
			{
			}
		}
		return new SteamArtworkFileResult(null, null, null);
	}

	private static void ApplySteamArtwork(GameMetadata game, SteamArtworkResult artwork)
	{
		string appId = (string.IsNullOrWhiteSpace(game.SteamAppId) ? "unknown" : game.SteamAppId);
		if (ShouldAssignManagedSteamArtwork(game.CoverArtPath, appId))
		{
			game.CoverArtPath = artwork.Cover.FinalAssetPath ?? string.Empty;
		}
		if (ShouldAssignManagedSteamArtwork(game.HeaderImagePath, appId) && !string.IsNullOrWhiteSpace(artwork.Header.FinalAssetPath))
		{
			game.HeaderImagePath = artwork.Header.FinalAssetPath;
		}
		string text = ((!string.IsNullOrWhiteSpace(artwork.Hero.FinalAssetPath)) ? artwork.Hero.FinalAssetPath : artwork.Header.FinalAssetPath);
		if (ShouldAssignManagedSteamArtwork(game.BackgroundArtPath, appId) && !string.IsNullOrWhiteSpace(text))
		{
			game.BackgroundArtPath = text;
		}
		if (ShouldAssignManagedSteamArtwork(game.LogoImagePath, appId) && !string.IsNullOrWhiteSpace(artwork.Logo.FinalAssetPath))
		{
			game.LogoImagePath = artwork.Logo.FinalAssetPath;
		}
	}

	private static bool ShouldAssignManagedSteamArtwork(string existingPath, string appId)
	{
		if (string.IsNullOrWhiteSpace(existingPath))
		{
			return true;
		}
		string text = Path.Combine("Assets", "GameArt", "Steam", appId).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		if (!Path.IsPathRooted(existingPath))
		{
			return existingPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).StartsWith(text, StringComparison.OrdinalIgnoreCase);
		}
		string path = Path.Combine(AppPaths.UserDataFolder, text);
		return Path.GetFullPath(existingPath).StartsWith(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
	}

	private static void ApplyDefaultSteamCoverZoom(GameMetadata game)
	{
		if (game.CoverZoom <= 1.351 && !string.IsNullOrWhiteSpace(game.CoverArtPath) && IsManagedSteamArtwork(game.CoverArtPath, game.SteamAppId))
		{
			game.CoverZoom = DefaultSteamCoverZoom;
			game.CoverOffsetX = 0.0;
			game.CoverOffsetY = 0.0;
		}
	}

	private static bool IsManagedSteamArtwork(string existingPath, string appId)
	{
		if (string.IsNullOrWhiteSpace(existingPath) || string.IsNullOrWhiteSpace(appId))
		{
			return false;
		}
		string text = Path.Combine("Assets", "GameArt", "Steam", appId).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		if (!Path.IsPathRooted(existingPath))
		{
			return existingPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).StartsWith(text, StringComparison.OrdinalIgnoreCase);
		}
		string path = Path.Combine(AppPaths.UserDataFolder, text);
		return Path.GetFullPath(existingPath).StartsWith(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
	}

	private static string MakePortableAssetPath(string appId, string fileName)
	{
		return Path.Combine(AppPaths.UserDataFolder, "Assets", "GameArt", "Steam", appId, fileName);
	}

	private static bool IsValidArtworkFile(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return false;
		}
		try
		{
			return new FileInfo(path).Length >= 128;
		}
		catch
		{
			return false;
		}
	}

	private static void AppendArtworkDebug(StringBuilder builder, string appId, string title, SteamArtworkResult artwork, string finalCoverArtPath)
	{
		StringBuilder stringBuilder = builder;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("appid: ");
		handler.AppendFormatted(appId);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = builder;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder);
		handler.AppendLiteral("title: ");
		handler.AppendFormatted(title);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = builder;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder);
		handler.AppendLiteral("found local cover path: ");
		handler.AppendFormatted(artwork.Cover.LocalSourcePath ?? "<none>");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = builder;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(23, 1, stringBuilder);
		handler.AppendLiteral("downloaded cover path: ");
		handler.AppendFormatted(artwork.Cover.DownloadedPath ?? "<none>");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder = builder;
		StringBuilder stringBuilder6 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder);
		handler.AppendLiteral("final CoverImagePath value: ");
		handler.AppendFormatted(finalCoverArtPath);
		stringBuilder6.AppendLine(ref handler);
		builder.AppendLine();
	}

	private static void WriteArtworkDebugReport(StringBuilder builder)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SteamArtworkDebugLogPath));
			File.WriteAllText(SteamArtworkDebugLogPath, builder.ToString());
		}
		catch
		{
		}
	}
}
