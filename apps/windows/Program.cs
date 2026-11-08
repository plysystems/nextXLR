using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace NextXLR;

/// <summary>
/// Single-instance application bootstrapper.
/// Guarantees that NEXT : XLR can only be started once. If an instance is already running,
/// the existing window is restored and brought to the front, and the second instance exits immediately.
/// </summary>
public static class Program
{
    private const string MutexName = "Local\\NextXLR_SingleInstance_Mutex_plysystems_9a8f";
    private static Mutex? _singleInstanceMutex;

    // Custom window message registered across the desktop to wake up the existing instance (even if hidden in tray)
    public static readonly uint WM_SHOW_NEXTXLR = RegisterWindowMessage("NEXTXLR_SHOW_INSTANCE_MSG");

    [STAThread]
    static void Main(string[] args)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            // Another instance is already running!
            // Broadcast WM_SHOW_NEXTXLR so the existing instance wakes up from tray or minimized state
            PostMessage((IntPtr)0xFFFF, WM_SHOW_NEXTXLR, UIntPtr.Zero, IntPtr.Zero); // HWND_BROADCAST = 0xFFFF

            // Also try direct window handle restoration
            BringExistingInstanceToFront();
            return;
        }

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start((p) => {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static void BringExistingInstanceToFront()
    {
        try
        {
            var currentProc = Process.GetCurrentProcess();
            var running = Process.GetProcessesByName("NextXLR")
                .FirstOrDefault(p => p.Id != currentProc.Id);

            if (running != null && running.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(running.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(running.MainWindowHandle);
            }
            else
            {
                var hWnd = FindWindow(null, "NEXT : XLR");
                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, 9);
                    SetForegroundWindow(hWnd);
                }
            }
        }
        catch { }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
}

