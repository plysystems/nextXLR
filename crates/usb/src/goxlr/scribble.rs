//! Scribble strips: the 128×64 monochrome LCDs above faders A–D (full GoXLR only).
//!
//! Payload of `SetScribble` (0x802 | fader): 1024 bytes, 8-pixel-high pages, one byte per
//! column: `byte = 128 · (y / 8) + x`, `bit = y % 8`. The background has every bit set; a dark
//! pixel clears its bit. Layout and the 1.2× horizontal icon stretch (the LCD's pixels are not
//! square) follow goxlr-utility (MIT), `scribbles/src/lib.rs`.
//!
//! Text and icons are rendered with the fonts Windows already ships — Segoe UI Bold for the
//! label and Segoe Fluent Icons (Win 11) / Segoe MDL2 Assets (Win 10) for the icon, i.e. the very
//! glyphs the app shows — so no font is bundled.

use ab_glyph::{Font, FontVec, PxScale, ScaleFont};

pub const WIDTH: usize = 128;
pub const HEIGHT: usize = 64;
pub const BYTES: usize = WIDTH * HEIGHT / 8;

/// 1-bit canvas; `true` = dark pixel.
pub struct Canvas {
    px: Vec<bool>,
}

impl Default for Canvas {
    fn default() -> Self {
        Self { px: vec![false; WIDTH * HEIGHT] }
    }
}

impl Canvas {
    pub fn set(&mut self, x: i32, y: i32) {
        if (0..WIDTH as i32).contains(&x) && (0..HEIGHT as i32).contains(&y) {
            self.px[y as usize * WIDTH + x as usize] = true;
        }
    }

    pub fn get(&self, x: usize, y: usize) -> bool {
        self.px[y * WIDTH + x]
    }

    /// Packs into the GoXLR page format.
    pub fn to_goxlr(&self) -> [u8; BYTES] {
        let mut bytes = [0xFFu8; BYTES];
        for y in 0..HEIGHT {
            for x in 0..WIDTH {
                if self.get(x, y) {
                    bytes[WIDTH * (y / 8) + x] &= !(1 << (y % 8));
                }
            }
        }
        bytes
    }

    /// Draws one glyph with its top-left at (x, y); coverage ≥ 0.5 becomes a dark pixel.
    fn draw_glyph(&mut self, font: &FontVec, ch: char, scale: PxScale, x: f32, baseline: f32) {
        let glyph = font.glyph_id(ch).with_scale_and_position(scale, ab_glyph::point(x, baseline));
        if let Some(outline) = font.outline_glyph(glyph) {
            let b = outline.px_bounds();
            outline.draw(|gx, gy, c| {
                if c >= 0.5 {
                    self.set(b.min.x as i32 + gx as i32, b.min.y as i32 + gy as i32);
                }
            });
        }
    }
}

pub struct Fonts {
    text: FontVec,
    icons: Option<FontVec>,
}

impl Fonts {
    /// Loads Segoe UI Bold and the Fluent/MDL2 icon font from the Windows fonts folder.
    pub fn load_system() -> anyhow::Result<Self> {
        let dir = std::env::var("WINDIR").map(|w| format!("{w}\\Fonts")).unwrap_or_else(|_| r"C:\Windows\Fonts".into());
        let read = |name: &str| std::fs::read(format!("{dir}\\{name}")).ok().and_then(|b| FontVec::try_from_vec(b).ok());
        let text = read("segoeuib.ttf")
            .or_else(|| read("segoeui.ttf"))
            .ok_or_else(|| anyhow::anyhow!("Segoe UI not found in {dir}"))?;
        let icons = read("SegoeIcons.ttf").or_else(|| read("segmdl2.ttf"));
        Ok(Self { text, icons })
    }
}

/// Renders a label (bottom) and an optional icon glyph (top), like the official app.
pub fn render(fonts: &Fonts, label: &str, icon: Option<char>) -> [u8; BYTES] {
    let mut canvas = Canvas::default();
    let has_icon = icon.is_some() && fonts.icons.is_some();

    // ---- label: largest size (≤ 20 px) that fits the width ----
    let label = label.trim();
    if !label.is_empty() {
        let mut size = if has_icon { 19.0 } else { 26.0 };
        let width = |s: f32| {
            let f = fonts.text.as_scaled(PxScale::from(s));
            label.chars().map(|c| f.h_advance(fonts.text.glyph_id(c))).sum::<f32>()
        };
        while size > 9.0 && width(size) > (WIDTH - 4) as f32 {
            size -= 1.0;
        }
        let scale = PxScale::from(size);
        let scaled = fonts.text.as_scaled(scale);
        let mut x = ((WIDTH as f32 - width(size)) / 2.0).max(1.0);
        let baseline = if has_icon {
            HEIGHT as f32 - 3.0 - scaled.descent().abs() * 0.5
        } else {
            (HEIGHT as f32 + scaled.ascent() * 0.75) / 2.0
        };
        for c in label.chars() {
            canvas.draw_glyph(&fonts.text, c, scale, x, baseline);
            x += scaled.h_advance(fonts.text.glyph_id(c));
        }
    }

    // ---- icon: centred in the upper area, stretched 1.2× horizontally ----
    if let (Some(ch), Some(icons)) = (icon, fonts.icons.as_ref()) {
        let size = if label.is_empty() { 50.0 } else { 34.0 };
        let scale = PxScale { x: size * 1.2, y: size };
        let glyph = icons.glyph_id(ch).with_scale_and_position(scale, ab_glyph::point(0.0, 0.0));
        if let Some(outline) = icons.outline_glyph(glyph) {
            let b = outline.px_bounds();
            let (w, h) = (b.width(), b.height());
            let area_h = if label.is_empty() { HEIGHT as f32 } else { 42.0 };
            let ox = ((WIDTH as f32 - w) / 2.0).round() as i32;
            let oy = ((area_h - h) / 2.0).round().max(1.0) as i32;
            outline.draw(|gx, gy, c| {
                if c >= 0.5 {
                    canvas.set(ox + gx as i32, oy + gy as i32);
                }
            });
        }
    }

    canvas.to_goxlr()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn page_packing_matches_goxlr_layout() {
        let mut c = Canvas::default();
        c.set(0, 0); // byte 0, bit 0
        c.set(5, 9); // page 1 → byte 128 + 5, bit 1
        c.set(127, 63); // last page, last column, bit 7
        let b = c.to_goxlr();
        assert_eq!(b[0], 0xFE);
        assert_eq!(b[128 + 5], 0xFD);
        assert_eq!(b[1023], 0x7F);
        assert_eq!(b.iter().filter(|&&v| v != 0xFF).count(), 3);
    }

    #[test]
    fn renders_label_and_icon_with_system_fonts() {
        let Ok(fonts) = Fonts::load_system() else { return }; // non-Windows CI: skip
        let text_only = render(&fonts, "Discord", None);
        let with_icon = render(&fonts, "Discord", Some('\u{E8BD}'));
        let dark = |b: &[u8; BYTES]| b.iter().map(|v| v.count_zeros()).sum::<u32>();
        assert!(dark(&text_only) > 60, "label drawn");
        assert!(dark(&with_icon) > dark(&text_only) - 40, "icon adds pixels");
        assert_ne!(text_only, with_icon);
        assert_eq!(render(&fonts, "", None), [0xFF; BYTES], "empty = blank screen");
    }
}
