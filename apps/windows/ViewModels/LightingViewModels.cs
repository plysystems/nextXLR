using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.Protocol;
using NextXLR.Services;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NextXLR.ViewModels;

/// <summary>
/// Lighting page model. Zones come from the device capabilities, colours from the daemon state.
/// Every edit is sent straight to the daemon (coalesced to ~60/s while dragging a picker), which
/// rebuilds the GoXLR colour map and writes it to the device — so the hardware follows live.
/// </summary>
public sealed class LightingViewModel
{
    private static readonly (string Id, string Glyph)[] GroupOrder =
        [("faders", ""), ("buttons", ""), ("effects", ""), ("sampler", ""), ("device", "")];

    private readonly PipeTransport _transport;
    private IReadOnlyList<string> _zoneIds = [];

    public ObservableCollection<LightingGroupViewModel> Groups { get; } = new();
    /// <summary>Fader A–D, shown in their own gradient section.</summary>
    public ObservableCollection<LightingZoneViewModel> Faders { get; } = new();

    /// <summary>User-created custom lighting themes saved on disk.</summary>
    public ObservableCollection<CustomLightingTheme> CustomThemes { get; } = new();

    /// <summary>Featured multi-colour preset.</summary>
    public LightingTheme GtaVi { get; } = LightingTheme.GtaVi();

    /// <summary>One-click single-colour and multi-colour game schemes for the whole device.</summary>
    public IReadOnlyList<LightingTheme> Themes { get; } =
    [
        LightingTheme.Wardogs(),
        LightingTheme.GtaVi(),
        LightingTheme.GtaV(),
        LightingTheme.LeagueOfLegends(),
        LightingTheme.WorldOfWarcraft(),
        LightingTheme.Valorant(),
        LightingTheme.CounterStrike(),
        new("ThemeTeal", "#1FC79A", "#103A30"),
        new("ThemeOcean", "#2F8CFF", "#0C2547"),
        new("ThemeViolet", "#9B6BFF", "#2A1B4D"),
        new("ThemeSunset", "#FF7A3D", "#4A2010"),
        new("ThemeCrimson", "#FF3355", "#4A0E18"),
        new("ThemeIce", "#E8F4FF", "#3A4652"),
        new("ThemeOff", "#000000", "#000000"),
    ];

    public LightingViewModel(PipeTransport transport)
    {
        _transport = transport;
        RefreshCustomThemes();
    }

    public void RefreshCustomThemes()
    {
        CustomThemes.Clear();
        foreach (var t in CustomThemeService.List())
        {
            CustomThemes.Add(t);
        }
    }

    public CustomLightingTheme SaveCurrentAsTheme(string name)
    {
        var theme = new CustomLightingTheme
        {
            Name = name.Trim(),
            Author = Environment.UserName,
            CreatedAt = DateTime.Now
        };

        var allZones = Faders.Concat(Groups.SelectMany(g => g.Zones)).ToList();
        var swatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var z in allZones)
        {
            theme.Zones[z.Id] = new ZoneColorData
            {
                Active = z.ActiveHex,
                Inactive = z.InactiveHex,
                Gradient = z.IsFader && z.Gradient
            };

            if (!string.IsNullOrEmpty(z.ActiveHex)) swatches.Add(z.ActiveHex);
        }

