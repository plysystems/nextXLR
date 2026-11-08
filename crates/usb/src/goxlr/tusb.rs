//! Bindings to the TC-Helicon GoXLR driver API (`goxlr_audioapi_x64.dll`, a Thesycon TUSBAUDIO
//! build). Function signatures follow goxlr-utility (MIT), which reverse-engineered them.
//!
//! The DLL is loaded once and never unloaded; all function pointers live as long as the process.

use std::ffi::{c_char, CStr};
use std::sync::OnceLock;
use std::time::{Duration, Instant};

use anyhow::{anyhow, bail, Context, Result};
use libloading::Library;
use windows_sys::Win32::Foundation::{CloseHandle, HANDLE, WAIT_OBJECT_0};
use windows_sys::Win32::System::Threading::{CreateEventW, WaitForSingleObject};

const DEFAULT_DLL: &str = r"C:\Program Files\TC-HELICON\GoXLR_Audio_Driver\x64\goxlr_audioapi_x64.dll";
const CLSID_KEY: &str = r"CLSID\{024D0372-641F-4B7B-8140-F4DFE458C982}\InprocServer32";
/// `ReadDeviceNotification` status meaning "queue empty".
const STATUS_QUEUE_EMPTY: u32 = 3_992_977_442;

#[repr(C)]
#[derive(Clone, Copy)]
struct ApiVersion {
    major: u16,
    minor: u16,
}

#[repr(C)]
struct Properties {
    vendor_id: i32,
    product_id: i32,
    revision_number: i32,
    serial_number: [u16; 128],
    manufacturer: [u16; 128],
    model: [u16; 128],
    unknown_number: i32,
    unknown_string: [u16; 128],
}

type FnU32 = unsafe extern "C" fn() -> u32;
type FnApiVersion = unsafe extern "C" fn() -> ApiVersion;
type FnOpenByIndex = unsafe extern "C" fn(u32, *mut u32) -> u32;
type FnProperties = unsafe extern "C" fn(u32, *mut Properties) -> u32;
type FnVendorOut = unsafe extern "C" fn(u32, u32, u32, u32, u32, u16, u16, *const u8, *mut u8, u32) -> u32;
type FnVendorIn = unsafe extern "C" fn(u32, u32, u32, u32, u32, u16, u16, *mut u8, *mut u8, u32) -> u32;
type FnRegisterNotify = unsafe extern "C" fn(u32, u32, HANDLE, u32) -> u32;
type FnReadNotify = unsafe extern "C" fn(u32, *const u32, *mut u8, u32, *mut u32) -> u32;
type FnStatus = unsafe extern "C" fn(u32) -> *const c_char;
type FnClose = unsafe extern "C" fn(u32) -> u32;

struct Api {
    _lib: Library,
    enumerate: FnU32,
    api_version: FnApiVersion,
    device_count: FnU32,
    open_by_index: FnOpenByIndex,
    properties: FnProperties,
    vendor_out: FnVendorOut,
    vendor_in: FnVendorIn,
    register_notify: FnRegisterNotify,
    read_notify: FnReadNotify,
    status: FnStatus,
    close: FnClose,
}

// SAFETY: plain function pointers into a DLL that is never unloaded.
unsafe impl Send for Api {}
unsafe impl Sync for Api {}

fn locate_dll() -> String {
    use winreg::enums::HKEY_CLASSES_ROOT;
    let from_registry = winreg::RegKey::predef(HKEY_CLASSES_ROOT)
        .open_subkey(CLSID_KEY)
        .and_then(|k| k.get_value::<String, _>(""))
        .ok()
        .filter(|p| std::path::Path::new(p).exists());
    if let Some(path) = from_registry {
        return path;
    }

    let candidates = [
        r"C:\Program Files\TC-HELICON\GoXLR_Audio_Driver\x64\goxlr_audioapi_x64.dll",
        r"C:\Program Files\TC-HELICON\GoXLR_Audio_Driver\W10_x64\goxlr_audioapi_x64.dll",
        r"C:\Program Files (x86)\TC-HELICON\GoXLR_Audio_Driver\x64\goxlr_audioapi_x64.dll",
        r"C:\Program Files (x86)\TC-HELICON\GoXLR_Audio_Driver\W10_x64\goxlr_audioapi_x64.dll",
    ];

    for candidate in candidates {
        if std::path::Path::new(candidate).exists() {
            return candidate.to_string();
        }
    }

    if let Ok(exe_path) = std::env::current_exe() {
        if let Some(dir) = exe_path.parent() {
            let next_to_exe = dir.join("goxlr_audioapi_x64.dll");
            if next_to_exe.exists() {
                return next_to_exe.to_string_lossy().to_string();
            }
            let asset_dir = dir.join("Assets").join("Driver").join("goxlr_audioapi_x64.dll");
            if asset_dir.exists() {
                return asset_dir.to_string_lossy().to_string();
            }
        }
    }

    DEFAULT_DLL.to_string()
}

