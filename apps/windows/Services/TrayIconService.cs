using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace NextXLR.Services;

public sealed class TrayIconService : IDisposable
{
    private const uint WM_USER = 0x0400;
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAY = WM_APP + 1042;
    private const uint WM_COMMAND = 0x0111;

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int NIN_SELECT = 0x0400;
    private const int NIN_KEYSELECT = 0x0401;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_INFO = 0x00000001;

    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;

    private const int CMD_OPEN = 1001;
    private const int CMD_MIXER = 1002;
    private const int CMD_MIC = 1003;
    private const int CMD_ROUTING = 1004;
    private const int CMD_SETTINGS = 1005;
    private const int CMD_EXIT = 1006;

    private static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

    private readonly nint _hWnd;
    private readonly Action _onRestore;
    private readonly Action<string> _onNavigate;
    private readonly Action _onExit;
    private readonly SUBCLASSPROC _subclassDelegate;

    private nint _hIcon = nint.Zero;
    private bool _isAdded = false;
    private bool _disposed = false;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate nint SUBCLASSPROC(nint hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(nint hMenu, uint uItem, uint fByPos);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint Msg, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lpTPMParams);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    public TrayIconService(nint hWnd, Action onRestore, Action<string> onNavigate, Action onExit)
    {
        _hWnd = hWnd;
        _onRestore = onRestore;
        _onNavigate = onNavigate;
        _onExit = onExit;

        _subclassDelegate = SubclassProc;
        SetWindowSubclass(_hWnd, _subclassDelegate, 1042, 0);

        AddOrUpdateTrayIcon();
    }

    private void EnsureIconHandle()
    {
        if (_hIcon != nint.Zero) return;

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "nextXLR_icon.ico");
            if (File.Exists(iconPath))
            {
                _hIcon = LoadImage(nint.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            }
        }
        catch { }

        if (_hIcon == nint.Zero)
        {
            try
            {
                var procPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
                {
                    ExtractIconEx(procPath, 0, out _, out _hIcon, 1);
                }
            }
            catch { }
        }
    }

    public bool AddOrUpdateTrayIcon()
    {
        if (_disposed) return false;

        try
        {
            EnsureIconHandle();

            var nid = CreateNotifyData();
            nid.uFlags = (int)(NIF_MESSAGE | NIF_ICON | NIF_TIP);
            nid.szTip = "NEXT : XLR (GoXLR Audiosteuerung)";

            if (_isAdded)
            {
                if (Shell_NotifyIcon(NIM_MODIFY, ref nid))
                {
                    return true;
                }
            }

            for (int i = 0; i < 5; i++)
            {
                if (Shell_NotifyIcon(NIM_ADD, ref nid))
                {
                    _isAdded = true;
                    return true;
                }
                Thread.Sleep(100);
            }
        }
        catch { }

        return false;
    }

    public void ShowNotification(string title, string message)
    {
        if (!_isAdded && !AddOrUpdateTrayIcon()) return;
        try
        {
            var nid = CreateNotifyData();
            nid.uFlags = (int)(NIF_INFO);
            nid.dwInfoFlags = (int)NIIF_INFO;
            nid.szInfoTitle = title;
            nid.szInfo = message;
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }
        catch { }
    }

    private NOTIFYICONDATA CreateNotifyData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1042,
            uCallbackMessage = (int)WM_TRAY,
            hIcon = _hIcon
        };
    }

    private nint SubclassProc(nint hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == Program.WM_SHOW_NEXTXLR)
        {
            _onRestore?.Invoke();
            return nint.Zero;
        }

        if (WM_TASKBARCREATED != 0 && uMsg == WM_TASKBARCREATED)
        {
            AddOrUpdateTrayIcon();
            return nint.Zero;
        }

        if (uMsg == WM_TRAY)
        {
            int eventId = (int)(lParam.ToInt64() & 0xFFFF);
            if (eventId == WM_LBUTTONUP || eventId == WM_LBUTTONDBLCLK || eventId == NIN_SELECT || eventId == NIN_KEYSELECT)
            {
                _onRestore?.Invoke();
                return nint.Zero;
            }
            if (eventId == WM_RBUTTONUP || eventId == WM_CONTEXTMENU)
            {
                ShowContextMenu();
                return nint.Zero;
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        nint hMenu = CreatePopupMenu();
        if (hMenu == nint.Zero) return;

        try
        {
            AppendMenu(hMenu, MF_STRING, CMD_OPEN, "NEXT : XLR öffnen");
            AppendMenu(hMenu, MF_STRING, CMD_MIXER, "Mischpult");
            AppendMenu(hMenu, MF_STRING, CMD_MIC, "Mikrofon (DSP)");
            AppendMenu(hMenu, MF_STRING, CMD_ROUTING, "Routing Matrix");
            AppendMenu(hMenu, MF_STRING, CMD_SETTINGS, "Einstellungen");
            AppendMenu(hMenu, MF_SEPARATOR, 0, string.Empty);
            AppendMenu(hMenu, MF_STRING, CMD_EXIT, "Beenden");

            SetMenuDefaultItem(hMenu, CMD_OPEN, 0);

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hWnd);
            uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, _hWnd, nint.Zero);
            PostMessage(_hWnd, 0 /* WM_NULL */, 0, 0);

            switch (cmd)
            {
                case CMD_OPEN:
                    _onRestore?.Invoke();
                    break;
                case CMD_MIXER:
                    _onRestore?.Invoke();
                    _onNavigate?.Invoke("mixer");
                    break;
                case CMD_MIC:
                    _onRestore?.Invoke();
                    _onNavigate?.Invoke("mic");
                    break;
                case CMD_ROUTING:
                    _onRestore?.Invoke();
                    _onNavigate?.Invoke("routing");
                    break;
                case CMD_SETTINGS:
                    _onRestore?.Invoke();
                    _onNavigate?.Invoke("settings");
                    break;
                case CMD_EXIT:
                    _onExit?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isAdded)
        {
            var nid = CreateNotifyData();
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _isAdded = false;
        }

        RemoveWindowSubclass(_hWnd, _subclassDelegate, 1042);

        if (_hIcon != nint.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = nint.Zero;
        }
    }
}
