using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NextXLR.Services;

/// <summary>A saved profile in a library.</summary>
public sealed class ProfileEntry(string name, string path, DateTime saved, bool isDefault, bool isActive = false)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public DateTime Saved { get; } = saved;
    public bool IsDefault { get; } = isDefault;
    public bool IsActive { get; } = isActive;
    public string SavedText => Saved.ToString("g");
    /// <summary>Filled star = loaded automatically at start.</summary>
    public string DefaultGlyph => IsDefault ? "\uE735" : "\uE734";
    public Microsoft.UI.Xaml.Visibility DefaultBadge =>
        IsDefault ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility ActiveBadge =>
        IsActive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string OverwriteTooltip => Loc.Format("ProfileOverwriteTooltip", Name);
    public override string ToString() => IsDefault ? $"[Standard] {Name}" : Name;
}

/// <summary>
/// A folder of profile files: { "app": "NEXT : XLR", "kind", "name", "saved", "profile": {…} }.
/// The daemon owns and validates the content; this class stores, lists, imports and exports.
/// "Set as default" copies a profile to <see cref="DefaultFile"/>, which the daemon applies to the
/// GoXLR every time it starts.
/// </summary>
public sealed class ProfileLibrary(string folder, string defaultFile, string kind, Func<JsonNode, bool> isValid)
{
    public static readonly string DataDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nextxlr");

    private readonly string _folder = folder;
    public string Dir => LibraryDir;
    public string LibraryDir
    {
        get
        {
            var dir = System.IO.Path.Combine(DataDir, _folder);
            if (!Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    if (_folder == "micprofiles")
                    {
                        var oldDir = System.IO.Path.Combine(DataDir, "mic-profiles");
                        if (Directory.Exists(oldDir))
                        {
                            foreach (var f in Directory.GetFiles(oldDir, "*.json"))
                            {
                                var dest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f));
                                if (!File.Exists(dest)) File.Copy(f, dest);
                            }
                        }
                    }
                }
                catch { }
            }
            return dir;
        }
    }
    public string DefaultFile { get; } = System.IO.Path.Combine(DataDir, defaultFile);

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public string? DefaultName
    {
        get
        {
            try { return File.Exists(DefaultFile) ? Read(DefaultFile).Name : null; }
            catch { return null; }
        }
    }

    public IReadOnlyList<ProfileEntry> List(string? activeName = null)
    {
        if (!Directory.Exists(LibraryDir)) return [];
        var def = DefaultName;
        return Directory.EnumerateFiles(LibraryDir, "*.json")
            .Select(p => TryReadEntry(p, def, activeName))
            .OfType<ProfileEntry>()
            .OrderByDescending(e => e.IsActive).ThenByDescending(e => e.IsDefault).ThenByDescending(e => e.Saved)
            .ToList();
    }

    /// <summary>Saves into the library (same name overwrites; updates the default copy too).</summary>
    public ProfileEntry Save(string name, JsonNode profile)
    {
        Directory.CreateDirectory(LibraryDir);
        var path = System.IO.Path.Combine(LibraryDir, SafeFileName(name) + ".json");
        Write(path, name, profile);
        var isDefault = DefaultName == name;
        if (isDefault) Write(DefaultFile, name, profile);
        return new ProfileEntry(name, path, DateTime.Now, isDefault);
    }

    public void Write(string path, string name, JsonNode profile)
    {
        var doc = new JsonObject
        {
            ["app"] = "NEXT : XLR",
            ["kind"] = kind,
            ["name"] = name,
            ["saved"] = DateTime.Now.ToString("o"),
            ["profile"] = profile.DeepClone(),
        };

        if (kind == "mic")
        {
            try
            {
                doc["vst_master_enabled"] = VstPluginService.Instance.IsMasterEnabled;
                doc["vst_rack"] = VstPluginService.Instance.SerializeRackToJsonNode();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to embed VST rack in mic profile: {ex.Message}");
            }
        }

        var dir = System.IO.Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, doc.ToJsonString(Pretty));
        File.Move(tempPath, path, true);
    }

    /// <summary>Reads a profile file (wrapped, bare, or legacy GoXLR .goxlr / XML) with full raw JSON doc.</summary>
    public (string Name, JsonNode Profile, JsonNode? RawDoc) ReadFull(string path)
    {
        if (LegacyProfileImporter.IsLegacyOrArchive(path))
        {
            var (legacyName, legacyProfile) = LegacyProfileImporter.Import(path, kind);
            return (legacyName, legacyProfile, null);
        }

        var node = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException("empty file");
        var profile = node["profile"] ?? node;
        if (!isValid(profile)) throw new InvalidDataException(Loc.Get(kind == "mic" ? "MicProfileInvalid" : "ProfileInvalid"));
        var name = node["name"]?.GetValue<string>() ?? System.IO.Path.GetFileNameWithoutExtension(path);
        return (name, profile.DeepClone(), node);
    }

    /// <summary>Reads a profile file (wrapped, bare, or legacy GoXLR .goxlr / XML). Throws with a readable message.</summary>
    public (string Name, JsonNode Profile) Read(string path)
    {
        var (name, profile, _) = ReadFull(path);
        return (name, profile);
    }

    public void SetDefault(ProfileEntry e)
    {
        var (name, profile) = Read(e.Path);
        Write(DefaultFile, name, profile);
    }

    public void ClearDefault()
    {
        if (File.Exists(DefaultFile)) File.Delete(DefaultFile);
    }

    public void Delete(ProfileEntry e)
    {
        if (File.Exists(e.Path)) File.Delete(e.Path);
        if (e.IsDefault) ClearDefault();
    }

    private ProfileEntry? TryReadEntry(string path, string? defaultName, string? activeName = null)
    {
        try
        {
            var (name, _) = Read(path);
            var isDefault = name == defaultName;
            var isActive = !string.IsNullOrEmpty(activeName) && string.Equals(name, activeName, StringComparison.OrdinalIgnoreCase);
            return new ProfileEntry(name, path, File.GetLastWriteTime(path), isDefault, isActive);
        }
        catch { return null; }
    }

    public static string SafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) || c == '.' || c == '/' || c == '\\' ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "profile" : cleaned;
    }
}

public static class ProfileService
{
    /// <summary>Whole setup (labels, icons, levels, routing, lighting, effects, mic). Default: loaded at start.</summary>
    public static readonly ProfileLibrary Setups = new("profiles", "default-profile.json", "setup",
        p => p["format"] is not null && p["channels"] is JsonArray);

    /// <summary>Mic processing only (gate, compressor, EQ, de-esser). Default: loaded at start.</summary>
    public static readonly ProfileLibrary Mic = new("micprofiles", "default-mic-profile.json", "mic",
        p => p["gate_threshold"] is not null && p["comp_ratio"] is not null);

    private static string? _activeSetupProfile;
    public static string ActiveSetupProfileName
    {
        get => _activeSetupProfile ?? AppSettings.Current.ActiveSetupProfile ?? Setups.DefaultName ?? "Standard";
        set
        {
            _activeSetupProfile = value;
            var s = AppSettings.Current;
            if (s.ActiveSetupProfile != value)
            {
                s.ActiveSetupProfile = value;
                s.Save();
            }
        }
    }

    private static string? _activeMicProfile;
    public static string ActiveMicProfileName
    {
        get => _activeMicProfile ?? AppSettings.Current.ActiveMicProfile ?? Mic.DefaultName ?? "Standard";
        set
        {
            _activeMicProfile = value;
            var s = AppSettings.Current;
            if (s.ActiveMicProfile != value)
            {
                s.ActiveMicProfile = value;
                s.Save();
            }
        }
    }
}
