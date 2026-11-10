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

[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceNative
{
    [PreserveSig] int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2Native
{
    [PreserveSig] int GetAudioSessionControl(IntPtr AudioSessionGuid, int StreamFlags, out IntPtr SessionControl);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr AudioSessionGuid, int StreamFlags, out IntPtr AudioVolume);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumeratorNative SessionEnum);
    [PreserveSig] int RegisterSessionNotification(IntPtr SessionNotification);
    [PreserveSig] int UnregisterSessionNotification(IntPtr SessionNotification);
    [PreserveSig] int RegisterDuckNotification(string sessionID, IntPtr duckNotification);
    [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
}

[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumeratorNative
{
    [PreserveSig] int GetCount(out int SessionCount);
    [PreserveSig] int GetSession(int SessionIndex, out IntPtr Session);
}

[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2Native
{
    [PreserveSig] int GetState(out int pRetVal);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, IntPtr EventContext);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, IntPtr EventContext);
    [PreserveSig] int GetGroupingParam(out Guid pRetVal);
    [PreserveSig] int SetGroupingParam(ref Guid Override, IntPtr EventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr NewNotifications);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr NewNotifications);
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    [PreserveSig] int GetProcessId(out uint pRetVal);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference(bool optOut);
}

[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISimpleAudioVolumeNative
{
    [PreserveSig] int SetMasterVolume(float fLevel, IntPtr EventContext);
    [PreserveSig] int GetMasterVolume(out float pfLevel);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, IntPtr EventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioMeterInformationNative
{
    [PreserveSig] int GetPeakValue(out float pfPeak);
    [PreserveSig] int GetMeteringChannelCount(out uint pnChannelCount);
    [PreserveSig] int GetChannelsPeakValues(uint u32ChannelCount, [Out, MarshalAs(UnmanagedType.LPArray)] float[] afPeakValues);
    [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
}

[Guid("ab3d4648-e242-459f-b02f-541c70306324")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigFactoryVariantFor21H2
{
    int GetIids(out uint iidCount, out IntPtr iids);
    int GetRuntimeClassName(out IntPtr className);
    int GetTrustLevel(out int trustLevel);

    int m01(); int m02(); int m03(); int m04(); int m05(); int m06();
    int m07(); int m08(); int m09(); int m10(); int m11(); int m12();
    int m13(); int m14(); int m15(); int m16(); int m17(); int m18(); int m19();

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
    int GetIids(out uint iidCount, out IntPtr iids);
    int GetRuntimeClassName(out IntPtr className);
    int GetTrustLevel(out int trustLevel);

    int m01(); int m02(); int m03(); int m04(); int m05(); int m06();
    int m07(); int m08(); int m09(); int m10(); int m11(); int m12();
    int m13(); int m14(); int m15(); int m16(); int m17(); int m18(); int m19();

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
    public ISimpleAudioVolumeNative? VolumeControl { get; set; }
    public IAudioMeterInformationNative? MeterInformation { get; set; }
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
            new("", "Windows-Standard (Default)", "Standard", "\uE774")
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

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
                        "System" => "\uE7F8",
                        "Game" => "\uE7FC",
                        "Chat" => "\uE8BD",
                        "Music" => "\uEC4F",
                        "Sample" => "\uEA38",
                        _ => "\uE7F6"
                    };
                    list.Add(new AudioEndpointChoice(dev.ID, $"{pref} (TC-Helicon GoXLR)", pref, glyph));
                }
            }

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
                list.Add(new AudioEndpointChoice(chatMic.ID, "Chat Mic (TC-Helicon GoXLR) — Voice-Chat", "Chat Mic", "\uE720"));
            }

            var broadcast = captureDevices.FirstOrDefault(d =>
                (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.StartsWith("Broadcast", StringComparison.OrdinalIgnoreCase)) &&
                (d.FriendlyName.Contains("Broadcast", StringComparison.OrdinalIgnoreCase) || d.FriendlyName.Contains("Stream", StringComparison.OrdinalIgnoreCase)));
            if (broadcast != null)
            {
                list.Add(new AudioEndpointChoice(broadcast.ID, "Broadcast Stream Mix (TC-Helicon GoXLR) — OBS / Stream", "Broadcast Mix", "\uE93C"));
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

    /// <summary>
    /// Scans active applications playing audio in Windows via high-performance direct COM interop.
    /// </summary>
    public static List<AppAudioSessionInfo> GetActiveAppSessions()
    {
        var results = new Dictionary<uint, AppAudioSessionInfo>();
        var factory = GetPolicyFactory();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

            var devInterfaceField = typeof(MMDevice).GetField("deviceInterface", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Guid iidMgr2 = typeof(IAudioSessionManager2Native).GUID;

            foreach (var dev in renderDevices)
            {
                try
                {
                    var rawImmObj = devInterfaceField?.GetValue(dev);
                    if (rawImmObj is not IMMDeviceNative imm) continue;

                    if (imm.Activate(ref iidMgr2, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object objMgr2) != 0 ||
                        objMgr2 is not IAudioSessionManager2Native mgr2)
                    {
                        continue;
                    }

                    if (mgr2.GetSessionEnumerator(out var sEnum) != 0 || sEnum == null)
                    {
                        continue;
                    }

                    if (sEnum.GetCount(out int sessionCount) != 0)
                    {
                        sessionCount = 0;
                    }

                    for (int i = 0; i < sessionCount; i++)
                    {
                        if (sEnum.GetSession(i, out IntPtr pSession) != 0 || pSession == IntPtr.Zero)
                        {
                            continue;
                        }

                        try
                        {
                            if (Marshal.GetObjectForIUnknown(pSession) is not IAudioSessionControl2Native sc2)
                            {
                                continue;
                            }
                            sc2.GetProcessId(out uint pid);
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

                            // Skip our own background & app processes
                            if (procName.Equals("NextXLR", StringComparison.OrdinalIgnoreCase) ||
                                procName.Equals("mixforged", StringComparison.OrdinalIgnoreCase) ||
                                procName.Equals("mixforge", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            float vol = 1.0f;
                            bool muted = false;
                            ISimpleAudioVolumeNative? volCtrl = null;
                            IAudioMeterInformationNative? meterInfo = null;

                            try
                            {
                                if (Marshal.GetObjectForIUnknown(pSession) is ISimpleAudioVolumeNative sav)
                                {
                                    volCtrl = sav;
                                    sav.GetMasterVolume(out vol);
                                    sav.GetMute(out muted);
                                }
                            }
                            catch { }

                            try
                            {
                                if (Marshal.GetObjectForIUnknown(pSession) is IAudioMeterInformationNative ami)
                                {
                                    meterInfo = ami;
                                }
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

                            // Query Windows Policy for explicit assigned device
                            if (factory != null)
                            {
                                try
                                {
                                    if (factory.GetPersistedDefaultAudioEndpoint(pid, EDataFlow.eRender, ERole.eMultimedia, out var outDevId) == 0 && !string.IsNullOrEmpty(outDevId))
                                    {
                                        info.CurrentOutputDeviceId = UnpackDeviceId(outDevId);
                                    }
                                    if (factory.GetPersistedDefaultAudioEndpoint(pid, EDataFlow.eCapture, ERole.eCommunications, out var inDevId) == 0 && !string.IsNullOrEmpty(inDevId))
                                    {
                                        info.CurrentInputDeviceId = UnpackDeviceId(inDevId);
                                    }
                                }
                                catch { }
                            }

                            results[pid] = info;
                        }
                        finally
                        {
                            Marshal.Release(pSession);
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return results.Values.OrderBy(a => a.ProcessName).ToList();
    }

    /// <summary>
    /// Routes an application (and all of its child/sibling processes) to a specific audio output or microphone endpoint.
    /// Sets all 3 roles: Multimedia, Console, and Communications.
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
            else
            {
                // Windows requires empty string HSTRING to reset to system default
                WindowsCreateString("", 0, out hStr);
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
                    factory.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.eCommunications, hStr);
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
                                try { subKey.DeleteValue("002_001", false); } catch { }
                            }
                            else
                            {
                                subKey.SetValue("000_001", fullId, Microsoft.Win32.RegistryValueKind.String);
                                subKey.SetValue("001_001", fullId, Microsoft.Win32.RegistryValueKind.String);
                                subKey.SetValue("002_001", fullId, Microsoft.Win32.RegistryValueKind.String);
                            }
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(fullId))
                            {
                                try { subKey.DeleteValue("000_000", false); } catch { }
                                try { subKey.DeleteValue("001_000", false); } catch { }
                                try { subKey.DeleteValue("002_000", false); } catch { }
                            }
                            else
                            {
                                subKey.SetValue("000_000", fullId, Microsoft.Win32.RegistryValueKind.String);
                                subKey.SetValue("001_000", fullId, Microsoft.Win32.RegistryValueKind.String);
                                subKey.SetValue("002_000", fullId, Microsoft.Win32.RegistryValueKind.String);
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