        theme.Swatches = swatches.Take(5).ToList();
        CustomThemeService.Save(theme);
        RefreshCustomThemes();
        return theme;
    }

    public void ApplyCustomTheme(CustomLightingTheme t)
    {
        var zones = new List<LightingZone>();
        foreach (var zid in _zoneIds)
        {
            if (t.Zones.TryGetValue(zid, out var zd))
            {
                zones.Add(new LightingZone(zid, zd.Active, zd.Inactive, zd.Gradient));
            }
            else
            {
                zones.Add(new LightingZone(zid, "#FFFFFF", "#202020", zid.StartsWith("fader_")));
            }
        }

        _transport.Send(new SetLightingScheme(zones));

        foreach (var z in Faders.Concat(Groups.SelectMany(g => g.Zones)))
        {
            if (t.Zones.TryGetValue(z.Id, out var zd))
            {
                z.ApplyFromServer(new LightingZone(z.Id, zd.Active, zd.Inactive, zd.Gradient));
            }
        }
    }

    public void DeleteCustomTheme(CustomLightingTheme t)
    {
        CustomThemeService.Delete(t.Name);
        CustomThemes.Remove(t);
    }

    /// <summary>Applies a preset to every zone this device has, as one colour-map update.</summary>
    public void ApplyTheme(LightingTheme t)
    {
        var ids = _zoneIds.Count > 0
            ? _zoneIds
            : (MainViewModel.IsCurrentMini
                ? new[] { "fader_a", "fader_b", "fader_c", "fader_d", "mute_a", "mute_b", "mute_c", "mute_d", "bleep", "cough" }
                : new[] { "fader_a", "fader_b", "fader_c", "fader_d", "mute_a", "mute_b", "mute_c", "mute_d", "bleep", "cough", "effects", "sampler_top", "sampler_bottom" });

        var zones = t.Build(ids);
        _transport.Send(new SetLightingScheme(zones));

        // Immediately update local controls for instant responsive visual feedback
        foreach (var z in Faders.Concat(Groups.SelectMany(g => g.Zones)))
        {
            if (zones.FirstOrDefault(c => c.Zone == z.Id) is { } c)
            {
                z.ApplyFromServer(c);
            }
        }
    }

    /// <summary>Called on every state message: rebuild when the device's zone set changes, then sync colours.</summary>
    public void Apply(IReadOnlyList<string> zoneIds, IReadOnlyList<LightingZone>? colours)
    {
        if (!zoneIds.SequenceEqual(_zoneIds))
        {
            _zoneIds = zoneIds;
            Groups.Clear();
            Faders.Clear();
            var zones = zoneIds.Select(id => new LightingZoneViewModel(id, _transport)).ToList();
            foreach (var f in zones.Where(z => z.IsFader)) Faders.Add(f);
            foreach (var (gid, glyph) in GroupOrder)
            {
                if (gid == "faders") continue;
                if (MainViewModel.IsCurrentMini && (gid == "effects" || gid == "sampler")) continue;
                var items = zones.Where(z => z.GroupId == gid).ToList();
                if (items.Count == 0) continue;
                var g = new LightingGroupViewModel(gid, Loc.Get("LightGroup_" + gid), glyph);
                foreach (var z in items) g.Zones.Add(z);
                Groups.Add(g);
            }
        }
        if (colours is null) return;
        foreach (var z in Faders.Concat(Groups.SelectMany(g => g.Zones)))
            if (colours.FirstOrDefault(c => c.Zone == z.Id) is { } c) z.ApplyFromServer(c);
    }
}

/// <summary>
/// A lighting preset. Simple presets use one colour pair everywhere; multi-colour presets
/// (GTA VI) give each zone its own colours via <see cref="PerZone"/>. Inactive colours are the
/// active colour dimmed, so "off" buttons glow faintly in the same hue.
/// </summary>
public sealed record LightingTheme(string NameKey, string Active, string Inactive)
{
    public string Name => Loc.Get(NameKey);
    public SolidColorBrush ActiveBrush => new(ColorHex.Parse(Active));
    public SolidColorBrush InactiveBrush => new(ColorHex.Parse(Inactive));

    /// <summary>zone id → (active/top, inactive/bottom, gradient). Null = simple preset.</summary>
    public Func<string, (string Active, string Inactive, bool Gradient)>? PerZone { get; init; }
    /// <summary>Palette swatches shown on the preset card.</summary>
    public IReadOnlyList<PaletteColor> Palette { get; init; } = [];

    public IReadOnlyList<LightingZone> Build(IEnumerable<string> zoneIds) => zoneIds.Select(z =>
    {
        if (PerZone is { } f)
        {
            var (a, i, g) = f(z);
            return new LightingZone(z, a, i, g);
        }
        return new LightingZone(z, Active, Inactive, z.StartsWith("fader_"));
    }).ToList();

