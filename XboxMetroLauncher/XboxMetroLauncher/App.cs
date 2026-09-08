using System;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private FileStream? _dataLease;

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
        try
        {
            var root = AppPaths.UserDataFolder;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant())));
            _instanceMutex = new Mutex(false, "Local\\DashX360-" + key);
            try { _ownsMutex = _instanceMutex.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
            if (!_ownsMutex)
            {
                MessageBox.Show("DashX360 is already running with this data folder.", "DashX360");
                Shutdown();
                return;
            }

            _dataLease = DataDirectoryLease.Acquire(root);
            Directory.CreateDirectory(AppPaths.LogsFolder);
            DataTransaction.Recover(root);
            AppPaths.MigrateMutableAssets();
            base.OnStartup(e);

            // First-run setup is hosted by MainWindow so the dashboard shell, boot
            // animation, scaling and input stack stay active for the whole launch.
            var mainWindow = new MainWindow();
            mainWindow.InstallFirstRunVisualRefresh();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            LogException(ex, "Startup");
            MessageBox.Show("DashX360 could not start: " + ex.Message, "DashX360");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _dataLease?.Dispose();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception, "Dispatcher");
        MessageBox.Show("DashX360 encountered an unexpected error and must close. Your saved data is retained.", "DashX360");
        e.Handled = true;
        Current.Shutdown(1);
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
