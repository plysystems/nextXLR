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
        new("mic", ""), new("headset", ""), new("headphones", ""), new("speakers", ""),
        new("chat", ""), new("people", ""), new("phone", ""), new("globe", ""),
        new("music", ""), new("album", ""), new("game", ""), new("monitor", ""),
        new("video", ""), new("camera", ""), new("megaphone", ""), new("robot", ""),
        new("bolt", ""), new("star", ""), new("heart", ""), new("bell", ""),
        new("volume", ""),
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