    // ---- WARDOGS: Tactical Military / Combat Warzone palette ----
    public const string TacticalOlive = "#4B6B38";
    public const string WarBloodRed = "#C62828";
    public const string BrassBulletGold = "#E5A93C";
    public const string CarbonSlate = "#2C3539";
    public const string MuzzleFlashOrange = "#FF6F00";
    public const string HazardGreen = "#8BC34A";

    public static LightingTheme Wardogs() => new("ThemeWardogs", WarBloodRed, Dim(TacticalOlive))
    {
        Palette =
        [
            new("Tactical Olive", TacticalOlive),
            new("Blood Red", WarBloodRed),
            new("Brass Bullet Gold", BrassBulletGold),
            new("Muzzle Flash", MuzzleFlashOrange),
            new("Carbon Slate", CarbonSlate),
            new("Hazard Green", HazardGreen),
        ],
        PerZone = zone => zone switch
        {
            "fader_a" => (WarBloodRed, CarbonSlate, true),
            "fader_b" => (MuzzleFlashOrange, TacticalOlive, true),
            "fader_c" => (BrassBulletGold, CarbonSlate, true),
            "fader_d" => (TacticalOlive, WarBloodRed, true),
            "mute_a" or "mute_b" or "mute_c" or "mute_d" => (WarBloodRed, Dim(WarBloodRed), false),
            "cough" => (WarBloodRed, Dim(CarbonSlate), false),
            "bleep" => (MuzzleFlashOrange, Dim(MuzzleFlashOrange), false),
            "global" => (TacticalOlive, Dim(TacticalOlive), false),
            "accent" => (BrassBulletGold, Dim(BrassBulletGold), false),
            "effect_presets" => (TacticalOlive, Dim(CarbonSlate), false),
            "effect_megaphone" => (MuzzleFlashOrange, Dim(MuzzleFlashOrange), false),
            "effect_robot" => (HazardGreen, Dim(HazardGreen), false),
            "effect_hardtune" => (BrassBulletGold, Dim(BrassBulletGold), false),
            "effect_fx" => (WarBloodRed, Dim(WarBloodRed), false),
            "encoders" => (TacticalOlive, BrassBulletGold, false),
            "sampler_banks" => (BrassBulletGold, Dim(BrassBulletGold), false),
            "sampler_pads" => (WarBloodRed, Dim(CarbonSlate), false),
            _ => (WarBloodRed, Dim(TacticalOlive), false),
        },
    };

    // ---- GTA VI: Miami / Vice City palette ----
    public const string MiamiBlue = "#1ACDFF";    // hsl(195, 100%, 55%)
    public const string VicePurple = "#A34DFF";   // hsl(275, 85%, 62%)
    public const string NeonPink = "#FF4FA2";     // hsl(325, 100%, 65%)
    public const string SunsetPink = "#FF73A2";   // hsl(340, 95%, 70%)
    public const string SunsetOrange = "#FF8A33"; // hsl(25, 100%, 60%)
    public const string FloridaGold = "#FFC23A";  // hsl(42, 100%, 62%)

