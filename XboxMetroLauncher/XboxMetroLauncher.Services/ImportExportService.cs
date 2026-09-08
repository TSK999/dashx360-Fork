using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class ImportExportService : IImportExportService
{
	private const string BackupFilePrefix = "DashX360_Backup";

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip
	};

	private readonly IGameLibraryService _libraryService;

	private readonly IProfileService _profileService;

	private readonly ISettingsService _settingsService;

	private readonly string _dataRoot;

	private readonly string _themesRoot;

    private string? _destinationRoot;

	public ImportExportService(IGameLibraryService libraryService, IProfileService profileService, ISettingsService settingsService, string dataRoot)
	{
		_libraryService = libraryService;
		_profileService = profileService;
		_settingsService = settingsService;
		_dataRoot = dataRoot;
		_themesRoot = Path.Combine(_dataRoot, "Assets", "Custom Files", "Themes");
		Directory.CreateDirectory(_themesRoot);
	}

	public async Task ExportAsync(GameLibrary library, Profile profile, AppSettings settings, string filePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		DashboardBackup dashboardBackup = new DashboardBackup();
		DashboardBackup dashboardBackup2 = dashboardBackup;
		dashboardBackup2.Settings = await BuildSettingsBackupAsync(settings, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		DashboardBackup dashboardBackup3 = dashboardBackup;
		dashboardBackup3.Profile = await BuildProfileBackupAsync(profile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		dashboardBackup.Library = CloneLibrary(library);
		DashboardBackup dashboardBackup4 = dashboardBackup;
		dashboardBackup4.CustomThemes = await BuildThemesBackupAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		dashboardBackup.Friends = await new JsonStore(_dataRoot).ReadAsync<FriendsData>("friends.json", cancellationToken) ?? new FriendsData();
        dashboardBackup.GameArtwork = await BuildArtworkAsync(library, cancellationToken);
        await WriteBackupAsync(dashboardBackup, filePath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

    public async Task<DashboardImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string? safetyPath = null;
        try
        {
            var data = await ReadAndValidateBackupAsync(filePath, cancellationToken).ConfigureAwait(false);
            return await StorageLock.RunAsync(_dataRoot, async () =>
            {
                var library = await _libraryService.LoadAsync(cancellationToken).ConfigureAwait(false);
                var profile = Clone(await _profileService.LoadAsync(cancellationToken).ConfigureAwait(false));
                var settings = Clone(await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false));
                using var transaction = new DataTransaction(_dataRoot);
                var staged = new ImportExportService(_libraryService, _profileService, _settingsService, transaction.StagingRoot) { _destinationRoot = _dataRoot };
                var stagedStore = new JsonStore(transaction.StagingRoot);
                if (data.IncludesSettings)
                {
                    var merged = MergeFields(await BuildSettingsBackupAsync(settings, cancellationToken), data.Backup.Settings, data.SettingsJson);
                    await stagedStore.WriteAsync("settings.json", await staged.MergeSettingsAsync(Clone(settings), merged, cancellationToken), cancellationToken);
                }
                if (data.IncludesProfile)
                {
                    var merged = MergeFields(await BuildProfileBackupAsync(profile, cancellationToken), data.Backup.Profile, data.ProfileJson);
                    await stagedStore.WriteAsync("profile.json", await staged.MergeProfileAsync(Clone(profile), merged, cancellationToken), cancellationToken);
                }
                if (data.IncludesLibrary)
                {
                    var importedLibrary = NormalizeLibrary(data.Backup.Library);
                    if (data.LibraryJson is JsonElement libraryJson)
                    {
                        if (!BackupSchema.Has(libraryJson, "Games")) importedLibrary.Games = CloneLibrary(library).Games;
                        if (!BackupSchema.Has(libraryJson, "LibraryPaths")) importedLibrary.LibraryPaths = library.LibraryPaths.ToList();
                    }
                    await staged.RestoreArtworkAsync(importedLibrary, data.Backup.GameArtwork, cancellationToken);
                    await stagedStore.WriteAsync("library.json", importedLibrary, cancellationToken);
                }
                if (data.IncludesThemes) await staged.RestoreThemesAsync(data.Backup.CustomThemes, cancellationToken);
                if (data.IncludesFriends) await stagedStore.WriteAsync("friends.json", data.Backup.Friends, cancellationToken);
                // All decoding and validation finishes before the first live file is replaced.
                safetyPath = await CreateSafetyBackupAsync(library, profile, settings, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new DashboardImportResult { Success = true, Message = "Dashboard data imported successfully.", SafetyBackupPath = safetyPath };
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            App.LogException(ex, "ImportExportService.ImportAsync");
            return new DashboardImportResult { Success = false, Message = "Import failed: " + ex.Message, SafetyBackupPath = safetyPath ?? string.Empty };
        }
    }

    private string PublishedPath(string stagedPath) => _destinationRoot == null ? stagedPath : SafePaths.Within(_destinationRoot, Path.GetRelativePath(_dataRoot, stagedPath));
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, SerializerOptions), SerializerOptions)!;
    private static T MergeFields<T>(T current, T imported, JsonElement? source)
    {
        if (source == null) return imported;
        var merged = JsonSerializer.SerializeToNode(current, SerializerOptions)!.AsObject();
        var values = JsonSerializer.SerializeToNode(imported, SerializerOptions)!.AsObject();
        foreach (var field in source.Value.EnumerateObject())
        {
            var key = merged.Select(p => p.Key).FirstOrDefault(k => k.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
            if (key != null) merged[key] = values[key]?.DeepClone();
        }
        return merged.Deserialize<T>(SerializerOptions)!;
    }

	private async Task<string> CreateSafetyBackupAsync(GameLibrary library, Profile profile, AppSettings settings, CancellationToken cancellationToken)
	{
		string text = Path.Combine(_dataRoot, "Backups");
		Directory.CreateDirectory(text);
		string path = $"{"DashX360_Backup"}_PreImport_{DateTime.Now:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.json";
		string backupPath = Path.Combine(text, path);
		await ExportAsync(library, profile, settings, backupPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		return backupPath;
	}

	private static async Task<DashboardBackupSettings> BuildSettingsBackupAsync(AppSettings settings, CancellationToken cancellationToken)
	{
		DashboardBackupSettings backup = new DashboardBackupSettings
		{
			StartFullscreen = settings.StartFullscreen,
            DashboardVolume = settings.DashboardVolume,
			PlayUiSounds = settings.PlayUiSounds,
			EnableControllerInput = settings.EnableControllerInput,
			LaunchOnWindowsStartup = settings.LaunchOnWindowsStartup,
			MinimizeOnGameLaunch = settings.MinimizeOnGameLaunch,
			EnableFakeLoading = settings.EnableFakeLoading,
			ThemeName = settings.ThemeName,
			BingSearchBaseUrl = settings.BingSearchBaseUrl,
			DisplayResolution = settings.DisplayResolution,
			OpenTrayGameId = settings.OpenTrayGameId,
			GameCoverFitMode = settings.GameCoverFitMode,
			DefaultAddDestination = settings.DefaultAddDestination,
			AudioOutputDeviceName = settings.AudioOutputDeviceName,
			DashboardTileColor = settings.DashboardTileColor,
			DashboardTileCustomizations = settings.DashboardTileCustomizations.ToDictionary<KeyValuePair<string, DashboardTileCustomization>, string, DashboardTileCustomization>((KeyValuePair<string, DashboardTileCustomization> keyValuePair) => keyValuePair.Key, (KeyValuePair<string, DashboardTileCustomization> keyValuePair) => new DashboardTileCustomization
			{
				ImagePath = keyValuePair.Value.ImagePath,
				TitleOverride = keyValuePair.Value.TitleOverride,
				SecondaryTitleOverride = keyValuePair.Value.SecondaryTitleOverride,
				LaunchExecutablePath = keyValuePair.Value.LaunchExecutablePath,
				LaunchWebAddress = keyValuePair.Value.LaunchWebAddress,
				Zoom = keyValuePair.Value.Zoom,
				OffsetX = keyValuePair.Value.OffsetX,
				OffsetY = keyValuePair.Value.OffsetY
			}, StringComparer.OrdinalIgnoreCase)
		};
		foreach (KeyValuePair<string, DashboardTileCustomization> pair in settings.DashboardTileCustomizations)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string imagePath = string.IsNullOrWhiteSpace(pair.Value.ImagePath) ? string.Empty : AppPaths.ResolvePath(pair.Value.ImagePath);
			if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
			{
				byte[] inArray = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				backup.DashboardTileImages.Add(new DashboardBackupTileImage
				{
					TileKey = pair.Key,
					FileName = Path.GetFileName(imagePath),
					ImageBase64 = Convert.ToBase64String(inArray)
				});
			}
		}
		return backup;
	}

	private async Task<AppSettings> MergeSettingsAsync(AppSettings current, DashboardBackupSettings imported, CancellationToken cancellationToken)
	{
		current.StartFullscreen = imported.StartFullscreen;
        if (imported.DashboardVolume.HasValue) current.DashboardVolume = imported.DashboardVolume.Value;
		current.PlayUiSounds = imported.PlayUiSounds;
		current.EnableControllerInput = imported.EnableControllerInput;
		current.LaunchOnWindowsStartup = imported.LaunchOnWindowsStartup;
		current.MinimizeOnGameLaunch = imported.MinimizeOnGameLaunch;
		current.EnableFakeLoading = imported.EnableFakeLoading;
		current.ThemeName = (string.IsNullOrWhiteSpace(imported.ThemeName) ? current.ThemeName : imported.ThemeName);
		current.BingSearchBaseUrl = (string.IsNullOrWhiteSpace(imported.BingSearchBaseUrl) ? current.BingSearchBaseUrl : imported.BingSearchBaseUrl);
		current.DisplayResolution = (string.IsNullOrWhiteSpace(imported.DisplayResolution) ? current.DisplayResolution : imported.DisplayResolution);
		current.OpenTrayGameId = imported.OpenTrayGameId ?? string.Empty;
		current.GameCoverFitMode = (string.IsNullOrWhiteSpace(imported.GameCoverFitMode) ? current.GameCoverFitMode : imported.GameCoverFitMode);
		current.DefaultAddDestination = (string.IsNullOrWhiteSpace(imported.DefaultAddDestination) ? current.DefaultAddDestination : imported.DefaultAddDestination);
		current.AudioOutputDeviceName = (string.IsNullOrWhiteSpace(imported.AudioOutputDeviceName) ? current.AudioOutputDeviceName : imported.AudioOutputDeviceName);
		current.DashboardTileColor = (string.IsNullOrWhiteSpace(imported.DashboardTileColor) ? current.DashboardTileColor : imported.DashboardTileColor);
		current.DashboardTileCustomizations = await RestoreDashboardTileCustomizationsAsync(imported.DashboardTileCustomizations, imported.DashboardTileImages, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		return current;
	}

	private async Task<Dictionary<string, DashboardTileCustomization>> RestoreDashboardTileCustomizationsAsync(Dictionary<string, DashboardTileCustomization>? importedCustomizations, IEnumerable<DashboardBackupTileImage>? importedImages, CancellationToken cancellationToken)
	{
		Dictionary<string, DashboardTileCustomization> restored = new Dictionary<string, DashboardTileCustomization>(StringComparer.OrdinalIgnoreCase);
		if (importedCustomizations == null)
		{
			return restored;
		}
		foreach (KeyValuePair<string, DashboardTileCustomization> importedCustomization in importedCustomizations)
		{
			restored[importedCustomization.Key] = new DashboardTileCustomization
			{
				ImagePath = (importedCustomization.Value.ImagePath ?? string.Empty),
				TitleOverride = (importedCustomization.Value.TitleOverride ?? string.Empty),
				SecondaryTitleOverride = (importedCustomization.Value.SecondaryTitleOverride ?? string.Empty),
				LaunchExecutablePath = (importedCustomization.Value.LaunchExecutablePath ?? string.Empty),
				LaunchWebAddress = (importedCustomization.Value.LaunchWebAddress ?? string.Empty),
				Zoom = ((importedCustomization.Value.Zoom <= 0.0) ? 1.0 : importedCustomization.Value.Zoom),
				OffsetX = importedCustomization.Value.OffsetX,
				OffsetY = importedCustomization.Value.OffsetY
			};
		}
		if (importedImages == null)
		{
			return restored;
		}
		string tileImageFolder = Path.Combine(_dataRoot, "ImportedAssets", "DashboardTiles");
		Directory.CreateDirectory(tileImageFolder);
		foreach (DashboardBackupTileImage importedImage in importedImages)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!string.IsNullOrWhiteSpace(importedImage.TileKey) && !string.IsNullOrWhiteSpace(importedImage.ImageBase64) && restored.TryGetValue(importedImage.TileKey, out DashboardTileCustomization customization))
			{
				string text = (string.IsNullOrWhiteSpace(importedImage.FileName) ? (MakeSafeFileName(importedImage.TileKey) + ".png") : MakeSafeFileName(importedImage.FileName));
				string destination = Path.Combine(tileImageFolder, text);
				if (File.Exists(destination))
				{
					destination = Path.Combine(tileImageFolder, $"{Path.GetFileNameWithoutExtension(text)}-{Guid.NewGuid():N}{Path.GetExtension(text)}");
				}
				byte[] bytes = BackupImages.Decode(importedImage.ImageBase64, Path.GetFileName(destination));
				await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				customization.ImagePath = PublishedPath(destination);
				customization = null;
			}
		}
		return restored;
	}

	private static async Task<DashboardBackupProfile> BuildProfileBackupAsync(Profile profile, CancellationToken cancellationToken)
	{
		DashboardBackupProfile backup = new DashboardBackupProfile
		{
			Gamertag = profile.Gamertag,
			Name = profile.Name,
			GamerPicturePath = profile.GamerPicturePath,
			Gamerscore = profile.Gamerscore,
			OnlineStatus = profile.OnlineStatus,
			Motto = profile.Motto,
			Location = profile.Location,
			Description = profile.Description
		};
		var picturePath = string.IsNullOrWhiteSpace(profile.GamerPicturePath) ? string.Empty : AppPaths.ResolvePath(profile.GamerPicturePath);
        if (File.Exists(picturePath))
		{
			backup.GamerPictureFileName = Path.GetFileName(profile.GamerPicturePath);
			backup.GamerPictureBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(picturePath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
		}
		return backup;
	}

	private async Task<Profile> MergeProfileAsync(Profile current, DashboardBackupProfile imported, CancellationToken cancellationToken)
	{
		current.Gamertag = (string.IsNullOrWhiteSpace(imported.Gamertag) ? current.Gamertag : imported.Gamertag);
		current.Name = string.IsNullOrWhiteSpace(imported.Name) ? current.Name : imported.Name;
		current.Gamerscore = Math.Max(0, imported.Gamerscore);
		current.OnlineStatus = (string.IsNullOrWhiteSpace(imported.OnlineStatus) ? current.OnlineStatus : imported.OnlineStatus);
		current.Motto = imported.Motto ?? string.Empty;
		current.Location = string.IsNullOrWhiteSpace(imported.Location) ? current.Location : imported.Location;
		current.Description = imported.Description ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(imported.GamerPictureBase64))
		{
			current.GamerPicturePath = await RestoreProfilePictureAsync(imported, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		else if (!string.IsNullOrWhiteSpace(imported.GamerPicturePath) && File.Exists(imported.GamerPicturePath))
		{
			current.GamerPicturePath = imported.GamerPicturePath;
		}
		return current;
	}

	private async Task<string> RestoreProfilePictureAsync(DashboardBackupProfile imported, CancellationToken cancellationToken)
	{
		string text = Path.Combine(_dataRoot, "ImportedAssets", "Profile");
		Directory.CreateDirectory(text);
		string text2 = (string.IsNullOrWhiteSpace(imported.GamerPictureFileName) ? "profile-import.png" : MakeSafeFileName(imported.GamerPictureFileName));
		string destination = Path.Combine(text, text2);
		if (File.Exists(destination))
		{
			destination = Path.Combine(text, $"{Path.GetFileNameWithoutExtension(text2)}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(text2)}");
		}
		byte[] bytes = BackupImages.Decode(imported.GamerPictureBase64, Path.GetFileName(destination));
		await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		return PublishedPath(destination);
	}

	private async Task<List<DashboardBackupTheme>> BuildThemesBackupAsync(CancellationToken cancellationToken)
	{
		List<DashboardBackupTheme> themes = new List<DashboardBackupTheme>();
		foreach (string folderPath in Directory.EnumerateDirectories(_themesRoot).OrderBy<string, string>((string result) => result, StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string path = Path.Combine(folderPath, "theme.json");
			DashboardThemeManifest manifest;
			if (File.Exists(path))
			{
				await using FileStream stream = File.OpenRead(path);
				manifest = (await JsonSerializer.DeserializeAsync<DashboardThemeManifest>(stream, SerializerOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)) ?? new DashboardThemeManifest();
			}
			else
			{
				manifest = new DashboardThemeManifest
				{
					Name = Path.GetFileName(folderPath)
				};
			}
			List<DashboardBackupTheme> list = themes;
			DashboardBackupTheme dashboardBackupTheme = new DashboardBackupTheme
			{
				Name = (string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileName(folderPath) : manifest.Name),
				FolderName = Path.GetFileName(folderPath),
				HomeImageFileName = (string.IsNullOrWhiteSpace(manifest.HomeImage) ? "home.png" : manifest.HomeImage)
			};
			DashboardBackupTheme dashboardBackupTheme2 = dashboardBackupTheme;
			dashboardBackupTheme2.HomeImageBase64 = await ReadThemeImageBase64Async(folderPath, manifest.HomeImage, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			dashboardBackupTheme.GamesImageFileName = (string.IsNullOrWhiteSpace(manifest.GamesImage) ? "games.png" : manifest.GamesImage);
			DashboardBackupTheme dashboardBackupTheme3 = dashboardBackupTheme;
			dashboardBackupTheme3.GamesImageBase64 = await ReadThemeImageBase64Async(folderPath, manifest.GamesImage, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			dashboardBackupTheme.SettingsImageFileName = (string.IsNullOrWhiteSpace(manifest.SettingsImage) ? "settings.png" : manifest.SettingsImage);
			DashboardBackupTheme dashboardBackupTheme4 = dashboardBackupTheme;
			dashboardBackupTheme4.SettingsImageBase64 = await ReadThemeImageBase64Async(folderPath, manifest.SettingsImage, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			dashboardBackupTheme.AppsImageFileName = (string.IsNullOrWhiteSpace(manifest.AppsImage) ? "apps.png" : manifest.AppsImage);
			DashboardBackupTheme dashboardBackupTheme5 = dashboardBackupTheme;
			dashboardBackupTheme5.AppsImageBase64 = await ReadThemeImageBase64Async(folderPath, manifest.AppsImage, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			list.Add(dashboardBackupTheme);
			manifest = null;
		}
		return themes;
	}

	private async Task RestoreThemesAsync(IEnumerable<DashboardBackupTheme>? themes, CancellationToken cancellationToken)
	{
		if (themes == null)
		{
			return;
		}
		foreach (DashboardBackupTheme theme in themes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string text = (string.IsNullOrWhiteSpace(theme.FolderName) ? MakeSafeFolderName(theme.Name) : MakeSafeFolderName(theme.FolderName));
			string folderPath = SafePaths.Within(_themesRoot, text);
			Directory.CreateDirectory(folderPath);
			DashboardThemeManifest manifest = new DashboardThemeManifest
			{
				Name = (string.IsNullOrWhiteSpace(theme.Name) ? text : theme.Name),
				HomeImage = (string.IsNullOrWhiteSpace(theme.HomeImageFileName) ? "home.png" : MakeSafeFileName(theme.HomeImageFileName)),
				GamesImage = (string.IsNullOrWhiteSpace(theme.GamesImageFileName) ? "games.png" : MakeSafeFileName(theme.GamesImageFileName)),
				SettingsImage = (string.IsNullOrWhiteSpace(theme.SettingsImageFileName) ? "settings.png" : MakeSafeFileName(theme.SettingsImageFileName)),
				AppsImage = (string.IsNullOrWhiteSpace(theme.AppsImageFileName) ? "apps.png" : MakeSafeFileName(theme.AppsImageFileName))
			};
			await WriteThemeImageAsync(folderPath, manifest.HomeImage, theme.HomeImageBase64, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			await WriteThemeImageAsync(folderPath, manifest.GamesImage, theme.GamesImageBase64, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			await WriteThemeImageAsync(folderPath, manifest.SettingsImage, theme.SettingsImageBase64, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			await WriteThemeImageAsync(folderPath, manifest.AppsImage, theme.AppsImageBase64, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			await using FileStream stream = File.Create(Path.Combine(folderPath, "theme.json"));
			await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private static async Task<string> ReadThemeImageBase64Async(string folderPath, string fileName, CancellationToken cancellationToken)
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
		return Convert.ToBase64String(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
	}

	private static async Task WriteThemeImageAsync(string folderPath, string fileName, string base64, CancellationToken cancellationToken)
	{
		string path = SafePaths.Within(folderPath, fileName);
		if (string.IsNullOrWhiteSpace(base64))
		{
			DeleteIfExists(path);
			return;
		}
		byte[] bytes = BackupImages.Decode(base64, fileName);
		await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static GameLibrary CloneLibrary(GameLibrary library) => NormalizeLibrary(Clone(library));

	private static GameLibrary NormalizeLibrary(GameLibrary? library)
	{
		if (library == null)
		{
			library = new GameLibrary();
		}
		GameLibrary gameLibrary = library;
		if (gameLibrary.LibraryPaths == null)
		{
			List<string> list = (gameLibrary.LibraryPaths = new List<string>());
		}
		gameLibrary = library;
		if (gameLibrary.Games == null)
		{
			List<GameMetadata> list3 = (gameLibrary.Games = new List<GameMetadata>());
		}
		foreach (GameMetadata game in library.Games)
		{
            if (game == null) throw new InvalidDataException("The game library contains a null entry.");
            if (!string.IsNullOrWhiteSpace(game.LaunchType) && !new[] { "Exe", "Steam", "Url" }.Contains(game.LaunchType, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("The game library contains an unsupported launch type.");
			game.Id = (string.IsNullOrWhiteSpace(game.Id) ? Guid.NewGuid().ToString("N") : game.Id);
			GameMetadata gameMetadata = game;
			if (gameMetadata.Title == null)
			{
				string text = (gameMetadata.Title = string.Empty);
			}
			game.LaunchType = (string.IsNullOrWhiteSpace(game.LaunchType) ? "Exe" : game.LaunchType);
			gameMetadata = game;
			if (gameMetadata.ExecutablePath == null)
			{
				string text = (gameMetadata.ExecutablePath = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.SteamAppId == null)
			{
				string text = (gameMetadata.SteamAppId = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.InstallPath == null)
			{
				string text = (gameMetadata.InstallPath = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.LaunchCommand == null)
			{
				string text = (gameMetadata.LaunchCommand = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.Arguments == null)
			{
				string text = (gameMetadata.Arguments = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.WorkingDirectory == null)
			{
				string text = (gameMetadata.WorkingDirectory = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.CoverArtPath == null)
			{
				string text = (gameMetadata.CoverArtPath = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.HeaderImagePath == null)
			{
				string text = (gameMetadata.HeaderImagePath = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.StoreScreenshotPath == null)
			{
				string text = (gameMetadata.StoreScreenshotPath = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.BackgroundArtPath == null)
			{
				string text = (gameMetadata.BackgroundArtPath = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.LogoImagePath == null)
			{
				string text = (gameMetadata.LogoImagePath = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.Genre == null)
			{
				string text = (gameMetadata.Genre = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.Rating == null)
			{
				string text = (gameMetadata.Rating = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.MultiplayerInfo == null)
			{
				string text = (gameMetadata.MultiplayerInfo = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.CoOpInfo == null)
			{
				string text = (gameMetadata.CoOpInfo = string.Empty);
			}
			gameMetadata = game;
			if (gameMetadata.Platform == null)
			{
				string text = (gameMetadata.Platform = string.Empty);
			}
		}
		return library;
	}

	private static async Task WriteBackupAsync(DashboardBackup backup, string filePath, CancellationToken cancellationToken)
	{
		string directoryName = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		ValidateExportImages(backup);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, SerializerOptions);
        if (bytes.Length > 256L * 1024 * 1024) throw new InvalidDataException("The backup exceeds the supported 256 MB limit.");
        await AtomicFile.WriteAsync(filePath, bytes, cancellationToken).ConfigureAwait(false);
	}

    private static void ValidateExportImages(DashboardBackup backup)
    {
        static void Validate(string data, string name) { if (!string.IsNullOrWhiteSpace(data)) BackupImages.Decode(data, name); }
        Validate(backup.Profile.GamerPictureBase64, backup.Profile.GamerPictureFileName);
        foreach (var image in backup.Settings.DashboardTileImages) Validate(image.ImageBase64, image.FileName);
        foreach (var image in backup.GameArtwork) Validate(image.ImageBase64, image.FileName);
        foreach (var theme in backup.CustomThemes)
        {
            Validate(theme.HomeImageBase64, theme.HomeImageFileName);
            Validate(theme.GamesImageBase64, theme.GamesImageFileName);
            Validate(theme.SettingsImageBase64, theme.SettingsImageFileName);
            Validate(theme.AppsImageBase64, theme.AppsImageFileName);
        }
    }

	private static async Task<ImportedDashboardData> ReadAndValidateBackupAsync(string filePath, CancellationToken cancellationToken)
	{
		if (!File.Exists(filePath))
		{
			throw new InvalidDataException("The selected backup file could not be found.");
		}
		if (new FileInfo(filePath).Length > 256L * 1024 * 1024) throw new InvalidDataException("The backup exceeds the 256 MB limit.");
        await using FileStream stream = File.OpenRead(filePath);
		using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip
		}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		JsonElement root = document.RootElement;
		if (root.ValueKind == JsonValueKind.Object && IsFullBackupJson(root))
		{
			DashboardBackup dashboardBackup = root.Deserialize<DashboardBackup>(SerializerOptions) ?? throw new InvalidDataException("The selected backup file is empty or unreadable.");
			var sections = BackupSchema.Sections(root);
            return NormalizeImportedBackup(dashboardBackup, sections.Contains("Settings"), sections.Contains("Profile"), sections.Contains("Library"), sections.Contains("CustomThemes")) with { IncludesFriends = sections.Contains("Friends"), SettingsJson = BackupSchema.Get(root, "Settings"), ProfileJson = BackupSchema.Get(root, "Profile"), LibraryJson = BackupSchema.Get(root, "Library") };
		}
		if (root.ValueKind == JsonValueKind.Object && IsSettingsJson(root))
		{
			AppSettings settings = root.Deserialize<AppSettings>(SerializerOptions) ?? throw new InvalidDataException("The selected settings file is empty or unreadable.");
			return NormalizeImportedBackup(new DashboardBackup
			{
				Settings = await BuildSettingsBackupAsync(settings, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
			}, includesSettings: true, includesProfile: false, includesLibrary: false, includesThemes: false) with { SettingsJson = root.Clone() };
		}
		if (root.ValueKind == JsonValueKind.Object && IsProfileJson(root))
		{
			Profile profile = root.Deserialize<Profile>(SerializerOptions) ?? throw new InvalidDataException("The selected profile file is empty or unreadable.");
			return NormalizeImportedBackup(new DashboardBackup
			{
				Profile = await BuildProfileBackupAsync(profile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
			}, includesSettings: false, includesProfile: true, includesLibrary: false, includesThemes: false) with { ProfileJson = root.Clone() };
		}
		if (root.ValueKind == JsonValueKind.Object && IsLibraryJson(root))
		{
			BackupSchema.ValidateLibrary(root);
            GameLibrary library = root.Deserialize<GameLibrary>(SerializerOptions) ?? throw new InvalidDataException("The selected library file is empty or unreadable.");
			return NormalizeImportedBackup(new DashboardBackup
			{
				Library = library
			}, includesSettings: false, includesProfile: false, includesLibrary: true, includesThemes: false) with { LibraryJson = root.ValueKind == JsonValueKind.Array ? JsonSerializer.SerializeToElement(new { Games = root }) : root.Clone() };
		}
		if (root.ValueKind == JsonValueKind.Array)
		{
			List<GameMetadata> games = root.Deserialize<List<GameMetadata>>(SerializerOptions) ?? new List<GameMetadata>();
			return NormalizeImportedBackup(new DashboardBackup
			{
				Library = new GameLibrary
				{
					Games = games
				}
			}, includesSettings: false, includesProfile: false, includesLibrary: true, includesThemes: false) with { LibraryJson = root.ValueKind == JsonValueKind.Array ? JsonSerializer.SerializeToElement(new { Games = root }) : root.Clone() };
		}
		throw new InvalidDataException("The selected JSON file is not a DashX360 backup, settings, profile, or library file.");
	}

	private static ImportedDashboardData NormalizeImportedBackup(DashboardBackup dashboardBackup, bool includesSettings, bool includesProfile, bool includesLibrary, bool includesThemes)
	{
		if (dashboardBackup.Settings == null)
		{
			dashboardBackup.Settings = new DashboardBackupSettings();
		}
		if (dashboardBackup.Profile == null)
		{
			dashboardBackup.Profile = new DashboardBackupProfile();
		}
		dashboardBackup.Library = NormalizeLibrary(dashboardBackup.Library);
		if (dashboardBackup.CustomThemes == null)
		{
			dashboardBackup.CustomThemes = new List<DashboardBackupTheme>();
		}
		if (dashboardBackup.CustomThemes.Any(t => t == null) || dashboardBackup.Settings.DashboardTileImages == null || dashboardBackup.Settings.DashboardTileImages.Any(i => i == null) || dashboardBackup.Settings.DashboardTileCustomizations == null || dashboardBackup.Settings.DashboardTileCustomizations.Any(p => p.Value == null) || dashboardBackup.GameArtwork == null || dashboardBackup.GameArtwork.Any(a => a == null) || dashboardBackup.Friends == null || dashboardBackup.Friends.Friends == null || dashboardBackup.Friends.Friends.Any(f => f == null))
            throw new InvalidDataException("The backup contains an invalid or null entry.");
        return new ImportedDashboardData(dashboardBackup, includesSettings, includesProfile, includesLibrary, includesThemes);
	}

	private static bool IsFullBackupJson(JsonElement root)
	{
		return BackupSchema.Has(root, "ExportVersion") || BackupSchema.Has(root, "Settings") || BackupSchema.Has(root, "Profile") || BackupSchema.Has(root, "Library") || BackupSchema.Has(root, "CustomThemes") || BackupSchema.Has(root, "Friends") || BackupSchema.Has(root, "GameArtwork");
	}

	private static bool IsSettingsJson(JsonElement root)
	{
		return BackupSchema.Has(root, "DashboardTileCustomizations") || BackupSchema.Has(root, "DashboardTileColor") || BackupSchema.Has(root, "ThemeName") || BackupSchema.Has(root, "StartFullscreen");
	}

	private static bool IsProfileJson(JsonElement root)
	{
		return BackupSchema.Has(root, "Gamertag") || BackupSchema.Has(root, "GamerPicturePath") || BackupSchema.Has(root, "Gamerscore");
	}

	private static bool IsLibraryJson(JsonElement root)
	{
		return BackupSchema.Has(root, "Games") || BackupSchema.Has(root, "LibraryPaths");
	}

	private sealed record ImportedDashboardData(DashboardBackup Backup, bool IncludesSettings, bool IncludesProfile, bool IncludesLibrary, bool IncludesThemes, bool IncludesFriends = false, JsonElement? SettingsJson = null, JsonElement? ProfileJson = null, JsonElement? LibraryJson = null);

    private static string MakeSafeFileName(string value) => SafePaths.FileName(value);
    private static string MakeSafeFolderName(string value) => SafePaths.FileName(value);

    private static readonly string[] ArtworkFields = { "CoverArtPath", "HeaderImagePath", "StoreScreenshotPath", "BackgroundArtPath", "LogoImagePath" };
    private static async Task<List<DashboardBackupArtwork>> BuildArtworkAsync(GameLibrary library, CancellationToken token)
    {
        var result = new List<DashboardBackupArtwork>();
        foreach (var game in library.Games)
            foreach (var field in ArtworkFields)
            {
                var path = (string?)typeof(GameMetadata).GetProperty(field)!.GetValue(game);
                if (string.IsNullOrWhiteSpace(path)) continue;
                path = AppPaths.ResolvePath(path);
                if (!File.Exists(path)) continue;
                var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
                result.Add(new() { GameId = game.Id, Field = field, FileName = Path.GetFileName(path), ImageBase64 = Convert.ToBase64String(bytes) });
            }
        return result;
    }
    private async Task RestoreArtworkAsync(GameLibrary library, List<DashboardBackupArtwork> artwork, CancellationToken token)
    {
        foreach (var asset in artwork)
        {
            if (!ArtworkFields.Contains(asset.Field)) throw new InvalidDataException("The backup contains an unknown artwork field.");
            var game = library.Games.FirstOrDefault(g => g.Id == asset.GameId) ?? throw new InvalidDataException("Artwork refers to a missing game.");
            var bytes = BackupImages.Decode(asset.ImageBase64, asset.FileName);
            var destination = SafePaths.Within(_dataRoot, Path.Combine("ImportedAssets", "GameArt", Guid.NewGuid().ToString("N") + Path.GetExtension(asset.FileName)));
            await AtomicFile.WriteAsync(destination, bytes, token).ConfigureAwait(false);
            typeof(GameMetadata).GetProperty(asset.Field)!.SetValue(game, PublishedPath(destination));
        }
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
