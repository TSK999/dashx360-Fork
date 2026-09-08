using System.Collections.Generic;
using System;
using XboxMetroLauncher.Utilities;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface IGameLibraryService
{
	Task<GameLibrary> LoadAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SaveAsync(GameLibrary library, CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<GameMetadata>> ScanFolderAsync(string folderPath, CancellationToken cancellationToken = default(CancellationToken), IProgress<LibraryScanProgress>? progress = null);
}
