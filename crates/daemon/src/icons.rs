//! Icon ids → Segoe Fluent Icons code points, for the hardware scribble strips.
//! Must match `apps/windows/Services/IconCatalog.cs` (same ids, same glyphs, same defaults).

use mixforge_protocol::ChannelKind;

const ICONS: &[(&str, char)] = &[
    // Audio & Hardware
    ("mic", '\u{E720}'),
    ("mic_off", '\u{E74F}'),
    ("headset", '\u{E95B}'),
    ("headphones", '\u{E7F6}'),
    ("speakers", '\u{E7F5}'),
    ("volume", '\u{E767}'),
    ("volume_low", '\u{E992}'),
    ("equalizer", '\u{E9E9}'),
    ("broadcast", '\u{EC52}'),
    ("radio", '\u{E753}'),
    ("soundwave", '\u{EA69}'),

    // Musik & Medien
    ("music", '\u{E8D6}'),
    ("album", '\u{E93C}'),
    ("playlist", '\u{E8D7}'),
    ("guitar", '\u{E7C0}'),
    ("play", '\u{E768}'),
    ("record", '\u{E7C8}'),
    ("film", '\u{E8B2}'),

    // Gaming & Streaming
    ("game", '\u{E7FC}'),
    ("console", '\u{E756}'),
    ("trophy", '\u{E7F8}'),
    ("dice", '\u{E7C3}'),
    ("robot", '\u{E99A}'),
    ("alien", '\u{E7F0}'),
    ("skull", '\u{E722}'),

    // Kommunikation & Social
    ("chat", '\u{E8BD}'),
    ("message", '\u{E8F2}'),
    ("people", '\u{E716}'),
    ("phone", '\u{E717}'),
    ("globe", '\u{E774}'),
    ("video", '\u{E714}'),
    ("webcam", '\u{E8B8}'),
    ("camera", '\u{E722}'),
    ("megaphone", '\u{E789}'),
    ("podcast", '\u{E95A}'),

    // Hardware & PC
    ("monitor", '\u{E7F4}'),
    ("tv", '\u{E790}'),
    ("keyboard", '\u{E92E}'),
    ("mouse", '\u{E962}'),
    ("cable", '\u{E8AB}'),
    ("usb", '\u{E88E}'),
    ("bluetooth", '\u{E702}'),
    ("wifi", '\u{E701}'),
    ("power", '\u{E7E8}'),
    ("settings", '\u{E713}'),
    ("code", '\u{E943}'),
    ("terminal", '\u{E756}'),

    // Emotes & Effekte
    ("bolt", '\u{E945}'),
    ("star", '\u{E735}'),
    ("heart", '\u{EB51}'),
    ("fire", '\u{E7A6}'),
    ("sparkle", '\u{E728}'),
    ("bell", '\u{EA8F}'),
    ("shield", '\u{EA18}'),
    ("coffee", '\u{E70F}'),
    ("clock", '\u{E823}'),
    ("target", '\u{E7C4}'),
    ("bookmark", '\u{E8A4}'),
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