    public static LightingTheme GtaVi() => new("ThemeGtaVi", NeonPink, Dim(VicePurple))
    {
        Palette =
        [
            new("Miami Blue", MiamiBlue), new("Vice Purple", VicePurple), new("Neon Pink", NeonPink),
            new("Sunset Pink", SunsetPink), new("Sunset Orange", SunsetOrange), new("Florida Gold", FloridaGold),
        ],
        PerZone = zone => zone switch
        {
            // Faders: a Vice City sky on each strip (top → bottom).
            "fader_a" => (MiamiBlue, VicePurple, true),
            "fader_b" => (NeonPink, VicePurple, true),
            "fader_c" => (SunsetOrange, NeonPink, true),
            "fader_d" => (FloridaGold, SunsetPink, true),
            "mute_a" or "mute_b" or "mute_c" or "mute_d" => (NeonPink, Dim(NeonPink), false),
            "cough" => (MiamiBlue, Dim(MiamiBlue), false),
            "bleep" => (SunsetOrange, Dim(SunsetOrange), false),
            "global" => (VicePurple, Dim(VicePurple), false),
            "accent" => (MiamiBlue, Dim(MiamiBlue), false),
            "effect_presets" => (NeonPink, Dim(VicePurple), false),
            "effect_megaphone" => (SunsetOrange, Dim(SunsetOrange), false),
            "effect_robot" => (MiamiBlue, Dim(MiamiBlue), false),
            "effect_hardtune" => (FloridaGold, Dim(FloridaGold), false),
            "effect_fx" => (SunsetPink, Dim(SunsetPink), false),
            "encoders" => (MiamiBlue, VicePurple, false),
            "sampler_banks" => (FloridaGold, Dim(FloridaGold), false),
            "sampler_pads" => (SunsetPink, Dim(SunsetPink), false),
            _ => (NeonPink, Dim(NeonPink), false),
        },
    };

    // ---- Grand Theft Auto V: Los Santos palette ----
    public const string FranklinGreen = "#39B54A";
    public const string MichaelBlue = "#00A3E0";
    public const string TrevorOrange = "#F36F21";
    public const string DollarGold = "#FFD100";

    public static LightingTheme GtaV() => new("ThemeGtaV", FranklinGreen, Dim(MichaelBlue))
    {
        Palette =
        [
            new("Franklin Green", FranklinGreen), new("Michael Blue", MichaelBlue),
            new("Trevor Orange", TrevorOrange), new("Dollar Gold", DollarGold),
        ],
        PerZone = zone => zone switch
        {
            "fader_a" => (FranklinGreen, MichaelBlue, true),
            "fader_b" => (MichaelBlue, TrevorOrange, true),
            "fader_c" => (TrevorOrange, DollarGold, true),
            "fader_d" => (DollarGold, FranklinGreen, true),
            "mute_a" or "mute_b" or "mute_c" or "mute_d" => (FranklinGreen, Dim(FranklinGreen), false),
            "cough" => (MichaelBlue, Dim(MichaelBlue), false),
            "bleep" => (TrevorOrange, Dim(TrevorOrange), false),
            "global" => (DollarGold, Dim(DollarGold), false),
            "accent" => (FranklinGreen, Dim(FranklinGreen), false),
            "effect_presets" => (DollarGold, Dim(DollarGold), false),
            "effect_megaphone" => (TrevorOrange, Dim(TrevorOrange), false),
            "effect_robot" => (MichaelBlue, Dim(MichaelBlue), false),
            "effect_hardtune" => (FranklinGreen, Dim(FranklinGreen), false),
            "effect_fx" => (DollarGold, Dim(DollarGold), false),
            "encoders" => (MichaelBlue, FranklinGreen, false),
            "sampler_banks" => (TrevorOrange, Dim(TrevorOrange), false),
            "sampler_pads" => (FranklinGreen, Dim(FranklinGreen), false),
            _ => (FranklinGreen, Dim(FranklinGreen), false),
        },
    };

    // ---- League of Legends: Hextech Magic & Piltover Gold ----
    public const string HextechCyan = "#0AC8B9";
    public const string PiltoverGold = "#C89B3C";
    public const string ArcaneBlue = "#0E96E6";
    public const string VoidPurple = "#9D5CFF";

