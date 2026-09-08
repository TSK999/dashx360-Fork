using System;
using System.Collections.Generic;
using System.Reflection;

namespace XboxMetroLauncher.Models;

public sealed class DashboardBackup
{
	public string ExportVersion { get; set; } = "2";

	public string AppVersion { get; set; } = ResolveAppVersion();

	public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;

	public DashboardBackupSettings Settings { get; set; } = new DashboardBackupSettings();

	public DashboardBackupProfile Profile { get; set; } = new DashboardBackupProfile();

	public GameLibrary Library { get; set; } = new GameLibrary();

	public List<DashboardBackupTheme> CustomThemes { get; set; } = new List<DashboardBackupTheme>();

	public FriendsData Friends { get; set; } = new();

    public List<DashboardBackupArtwork> GameArtwork { get; set; } = new();

	private static string ResolveAppVersion()
	{
		return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "v1.0.0-public";
	}
}