fn load() -> Result<Api> {
    let path = locate_dll();
    // SAFETY: loading the vendor driver DLL; it has no unusual init requirements.
    let lib = unsafe { Library::new(&path) }.with_context(|| format!("loading {path}"))?;
    macro_rules! sym {
        ($name:literal) => {
            // SAFETY: types match the reverse-engineered signatures.
            *unsafe { lib.get(concat!($name, "\0").as_bytes()) }.with_context(|| $name)?
        };
    }
    let api = Api {
        enumerate: sym!("TUSBAUDIO_EnumerateDevices"),
        api_version: sym!("TUSBAUDIO_GetApiVersion"),
        device_count: sym!("TUSBAUDIO_GetDeviceCount"),
        open_by_index: sym!("TUSBAUDIO_OpenDeviceByIndex"),
        properties: sym!("TUSBAUDIO_GetDeviceProperties"),
        vendor_out: sym!("TUSBAUDIO_ClassVendorRequestOut"),
        vendor_in: sym!("TUSBAUDIO_ClassVendorRequestIn"),
        register_notify: sym!("TUSBAUDIO_RegisterDeviceNotification"),
        read_notify: sym!("TUSBAUDIO_ReadDeviceNotification"),
        status: sym!("TUSBAUDIO_StatusCodeStringA"),
        close: sym!("TUSBAUDIO_CloseDevice"),
        _lib: lib,
    };
    // SAFETY: no arguments.
    let v = unsafe { (api.api_version)() };
    if !matches!((v.major, v.minor), (7, 5) | (11, 5) | (12, 5)) {
        tracing::warn!("untested GoXLR driver API {}.{} (known good: 7.5, 11.5, 12.5)", v.major, v.minor);
    } else {
        tracing::info!("GoXLR driver API {}.{} at {path}", v.major, v.minor);
    }
    Ok(api)
}

fn api() -> Result<&'static Api> {
    static API: OnceLock<std::result::Result<Api, String>> = OnceLock::new();
    API.get_or_init(|| load().map_err(|e| format!("{e:#}")))
        .as_ref()
        .map_err(|e| anyhow!("GoXLR driver unavailable: {e}"))
}

fn status_text(api: &Api, code: u32) -> String {
    // SAFETY: returns a static NUL-terminated string.
    let p = unsafe { (api.status)(code) };
    if p.is_null() {
        return format!("status {code}");
    }
    unsafe { CStr::from_ptr(p) }.to_string_lossy().into_owned()
}

fn check(api: &Api, code: u32, what: &str) -> Result<()> {
    if code == 0 {
        Ok(())
    } else {
        bail!("{what}: {}", status_text(api, code))
    }
}

/// What a driver notification told us.
#[derive(Debug, Default, Clone, Copy)]
pub struct Notifications {
    /// A response to our last vendor request is ready to read.
    pub data_ready: bool,
    /// A button / fader / encoder changed.
    pub input_changed: bool,
}

/// An open device handle with a registered notification event.
pub struct TusbDevice {
    api: &'static Api,
    handle: u32,
    event: HANDLE,
    pub vendor_id: u16,
    pub product_id: u16,
}

// SAFETY: the handle and event are only used from the owning hardware thread.
unsafe impl Send for TusbDevice {}