    public static LightingTheme LeagueOfLegends() => new("ThemeLol", HextechCyan, Dim(PiltoverGold))
    {
        Palette =
        [
            new("Hextech Cyan", HextechCyan), new("Piltover Gold", PiltoverGold),
            new("Arcane Blue", ArcaneBlue), new("Void Purple", VoidPurple),
        ],
        PerZone = zone => zone switch
        {
            "fader_a" => (HextechCyan, PiltoverGold, true),
            "fader_b" => (PiltoverGold, ArcaneBlue, true),
            "fader_c" => (ArcaneBlue, VoidPurple, true),
            "fader_d" => (VoidPurple, HextechCyan, true),
            "mute_a" or "mute_b" or "mute_c" or "mute_d" => (HextechCyan, Dim(HextechCyan), false),
            "cough" => (HextechCyan, Dim(HextechCyan), false),
            "bleep" => (PiltoverGold, Dim(PiltoverGold), false),
            "global" => (PiltoverGold, Dim(PiltoverGold), false),
            "accent" => (HextechCyan, Dim(HextechCyan), false),
            "effect_presets" => (PiltoverGold, Dim(PiltoverGold), false),
            "effect_megaphone" => (ArcaneBlue, Dim(ArcaneBlue), false),
            "effect_robot" => (HextechCyan, Dim(HextechCyan), false),
            "effect_hardtune" => (PiltoverGold, Dim(PiltoverGold), false),
            "effect_fx" => (VoidPurple, Dim(VoidPurple), false),
            "encoders" => (HextechCyan, PiltoverGold, false),
            "sampler_banks" => (PiltoverGold, Dim(PiltoverGold), false),
            "sampler_pads" => (HextechCyan, Dim(HextechCyan), false),
            _ => (HextechCyan, Dim(HextechCyan), false),
        },
    };

    // ---- World of Warcraft: Horde vs Alliance & Azeroth ----
    public const string HordeRed = "#C41F3B";
    public const string AllianceBlue = "#0070DD";
    public const string AzerothGold = "#FFB800";
    public const string FelGreen = "#00FF7F";

    public static LightingTheme WorldOfWarcraft() => new("ThemeWow", HordeRed, Dim(AllianceBlue))
    {
        Palette =
        [
            new("Horde Red", HordeRed), new("Alliance Blue", AllianceBlue),
            new("Azeroth Gold", AzerothGold), new("Fel Green", FelGreen),
        ],
        PerZone = zone => zone switch
        {
            "fader_a" => (HordeRed, AzerothGold, true),
            "fader_b" => (AllianceBlue, AzerothGold, true),
            "fader_c" => (AzerothGold, FelGreen, true),
            "fader_d" => (FelGreen, HordeRed, true),
            "mute_a" or "mute_b" or "mute_c" or "mute_d" => (HordeRed, Dim(HordeRed), false),
            "cough" => (AllianceBlue, Dim(AllianceBlue), false),
            "bleep" => (HordeRed, Dim(HordeRed), false),
            "global" => (AzerothGold, Dim(AzerothGold), false),
            "accent" => (AllianceBlue, Dim(AllianceBlue), false),
            "effect_presets" => (AzerothGold, Dim(AzerothGold), false),
            "effect_megaphone" => (HordeRed, Dim(HordeRed), false),
            "effect_robot" => (AllianceBlue, Dim(AllianceBlue), false),
            "effect_hardtune" => (AzerothGold, Dim(AzerothGold), false),
            "effect_fx" => (FelGreen, Dim(FelGreen), false),
            "encoders" => (HordeRed, AllianceBlue, false),
            "sampler_banks" => (AzerothGold, Dim(AzerothGold), false),
            "sampler_pads" => (HordeRed, Dim(HordeRed), false),
            _ => (HordeRed, Dim(HordeRed), false),
        },
    };

    // ---- Valorant: V-Crimson & Radianite Cyan ----
    public const string ValorantCrimson = "#FF4655";
    public const string RadianiteCyan = "#00F5D4";
    public const string SpikeAmber = "#FFC700";
    public const string NeonViolet = "#B446FF";

