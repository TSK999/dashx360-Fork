using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class JsonGameLibraryService : IGameLibraryService
{
	private const string LibraryFileName = "library.json";

	private readonly IJsonStore _store;


	public JsonGameLibraryService(IJsonStore store)
	{
		_store = store;
	}

	public async Task<GameLibrary> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		var root = await _store.ReadAsync<JsonElement>(LibraryFileName, cancellationToken).ConfigureAwait(false);
        GameLibrary? gameLibrary = root.ValueKind == JsonValueKind.Array ? new GameLibrary { Games = root.Deserialize<List<GameMetadata>>(ReadOptions) ?? new() } : root.ValueKind == JsonValueKind.Object ? root.Deserialize<GameLibrary>(ReadOptions) : null;
		if (gameLibrary != null)
		{
			return gameLibrary;
		}
		string path = AppPaths.FindFile(Path.Combine("Data", "library.seed.json"));
		if (File.Exists(path))
		{
			GameLibrary seeded = await ReadLibraryFileAsync(path, cancellationToken);
			if (seeded != null)
			{
				await SaveAsync(seeded, cancellationToken);
				return seeded;
			}
		}
		return new GameLibrary();
	}

	public Task SaveAsync(GameLibrary library, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _store.WriteAsync("library.json", library, cancellationToken);
	}

	private static async Task<GameLibrary?> ReadLibraryFileAsync(string path, CancellationToken cancellationToken)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		await using FileStream stream = File.OpenRead(path);
		using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip
		}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		JsonElement root = document.RootElement;
		if (root.ValueKind == JsonValueKind.Array)
		{
			return new GameLibrary
			{
				Games = root.Deserialize<List<GameMetadata>>(ReadOptions) ?? new List<GameMetadata>()
			};
		}
		if (root.ValueKind == JsonValueKind.Object)
		{
			return root.Deserialize<GameLibrary>(ReadOptions);
		}
		return null;
	}

    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new FlexibleTimeSpanJsonConverter());
        return options;
    }

    public Task<IReadOnlyList<GameMetadata>> ScanFolderAsync(string folderPath, CancellationToken cancellationToken = default, IProgress<LibraryScanProgress>? progress = null) => Task.Run<IReadOnlyList<GameMetadata>>(() =>
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UnityCrashHandler64", "UnityCrashHandler32", "CrashReportClient", "unins000", "uninstall" };
        return LibraryScan.FindExecutables(folderPath, cancellationToken, progress)
            .Where(path => !ignored.Contains(Path.GetFileNameWithoutExtension(path)))
            .Select(path => new GameMetadata { Title = CleanTitle(Path.GetFileNameWithoutExtension(path)), LaunchType = "Exe", ExecutablePath = path, WorkingDirectory = Path.GetDirectoryName(path) ?? folderPath, Platform = "PC", Genre = "Imported" })
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }, cancellationToken);

	private static string CleanTitle(string value)
	{
		return value.Replace("_", " ").Replace("-", " ").Trim();
	}
}
