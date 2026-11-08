//! Real GoXLR support. Protocol is portable; the transport is Windows-only for now
//! (the Linux/macOS libusb transport is a later addition).

pub mod effects;
pub mod lighting;
pub mod mic;
pub mod protocol;
pub mod routing;
pub mod scribble;

#[cfg(windows)]
mod device;
#[cfg(windows)]
mod tusb;

#[cfg(windows)]
pub use device::GoXlrDevice;