    public static LightingTheme Valorant() => new("ThemeValo", ValorantCrimson, Dim(RadianiteCyan))
    {
        Palette =
        [
            new("Valorant Crimson", ValorantCrimson), new("Radianite Cyan", RadianiteCyan),
            new("Spike Amber", SpikeAmber), new("Neon Violet", NeonViolet),
        ],
        PerZone = zone => zone switch
        {
            "fader_a" => (ValorantCrimson, RadianiteCyan, true),
            "fader_b" => (RadianiteCyan, SpikeAmber, true),
            "fader_c" => (SpikeAmber, NeonViolet, true),
            "fader_d" => (NeonViolet, ValorantCrimson, true),
            "mute_a" or "mute_b" or "mute_c" or "mute_d" => (ValorantCrimson, Dim(ValorantCrimson), false),
            "cough" => (RadianiteCyan, Dim(RadianiteCyan), false),
            "bleep" => (ValorantCrimson, Dim(ValorantCrimson), false),
            "global" => (RadianiteCyan, Dim(RadianiteCyan), false),
            "accent" => (ValorantCrimson, Dim(ValorantCrimson), false),
            "effect_presets" => (RadianiteCyan, Dim(RadianiteCyan), false),
            "effect_megaphone" => (ValorantCrimson, Dim(ValorantCrimson), false),
            "effect_robot" => (RadianiteCyan, Dim(RadianiteCyan), false),
            "effect_hardtune" => (SpikeAmber, Dim(SpikeAmber), false),
            "effect_fx" => (NeonViolet, Dim(NeonViolet), false),
            "encoders" => (ValorantCrimson, RadianiteCyan, false),
            "sampler_banks" => (SpikeAmber, Dim(SpikeAmber), false),
            "sampler_pads" => (ValorantCrimson, Dim(ValorantCrimson), false),
            _ => (ValorantCrimson, Dim(ValorantCrimson), false),
        },
    };

    // ---- Counter-Strike: CT Blue & T Amber / Flame ----
    public const string CtBlue = "#5D79AE";
    public const string TerroristGold = "#DE9B35";
    public const string CsFlame = "#FF6B00";
    public const string DefuseMint = "#22E5B5";

    public static LightingTheme CounterStrike() => new("ThemeCs", CtBlue, Dim(TerroristGold))
    {
        Palette =
        [
            new("CT Blue", CtBlue), new("Terrorist Gold", TerroristGold),
            new("CS Flame", CsFlame), new("Defuse Mint", DefuseMint),
        ],
        PerZone = zone => zone switch
        {
            "fader_a" => (CtBlue, TerroristGold, true),
            "fader_b" => (TerroristGold, CsFlame, true),
            "fader_c" => (CsFlame, DefuseMint, true),
            "fader_d" => (DefuseMint, CtBlue, true),
            "mute_a" or "mute_b" or "mute_c" or "mute_d" => (CtBlue, Dim(CtBlue), false),
            "cough" => (DefuseMint, Dim(DefuseMint), false),
            "bleep" => (CsFlame, Dim(CsFlame), false),
            "global" => (TerroristGold, Dim(TerroristGold), false),
            "accent" => (CtBlue, Dim(CtBlue), false),
            "effect_presets" => (TerroristGold, Dim(TerroristGold), false),
            "effect_megaphone" => (CsFlame, Dim(CsFlame), false),
            "effect_robot" => (CtBlue, Dim(CtBlue), false),
            "effect_hardtune" => (TerroristGold, Dim(TerroristGold), false),
            "effect_fx" => (DefuseMint, Dim(DefuseMint), false),
            "encoders" => (CtBlue, TerroristGold, false),
            "sampler_banks" => (TerroristGold, Dim(TerroristGold), false),
            "sampler_pads" => (CsFlame, Dim(CsFlame), false),
            _ => (CtBlue, Dim(CtBlue), false),
        },
    };

    /// <summary>Same hue at ~22 % brightness: the "off" glow.</summary>
    public static string Dim(string hex)
    {
        var c = ColorHex.Parse(hex);
        return ColorHex.Format(Windows.UI.Color.FromArgb(255, (byte)(c.R * 0.22), (byte)(c.G * 0.22), (byte)(c.B * 0.22)));
    }

    public LinearGradientBrush BannerBrush => new()
    {
        StartPoint = new(0, 0), EndPoint = new(1, 1),
        GradientStops =
        {
            new() { Color = ColorHex.Parse(MiamiBlue), Offset = 0 },
            new() { Color = ColorHex.Parse(VicePurple), Offset = 0.35 },
            new() { Color = ColorHex.Parse(NeonPink), Offset = 0.6 },
            new() { Color = ColorHex.Parse(SunsetOrange), Offset = 0.85 },
            new() { Color = ColorHex.Parse(FloridaGold), Offset = 1 },
        },
    };
}

