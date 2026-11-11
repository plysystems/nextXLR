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
    public bool AutostartEnabled { get; set; } = false;
    public bool MuteOnStartup { get; set; } = false;
    public bool CheckForUpdatesOnStart { get; set; } = true;
    public double BleepVolume { get; set; } = 100.0;
    public bool HasCompletedTour { get; set; } = true;

    // Auto-Ducking (Sidechain Speech Detection)
    public bool AutoDuckingEnabled { get; set; } = false;
    public string AutoDuckingTrigger { get; set; } = "Mic";
    public string AutoDuckingTarget { get; set; } = "Music";
    public double AutoDuckingThresholdDb { get; set; } = -32.0;
    public double AutoDuckingDuckAmountDb { get; set; } = -12.0;
    public double AutoDuckingHoldMs { get; set; } = 600.0;
    public double AutoDuckingReleaseMs { get; set; } = 500.0;

    // Bluetooth / Wireless Headset Monitoring
    public bool EnableBluetoothHeadset { get; set; } = false;
    public string? BluetoothHeadsetDeviceId { get; set; }
    public string? BluetoothHeadsetDeviceName { get; set; }
    public bool BluetoothMuteMicSidetone { get; set; } = true;

    // Mixer Pages (Bank 1 & Bank 2) - GoXLR full model only
    public int ActiveMixerPage { get; set; } = 1;
    public uint[] MixerPage1Channels { get; set; } = [0, 1, 2, 4]; // Mic, Chat, Music, System
    public uint[] MixerPage2Channels { get; set; } = [3, 7, 6, 5]; // Game, Console, Line In, Samples
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

    public void EnsureDistinctMixerPages()
    {
        uint[] allDefaults = [0, 1, 2, 4, 3, 7, 6, 5];
        if (MixerPage1Channels == null || MixerPage1Channels.Length != 4)
            MixerPage1Channels = [0, 1, 2, 4];
        if (MixerPage2Channels == null || MixerPage2Channels.Length != 4)
            MixerPage2Channels = [3, 7, 6, 5];

        var used = new HashSet<uint>();
        for (int i = 0; i < 4; i++)
        {
            if (!used.Add(MixerPage1Channels[i]))
            {
                var replacement = allDefaults.FirstOrDefault(c => !used.Contains(c));
                MixerPage1Channels[i] = replacement;
                used.Add(replacement);
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (!used.Add(MixerPage2Channels[i]))
            {
                var replacement = allDefaults.FirstOrDefault(c => !used.Contains(c));
                MixerPage2Channels[i] = replacement;
                used.Add(replacement);
            }
        }
    }

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
            AppSettings settings;
            if (File.Exists(FilePath))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            }
            else
            {
                settings = new();
            }
            settings.EnsureDistinctMixerPages();
            return settings;
        }
        catch
        {
            var fallback = new AppSettings();
            fallback.EnsureDistinctMixerPages();
            return fallback;
        }
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
            var settings = AppSettings.Load();
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            bool regEnabled = key?.GetValue(ValueName) is string;

            // Self-heal: If settings.json states autostart is enabled but registry was cleared by update, restore it!
            if (settings.AutostartEnabled && !regEnabled)
            {
                Set(true);
                return true;
            }

            return settings.AutostartEnabled || regEnabled;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            var settings = AppSettings.Load();
            settings.AutostartEnabled = enabled;
            settings.Save();
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.DeleteValue("NextXLR_BackgroundService", throwOnMissingValue: false);
            key.DeleteValue("mixforge", throwOnMissingValue: false);
            key.DeleteValue("mixforged", throwOnMissingValue: false);
            key.DeleteValue("GoXLRNext", throwOnMissingValue: false);

            if (enabled && Environment.ProcessPath is { } exe)
            {
                key.SetValue(ValueName, $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { }
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
            if (key != null)
            {
                key.DeleteValue("NextXLR_BackgroundService", throwOnMissingValue: false);
                key.DeleteValue("mixforge", throwOnMissingValue: false);
                key.DeleteValue("mixforged", throwOnMissingValue: false);
                if (key.GetValue("GoXLRNext") is string)
                {
                    key.DeleteValue("GoXLRNext", throwOnMissingValue: false);
                    Autostart.Set(true);   // re-register with the new executable
                }
            }
        }
        catch { /* best effort: the app works without the old data */ }
    }
}
