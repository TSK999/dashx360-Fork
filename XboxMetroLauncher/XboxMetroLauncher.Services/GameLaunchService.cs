using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class GameLaunchService : IGameLaunchService
{
	private static readonly string DebugLogPath = Path.Combine(AppPaths.LogsFolder, "running-game-debug.log");

	private static readonly HashSet<string> IgnoredProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"steam", "gameoverlayui", "steamwebhelper", "explorer", "xboxmetrolauncher", "steamservice", "steamerrorreporter", "crashhandler", "crashpad_handler", "conhost",
		"cmd", "rundll32"
	};

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	public async Task<GameLaunchResult> LaunchAsync(GameMetadata game, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.Equals(game.LaunchType, "Url", StringComparison.OrdinalIgnoreCase))
		{
			string url = NormalizeUrl(game.LaunchCommand);
			if (string.IsNullOrWhiteSpace(url))
			{
				throw new InvalidOperationException("'" + game.Title + "' does not have a URL.");
			}
			Process process = Process.Start(new ProcessStartInfo
			{
				FileName = url,
				UseShellExecute = true
			});
			process?.Dispose();
            Log($"url launch request | title={game.Title} | url={url}");
			return new GameLaunchResult
			{
				TrackedProcess = null
			};
		}
		if (string.Equals(game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(game.LaunchCommand))
			{
				throw new InvalidOperationException("'" + game.Title + "' does not have a Steam launch command.");
			}
			if (!ulong.TryParse(game.SteamAppId, out var appId) || appId == 0)
                throw new InvalidOperationException("A valid Steam app ID is required.");
            string? installPath = NormalizeFolderPath(game.InstallPath);
			HashSet<int> knownProcessIds = CaptureProcessIds();
			Log($"steam launch request | title={game.Title} | steamAppId={game.SteamAppId} | installPath={game.InstallPath} | exePath={game.ExecutablePath}");
			Process.Start(new ProcessStartInfo
			{
				FileName = "steam://rungameid/" + appId,
				UseShellExecute = true
			});
			Process process = await TryFindSteamGameProcessAsync(installPath, knownProcessIds, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			Log((process == null) ? ("steam launch unresolved | title=" + game.Title) : $"steam launch matched | title={game.Title} | pid={process.Id} | process={process.ProcessName} | path={TryGetProcessPath(process)}");
			return new GameLaunchResult
			{
				TrackedProcess = process
			};
		}
		if (string.IsNullOrWhiteSpace(game.ExecutablePath))
		{
			throw new InvalidOperationException("'" + game.Title + "' does not have an executable path.");
		}
		string text = (string.IsNullOrWhiteSpace(game.WorkingDirectory) ? Path.GetDirectoryName(game.ExecutablePath) : game.WorkingDirectory);
		Process process2 = Process.Start(new ProcessStartInfo
		{
			FileName = game.ExecutablePath,
			Arguments = game.Arguments,
			WorkingDirectory = (text ?? string.Empty),
			UseShellExecute = true
		});
		Log($"exe launch request | title={game.Title} | pid={process2?.Id} | exePath={game.ExecutablePath}");
		return new GameLaunchResult
		{
			TrackedProcess = process2
		};
	}

	private static HashSet<int> CaptureProcessIds()
	{
		HashSet<int> hashSet = new HashSet<int>();
		Process[] processes = Process.GetProcesses();
		foreach (Process process in processes)
		{
			try
			{
				hashSet.Add(process.Id);
			}
			catch
			{
			}
			finally
			{
				process.Dispose();
			}
		}
		return hashSet;
	}

	private static async Task<Process?> TryFindSteamGameProcessAsync(string? installPath, HashSet<int> knownProcessIds, CancellationToken cancellationToken)
	{
		Process bestCandidate = null;
		int bestScore = int.MinValue;
		int stableBestProcessId = -1;
		int stableBestCount = 0;
		for (int attempt = 0; attempt < 24; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int foregroundProcessId = TryGetForegroundProcessId();
			List<(Process, int)> list = new List<(Process, int)>();
			Process[] processes = Process.GetProcesses();
			foreach (Process process in processes)
			{
				try
				{
					if (process.HasExited || knownProcessIds.Contains(process.Id) || IsIgnoredProcess(process))
					{
						process.Dispose();
						continue;
					}
					string text = TryGetProcessPath(process);
					int num = ScoreCandidate(process, text, installPath, foregroundProcessId);
					if (num <= 0)
					{
						process.Dispose();
						continue;
					}
					Log($"steam match attempt | candidate={process.ProcessName} | pid={process.Id} | score={num} | path={text}");
					list.Add((process, num));
				}
				catch
				{
					process.Dispose();
				}
			}
			(Process, int) tuple = (from candidate in list
				orderby candidate.Item2 descending, GetProcessStartTicks(candidate.Item1) descending
				select candidate).FirstOrDefault();
			foreach (var item in list)
			{
				if (item.Item1 != tuple.Item1)
				{
					item.Item1.Dispose();
				}
			}
			if (tuple.Item1 != null)
			{
				if (tuple.Item1.Id == stableBestProcessId)
				{
					stableBestCount++;
				}
				else
				{
					stableBestProcessId = tuple.Item1.Id;
					stableBestCount = 1;
				}
				if (tuple.Item2 > bestScore)
				{
					bestCandidate?.Dispose();
					(bestCandidate, bestScore) = tuple;
				}
				else if (bestCandidate != tuple.Item1)
				{
					tuple.Item1.Dispose();
				}
				if (tuple.Item2 >= 120 || (stableBestCount >= 2 && tuple.Item2 >= 80))
				{
					return bestCandidate;
				}
			}
			await Task.Delay(TimeSpan.FromMilliseconds(450.0), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		return bestCandidate;
	}

	private static string? TryGetProcessPath(Process process)
	{
		try
		{
			return ProcessPaths.TryRead(process);
		}
		catch
		{
			return null;
		}
	}

	private static int ScoreCandidate(Process process, string? processPath, string? installPath, int foregroundProcessId)
	{
        if (!SafePaths.MatchesExecutable(processPath, null, installPath)) return 0;
		int num = 0;
		if (!string.IsNullOrWhiteSpace(processPath))
		{
			num += 10;
		}
		if (!string.IsNullOrWhiteSpace(installPath) && !string.IsNullOrWhiteSpace(processPath) && processPath.StartsWith(installPath, StringComparison.OrdinalIgnoreCase))
		{
			num += 80;
		}
		if (process.Id == foregroundProcessId)
		{
			num += 120;
		}
		try
		{
			if (process.MainWindowHandle != IntPtr.Zero)
			{
				num += 70;
			}
		}
		catch
		{
		}
		try
		{
			if (process.Responding)
			{
				num += 15;
			}
		}
		catch
		{
		}
		try
		{
			if (DateTime.Now - process.StartTime.ToLocalTime() < TimeSpan.FromSeconds(15.0))
			{
				num += 15;
			}
		}
		catch
		{
		}
		return num;
	}

	private static int TryGetForegroundProcessId()
	{
		try
		{
			nint foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == IntPtr.Zero)
			{
				return -1;
			}
			GetWindowThreadProcessId(foregroundWindow, out var processId);
			return (int)processId;
		}
		catch
		{
			return -1;
		}
	}

	private static long GetProcessStartTicks(Process process)
	{
		try
		{
			return process.StartTime.ToUniversalTime().Ticks;
		}
		catch
		{
			return 0L;
		}
	}

	private static bool IsIgnoredProcess(Process process)
	{
		try
		{
			return process.Id == Environment.ProcessId || string.Equals(process.ProcessName, "dashx360", StringComparison.OrdinalIgnoreCase) || IgnoredProcessNames.Contains(process.ProcessName);
		}
		catch
		{
			return true;
		}
	}

	private static string? NormalizeFolderPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		try
		{
			return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		}
		catch
		{
			return null;
		}
	}

	private static string NormalizeUrl(string? url)
	{
		string text = url?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
		{
			return text;
		}
		return "https://" + text;
	}

	private static void Log(string message)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(DebugLogPath));
			File.AppendAllText(DebugLogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
		}
		catch
		{
		}
	}
}