public sealed record PaletteColor(string Name, string Hex)
{
    public SolidColorBrush Brush => new(ColorHex.Parse(Hex));
}

public sealed class LightingGroupViewModel(string id, string title, string glyph)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public ObservableCollection<LightingZoneViewModel> Zones { get; } = new();
}

public partial class LightingZoneViewModel : ObservableObject
{
    private readonly PipeTransport _transport;
    private bool _applyingServer;

    // While the user is dragging a colour picker, edits stream out (coalesced to ~60/s) and the
    // daemon echoes each one back. Those echoes lag behind the pointer; applying them made the
    // picker and the gradient preview jump back and forth ("glitchy gradient"). So server
    // updates are held while a local edit is in flight and applied once the user pauses.
    private static readonly TimeSpan EditQuiet = TimeSpan.FromMilliseconds(500);
    private DateTime _lastLocalEdit = DateTime.MinValue;
    private LightingZone? _deferred;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _deferTimer;

    public string Id { get; }
    public string Name { get; }
    public string GroupId { get; }
    /// <summary>Faders show a gradient (top/bottom); buttons have on/off colours.</summary>
    public bool IsFader { get; }
    public string ActiveLabel => Loc.Get(IsFader ? "LightTop" : "LightOn");
    public string InactiveLabel => Loc.Get(IsFader ? "LightBottom" : "LightOff");

    [ObservableProperty] private Color _activeColor = ColorHex.Parse("#1FC79A");
    [ObservableProperty] private Color _inactiveColor = ColorHex.Parse("#103A30");
    /// <summary>Faders: gradient bottom → top; off = solid top colour.</summary>
    [ObservableProperty] private bool _gradient = true;

    public SolidColorBrush ActiveBrush => new(ActiveColor);
    public SolidColorBrush InactiveBrush => new(InactiveColor);
    /// <summary>What the LEDs look like: top (active) → bottom (inactive), or solid when gradient is off.</summary>
    public LinearGradientBrush PreviewBrush => new()
    {
        StartPoint = new(0, 0), EndPoint = new(0, 1),
        GradientStops =
        {
            new() { Color = ActiveColor, Offset = 0 },
            new() { Color = IsFader && !Gradient ? ActiveColor : InactiveColor, Offset = 1 },
        },
    };

    /// <summary>Swap top and bottom (one message, so the device updates once).</summary>
    public CommunityToolkit.Mvvm.Input.RelayCommand Swap => _swap ??= new(() =>
    {
        _applyingServer = true;
        (ActiveColor, InactiveColor) = (InactiveColor, ActiveColor);
        _applyingServer = false;
        _lastLocalEdit = DateTime.UtcNow;
        _transport.Send(new SetLighting(Id, ColorHex.Format(ActiveColor), ColorHex.Format(InactiveColor)));
    });
    private CommunityToolkit.Mvvm.Input.RelayCommand? _swap;
    public string ActiveHex => ColorHex.Format(ActiveColor);
    public string InactiveHex => ColorHex.Format(InactiveColor);

    /// <summary>Quick swatches shown in each colour flyout.</summary>
    public IReadOnlyList<PaletteSwatch> ActivePalette => _activePalette ??= PaletteSwatch.For(c => ActiveColor = c);
    public IReadOnlyList<PaletteSwatch> InactivePalette => _inactivePalette ??= PaletteSwatch.For(c => InactiveColor = c);
    private IReadOnlyList<PaletteSwatch>? _activePalette, _inactivePalette;

