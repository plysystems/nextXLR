//! Power / session awareness: puts the GoXLR into standby when Windows shuts down, logs off
//! or suspends, and wakes it again on resume.
//!
//! `mixforged` is a windowless GUI-subsystem process, so console control handlers never fire
//! for it. Windows only tells *top-level windows* about the end of a session
//! (`WM_QUERYENDSESSION` / `WM_ENDSESSION`) and about suspend / resume (`WM_POWERBROADCAST`),
//! therefore a hidden, never-shown top-level window is created on its own thread.

use std::sync::OnceLock;
use std::time::Duration;

use tokio::sync::mpsc::UnboundedSender;

use crate::HubMsg;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PowerAction {
    Sleep,
    Wake,
}

/// Sends a power action to the hub and blocks (std thread!) until it was applied.
pub fn request_blocking(hub: &UnboundedSender<HubMsg>, action: PowerAction, timeout: Duration) -> bool {
    let (tx, rx) = std::sync::mpsc::channel();
    if hub.send(HubMsg::Power { action, ack: Some(tx) }).is_err() {
        return false;
    }
    rx.recv_timeout(timeout).is_ok()
}

static HUB: OnceLock<UnboundedSender<HubMsg>> = OnceLock::new();

/// How long Windows shutdown / suspend is held up at most while the device goes dark.
const STANDBY_TIMEOUT: Duration = Duration::from_millis(2500);

#[cfg(windows)]
pub fn spawn_watcher(hub: UnboundedSender<HubMsg>) {
    let _ = HUB.set(hub);
    let spawned = std::thread::Builder::new()
        .name("mixforge-power".into())
        .spawn(|| unsafe { win::run() });
    if let Err(e) = spawned {
        tracing::warn!("power watcher not started: {e}");
    }
}

#[cfg(not(windows))]
pub fn spawn_watcher(hub: UnboundedSender<HubMsg>) {
    let _ = HUB.set(hub);
}

fn on_standby(reason: &str) {
    if let Some(hub) = HUB.get() {
        tracing::info!("{reason}: putting GoXLR into standby");
        if !request_blocking(hub, PowerAction::Sleep, STANDBY_TIMEOUT) {
            tracing::warn!("{reason}: standby not confirmed in time");
        }
    }
}

fn on_resume() {
    if let Some(hub) = HUB.get() {
        tracing::info!("system resumed: waking GoXLR");
        let _ = hub.send(HubMsg::Power { action: PowerAction::Wake, ack: None });
    }
}

#[cfg(windows)]
#[allow(non_snake_case, clippy::upper_case_acronyms)]
mod win {
    use std::ffi::c_void;

    type HWND = isize;
    type HINSTANCE = isize;
    type WPARAM = usize;
    type LPARAM = isize;
    type LRESULT = isize;
    type WNDPROC = unsafe extern "system" fn(HWND, u32, WPARAM, LPARAM) -> LRESULT;

    #[repr(C)]
    struct WNDCLASSEXW {
        cbSize: u32,
        style: u32,
        lpfnWndProc: Option<WNDPROC>,
        cbClsExtra: i32,
        cbWndExtra: i32,
        hInstance: HINSTANCE,
        hIcon: isize,
        hCursor: isize,
        hbrBackground: isize,
        lpszMenuName: *const u16,
        lpszClassName: *const u16,
        hIconSm: isize,
    }

    #[repr(C)]
    struct MSG {
        hwnd: HWND,
        message: u32,
        wParam: WPARAM,
        lParam: LPARAM,
        time: u32,
        pt_x: i32,
        pt_y: i32,
        lPrivate: u32,
    }

