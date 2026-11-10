using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace NextXLR.Services;

public enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

[Guid("ab3d4648-e242-459f-b02f-541c70306324")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigFactoryVariantFor21H2
{
    // IInspectable methods
    int GetIids(out uint iidCount, out IntPtr iids);
    int GetRuntimeClassName(out IntPtr className);
    int GetTrustLevel(out int trustLevel);

    // 19 dummy methods
    int __incomplete__add_CtxVolumeChange();
    int __incomplete__remove_CtxVolumeChanged();
    int __incomplete__add_RingerVibrateStateChanged();
    int __incomplete__remove_RingerVibrateStateChange();
    int __incomplete__SetVolumeGroupGainForId();
    int __incomplete__GetVolumeGroupGainForId();
    int __incomplete__GetActiveVolumeGroupForEndpointId();
    int __incomplete__GetVolumeGroupsForEndpoint();
    int __incomplete__GetCurrentVolumeContext();
    int __incomplete__SetVolumeGroupMuteForId();
    int __incomplete__GetVolumeGroupMuteForId();
    int __incomplete__SetRingerVibrateState();
    int __incomplete__GetRingerVibrateState();
    int __incomplete__SetPreferredChatApplication();
    int __incomplete__ResetPreferredChatApplication();
    int __incomplete__GetPreferredChatApplication();
    int __incomplete__GetCurrentChatApplications();
    int __incomplete__add_ChatContextChanged();
    int __incomplete__remove_ChatContextChanged();

    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);
    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, out IntPtr deviceId);
    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}

[Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigFactoryVariantForDownlevel
{
    // IInspectable methods
    int GetIids(out uint iidCount, out IntPtr iids);
    int GetRuntimeClassName(out IntPtr className);
    int GetTrustLevel(out int trustLevel);

    // 19 dummy methods
    int __incomplete__add_CtxVolumeChange();
    int __incomplete__remove_CtxVolumeChanged();
    int __incomplete__add_RingerVibrateStateChanged();
    int __incomplete__remove_RingerVibrateStateChange();
    int __incomplete__SetVolumeGroupGainForId();
    int __incomplete__GetVolumeGroupGainForId();
    int __incomplete__GetActiveVolumeGroupForEndpointId();
    int __incomplete__GetVolumeGroupsForEndpoint();
    int __incomplete__GetCurrentVolumeContext();
    int __incomplete__SetVolumeGroupMuteForId();
    int __incomplete__GetVolumeGroupMuteForId();
    int __incomplete__SetRingerVibrateState();
    int __incomplete__GetRingerVibrateState();
    int __incomplete__SetPreferredChatApplication();
    int __incomplete__ResetPreferredChatApplication();
    int __incomplete__GetPreferredChatApplication();
    int __incomplete__GetCurrentChatApplications();
    int __incomplete__add_ChatContextChanged();
    int __incomplete__remove_ChatContextChanged();

    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);
    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, out IntPtr deviceId);
    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}