impl TusbDevice {
    /// Opens the first GoXLR the driver knows about.
    pub fn open_first() -> Result<Self> {
        let api = api()?;
        // SAFETY: plain driver calls with valid out-pointers.
        unsafe { (api.enumerate)() };
        let count = unsafe { (api.device_count)() };
        if count == 0 {
            bail!("the GoXLR driver reports no devices");
        }
        let mut handle = 0u32;
        check(api, unsafe { (api.open_by_index)(0, &mut handle) }, "OpenDeviceByIndex")?;

        let mut props: Properties = unsafe { std::mem::zeroed() };
        check(api, unsafe { (api.properties)(handle, &mut props) }, "GetDeviceProperties")?;

        // Auto-reset event, signalled by the driver whenever a notification is queued.
        let event = unsafe { CreateEventW(std::ptr::null(), 0, 0, std::ptr::null()) };
        if event.is_null() {
            unsafe { (api.close)(handle) };
            bail!("CreateEventW failed");
        }
        let dev = Self {
            api,
            handle,
            event,
            vendor_id: props.vendor_id as u16,
            product_id: props.product_id as u16,
        };
        check(api, unsafe { (api.register_notify)(handle, u32::MAX, event, 0) }, "RegisterDeviceNotification")?;
        Ok(dev)
    }

    pub fn vendor_out(&self, request: u8, value: u16, index: u16, data: &[u8]) -> Result<()> {
        let mut transferred = [0u8; 64];
        let len: u16 = data.len().try_into().context("request too large")?;
        // SAFETY: data and transferred are valid for the given lengths.
        let code = unsafe {
            (self.api.vendor_out)(
                self.handle, 1, 0, request.into(), value.into(), index, len,
                data.as_ptr(), transferred.as_mut_ptr(), transferred.len() as u32,
            )
        };
        check(self.api, code, "vendor request out")
    }

    pub fn vendor_in(&self, request: u8, value: u16, index: u16, length: usize) -> Result<Vec<u8>> {
        let mut buf = vec![0u8; length];
        let mut transferred = [0u8; 64];
        // SAFETY: buf and transferred are valid for the given lengths.
        let code = unsafe {
            (self.api.vendor_in)(
                self.handle, 1, 0, request.into(), value.into(), index, length as u16,
                buf.as_mut_ptr(), transferred.as_mut_ptr(), transferred.len() as u32,
            )
        };
        check(self.api, code, "vendor request in")?;
        let read = u32::from_le_bytes(transferred[..4].try_into().unwrap()) as usize;
        buf.truncate(read.min(length));
        Ok(buf)
    }

    /// Waits up to `timeout` for the driver event, then drains every queued notification.
    pub fn wait_notifications(&self, timeout: Duration) -> Notifications {
        let mut n = Notifications::default();
        let ms = timeout.as_millis().min(u32::MAX as u128) as u32;
        // SAFETY: event is a valid handle owned by self.
        if unsafe { WaitForSingleObject(self.event, ms) } != WAIT_OBJECT_0 {
            return n;
        }
        let mut buf = [0u8; 1024];
        let zero = 0u32;
        loop {
            let mut len = 0u32;
            // SAFETY: buf/len valid; `zero` mirrors the unknown in-param goxlr-utility passes.
            let code = unsafe {
                (self.api.read_notify)(self.handle, &zero, buf.as_mut_ptr(), buf.len() as u32, &mut len)
            };
            if code != 0 {
                if code != STATUS_QUEUE_EMPTY {
                    tracing::debug!("ReadDeviceNotification: {}", status_text(self.api, code));
                }
                break;
            }
            match &buf[..(len as usize).min(buf.len())] {
                [1, 1, 1, ..] => n.data_ready = true,
                [1, 1, 0, ..] => n.input_changed = true,
                _ => {}
            }
        }
        n
    }

    /// Blocks until a response is ready (or `timeout`), remembering input changes seen meanwhile.
    pub fn wait_data(&self, timeout: Duration, input_changed: &mut bool) -> bool {
        let deadline = Instant::now() + timeout;
        loop {
            let left = deadline.saturating_duration_since(Instant::now());
            if left.is_zero() {
                return false;
            }
            let n = self.wait_notifications(left);
            *input_changed |= n.input_changed;
            if n.data_ready {
                return true;
            }
        }
    }
}

impl Drop for TusbDevice {
    fn drop(&mut self) {
        // SAFETY: both handles were created by us and are closed exactly once.
        unsafe {
            (self.api.close)(self.handle);
            CloseHandle(self.event);
        }
    }
}
