using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NextXLR.Services;

public sealed class CustomLightingTheme
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "Benutzer";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, ZoneColorData> Zones { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Swatches { get; set; } = new();
}

public sealed class ZoneColorData
{
    public string Active { get; set; } = "#FFFFFF";
    public string Inactive { get; set; } = "#000000";
    public bool Gradient { get; set; }
}

public static class CustomThemeService
{
    private static readonly string ThemesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR", "themes");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WARDOGS", "GTA VI", "GTA 6", "GTA V", "GTA 5", "League of Legends", "LoL",
        "World of Warcraft", "WoW", "Valorant", "Counter-Strike", "CS", "CS2",
        "Türkis", "Ozean", "Violett", "Sonnenuntergang", "Karminrot", "Eisblau",
        "Aus / Schwarz", "Aus", "Off", "Default", "Standard", "ThemeTeal", "ThemeOcean",
        "ThemeViolet", "ThemeSunset", "ThemeCrimson", "ThemeIce", "ThemeOff",
        "ThemeWardogs", "ThemeGtaVi", "ThemeGtaV", "ThemeLoL", "ThemeWoW", "ThemeValorant", "ThemeCS"
    };

    public static bool IsReservedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return ReservedNames.Contains(name.Trim());
    }

    public static List<CustomLightingTheme> List()
    {
        var list = new List<CustomLightingTheme>();
        try
        {
            if (!Directory.Exists(ThemesDir)) return list;

            foreach (var file in Directory.GetFiles(ThemesDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var theme = JsonSerializer.Deserialize<CustomLightingTheme>(json, JsonOpts);
                    if (theme != null && !string.IsNullOrWhiteSpace(theme.Name))
                    {
                        list.Add(theme);
                    }
                }
                catch { }
            }
        }
        catch { }

        return list.OrderBy(t => t.Name).ToList();
    }

    public static void Save(CustomLightingTheme theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Name))
            throw new ArgumentException("Der Theme-Name darf nicht leer sein.");

        if (IsReservedName(theme.Name))
            throw new InvalidOperationException($"Der Name '{theme.Name}' ist für ein integriertes Standard-Design reserviert. Bitte wähle einen anderen Namen.");

        Directory.CreateDirectory(ThemesDir);
        var safeName = MakeSafeFilename(theme.Name) + ".json";
        var filePath = Path.Combine(ThemesDir, safeName);
        var json = JsonSerializer.Serialize(theme, JsonOpts);
        File.WriteAllText(filePath, json);
    }

    public static bool Delete(string themeName)
    {
        try
        {
            var safeName = MakeSafeFilename(themeName) + ".json";
            var filePath = Path.Combine(ThemesDir, safeName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch { }
        return false;
    }

    public static void Export(CustomLightingTheme theme, string destinationPath)
    {
        var json = JsonSerializer.Serialize(theme, JsonOpts);
        File.WriteAllText(destinationPath, json);
    }

    public static CustomLightingTheme Import(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Die Theme-Datei wurde nicht gefunden.", sourcePath);

        var json = File.ReadAllText(sourcePath);
        var theme = JsonSerializer.Deserialize<CustomLightingTheme>(json, JsonOpts)
            ?? throw new InvalidDataException("Die ausgewählte Datei enthält kein gültiges NEXT : XLR Beleuchtungs-Theme.");

        if (string.IsNullOrWhiteSpace(theme.Name))
        {
            theme.Name = Path.GetFileNameWithoutExtension(sourcePath);
        }

        if (IsReservedName(theme.Name))
        {
            theme.Name += " (Importiert)";
        }

        Save(theme);
        return theme;
    }

    private static string MakeSafeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }
}

