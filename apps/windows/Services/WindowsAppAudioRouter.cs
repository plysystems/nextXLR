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
    public string Glyph { get; set; } = "";
    public string CurrentOutputDeviceId { get; set; } = "";
    public string CurrentOutputName { get; set; } = "";
    public string CurrentInputDeviceId { get; set; } = "";
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
    public SimpleAudioVolume? VolumeControl { get; set; }
}

/// <summary>
/// Native Windows App Volume & Device Router (Lautstärkemixer / App volume and device preferences).
/// Reads active Windows audio sessions and assigns them to GoXLR channels (Game, Chat, Music, System).
/// </summary>
public static class WindowsAppAudioRouter
{
    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    private static IAudioPolicyConfigFactory? GetPolicyFactory()
    {
        try
        {
            // Windows 11 / Windows 10 21H2+
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
            new("", "Windows-Standard (Default)", "Standard", "")
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
                    d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                    d.FriendlyName.Contains(pref, StringComparison.OrdinalIgnoreCase));

                if (dev != null)
                {
                    var glyph = pref switch
                    {
                        "System" => "💻",
                        "Game" => "🎮",
                        "Chat" => "💬",
                        "Music" => "🎵",
                        "Sample" => "🔊",
                        _ => ""
                    };
                    list.Add(new AudioEndpointChoice(dev.ID, $"{pref} (TC-Helicon GoXLR)", pref, glyph));
                }
            }

            // Also add any other active render devices if GoXLR is not connected
            if (list.Count == 1)
            {
                foreach (var d in renderDevices)
                {
                    list.Add(new AudioEndpointChoice(d.ID, d.FriendlyName, d.FriendlyName, ""));
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
            new("", "Windows-Standard (Default)", "Standard", "")
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

            var chatMic = captureDevices.FirstOrDefault(d =>
                d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                d.FriendlyName.Contains("Chat", StringComparison.OrdinalIgnoreCase));
            if (chatMic != null)
            {
                list.Add(new AudioEndpointChoice(chatMic.ID, "Chat Mic (TC-Helicon GoXLR) — Voice-Chat", "Chat Mic", "🎙️"));
            }

            var broadcast = captureDevices.FirstOrDefault(d =>
                d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                (d.FriendlyName.Contains("Broadcast", StringComparison.OrdinalIgnoreCase) || d.FriendlyName.Contains("Stream", StringComparison.OrdinalIgnoreCase)));
            if (broadcast != null)
            {
                list.Add(new AudioEndpointChoice(broadcast.ID, "Broadcast Stream Mix (TC-Helicon GoXLR) — OBS / Stream", "Broadcast Mix", "📻"));
            }

            var sample = captureDevices.FirstOrDefault(d =>
                d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                d.FriendlyName.Contains("Sample", StringComparison.OrdinalIgnoreCase));
            if (sample != null)
            {
                list.Add(new AudioEndpointChoice(sample.ID, "Sample (TC-Helicon GoXLR)", "Sample", "🔊"));
            }

            if (list.Count == 1)
            {
                foreach (var d in captureDevices)
                {
                    list.Add(new AudioEndpointChoice(d.ID, d.FriendlyName, d.FriendlyName, "🎤"));
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
                var mgr = dev.AudioSessionManager;
                for (int i = 0; i < mgr.Sessions.Count; i++)
                {
                    var s = mgr.Sessions[i];
                    var pid = s.GetProcessID;
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

                    // Skip our own app
                    if (procName.Equals("NextXLR", StringComparison.OrdinalIgnoreCase) ||
                        procName.Equals("mixforged", StringComparison.OrdinalIgnoreCase) ||
                        procName.Equals("mixforge", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var info = new AppAudioSessionInfo
                    {
                        ProcessId = pid,
                        ProcessName = procName,
                        DisplayName = !string.IsNullOrWhiteSpace(windowTitle) ? windowTitle : procName,
                        Glyph = DetectGlyph(procName),
                        CurrentOutputDeviceId = dev.ID,
                        CurrentOutputName = dev.FriendlyName,
                        Volume = s.SimpleAudioVolume.Volume,
                        IsMuted = s.SimpleAudioVolume.Mute,
                        VolumeControl = s.SimpleAudioVolume
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
    /// Routes an application to a specific audio output or microphone endpoint.
    /// </summary>
    public static bool SetAppEndpoint(uint processId, bool isCapture, string? endpointDeviceId)
    {
        var factory = GetPolicyFactory();
        if (factory == null) return false;

        uint flow = isCapture ? 1u : 0u; // 0 = Render, 1 = Capture
        IntPtr pStr = IntPtr.Zero;

        try
        {
            if (!string.IsNullOrWhiteSpace(endpointDeviceId))
            {
                pStr = Marshal.StringToHGlobalUni(endpointDeviceId);
            }

            // Assign Console, Multimedia, and Communications roles
            factory.SetPersistedDefaultAudioEndpoint(processId, flow, 0, pStr);
            if (!isCapture)
            {
                factory.SetPersistedDefaultAudioEndpoint(processId, flow, 1, pStr);
            }
            else
            {
                factory.SetPersistedDefaultAudioEndpoint(processId, flow, 2, pStr);
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
                Marshal.FreeHGlobal(pStr);
            }
        }
    }

    private static string DetectGlyph(string procName)
    {
        var p = procName.ToLowerInvariant();
        if (p.Contains("spotify") || p.Contains("music") || p.Contains("itunes") || p.Contains("vlc") || p.Contains("aimp")) return "🎵";
        if (p.Contains("discord") || p.Contains("teamspeak") || p.Contains("ts3") || p.Contains("skype") || p.Contains("zoom") || p.Contains("teams") || p.Contains("slack")) return "💬";
        if (p.Contains("chrome") || p.Contains("edge") || p.Contains("firefox") || p.Contains("opera") || p.Contains("brave")) return "🌐";
        if (p.Contains("steam") || p.Contains("cs2") || p.Contains("valorant") || p.Contains("apex") || p.Contains("game") || p.Contains("overwatch")) return "🎮";
        if (p.Contains("obs") || p.Contains("streamlabs") || p.Contains("twitch") || p.Contains("broadcast")) return "📻";
        return "";
    }
}