internal interface IAudioPolicyConfigWrapper
{
    int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);
    int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, out string deviceId);
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
    private const string DEVINTERFACE_AUDIO_RENDER = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string DEVINTERFACE_AUDIO_CAPTURE = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";
    private const string MMDEVAPI_TOKEN = @"\\?\SWD#MMDEVAPI#";

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    private class PolicyConfig21H2Adapter : IAudioPolicyConfigWrapper
    {
        private readonly IAudioPolicyConfigFactoryVariantFor21H2 _factory;
        public PolicyConfig21H2Adapter(IAudioPolicyConfigFactoryVariantFor21H2 factory) => _factory = factory;
        public int SetPersistedDefaultAudioEndpoint(uint pid, EDataFlow flow, ERole role, IntPtr devId) => _factory.SetPersistedDefaultAudioEndpoint(pid, flow, role, devId);
        public int GetPersistedDefaultAudioEndpoint(uint pid, EDataFlow flow, ERole role, out string devId)
        {
            devId = "";
            int hr = _factory.GetPersistedDefaultAudioEndpoint(pid, flow, role, out IntPtr hStr);
            if (hr == 0 && hStr != IntPtr.Zero)
            {
                IntPtr buf = WindowsGetStringRawBuffer(hStr, out uint len);
                devId = Marshal.PtrToStringUni(buf, (int)len) ?? "";
                WindowsDeleteString(hStr);
            }
            return hr;
        }
        public int ClearAllPersistedApplicationDefaultEndpoints() => _factory.ClearAllPersistedApplicationDefaultEndpoints();
    }

    private class PolicyConfigDownlevelAdapter : IAudioPolicyConfigWrapper
    {
        private readonly IAudioPolicyConfigFactoryVariantForDownlevel _factory;
        public PolicyConfigDownlevelAdapter(IAudioPolicyConfigFactoryVariantForDownlevel factory) => _factory = factory;
        public int SetPersistedDefaultAudioEndpoint(uint pid, EDataFlow flow, ERole role, IntPtr devId) => _factory.SetPersistedDefaultAudioEndpoint(pid, flow, role, devId);
        public int GetPersistedDefaultAudioEndpoint(uint pid, EDataFlow flow, ERole role, out string devId)
        {
            devId = "";
            int hr = _factory.GetPersistedDefaultAudioEndpoint(pid, flow, role, out IntPtr hStr);
            if (hr == 0 && hStr != IntPtr.Zero)
            {
                IntPtr buf = WindowsGetStringRawBuffer(hStr, out uint len);
                devId = Marshal.PtrToStringUni(buf, (int)len) ?? "";
                WindowsDeleteString(hStr);
            }
            return hr;
        }
        public int ClearAllPersistedApplicationDefaultEndpoints() => _factory.ClearAllPersistedApplicationDefaultEndpoints();
    }

    private static IAudioPolicyConfigWrapper? _cachedFactory;
    private static readonly object _lock = new();

    private static IAudioPolicyConfigWrapper? GetPolicyFactory()
    {
        lock (_lock)
        {
            if (_cachedFactory != null) return _cachedFactory;

            const string className = "Windows.Media.Internal.AudioPolicyConfig";
            if (WindowsCreateString(className, (uint)className.Length, out IntPtr hClass) != 0) return null;

            try
            {
                // Primary: Windows 11 / Windows 10 21H2+
                Guid iid21H2 = typeof(IAudioPolicyConfigFactoryVariantFor21H2).GUID;
                if (RoGetActivationFactory(hClass, ref iid21H2, out var f21H2) == 0 &&
                    f21H2 is IAudioPolicyConfigFactoryVariantFor21H2 v21H2)
                {
                    _cachedFactory = new PolicyConfig21H2Adapter(v21H2);
                    return _cachedFactory;
                }
            }
            catch { }

            try
            {
                // Fallback: Windows 10 Downlevel
                Guid iidDl = typeof(IAudioPolicyConfigFactoryVariantForDownlevel).GUID;
                if (RoGetActivationFactory(hClass, ref iidDl, out var fDl) == 0 &&
                    fDl is IAudioPolicyConfigFactoryVariantForDownlevel vDl)
                {
                    _cachedFactory = new PolicyConfigDownlevelAdapter(vDl);
                    return _cachedFactory;
                }
            }
            catch { }
            finally
            {
                WindowsDeleteString(hClass);
            }

            return null;
        }
    }

    public static string FormatFullDeviceId(string deviceId, bool isCapture)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return "";
        if (deviceId.StartsWith(MMDEVAPI_TOKEN, StringComparison.OrdinalIgnoreCase)) return deviceId;
        return $"{MMDEVAPI_TOKEN}{deviceId}{(isCapture ? DEVINTERFACE_AUDIO_CAPTURE : DEVINTERFACE_AUDIO_RENDER)}";
    }

    public static string UnpackDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return "";
        if (deviceId.StartsWith(MMDEVAPI_TOKEN, StringComparison.OrdinalIgnoreCase))
            deviceId = deviceId.Substring(MMDEVAPI_TOKEN.Length);
        if (deviceId.EndsWith(DEVINTERFACE_AUDIO_RENDER, StringComparison.OrdinalIgnoreCase))
            deviceId = deviceId.Substring(0, deviceId.Length - DEVINTERFACE_AUDIO_RENDER.Length);
        if (deviceId.EndsWith(DEVINTERFACE_AUDIO_CAPTURE, StringComparison.OrdinalIgnoreCase))
            deviceId = deviceId.Substring(0, deviceId.Length - DEVINTERFACE_AUDIO_CAPTURE.Length);
        return deviceId;
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
                     d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase) ||
                     d.FriendlyName.Equals(pref, StringComparison.OrdinalIgnoreCase) ||
                     d.FriendlyName.StartsWith(pref + " (", StringComparison.OrdinalIgnoreCase)) &&
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
                 d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.StartsWith("Chat Mic", StringComparison.OrdinalIgnoreCase)) &&
                d.FriendlyName.Contains("Chat", StringComparison.OrdinalIgnoreCase));
            if (chatMic != null)
            {
                list.Add(new AudioEndpointChoice(chatMic.ID, "Chat Mic (TC-Helicon GoXLR) — Voice-Chat", "Chat Mic", "\uE720")); // Microphone
            }

            var broadcast = captureDevices.FirstOrDefault(d =>
                (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.StartsWith("Broadcast", StringComparison.OrdinalIgnoreCase)) &&
                (d.FriendlyName.Contains("Broadcast", StringComparison.OrdinalIgnoreCase) || d.FriendlyName.Contains("Stream", StringComparison.OrdinalIgnoreCase)));
            if (broadcast != null)
            {
                list.Add(new AudioEndpointChoice(broadcast.ID, "Broadcast Stream Mix (TC-Helicon GoXLR) — OBS / Stream", "Broadcast Mix", "\uE93C")); // Broadcast radio
            }

            var sample = captureDevices.FirstOrDefault(d =>
                (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.StartsWith("Sample", StringComparison.OrdinalIgnoreCase)) &&
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
                            if (factory.GetPersistedDefaultAudioEndpoint(pid, EDataFlow.eRender, ERole.eMultimedia, out var outDevId) == 0 && !string.IsNullOrEmpty(outDevId))
                            {
                                info.CurrentOutputDeviceId = UnpackDeviceId(outDevId);
                            }
                            if (factory.GetPersistedDefaultAudioEndpoint(pid, EDataFlow.eCapture, ERole.eMultimedia, out var inDevId) == 0 && !string.IsNullOrEmpty(inDevId))
                            {
                                info.CurrentInputDeviceId = UnpackDeviceId(inDevId);
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

        EDataFlow flow = isCapture ? EDataFlow.eCapture : EDataFlow.eRender;
        IntPtr hStr = IntPtr.Zero;

        try
        {
            if (!string.IsNullOrWhiteSpace(endpointDeviceId))
            {
                var fullId = FormatFullDeviceId(endpointDeviceId, isCapture);
                WindowsCreateString(fullId, (uint)fullId.Length, out hStr);
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
                    factory.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.eMultimedia, hStr);
                    factory.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.eConsole, hStr);
                    if (isCapture)
                    {
                        factory.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.eCommunications, hStr);
                    }
                }
                catch { }
            }

            // Sync to Windows Registry
            SyncRegistryDefaultEndpoint(processName, isCapture, endpointDeviceId);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetAppEndpoint error: {ex}");
            return false;
        }
        finally
        {
            if (hStr != IntPtr.Zero)
            {
                WindowsDeleteString(hStr);
            }
        }
    }

    private static void SyncRegistryDefaultEndpoint(string processName, bool isCapture, string? endpointDeviceId)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        try
        {
            using var baseKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Multimedia\Audio\DefaultEndpoint", true);
            if (baseKey != null)
            {
                string? fullId = string.IsNullOrWhiteSpace(endpointDeviceId) ? null : FormatFullDeviceId(endpointDeviceId, isCapture);
                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    using var subKey = baseKey.OpenSubKey(subName, true);
                    if (subKey == null) continue;
                    var stdVal = subKey.GetValue("") as string;
                    if (!string.IsNullOrEmpty(stdVal) && stdVal.Contains(processName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (isCapture)
                        {
                            if (string.IsNullOrEmpty(fullId))
                            {
                                try { subKey.DeleteValue("000_001", false); } catch { }
                                try { subKey.DeleteValue("001_001", false); } catch { }
                            }
                            else
                            {
                                subKey.SetValue("000_001", fullId, Microsoft.Win32.RegistryValueKind.String);
                                subKey.SetValue("001_001", fullId, Microsoft.Win32.RegistryValueKind.String);
                            }
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(fullId))
                            {
                                try { subKey.DeleteValue("000_000", false); } catch { }
                                try { subKey.DeleteValue("001_000", false); } catch { }
                            }
                            else
                            {
                                subKey.SetValue("000_000", fullId, Microsoft.Win32.RegistryValueKind.String);
                                subKey.SetValue("001_000", fullId, Microsoft.Win32.RegistryValueKind.String);
                            }
                        }
                    }
                }
            }
        }
        catch { }
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
