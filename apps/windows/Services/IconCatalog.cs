namespace NextXLR.Services;

/// <summary>
/// Icons a user can assign to a fader, from the Segoe Fluent Icons font that ships with
/// Windows 11 (MDL2 fallback on Windows 10) — no extra dependency. The id is what the daemon
/// stores, so the glyph set can change without breaking saved settings.
/// </summary>
public static class IconCatalog
{
    public sealed record Entry(string Id, string Glyph);

    public static readonly IReadOnlyList<Entry> All =
    [
        // Audio & Hardware
        new("mic", "\uE720"),
        new("mic_off", "\uE74F"),
        new("headset", "\uE95B"),
        new("headphones", "\uE7F6"),
        new("speakers", "\uE7F5"),
        new("volume", "\uE767"),
        new("volume_low", "\uE992"),
        new("equalizer", "\uE9E9"),
        new("broadcast", "\uEC52"),
        new("radio", "\uE753"),
        new("soundwave", "\uEA69"),

        // Musik & Medien
        new("music", "\uE8D6"),
        new("album", "\uE93C"),
        new("playlist", "\uE8D7"),
        new("guitar", "\uE7C0"),
        new("play", "\uE768"),
        new("record", "\uE7C8"),
        new("film", "\uE8B2"),

        // Gaming & Streaming
        new("game", "\uE7FC"),
        new("console", "\uE756"),
        new("trophy", "\uE7F8"),
        new("dice", "\uE7C3"),
        new("robot", "\uE99A"),
        new("alien", "\uE7F0"),
        new("skull", "\uE722"),

        // Kommunikation & Social
        new("chat", "\uE8BD"),
        new("message", "\uE8F2"),
        new("people", "\uE716"),
        new("phone", "\uE717"),
        new("globe", "\uE774"),
        new("video", "\uE714"),
        new("webcam", "\uE8B8"),
        new("camera", "\uE722"),
        new("megaphone", "\uE789"),
        new("podcast", "\uE95A"),

        // Hardware & PC
        new("monitor", "\uE7F4"),
        new("tv", "\uE790"),
        new("keyboard", "\uE92E"),
        new("mouse", "\uE962"),
        new("cable", "\uE8AB"),
        new("usb", "\uE88E"),
        new("bluetooth", "\uE702"),
        new("wifi", "\uE701"),
        new("power", "\uE7E8"),
        new("settings", "\uE713"),
        new("code", "\uE943"),
        new("terminal", "\uE756"),

        // Emotes & Effekte
        new("bolt", "\uE945"),
        new("star", "\uE735"),
        new("heart", "\uEB51"),
        new("fire", "\uE7A6"),
        new("sparkle", "\uE728"),
        new("bell", "\uEA8F"),
        new("shield", "\uEA18"),
        new("coffee", "\uE70F"),
        new("clock", "\uE823"),
        new("target", "\uE7C4"),
        new("bookmark", "\uE8A4")
    ];

    private static readonly Dictionary<string, string> ById = All.ToDictionary(e => e.Id, e => e.Glyph);

    /// <summary>Default icon for a channel when the user hasn't picked one.</summary>
    public static string DefaultFor(uint channelId, string kind) => kind switch
    {
        "microphone" => "mic",
        "system" => "monitor",
        "sampler" => "album",
        "line_in" => channelId == 7 ? "game" : "speakers",
        "output" => channelId switch { 8 => "headphones", 9 => "mic", 11 => "video", 12 => "headset", _ => "speakers" },
        _ => channelId switch { 1 => "chat", 2 => "music", 3 => "game", _ => "volume" },
    };

    public static string Glyph(string? id) => id is not null && ById.TryGetValue(id, out var g) ? g : "";
}
