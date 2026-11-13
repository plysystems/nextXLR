using System.Diagnostics;
using System.Net.Http;

namespace NextXLR.Services;

/// <summary>
/// Puts the GoXLR into standby (lighting, button LEDs and scribble strips dark) when the user
/// deliberately quits NEXT : XLR. The engine (mixforged) handles PC shutdown / suspend itself
/// through its own power watcher; this covers "Beenden" from the window or the tray.
/// </summary>
public static class HardwareStandbyService
{
    private static readonly int[] Ports = [StreamDeckStatusService.DefaultPort, 14565];

    /// <summary>
    /// Asks the engine to put the device into standby and exit. Returns true when the engine
    /// confirmed. Never throws; waits at most ~4 s in total.
    /// </summary>
    public static async Task<bool> ShutdownEngineAsync()
    {
        if (Process.GetProcessesByName("mixforged").Length == 0) return false;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3.5) };
        foreach (var port in Ports)
        {
            try
            {
                using var resp = await http.PostAsync($"http://127.0.0.1:{port}/api/power?action=shutdown", null);
                if (resp.IsSuccessStatusCode)
                {
                    await WaitForEngineExitAsync(TimeSpan.FromMilliseconds(1500));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Standby request on port {port} failed: {ex.Message}");
            }
        }
        return false;
    }

    private static async Task WaitForEngineExitAsync(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (Process.GetProcessesByName("mixforged").Length == 0) return;
            await Task.Delay(100);
        }
    }
}