    #[link(name = "user32")]
    extern "system" {
        fn RegisterClassExW(wc: *const WNDCLASSEXW) -> u16;
        fn CreateWindowExW(
            ex_style: u32,
            class: *const u16,
            title: *const u16,
            style: u32,
            x: i32,
            y: i32,
            w: i32,
            h: i32,
            parent: HWND,
            menu: isize,
            inst: HINSTANCE,
            param: *mut c_void,
        ) -> HWND;
        fn DefWindowProcW(hwnd: HWND, msg: u32, w: WPARAM, l: LPARAM) -> LRESULT;
        fn GetMessageW(msg: *mut MSG, hwnd: HWND, min: u32, max: u32) -> i32;
        fn TranslateMessage(msg: *const MSG) -> i32;
        fn DispatchMessageW(msg: *const MSG) -> LRESULT;
        fn ShutdownBlockReasonCreate(hwnd: HWND, reason: *const u16) -> i32;
        fn ShutdownBlockReasonDestroy(hwnd: HWND) -> i32;
    }

    #[link(name = "kernel32")]
    extern "system" {
        fn GetModuleHandleW(name: *const u16) -> HINSTANCE;
        fn SetProcessShutdownParameters(level: u32, flags: u32) -> i32;
    }

    const WM_QUERYENDSESSION: u32 = 0x0011;
    const WM_ENDSESSION: u32 = 0x0016;
    const WM_POWERBROADCAST: u32 = 0x0218;
    const PBT_APMSUSPEND: usize = 0x0004;
    const PBT_APMRESUMESUSPEND: usize = 0x0007;
    const PBT_APMRESUMEAUTOMATIC: usize = 0x0012;
    /// Ask to be notified early in the shutdown order (default apps are 0x280).
    const SHUTDOWN_LEVEL: u32 = 0x3FF;

    fn wide(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(std::iter::once(0)).collect()
    }

    unsafe extern "system" fn wnd_proc(hwnd: HWND, msg: u32, w: WPARAM, l: LPARAM) -> LRESULT {
        match msg {
            WM_QUERYENDSESSION => {
                // Show a reason while the standby command is being written, then allow the
                // end of the session. The actual work happens here because some shutdown
                // paths terminate the process right after WM_QUERYENDSESSION.
                let reason = wide("NEXT : XLR versetzt den GoXLR in den Ruhemodus …");
                ShutdownBlockReasonCreate(hwnd, reason.as_ptr());
                super::on_standby("Windows session ending");
                ShutdownBlockReasonDestroy(hwnd);
                1
            }
            WM_ENDSESSION => {
                if w != 0 {
                    super::on_standby("Windows session ended");
                } else {
                    // Shutdown was cancelled (e.g. by another app): light the device up again.
                    super::on_resume();
                }
                0
            }
            WM_POWERBROADCAST => {
                match w {
                    PBT_APMSUSPEND => super::on_standby("system suspending"),
                    PBT_APMRESUMEAUTOMATIC | PBT_APMRESUMESUSPEND => super::on_resume(),
                    _ => {}
                }
                1
            }
            _ => DefWindowProcW(hwnd, msg, w, l),
        }
    }

    pub unsafe fn run() {
        SetProcessShutdownParameters(SHUTDOWN_LEVEL, 0);
        let inst = GetModuleHandleW(std::ptr::null());
        let class = wide("NextXLR.PowerWatcher");
        let wc = WNDCLASSEXW {
            cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
            style: 0,
            lpfnWndProc: Some(wnd_proc),
            cbClsExtra: 0,
            cbWndExtra: 0,
            hInstance: inst,
            hIcon: 0,
            hCursor: 0,
            hbrBackground: 0,
            lpszMenuName: std::ptr::null(),
            lpszClassName: class.as_ptr(),
            hIconSm: 0,
        };
        if RegisterClassExW(&wc) == 0 {
            tracing::warn!("power watcher: RegisterClassExW failed");
            return;
        }
        let title = wide("NEXT : XLR Engine");
        // Top-level (not message-only: those don't receive session broadcasts), never shown.
        let hwnd = CreateWindowExW(
            0,
            class.as_ptr(),
            title.as_ptr(),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            inst,
            std::ptr::null_mut(),
        );
        if hwnd == 0 {
            tracing::warn!("power watcher: CreateWindowExW failed");
            return;
        }
        tracing::info!("power watcher active (shutdown / suspend → GoXLR standby)");
        let mut msg: MSG = std::mem::zeroed();
        while GetMessageW(&mut msg, 0, 0, 0) > 0 {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
}
