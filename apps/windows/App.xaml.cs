using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace NextXLR;

public partial class App : Application
{
    private MainWindow? _window;
    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
        Services.CrashHandler.Initialize();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Services.CrashHandler.Initialize();
            Services.CrashHandler.LogStartup("1. App.OnLaunched entered");

            // Asynchronous non-blocking background tasks so the UI window opens instantly
            Task.Run(() =>
            {
                try
                {
                    Services.Rebrand.MigrateFromGoXlrNext();
                    EnsureDaemonRunning();
                }
                catch { }
            });

            Services.CrashHandler.LogStartup("2. Creating MainWindow");
            _window = new MainWindow();
            MainWindowInstance = _window;
            Services.CrashHandler.LogStartup("3. Activating MainWindow");
            _window.Activate();
            Services.CrashHandler.LogStartup("4. MainWindow Activated");
        }
        catch (Exception ex)
        {
            Services.CrashHandler.HandleException(ex, "App.OnLaunched Exception");
        }
    }

    public static void EnsureDaemonRunning()
    {
        try
        {
            if (Process.GetProcessesByName("mixforged").Length > 0) return;

            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            [
                Path.Combine(baseDir, "mixforged.exe"),
                Path.Combine(baseDir, @"..\..\..\..\..\target\release\mixforged.exe"),
                Path.Combine(baseDir, @"..\..\..\..\..\target\debug\mixforged.exe"),
                Path.Combine(baseDir, @"..\..\..\..\target\release\mixforged.exe"),
                Path.Combine(baseDir, @"..\..\..\..\target\debug\mixforged.exe"),
                @"E:\Projects\plysys\Neuer Ordner\goxlr\target\release\mixforged.exe",
                @"E:\Projects\plysys\Neuer Ordner\goxlr\target\debug\mixforged.exe"
            ];

            string? daemonPath = candidates.FirstOrDefault(File.Exists);
            if (daemonPath is not null)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(daemonPath),
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
        }
        catch { /* best effort daemon launch */ }
    }
}
