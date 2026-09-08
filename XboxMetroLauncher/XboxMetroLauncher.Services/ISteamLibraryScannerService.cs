using System;
using XboxMetroLauncher.Utilities;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface ISteamLibraryScannerService
{
	Task<SteamGameScanResult> ScanAsync(GameLibrary library, CancellationToken cancellationToken = default(CancellationToken), IProgress<LibraryScanProgress>? progress = null);
}
