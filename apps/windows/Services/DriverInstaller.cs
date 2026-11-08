using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace NextXLR.Services;

/// <summary>
/// Status and automated installer for the TC-Helicon GoXLR USB audio drivers.
/// Installs the bundled signed INF drivers (goxlr_audio.inf, goxlr_audioks.inf)
/// so that Windows creates the 10 virtual audio endpoints (System, Game, Chat, Music, Sample, Broadcast Stream Mix, Chat Mic).
/// </summary>
public static class DriverInstaller
{
    private static readonly string[] RequiredPlaybackChannels =
        ["System", "Game", "Chat", "Music", "Sample"];

    private static readonly string[] RequiredCaptureChannels =
        ["Chat Mic", "Broadcast Stream Mix", "Sample"];

    /// <summary>
    /// Checks whether the TC-Helicon GoXLR driver service or API DLL is installed on the machine.
    /// </summary>
    public static bool IsDriverInstalled()
    {
        try
        {
            // 1) Check standard driver installation directory
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var dllPath = Path.Combine(pf, "TC-HELICON", "GoXLR_Audio_Driver", "x64", "goxlr_audioapi_x64.dll");
            var altDll = Path.Combine(pf, "TC-HELICON", "GoXLR_Audio_Driver", "W10_x64", "goxlr_audioapi_x64.dll");
            if (File.Exists(dllPath) || File.Exists(altDll)) return true;

            // 2) Check Windows Driver Service in registry
            using var svcKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\goxlr_audio");
            if (svcKey is not null) return true;

            using var svcKeyKs = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\goxlr_audioks");
            if (svcKeyKs is not null) return true;
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Checks how many of the official GoXLR audio channels are currently recognized by Windows.
    /// </summary>
    public static (int available, int total, string details) GetEndpointStatus()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int totalExpected = RequiredPlaybackChannels.Length + RequiredCaptureChannels.Length;

        try
        {
            // Scan Render (Playback) endpoints
            ScanEndpoints(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render", found);

            // Scan Capture (Recording) endpoints
            ScanEndpoints(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture", found);
        }
        catch { }

        int available = 0;
        foreach (var ch in RequiredPlaybackChannels) if (found.Contains(ch)) available++;
        foreach (var ch in RequiredCaptureChannels) if (found.Contains(ch)) available++;

        var details = available >= totalExpected
            ? "Alle 8 GoXLR Audiokanäle sind in Windows aktiv (System, Chat, Game, Musik, Sample, Chat Mic, Broadcast Mix)."
            : $"{available} von {totalExpected} Audiokanälen in Windows erkannt.";

        return (available, totalExpected, details);
    }

    private static void ScanEndpoints(string subKeyPath, HashSet<string> found)
    {
        using var root = Registry.LocalMachine.OpenSubKey(subKeyPath);
        if (root is null) return;

        foreach (var id in root.GetSubKeyNames())
        {
            try
            {
                using var devKey = root.OpenSubKey(id);
                if (devKey is null) continue;

                // State 1 = DEVICE_STATE_ACTIVE
                var state = devKey.GetValue("DeviceState");
                if (state is int s && s != 1) continue;

                using var propKey = devKey.OpenSubKey("Properties");
                if (propKey is null) continue;

                // {a45c254e-df1c-4efd-8020-67d146a850e0},2 = PKEY_Device_FriendlyName
                var name = propKey.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string;
                var desc = propKey.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string;

                if (!string.IsNullOrEmpty(name))
                {
                    if (desc != null && desc.Contains("GoXLR", StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(name.Trim());
                    }
                    else if (name.Contains("TC-HELICON", StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(name.Trim());
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Locates the bundled driver directory with goxlr_audio.inf and goxlr_audioks.inf.
    /// </summary>
    public static string? LocateDriverDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Driver"),
            Path.Combine(AppContext.BaseDirectory, "Driver"),
            Path.Combine(Directory.GetCurrentDirectory(), "goxlr", "apps", "windows", "Assets", "Driver"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Driver"),
            @"C:\Program Files\TC-HELICON\GoXLR_Audio_Driver\x64"
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir) &&
                File.Exists(Path.Combine(dir, "goxlr_audio.inf")) &&
                File.Exists(Path.Combine(dir, "goxlr_audioks.inf")))
            {
                return dir;
            }
        }

        return null;
    }

    /// <summary>
    /// Executes pnputil /add-driver to install both GoXLR INF drivers with UAC elevation.
    /// </summary>
    public static async Task<(bool success, string message)> InstallDriverAsync()
    {
        var driverDir = LocateDriverDirectory();
        if (driverDir is null)
        {
            return (false, "Die Treiberdateien (goxlr_audio.inf) wurden nicht im Assets-Verzeichnis gefunden.");
        }

        var inf1 = Path.Combine(driverDir, "goxlr_audio.inf");
        var inf2 = Path.Combine(driverDir, "goxlr_audioks.inf");

        return await Task.Run(() =>
        {
            try
            {
                // Create a temporary batch script to execute pnputil in one elevated step
                var tempBatch = Path.Combine(Path.GetTempPath(), $"InstallGoXLRDriver_{Guid.NewGuid():N}.bat");
                var script = $@"@echo off
echo ===================================================
echo NEXT : XLR - GoXLR Audiotreiber Installation
echo ===================================================
echo Installiere USB Audio Controller Treiber...
pnputil.exe /add-driver ""{inf1}"" /install
echo.
echo Installiere Kernel Streaming Audiokanaltreiber...
pnputil.exe /add-driver ""{inf2}"" /install
echo.
echo Treiberinstallation abgeschlossen!
timeout /t 3 >nul
del ""%~f0""
";
                File.WriteAllText(tempBatch, script);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{tempBatch}\"",
                    UseShellExecute = true,
                    Verb = "runas" // Prompt Windows UAC Administrator Elevation
                };

                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    return (false, "Der Installationsprozess konnte nicht gestartet werden.");
                }

                proc.WaitForExit();

                if (proc.ExitCode == 0)
                {
                    return (true, "GoXLR Treiber erfolgreich installiert! Die Windows-Audiokanäle stehen nun bereit.");
                }
                else
                {
                    return (false, $"Installation mit Code {proc.ExitCode} beendet. Bitte Administrator-Rechte gewähren.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Fehler bei der Treiberinstallation: {ex.Message}");
            }
        });
    }
}

