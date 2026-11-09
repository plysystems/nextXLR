using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace NextXLR.Services;

/// <summary>
/// Global crash reporting and exception handling service for NEXT : XLR.
/// Catches unhandled exceptions, writes structured crash logs to %LOCALAPPDATA%\NextXLR\crashlogs\,
/// and displays a user-friendly crash dialog with text export and copy options.
/// </summary>
public static class CrashHandler
{
    private static readonly string CrashLogsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NextXLR",
        "crashlogs");

    private static bool _hasCrashed = false;
    private static readonly object _crashLock = new();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VectoredExceptionHandlerDelegate(nint exceptionPointers);

    private static VectoredExceptionHandlerDelegate? _vehDelegate;
    private static nint _vehHandle = nint.Zero;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint AddVectoredExceptionHandler(uint firstHandler, VectoredExceptionHandlerDelegate handler);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetModuleFileName(nint hModule, StringBuilder lpFilename, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetModuleHandleEx(uint dwFlags, nint lpModuleName, out nint phModule);

    [DllImport("kernel32.dll")]
    private static extern void ExitProcess(uint uExitCode);

    private const uint GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = 0x00000004;
    private const uint GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT = 0x00000002;

    private const uint EXCEPTION_CONTINUE_SEARCH = 0;
    private const uint STATUS_ACCESS_VIOLATION = 0xC0000005;
    private const uint STATUS_IN_PAGE_ERROR = 0xC0000006;
    private const uint STATUS_ILLEGAL_INSTRUCTION = 0xC000001D;
    private const uint STATUS_ARRAY_BOUNDS_EXCEEDED = 0xC000008C;
    private const uint STATUS_STACK_OVERFLOW = 0xC00000FD;
    private const uint STATUS_STOWED_EXCEPTION = 0xC000027B;

    [StructLayout(LayoutKind.Sequential)]
    private struct EXCEPTION_POINTERS
    {
        public nint ExceptionRecord;
        public nint ContextRecord;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXCEPTION_RECORD
    {
        public uint ExceptionCode;
        public uint ExceptionFlags;
        public nint ExceptionRecord;
        public nint ExceptionAddress;
        public uint NumberParameters;
    }

    public static void Initialize()
    {
        // 0. Native Vectored Exception Handler (catches native Access Violations 0xC0000005, XAML crashes, etc.)
        try
        {
            if (_vehHandle == nint.Zero)
            {
                _vehDelegate = HandleNativeException;
                _vehHandle = AddVectoredExceptionHandler(1, _vehDelegate);
            }
        }
        catch { }

        // 1. AppDomain unhandled exceptions (background threads, threadpool, runtime crashes)
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    HandleException(ex, "AppDomain UnhandledException (IsTerminating: " + e.IsTerminating + ")");
                }
            };
        }
        catch { }

        // 2. Unobserved task exceptions (async Tasks)
        try
        {
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                HandleException(e.Exception, "TaskScheduler UnobservedTaskException");
                e.SetObserved();
            };
        }
        catch { }

        // 3. WinUI 3 XAML unhandled exceptions
        try
        {
            if (Application.Current != null)
            {
                Application.Current.UnhandledException += (s, e) =>
                {
                    HandleException(e.Exception, "WinUI 3 XAML UnhandledException");
                    e.Handled = true; // Prevent instant process termination so crash dialog can display
                };
            }
        }
        catch { }

        // 4. Ensure crashlogs directory exists
        try
        {
            Directory.CreateDirectory(CrashLogsDir);
        }
        catch { }
    }

    private static int HandleNativeException(nint pExceptionInfo)
    {
        if (pExceptionInfo == nint.Zero) return (int)EXCEPTION_CONTINUE_SEARCH;

        try
        {
            var pointers = Marshal.PtrToStructure<EXCEPTION_POINTERS>(pExceptionInfo);
            if (pointers.ExceptionRecord == nint.Zero) return (int)EXCEPTION_CONTINUE_SEARCH;

            var rec = Marshal.PtrToStructure<EXCEPTION_RECORD>(pointers.ExceptionRecord);
            uint code = rec.ExceptionCode;

            // Only intercept fatal native exceptions; leave CLR exceptions (0xE0434352) and benign RPC/debugger codes alone
            if (code != STATUS_ACCESS_VIOLATION &&
                code != STATUS_ILLEGAL_INSTRUCTION &&
                code != STATUS_IN_PAGE_ERROR &&
                code != STATUS_ARRAY_BOUNDS_EXCEEDED &&
                code != STATUS_STACK_OVERFLOW &&
                code != STATUS_STOWED_EXCEPTION)
            {
                return (int)EXCEPTION_CONTINUE_SEARCH;
            }

            lock (_crashLock)
            {
                if (_hasCrashed) return (int)EXCEPTION_CONTINUE_SEARCH;
                _hasCrashed = true;
            }

            string codeName = code switch
            {
                STATUS_ACCESS_VIOLATION => "0xC0000005 (STATUS_ACCESS_VIOLATION - Zugriffsverletzung)",
                STATUS_ILLEGAL_INSTRUCTION => "0xC000001D (STATUS_ILLEGAL_INSTRUCTION - Ungültige Instruktion)",
                STATUS_IN_PAGE_ERROR => "0xC0000006 (STATUS_IN_PAGE_ERROR - Auslagerungsfehler)",
                STATUS_ARRAY_BOUNDS_EXCEEDED => "0xC000008C (STATUS_ARRAY_BOUNDS_EXCEEDED - Bereichsüberschreitung)",
                STATUS_STACK_OVERFLOW => "0xC00000FD (STATUS_STACK_OVERFLOW - Stapelüberlauf)",
                STATUS_STOWED_EXCEPTION => "0xC000027B (STATUS_STOWED_EXCEPTION - XAML Stowed Exception)",
                _ => $"0x{code:X8}"
            };

            string moduleName = "Unbekanntes Modul";
            string modulePath = "N/A";
            nint moduleBase = nint.Zero;
            long offset = 0;

            if (GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, rec.ExceptionAddress, out var hModule) && hModule != nint.Zero)
            {
                moduleBase = hModule;
                var sb = new StringBuilder(1024);
                if (GetModuleFileName(hModule, sb, sb.Capacity) > 0)
                {
                    modulePath = sb.ToString();
                    moduleName = Path.GetFileName(modulePath);
                }
                offset = (long)rec.ExceptionAddress - (long)moduleBase;
            }

            var report = new StringBuilder();
            report.AppendLine("================================================================================");
            report.AppendLine("                  NEXT : XLR — NATIVER CRASH REPORT");
            report.AppendLine("================================================================================");
            report.AppendLine($"Timestamp:         {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            report.AppendLine($"Application:       NEXT : XLR (v{AppSettings.AppVersion})");
            report.AppendLine($"Git Commit:        {GitBuildInfo.CommitHash} ({GitBuildInfo.FullCommitHash})");
            report.AppendLine($"Ausnahmecode:      {codeName}");
            report.AppendLine($"Fehleradresse:     0x{rec.ExceptionAddress:X16}");
            report.AppendLine($"Fehlermodul:       {moduleName} (Offset: +0x{offset:X8})");
            report.AppendLine($"Modulpfad:         {modulePath}");
            report.AppendLine($"OS Version:        {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-Bit" : "32-Bit")})");
            report.AppendLine($".NET Runtime:      {Environment.Version}");
            report.AppendLine($"Process Mode:      {(Environment.Is64BitProcess ? "x64" : "x86")}");
            try
            {
                using var proc = Process.GetCurrentProcess();
                report.AppendLine($"Memory (WorkingSet): {proc.WorkingSet64 / 1024 / 1024} MB");
                report.AppendLine($"Threads Count:     {proc.Threads.Count}");
            }
            catch { }
            report.AppendLine("================================================================================");
            report.AppendLine("HINWEIS:");
            report.AppendLine("Dieser native Fehler wurde direkt vom Windows Kernel / Windows App SDK abgefangen.");
            report.AppendLine("================================================================================");

            string crashReport = report.ToString();
            string logFilePath = SaveCrashLog(crashReport);

            try
            {
                MessageBoxW(
                    nint.Zero,
                    $"Ein schwerwiegender nativer Systemfehler ist aufgetreten:\n\n" +
                    $"Ausnahme: {codeName}\n" +
                    $"Modul: {moduleName} (+0x{offset:X})\n" +
                    $"Adresse: 0x{rec.ExceptionAddress:X}\n\n" +
                    $"Ein detaillierter Crashlog wurde gespeichert unter:\n{logFilePath}\n\n" +
                    $"Das Protokoll wird jetzt zur Analyse geöffnet.",
                    "NEXT : XLR — Nativer Absturz abgefangen",
                    0x00000010 | 0x00010000 | 0x00040000 // MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST
                );

                if (File.Exists(logFilePath))
                {
                    Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{logFilePath}\"", UseShellExecute = true });
                }
            }
            catch { }

            ExitProcess(code);
        }
        catch { }

        return (int)EXCEPTION_CONTINUE_SEARCH;
    }

    public static void HandleException(Exception ex, string source)
    {
        lock (_crashLock)
        {
            if (_hasCrashed) return; // Prevent cascading dialog popups
            _hasCrashed = true;
        }

        string crashLog = BuildCrashReport(ex, source);
        string logFilePath = SaveCrashLog(crashLog);

        // Try displaying modern WinUI 3 Crash Window; fall back to native Win32 dialog if dispatcher/UI fails
        ShowCrashDialog(ex, crashLog, logFilePath);
    }

    public static string BuildCrashReport(Exception ex, string source)
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                       NEXT : XLR — CRASH REPORT");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Timestamp:         {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"Application:       NEXT : XLR (v{AppSettings.AppVersion})");
        sb.AppendLine($"Git Commit:        {GitBuildInfo.CommitHash} ({GitBuildInfo.FullCommitHash})");
        sb.AppendLine($"Crash Source:      {source}");
        sb.AppendLine($"OS Version:        {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-Bit" : "32-Bit")})");
        sb.AppendLine($".NET Runtime:      {Environment.Version}");
        sb.AppendLine($"Process Mode:      {(Environment.Is64BitProcess ? "x64" : "x86")}");
        try
        {
            using var proc = Process.GetCurrentProcess();
            sb.AppendLine($"Memory (WorkingSet): {proc.WorkingSet64 / 1024 / 1024} MB");
            sb.AppendLine($"Threads Count:     {proc.Threads.Count}");
        }
        catch { }

        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("EXCEPTION DETAILS:");
        sb.AppendLine($"Type:              {ex.GetType().FullName}");
        sb.AppendLine($"Message:           {ex.Message}");
        sb.AppendLine($"HResult:           0x{ex.HResult:X8}");
        if (ex.TargetSite != null)
        {
            sb.AppendLine($"TargetSite:        {ex.TargetSite.DeclaringType?.FullName}.{ex.TargetSite.Name}");
        }
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("STACK TRACE:");
        sb.AppendLine(ex.StackTrace ?? "No stack trace available.");

        // Inner Exceptions
        var inner = ex.InnerException;
        int depth = 1;
        while (inner != null)
        {
            sb.AppendLine();
            sb.AppendLine($"--- INNER EXCEPTION #{depth} ({inner.GetType().FullName}) ---");
            sb.AppendLine($"Message: {inner.Message}");
            sb.AppendLine($"HResult: 0x{inner.HResult:X8}");
            sb.AppendLine(inner.StackTrace ?? "No inner stack trace available.");
            inner = inner.InnerException;
            depth++;
        }

        sb.AppendLine("================================================================================");
        sb.AppendLine("End of Crash Report");
        return sb.ToString();
    }

    private static string SaveCrashLog(string crashReport)
    {
        try
        {
            Directory.CreateDirectory(CrashLogsDir);
            string fileName = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string filePath = Path.Combine(CrashLogsDir, fileName);
            File.WriteAllText(filePath, crashReport, Encoding.UTF8);
            return filePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write crash log file: {ex.Message}");
            return Path.Combine(CrashLogsDir, "crash_log.txt");
        }
    }

    private static void ShowCrashDialog(Exception ex, string crashLog, string logFilePath)
    {
        try
        {
            // Run on UI thread if possible
            if (App.MainWindowInstance?.DispatcherQueue is { } dispatcher && !dispatcher.HasThreadAccess)
            {
                dispatcher.TryEnqueue(() => CreateAndShowCrashWindow(ex, crashLog, logFilePath));
                return;
            }

            CreateAndShowCrashWindow(ex, crashLog, logFilePath);
        }
        catch
        {
            // If WinUI window creation fails, fall back to native Win32 dialog
            ShowNativeFallbackDialog(ex, crashLog, logFilePath);
        }
    }

    private static void CreateAndShowCrashWindow(Exception ex, string crashLog, string logFilePath)
    {
        try
        {
            var win = new Window
            {
                Title = "NEXT : XLR — Unerwarteter Fehler",
                SystemBackdrop = new MicaBackdrop()
            };

            var appWin = win.AppWindow;
            appWin.Resize(new SizeInt32(780, 600));

            // Set official NEXT : XLR app icon on window titlebar
            try
            {
                var iconCandidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Assets", "nextXLR_icon.ico"),
                    Path.Combine(AppContext.BaseDirectory, "nextXLR_icon.ico"),
                    Path.Combine(AppContext.BaseDirectory, @"..\..\..\Assets\nextXLR_icon.ico")
                };
                var iconPath = iconCandidates.FirstOrDefault(File.Exists);
                if (iconPath != null) appWin.SetIcon(iconPath);
            }
            catch { }

            var root = new Grid
            {
                RequestedTheme = ElementTheme.Dark,
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x16, 0x18, 0x1F)),
                Padding = new Thickness(24),
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto }, // Header
                    new RowDefinition { Height = GridLength.Auto }, // Info Banner
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Log content
                    new RowDefinition { Height = GridLength.Auto }  // Actions
                }
            };

            // 1. Header with Warning Icon and Official Logo
            var headerGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 16),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            var iconBorder = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0xE5, 0x39, 0x35)),
                Child = new FontIcon
                {
                    Glyph = "\uE783", // Warning shield
                    FontSize = 22,
                    Foreground = new SolidColorBrush(Colors.White)
                }
            };
            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleText = new TextBlock
            {
                Text = "Unerwarteter Fehler in NEXT : XLR",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            var subtitleText = new TextBlock
            {
                Text = "Ein Ausnahmefehler ist aufgetreten. Ein detaillierter Crashlog wurde automatisch erstellt.",
                FontSize = 13,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x88, 0x90, 0x9C)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            titleStack.Children.Add(titleText);
            titleStack.Children.Add(subtitleText);
            headerPanel.Children.Add(iconBorder);
            headerPanel.Children.Add(titleStack);
            Grid.SetColumn(headerPanel, 0);
            headerGrid.Children.Add(headerPanel);

            // Add official logo on the right
            try
            {
                var logoCandidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Assets", "nextXLR_logo.png"),
                    Path.Combine(AppContext.BaseDirectory, "nextXLR_logo.png"),
                    Path.Combine(AppContext.BaseDirectory, @"..\..\..\Assets\nextXLR_logo.png")
                };
                var logoPath = logoCandidates.FirstOrDefault(File.Exists);
                if (logoPath != null)
                {
                    var logoImg = new Image
                    {
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(logoPath))),
                        Height = 36,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(logoImg, 1);
                    headerGrid.Children.Add(logoImg);
                }
            }
            catch { }

            Grid.SetRow(headerGrid, 0);
            root.Children.Add(headerGrid);

            // 2. Info Banner with Exception Message & Path
            var infoBorder = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x22, 0x26, 0x2E)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 14),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0x3A, 0x40, 0x4D)),
                BorderThickness = new Thickness(1)
            };
            var infoStack = new StackPanel { Spacing = 4 };
            var errLabel = new TextBlock
            {
                Text = $"Fehler: {ex.Message}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0xFF, 0x8A, 0x80)),
                TextWrapping = TextWrapping.Wrap
            };
            var pathLabel = new TextBlock
            {
                Text = $"Gespeichert in: {logFilePath}",
                FontSize = 12,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x88, 0x90, 0x9C)),
                TextWrapping = TextWrapping.Wrap
            };
            infoStack.Children.Add(errLabel);
            infoStack.Children.Add(pathLabel);
            infoBorder.Child = infoStack;
            Grid.SetRow(infoBorder, 1);
            root.Children.Add(infoBorder);

            // 3. Log text display
            var logTextBox = new TextBox
            {
                Text = crashLog,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 12,
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x14, 0x16, 0x1C)),
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0xD0, 0xD4, 0xDC)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0x2E, 0x34, 0x40)),
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(logTextBox, 2);
            root.Children.Add(logTextBox);

            // 4. Action Buttons
            var actionsGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            // Save As Text button
            var saveAsBtn = new Button
            {
                Style = Application.Current?.Resources["AccentButtonStyle"] as Style,
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x1F, 0xC7, 0x9A)),
                Foreground = new SolidColorBrush(Colors.Black),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8, 14, 8),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE792", FontSize = 14 }, // Save
                        new TextBlock { Text = "Crashlog als Text speichern...", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                    }
                }
            };
            saveAsBtn.Click += async (_, _) =>
            {
                try
                {
                    var picker = new Windows.Storage.Pickers.FileSavePicker
                    {
                        SuggestedFileName = $"NextXLR_Crash_{DateTime.Now:yyyy-MM-dd_HHmmss}.txt"
                    };
                    picker.FileTypeChoices.Add("Textdatei (*.txt)", [".txt"]);
                    var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                    var file = await picker.PickSaveFileAsync();
                    if (file != null)
                    {
                        await Windows.Storage.FileIO.WriteTextAsync(file, crashLog);
                        saveAsBtn.Content = "Crashlog gespeichert!";
                    }
                }
                catch (Exception)
                {
                    // Fallback to Win32 Save Dialog if WinRT picker throws
                    SaveLogNativeFallback(win, crashLog);
                }
            };

            // Copy to Clipboard button
            var copyBtn = new Button
            {
                Style = Application.Current?.Resources["SmoothButton"] as Style,
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x2A, 0x2E, 0x38)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8C8", FontSize = 14 }, // Copy
                        new TextBlock { Text = "Log kopieren" }
                    }
                }
            };
            copyBtn.Click += (_, _) =>
            {
                try
                {
                    var dataPackage = new DataPackage();
                    dataPackage.SetText(crashLog);
                    Clipboard.SetContent(dataPackage);
                    copyBtn.Content = "Kopiert!";
                }
                catch { }
            };

            // Open Folder button
            var openFolderBtn = new Button
            {
                Style = Application.Current?.Resources["SmoothButton"] as Style,
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x2A, 0x2E, 0x38)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE838", FontSize = 14 }, // Folder
                        new TextBlock { Text = "Ordner öffnen" }
                    }
                }
            };
            openFolderBtn.Click += (_, _) =>
            {
                try
                {
                    Process.Start("explorer.exe", CrashLogsDir);
                }
                catch { }
            };

            leftActions.Children.Add(saveAsBtn);
            leftActions.Children.Add(copyBtn);
            leftActions.Children.Add(openFolderBtn);
            Grid.SetColumn(leftActions, 0);
            actionsGrid.Children.Add(leftActions);

            var rightActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            // Restart button
            var restartBtn = new Button
            {
                Content = "App neu starten",
                Style = Application.Current?.Resources?["AccentButtonStyle"] as Style,
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x1F, 0xC7, 0x9A)),
                Foreground = new SolidColorBrush(Colors.Black),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8, 14, 8)
            };
            restartBtn.Click += (_, _) =>
            {
                try
                {
                    var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                    if (currentExe != null)
                    {
                        Process.Start(new ProcessStartInfo { FileName = currentExe, UseShellExecute = true });
                    }
                }
                catch { }
                Environment.Exit(1);
            };

            // Close button
            var closeBtn = new Button
            {
                Content = "Beenden",
                Style = Application.Current?.Resources["SmoothButton"] as Style,
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x2A, 0x2E, 0x38)),
                Foreground = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8, 14, 8)
            };
            closeBtn.Click += (_, _) => Environment.Exit(1);

            rightActions.Children.Add(restartBtn);
            rightActions.Children.Add(closeBtn);
            Grid.SetColumn(rightActions, 1);
            actionsGrid.Children.Add(rightActions);

            Grid.SetRow(actionsGrid, 3);
            root.Children.Add(actionsGrid);

            win.Content = root;
            win.Activate();
        }
        catch
        {
            ShowNativeFallbackDialog(ex, crashLog, logFilePath);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileNameW([In, Out] OPENFILENAME ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class OPENFILENAME
    {
        public int lStructSize = Marshal.SizeOf(typeof(OPENFILENAME));
        public nint hwndOwner;
        public nint hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex = 1;
        public string? lpstrFile;
        public int nMaxFile = 260;
        public string? lpstrFileTitle;
        public int nMaxFileTitle = 100;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags = 0x00000002 | 0x00000004 | 0x00000800; // OFN_OVERWRITEPROMPT | OFN_HIDEREADONLY | OFN_PATHMUSTEXIST
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt = "txt";
        public nint lCustData;
        public nint lpfnHook;
        public string? lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    private static void SaveLogNativeFallback(Window win, string crashLog)
    {
        try
        {
            var ofn = new OPENFILENAME
            {
                hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(win),
                lpstrFilter = "Textdatei (*.txt)\0*.txt\0Alle Dateien (*.*)\0*.*\0",
                lpstrFile = new string(new char[260]),
                lpstrTitle = "Crashlog speichern als..."
            };
            if (GetSaveFileNameW(ofn))
            {
                string path = ofn.lpstrFile.TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(path))
                {
                    File.WriteAllText(path, crashLog, Encoding.UTF8);
                }
            }
        }
        catch { }
    }

    private static void ShowNativeFallbackDialog(Exception ex, string crashLog, string logFilePath)
    {
        try
        {
            MessageBoxW(
                nint.Zero,
                $"Ein unerwarteter Fehler ist in NEXT : XLR aufgetreten:\n\n{ex.Message}\n\nEin vollständiger Crashlog wurde gespeichert unter:\n{logFilePath}\n\nDas Protokoll wird jetzt im Editor geöffnet.",
                "NEXT : XLR — Unerwarteter Fehler",
                0x00000010 | 0x00010000 | 0x00040000 // MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST
            );

            // Open log in notepad
            if (File.Exists(logFilePath))
            {
                Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{logFilePath}\"", UseShellExecute = true });
            }
        }
        catch { }
        finally
        {
            Environment.Exit(1);
        }
    }
}

