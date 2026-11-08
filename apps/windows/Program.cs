using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace NextXLR;

/// <summary>
/// Bulletproof single-instance application bootstrapper.
/// Guarantees that NEXT : XLR can only ever be started once.
/// If an instance is already running (normal, minimized, or in the tray),
/// the existing instance is woken up and brought into the foreground, and the new instance terminates immediately.
/// </summary>
public static class Program
{
    private const string MutexName = "Local\\NextXLR_SingleInstance_Mutex_plysystems_9a8f";
    public const string WakeUpEventName = "NextXLR_WakeUp_Event_plysystems_7b8c";
    public static readonly uint WM_SHOW_NEXTXLR = RegisterWindowMessage("NEXTXLR_SHOW_INSTANCE_MSG");

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _wakeUpEvent;

    [STAThread]
    public static void Main(string[] args)
    {
        // 1. First line of defense: Check existing processes
        var currentProc = Process.GetCurrentProcess();
        var existingProcesses = Process.GetProcessesByName("NextXLR")
            .Where(p => p.Id != currentProc.Id)
            .ToList();

        if (existingProcesses.Count > 0)
        {
            // Another instance is definitely already executing!
            WakeUpExistingInstance(existingProcesses);
            return;
        }

        // 2. Second line of defense: Global/Local Mutex
        bool createdNew;
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
        }
        catch
        {
            createdNew = false;
        }

        if (!createdNew)
        {
            WakeUpExistingInstance(existingProcesses);
            return;
        }

        // 3. Start listener for future instance wake-up signals
        try
        {
            _wakeUpEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeUpEventName);
            var listenerThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_wakeUpEvent.WaitOne())
                        {
                            MainWindow.CurrentInstance?.RestoreWindow();
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "NextXLR_SingleInstance_Listener"
            };
            listenerThread.Start();
        }
        catch { }

        // 4. Launch WinUI 3 App
        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start((p) => {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static void WakeUpExistingInstance(System.Collections.Generic.List<Process> processes)
    {
        try
        {
            // A: Signal via Named EventWaitHandle
            if (EventWaitHandle.TryOpenExisting(WakeUpEventName, out var existingEvent))
            {
                existingEvent.Set();
            }
        }
        catch { }

        try
        {
            // B: Broadcast registered Windows desktop message
            PostMessage((IntPtr)0xFFFF, WM_SHOW_NEXTXLR, UIntPtr.Zero, IntPtr.Zero);
        }
        catch { }

        try
        {
            // C: Enumerate windows to restore & bring existing window to front
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (processes.Any(p => p.Id == pid))
                {
                    ShowWindow(hWnd, 9); // SW_RESTORE
                    SetForegroundWindow(hWnd);
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
