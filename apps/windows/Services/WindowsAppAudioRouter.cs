using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace NextXLR.Services;

public enum AudioFlow { Render = 0, Capture = 1 }
public enum AudioRole { Console = 0, Multimedia = 1, Communications = 2 }

[ComImport]
[Guid("ab3d4648-e242-459f-b02f-541c70306324")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigFactory
{
    // IInspectable slots
    int GetIids(out uint iidCount, out IntPtr iids);
    int GetRuntimeClassName(out IntPtr className);
    int GetTrustLevel(out uint trustLevel);

    // IAudioPolicyConfigFactory slots
    int SetPersistedDefaultAudioEndpoint(uint processId, uint flow, uint role, IntPtr deviceId);
    int GetPersistedDefaultAudioEndpoint(uint processId, uint flow, uint role, [MarshalAs(UnmanagedType.HString)] out string deviceId);
    int ClearAllPersistedApplicationDefaultEndpoints();
}

public sealed record AudioEndpointChoice(string Id, string DisplayName, string ShortName, string Glyph);

public sealed class AppAudioSessionInfo
{
    public uint ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Glyph { get; set; } = "\uE7F6";
    public string CurrentOutputDeviceId { get; set; } = "";
    public string CurrentOutputName { get; set; } = "";
    public string CurrentInputDeviceId { get; set; } = "";
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
    public SimpleAudioVolume? VolumeControl { get; set; }
    public AudioMeterInformation? MeterInformation { get; set; }
}

/// <summary>
/// Native Windows App Volume & Device Router (Lautstärkemixer / App volume and device preferences).
/// Reads active Windows audio sessions and assigns them to GoXLR channels (Game, Chat, Music, System).
/// </summary>
public static class WindowsAppAudioRouter
{
    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    private static IAudioPolicyConfigFactory? GetPolicyFactory()
    {
        try
        {
            // Windows 11 / Windows 10 20H1+
            Guid iid = new("ab3d4648-e242-459f-b02f-541c70306324");
            if (RoGetActivationFactory("Windows.Media.Internal.AudioPolicyConfig", ref iid, out var factory) == 0 && factory != null)
            {
                return factory as IAudioPolicyConfigFactory;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Returns list of GoXLR output endpoints available for assignment.</summary>
    public static List<AudioEndpointChoice> GetOutputChoices()
    {
        var list = new List<AudioEndpointChoice>
        {
            new("", "Windows-Standard (Default)", "Standard", "\uE774") // Globe / Default
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

            // Order: System, Game, Chat, Music, Sample
            var preferred = new[] { "System", "Game", "Chat", "Music", "Sample" };
            foreach (var pref in preferred)
            {
                var dev = renderDevices.FirstOrDefault(d =>
                    (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                     d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                     d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase)) &&
                    d.FriendlyName.Contains(pref, StringComparison.OrdinalIgnoreCase));

                if (dev != null)
                {
                    var glyph = pref switch
                    {
                        "System" => "\uE7F8",  // PC
                        "Game" => "\uE7FC",    // Game controller
                        "Chat" => "\uE8BD",    // Chat bubble
                        "Music" => "\uEC4F",   // Music note
                        "Sample" => "\uEA38",  // Audio waveform
                        _ => "\uE7F6"
                    };
                    list.Add(new AudioEndpointChoice(dev.ID, $"{pref} (TC-Helicon GoXLR)", pref, glyph));
                }
            }

            // Fallback: If no GoXLR channels detected, add active render devices
            if (list.Count == 1)
            {
                foreach (var d in renderDevices)
                {
                    list.Add(new AudioEndpointChoice(d.ID, d.FriendlyName, d.FriendlyName, "\uE7F6"));
                }
            }
        }
        catch { }

        return list;
    }

    /// <summary>Returns list of GoXLR microphone / capture endpoints available for assignment.</summary>
    public static List<AudioEndpointChoice> GetInputChoices()
    {
        var list = new List<AudioEndpointChoice>
        {
            new("", "Windows-Standard (Default)", "Standard", "\uE774")
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

            var chatMic = captureDevices.FirstOrDefault(d =>
                (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase)) &&
                d.FriendlyName.Contains("Chat", StringComparison.OrdinalIgnoreCase));
            if (chatMic != null)
            {
                list.Add(new AudioEndpointChoice(chatMic.ID, "Chat Mic (TC-Helicon GoXLR) — Voice-Chat", "Chat Mic", "\uE720")); // Microphone
            }

            var broadcast = captureDevices.FirstOrDefault(d =>
                (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase)) &&
                (d.FriendlyName.Contains("Broadcast", StringComparison.OrdinalIgnoreCase) || d.FriendlyName.Contains("Stream", StringComparison.OrdinalIgnoreCase)));
            if (broadcast != null)
            {
                list.Add(new AudioEndpointChoice(broadcast.ID, "Broadcast Stream Mix (TC-Helicon GoXLR) — OBS / Stream", "Broadcast Mix", "\uE93C")); // Broadcast radio
            }

            var sample = captureDevices.FirstOrDefault(d =>
                (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase)) &&
                d.FriendlyName.Contains("Sample", StringComparison.OrdinalIgnoreCase));
            if (sample != null)
            {
                list.Add(new AudioEndpointChoice(sample.ID, "Sample (TC-Helicon GoXLR)", "Sample", "\uEA38"));
            }

            if (list.Count == 1)
            {
                foreach (var d in captureDevices)
                {
                    list.Add(new AudioEndpointChoice(d.ID, d.FriendlyName, d.FriendlyName, "\uE720"));
                }
            }
        }
        catch { }

        return list;
    }

    /// <summary>Scans active applications playing audio in Windows.</summary>
    public static List<AppAudioSessionInfo> GetActiveAppSessions()
    {
        var results = new Dictionary<uint, AppAudioSessionInfo>();
        var factory = GetPolicyFactory();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

            foreach (var dev in renderDevices)
            {
                AudioSessionManager? mgr = null;
                try
                {
                    mgr = dev.AudioSessionManager;
                }
                catch { continue; }

                if (mgr == null) continue;

                int sessionCount = 0;
                try { sessionCount = mgr.Sessions.Count; } catch { }

                for (int i = 0; i < sessionCount; i++)
                {
                    AudioSessionControl? s = null;
                    try { s = mgr.Sessions[i]; } catch { continue; }
                    if (s == null) continue;

                    uint pid = 0;
                    try { pid = s.GetProcessID; } catch { continue; }
                    if (pid == 0) continue; // system sound session
                    if (results.ContainsKey(pid)) continue;

                    string procName = "";
                    string windowTitle = "";
                    try
                    {
                        using var p = Process.GetProcessById((int)pid);
                        procName = p.ProcessName;
                        windowTitle = p.MainWindowTitle;
                    }
                    catch { procName = $"App (PID {pid})"; }

                    // Skip our own app processes
                    if (procName.Equals("NextXLR", StringComparison.OrdinalIgnoreCase) ||
                        procName.Equals("mixforged", StringComparison.OrdinalIgnoreCase) ||
                        procName.Equals("mixforge", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    float vol = 1.0f;
                    bool muted = false;
                    SimpleAudioVolume? volCtrl = null;
                    AudioMeterInformation? meterInfo = null;
                    try
                    {
                        volCtrl = s.SimpleAudioVolume;
                        if (volCtrl != null)
                        {
                            vol = volCtrl.Volume;
                            muted = volCtrl.Mute;
                        }
                    }
                    catch { }

                    try
                    {
                        meterInfo = s.AudioMeterInformation;
                    }
                    catch { }

                    var info = new AppAudioSessionInfo
                    {
                        ProcessId = pid,
                        ProcessName = procName,
                        DisplayName = !string.IsNullOrWhiteSpace(windowTitle) ? windowTitle : procName,
                        Glyph = DetectGlyph(procName),
                        CurrentOutputDeviceId = dev.ID,
                        CurrentOutputName = dev.FriendlyName,
                        Volume = vol,
                        IsMuted = muted,
                        VolumeControl = volCtrl,
                        MeterInformation = meterInfo
                    };

                    // Check if an explicit persisted endpoint is assigned via Windows Policy
                    if (factory != null)
                    {
                        try
                        {
                            if (factory.GetPersistedDefaultAudioEndpoint(pid, 0, 0, out var outDevId) == 0 && !string.IsNullOrEmpty(outDevId))
                            {
                                info.CurrentOutputDeviceId = outDevId;
                            }
                            if (factory.GetPersistedDefaultAudioEndpoint(pid, 1, 0, out var inDevId) == 0 && !string.IsNullOrEmpty(inDevId))
                            {
                                info.CurrentInputDeviceId = inDevId;
                            }
                        }
                        catch { }
                    }

                    results[pid] = info;
                }
            }
        }
        catch { }

        return results.Values.OrderBy(a => a.ProcessName).ToList();
    }

    /// <summary>
    /// Routes an application (and all of its child/sibling processes) to a specific audio output or microphone endpoint.
    /// </summary>
    public static bool SetAppEndpoint(uint processId, string processName, bool isCapture, string? endpointDeviceId)
    {
        var factory = GetPolicyFactory();
        if (factory == null) return false;

        uint flow = isCapture ? 1u : 0u; // 0 = Render, 1 = Capture
        IntPtr pStr = IntPtr.Zero;

        try
        {
            if (!string.IsNullOrWhiteSpace(endpointDeviceId))
            {
                pStr = Marshal.StringToCoTaskMemUni(endpointDeviceId);
            }

            // Gather all process IDs related to this application (audio helper processes like Spotify, Chrome, Discord)
            var pids = new HashSet<uint>();
            if (processId > 0) pids.Add(processId);

            if (!string.IsNullOrWhiteSpace(processName))
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(processName))
                    {
                        pids.Add((uint)p.Id);
                    }
                }
                catch { }
            }

            foreach (var pid in pids)
            {
                try
                {
                    // Assign Console (0), Multimedia (1), and Communications (2) roles
                    factory.SetPersistedDefaultAudioEndpoint(pid, flow, 0, pStr);
                    if (!isCapture)
                    {
                        factory.SetPersistedDefaultAudioEndpoint(pid, flow, 1, pStr);
                    }
                    else
                    {
                        factory.SetPersistedDefaultAudioEndpoint(pid, flow, 2, pStr);
                    }
                }
                catch { }
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetAppEndpoint error: {ex}");
            return false;
        }
        finally
        {
            if (pStr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pStr);
            }
        }
    }

    private static string DetectGlyph(string procName)
    {
        var p = procName.ToLowerInvariant();
        if (p.Contains("spotify") || p.Contains("music") || p.Contains("itunes") || p.Contains("vlc") || p.Contains("aimp")) return "\uEC4F"; // Music note
        if (p.Contains("discord") || p.Contains("teamspeak") || p.Contains("ts3") || p.Contains("skype") || p.Contains("zoom") || p.Contains("teams") || p.Contains("slack")) return "\uE8BD"; // Chat
        if (p.Contains("chrome") || p.Contains("edge") || p.Contains("firefox") || p.Contains("opera") || p.Contains("brave")) return "\uE774"; // Web / Globe
        if (p.Contains("steam") || p.Contains("cs2") || p.Contains("valorant") || p.Contains("apex") || p.Contains("game") || p.Contains("overwatch") || p.Contains("league")) return "\uE7FC"; // Controller
        if (p.Contains("obs") || p.Contains("streamlabs") || p.Contains("twitch") || p.Contains("broadcast")) return "\uE93C"; // Broadcast
        return "\uE7F6"; // Default audio device
    }

    private static readonly string RulesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NextXLR",
        "app_routing_rules.json"
    );

    public record AppRoutingRule(string ProcessName, string? OutputEndpointId, string? InputEndpointId);

    public static Dictionary<string, AppRoutingRule> LoadRules()
    {
        try
        {
            if (File.Exists(RulesFilePath))
            {
                var json = File.ReadAllText(RulesFilePath);
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, AppRoutingRule>>(json);
                if (dict != null) return dict;
            }
        }
        catch { }
        return new Dictionary<string, AppRoutingRule>(StringComparer.OrdinalIgnoreCase);
    }

    public static void SaveRule(string processName, string? outputEndpointId, string? inputEndpointId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            var rules = LoadRules();
            rules[processName] = new AppRoutingRule(processName, outputEndpointId, inputEndpointId);

            var dir = Path.GetDirectoryName(RulesFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = System.Text.Json.JsonSerializer.Serialize(rules, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RulesFilePath, json);
        }
        catch { }
    }
}
