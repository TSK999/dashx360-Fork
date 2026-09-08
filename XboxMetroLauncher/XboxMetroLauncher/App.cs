using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			LogException(args.ExceptionObject as Exception, "AppDomain");
		};
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			LogException(args.Exception, "TaskScheduler");
			args.SetObserved();
		};
		base.OnStartup(e);
		MainWindow mainWindow = new MainWindow();
		MainWindow = mainWindow;
		mainWindow.Show();
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		LogException(e.Exception, "Dispatcher");

		// Unknown dispatcher exceptions can leave the WPF object graph in a corrupt
		// or partially-updated state. Log them, but do not suppress them globally.
		// Expected/recoverable failures should be handled at their command/service boundary.
		e.Handled = false;
	}

	internal static void LogException(Exception? exception, string source)
	{
		if (exception == null)
		{
			return;
		}

		try
		{
			Directory.CreateDirectory(AppPaths.LogsFolder);
			File.AppendAllText(
				Path.Combine(AppPaths.LogsFolder, "crash.log"),
				$"[{DateTimeOffset.Now:u}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
		}
		catch
		{
		}
	}

}
