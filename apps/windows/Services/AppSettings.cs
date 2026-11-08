using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace NextXLR.Services;

/// <summary>
/// App preferences in %LOCALAPPDATA%\NextXLR\settings.json. (An unpackaged app has no
/// ApplicationData.LocalSettings, so a small JSON file is used.)
/// </summary>
public sealed class AppSettings
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool MuteOnStartup { get; set; } = false;
    public bool CheckForUpdatesOnStart { get; set; } = true;
    public double BleepVolume { get; set; } = 100.0;
    public bool HasCompletedTour { get; set; }

    // Bluetooth / Wireless Headset Monitoring
    public bool EnableBluetoothHeadset { get; set; } = false;
    public string? BluetoothHeadsetDeviceId { get; set; }
    public string? BluetoothHeadsetDeviceName { get; set; }
    public bool BluetoothMuteMicSidetone { get; set; } = true;
    public Dictionary<string, bool> WirelessRouting { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mic"] = false,
        ["Chat"] = true,
        ["Musik"] = true,
        ["Game"] = true,
        ["Console"] = true,
        ["Line In"] = true,
        ["System"] = true,
        ["Sample"] = true,
        ["!@#$?*"] = false
    };

    public bool IsWirelessChannelRouted(string channelName)
    {
        if (WirelessRouting != null && WirelessRouting.TryGetValue(channelName, out var routed))
        {
            return routed;
        }

        // Default: everything except Mic and Bleep is routed
        return !channelName.Equals("Mic", StringComparison.OrdinalIgnoreCase) &&
               !channelName.Equals("Mikrofon", StringComparison.OrdinalIgnoreCase) &&
               !channelName.Equals("!@#$?*", StringComparison.OrdinalIgnoreCase);
    }

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* preferences are best-effort */ }
    }

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";

    public static string BuildCommitHash => GitBuildInfo.CommitHash;
    public static string FullBuildCommitHash => GitBuildInfo.FullCommitHash;
    public static string VersionDisplay => $"v{AppVersion} (Build {BuildCommitHash})";
}

/// <summary>
/// "Run on startup" via the per-user Run key (HKCU\…\Run). No admin rights needed; the value is
/// removed again when the option is turned off.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NextXLR";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe) key.SetValue(ValueName, $"\"{exe}\" --minimized");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>
/// One-time move from the old "GoXLR NEXT" identity: the data folder (settings, profiles,
/// default configuration) and the autostart entry.
/// </summary>
public static class Rebrand
{
    public static void MigrateFromGoXlrNext()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string oldDir = Path.Combine(local, "GoXLRNext"), newDir = Path.Combine(local, "NextXLR");
            if (Directory.Exists(oldDir) && !Directory.Exists(newDir)) Directory.Move(oldDir, newDir);

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key?.GetValue("GoXLRNext") is string)
            {
                key.DeleteValue("GoXLRNext", throwOnMissingValue: false);
                Autostart.Set(true);   // re-register with the new executable
            }
        }
        catch { /* best effort: the app works without the old data */ }
    }
}