    public LightingZoneViewModel(string id, PipeTransport transport)
    {
        Id = id;
        _transport = transport;
        _deferTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (_deferTimer is not null)
        {
            _deferTimer.Interval = EditQuiet;
            _deferTimer.IsRepeating = false;
            _deferTimer.Tick += (_, _) => { if (_deferred is { } d) { _deferred = null; ApplyFromServer(d); } };
        }
        IsFader = id.StartsWith("fader_");
        var letter = id[^1..].ToUpperInvariant();
        (Name, GroupId) = id switch
        {
            "fader_a" or "fader_b" or "fader_c" or "fader_d" => (Loc.Format("ZoneFader", letter), "faders"),
            "mute_a" or "mute_b" or "mute_c" or "mute_d" => (Loc.Format("ZoneMute", letter), "buttons"),
            "cough" => (Loc.Get("ZoneCough"), "buttons"),
            "bleep" => (Loc.Get("ZoneBleep"), "buttons"),
            "global" => (Loc.Get("ZoneGlobal"), "device"),
            "accent" => (Loc.Get("ZoneAccent"), "device"),
            "effect_presets" => (Loc.Get("ZoneEffectPresets"), "effects"),
            "effect_megaphone" => (Loc.Get("FxMegaphone"), "effects"),
            "effect_robot" => (Loc.Get("FxRobot"), "effects"),
            "effect_hardtune" => (Loc.Get("FxHardtune"), "effects"),
            "effect_fx" => ("FX", "effects"),
            "encoders" => (Loc.Get("ZoneEncoders"), "effects"),
            "sampler_banks" => (Loc.Get("ZoneSamplerBanks"), "sampler"),
            "sampler_pads" => (Loc.Get("ZoneSamplerPads"), "sampler"),
            _ => (id, "device"),
        };
    }

    public void ApplyFromServer(LightingZone z)
    {
        if (DateTime.UtcNow - _lastLocalEdit < EditQuiet && _deferTimer is not null)
        {
            _deferred = z;          // newest server value wins once the user pauses
            _deferTimer.Stop();
            _deferTimer.Start();
            return;
        }
        _applyingServer = true;
        try
        {
            ActiveColor = ColorHex.Parse(z.Active);
            InactiveColor = ColorHex.Parse(z.Inactive);
            Gradient = z.Gradient;
        }
        finally { _applyingServer = false; }
    }

    partial void OnActiveColorChanged(Color value)
    {
        OnPropertyChanged(nameof(ActiveBrush));
        OnPropertyChanged(nameof(ActiveHex));
        OnPropertyChanged(nameof(PreviewBrush));
        if (!_applyingServer)
        {
            _lastLocalEdit = DateTime.UtcNow;
            _transport.SendCoalesced($"light:{Id}:a", new SetLighting(Id, ColorHex.Format(value), null));
        }
    }

    partial void OnGradientChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewBrush));
        if (!_applyingServer)
        {
            _lastLocalEdit = DateTime.UtcNow;
            _transport.Send(new SetLighting(Id, null, null, value));
        }
    }

    partial void OnInactiveColorChanged(Color value)
    {
        OnPropertyChanged(nameof(InactiveBrush));
        OnPropertyChanged(nameof(InactiveHex));
        OnPropertyChanged(nameof(PreviewBrush));
        if (!_applyingServer)
        {
            _lastLocalEdit = DateTime.UtcNow;
            _transport.SendCoalesced($"light:{Id}:i", new SetLighting(Id, null, ColorHex.Format(value)));
        }
    }
}

public static class ColorHex
{
    public static Color Parse(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return Color.FromArgb(255, 0, 0, 0);
        return Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static string Format(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

public sealed class PaletteSwatch(string hex, Action<Color> pick)
{
    private static readonly string[] Colours =
        ["#1FC79A", "#00E5FF", "#2F8CFF", "#6C5CFF", "#B266FF", "#FF4FD8",
         "#FF3355", "#FF7A3D", "#FFC233", "#A8FF3E", "#FFFFFF", "#000000"];

    public string Hex { get; } = hex;
    public SolidColorBrush Brush { get; } = new(ColorHex.Parse(hex));
    public CommunityToolkit.Mvvm.Input.RelayCommand Pick { get; } = new(() => pick(ColorHex.Parse(hex)));

    public static IReadOnlyList<PaletteSwatch> For(Action<Color> pick) => Colours.Select(h => new PaletteSwatch(h, pick)).ToList();
}
