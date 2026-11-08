//! Icon ids → Segoe Fluent Icons code points, for the hardware scribble strips.
//! Must match `apps/windows/Services/IconCatalog.cs` (same ids, same glyphs, same defaults).

use mixforge_protocol::ChannelKind;

const ICONS: &[(&str, char)] = &[
    ("mic", '\u{E720}'),
    ("headset", '\u{E95B}'),
    ("headphones", '\u{E7F6}'),
    ("speakers", '\u{E7F5}'),
    ("chat", '\u{E8BD}'),
    ("people", '\u{E716}'),
    ("phone", '\u{E717}'),
    ("globe", '\u{E774}'),
    ("music", '\u{E8D6}'),
    ("album", '\u{E93C}'),
    ("game", '\u{E7FC}'),
    ("monitor", '\u{E7F4}'),
    ("video", '\u{E714}'),
    ("camera", '\u{E722}'),
    ("megaphone", '\u{E789}'),
    ("robot", '\u{E99A}'),
    ("bolt", '\u{E945}'),
    ("star", '\u{E734}'),
    ("heart", '\u{EB51}'),
    ("bell", '\u{EA8F}'),
    ("volume", '\u{E767}'),
];

/// Default icon when the user hasn't picked one.
pub fn default_for(id: u32, kind: &ChannelKind) -> &'static str {
    match kind {
        ChannelKind::Microphone => "mic",
        ChannelKind::System => "monitor",
        ChannelKind::Sampler => "album",
        ChannelKind::LineIn => {
            if id == 7 {
                "game"
            } else {
                "speakers"
            }
        }
        ChannelKind::Output => match id {
            8 => "headphones",
            9 => "mic",
            11 => "video",
            12 => "headset",
            _ => "speakers",
        },
        _ => match id {
            1 => "chat",
            2 => "music",
            3 => "game",
            _ => "volume",
        },
    }
}

pub fn glyph(id: &str) -> Option<char> {
    ICONS.iter().find(|(k, _)| *k == id).map(|(_, c)| *c)
}
