using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace NextXLR.Services;

/// <summary>
/// Imports original TC-Helicon GoXLR and GoXLR Mini profiles (.goxlr ZIP archives containing profile.xml)
/// and standalone microphone profiles (.goxlrMicProfile, .micProfile, and .xml) into NextXLR format.
/// </summary>
public static class LegacyProfileImporter
{
    public static bool IsLegacyOrArchive(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".goxlr" or ".goxlrmicprofile" or ".micprofile" or ".xml") return true;

        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[4];
            int read = stream.Read(header, 0, header.Length);
            if (read >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
            {
                return true; // ZIP archive
            }
            if (read >= 1 && (header[0] == (byte)'<' || (read >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)))
            {
                return true; // XML text
            }
        }
        catch { }

        return false;
    }

    public static (string Name, JsonNode Profile) Import(string path, string kind)
    {
        var xmlContent = ExtractXml(path);
        var doc = XDocument.Parse(xmlContent);
        var name = Path.GetFileNameWithoutExtension(path);

        // Auto-detect: if the file contains dspTreeMicProfile or MicProfileTree, it is a mic profile
        bool isMicTree = doc.Descendants().Any(e =>
            e.Name.LocalName.Equals("dspTreeMicProfile", StringComparison.OrdinalIgnoreCase) ||
            e.Name.LocalName.Equals("MicProfileTree", StringComparison.OrdinalIgnoreCase));

        if (kind == "mic" || isMicTree)
        {
            var micNode = ImportMicProfile(doc);
            if (kind != "mic")
            {
                // User imported a mic profile in the setup section: wrap it in a valid setup profile
                return (name, BuildDefaultSetupWithMic(name, micNode));
            }
            return (name, micNode);
        }
        else
        {
            var setupNode = ImportSetupProfile(doc, name);
            return (name, setupNode);
        }
    }

    private static string ExtractXml(string path)
    {
        try
        {
            using var fileStream = File.OpenRead(path);
            var header = new byte[4];
            int read = fileStream.Read(header, 0, 4);
            fileStream.Position = 0;

            if (read == 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
            {
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
                var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("profile.xml", StringComparison.OrdinalIgnoreCase))
                         ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                if (entry is not null)
                {
                    using var reader = new StreamReader(entry.Open());
                    return reader.ReadToEnd();
                }
                throw new InvalidDataException("Keine XML-Profildatei im GoXLR-Archiv gefunden.");
            }
        }
        catch (InvalidDataException) { throw; }
        catch { }

        return File.ReadAllText(path);
    }

    private static JsonObject ImportMicProfile(XDocument doc)
    {
        // 1. Check for official TC-Helicon GoXLR <dspTreeMicProfile>
        var dspEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("dspTreeMicProfile", StringComparison.OrdinalIgnoreCase));
        if (dspEl != null)
        {
            bool gateEnabled = (dspEl.Attribute("MIC_GATE_ENABLE")?.Value ?? "1") != "0";
            int gateThreshold = GetIntAttr(dspEl, "MIC_GATE_THRESOLD") ?? GetIntAttr(dspEl, "MIC_GATE_THRESHOLD") ?? -40;
            int gateAttenuation = GetIntAttr(dspEl, "MIC_GATE_ATTEN") ?? 100;
            int gateAttack = GetIntAttr(dspEl, "MIC_GATE_ATTACK") ?? 0;
            int gateRelease = GetIntAttr(dspEl, "MIC_GATE_RELEASE") ?? 14;

            int compThreshold = GetIntAttr(dspEl, "MIC_COMP_THRESHOLD") ?? -20;
            int compRatio = GetIntAttr(dspEl, "MIC_COMP_RATIO") ?? 6;
            int compAttack = GetIntAttr(dspEl, "MIC_COMP_ATTACK") ?? 6;
            int compRelease = GetIntAttr(dspEl, "MIC_COMP_RELEASE") ?? 8;
            int compMakeup = GetIntAttr(dspEl, "MIC_COMP_MAKEUPGAIN") ?? 0;

            int deesser = Math.Clamp(GetIntAttr(dspEl, "MIC_DEESS_AMOUNT") ?? 0, 0, 100);

            var eqGains = new JsonArray();
            string[] eqKeys =
            [
                "MIC_EQ_31.5HZ_GAIN", "MIC_EQ_63HZ_GAIN", "MIC_EQ_125HZ_GAIN", "MIC_EQ_250HZ_GAIN", "MIC_EQ_500HZ_GAIN",
                "MIC_EQ_1KHZ_GAIN", "MIC_EQ_2KHZ_GAIN", "MIC_EQ_4KHZ_GAIN", "MIC_EQ_8KHZ_GAIN", "MIC_EQ_16KHZ_GAIN"
            ];
            foreach (var key in eqKeys)
            {
                int val = GetIntAttr(dspEl, key) ?? 0;
                eqGains.Add(Math.Clamp(val, -9, 9));
            }

            return new JsonObject
            {
                ["gate_enabled"] = gateEnabled,
                ["gate_threshold"] = Math.Clamp(gateThreshold, -59, 0),
                ["gate_attenuation"] = Math.Clamp(gateAttenuation, 0, 100),
                ["gate_attack"] = Math.Clamp(gateAttack, 0, 45),
                ["gate_release"] = Math.Clamp(gateRelease, 0, 45),
                ["comp_threshold"] = Math.Clamp(compThreshold, -40, 0),
                ["comp_ratio"] = Math.Clamp(compRatio, 0, 14),
                ["comp_attack"] = Math.Clamp(compAttack, 0, 19),
                ["comp_release"] = Math.Clamp(compRelease, 0, 19),
                ["comp_makeup"] = Math.Clamp(compMakeup, -6, 24),
                ["eq_gains"] = eqGains,
                ["deesser"] = deesser,
            };
        }

        // 2. Generic XML elements fallback (<gate>, <compressor>, <equalizer>)
        var gateEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("gate", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("noisegate", StringComparison.OrdinalIgnoreCase));
        var compEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("compressor", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("comp", StringComparison.OrdinalIgnoreCase));
        var eqEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("equalizer", StringComparison.OrdinalIgnoreCase) || e.Name.LocalName.Equals("eq", StringComparison.OrdinalIgnoreCase));
        var deesserEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("deesser", StringComparison.OrdinalIgnoreCase));

        bool gEnabled = GetBool(gateEl, "enabled") ?? true;
        int gThreshold = GetInt(gateEl, "threshold") ?? -40;
        int gAttenuation = GetInt(gateEl, "attenuation") ?? 100;
        int gAttack = GetInt(gateEl, "attack") ?? 0;
        int gRelease = GetInt(gateEl, "release") ?? 19;

        int cThreshold = GetInt(compEl, "threshold") ?? -20;
        int cRatio = GetInt(compEl, "ratio") ?? 6;
        int cAttack = GetInt(compEl, "attack") ?? 6;
        int cRelease = GetInt(compEl, "release") ?? 8;
        int cMakeup = GetInt(compEl, "makeup") ?? GetInt(compEl, "makeUpGain") ?? 0;

        var eqList = new JsonArray();
        var defaultEq = new int[10];
        if (eqEl != null)
        {
            string[] freqKeys = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];
            for (int i = 0; i < 10; i++)
            {
                var val = GetInt(eqEl, $"gain{freqKeys[i]}") ?? GetInt(eqEl, $"band{i}") ?? GetInt(eqEl, $"gain_{i}");
                if (val is null)
                {
                    if (i < 3) val = GetInt(eqEl, "gainLow") ?? GetInt(eqEl, "gain_low");
                    else if (i < 7) val = GetInt(eqEl, "gainMid") ?? GetInt(eqEl, "gain_mid");
                    else val = GetInt(eqEl, "gainHigh") ?? GetInt(eqEl, "gain_high");
                }
                defaultEq[i] = Math.Clamp(val ?? 0, -9, 9);
            }
        }
        foreach (var g in defaultEq) eqList.Add(g);

        int deess = Math.Clamp(GetInt(deesserEl, "amount") ?? GetInt(deesserEl, "deesser") ?? 0, 0, 100);

        return new JsonObject
        {
            ["gate_enabled"] = gEnabled,
            ["gate_threshold"] = Math.Clamp(gThreshold, -59, 0),
            ["gate_attenuation"] = Math.Clamp(gAttenuation, 0, 100),
            ["gate_attack"] = Math.Clamp(gAttack, 0, 45),
            ["gate_release"] = Math.Clamp(gRelease, 0, 45),
            ["comp_threshold"] = Math.Clamp(cThreshold, -40, 0),
            ["comp_ratio"] = Math.Clamp(cRatio, 0, 14),
            ["comp_attack"] = Math.Clamp(cAttack, 0, 19),
            ["comp_release"] = Math.Clamp(cRelease, 0, 19),
            ["comp_makeup"] = Math.Clamp(cMakeup, -6, 24),
            ["eq_gains"] = eqList,
            ["deesser"] = deess,
        };
    }

    private static JsonObject ImportSetupProfile(XDocument doc, string name)
    {
        var mixerTree = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("mixerTree", StringComparison.OrdinalIgnoreCase));

        var defaultChannels = new (uint Id, string Name, string? AttrName, double DefaultVol)[]
        {
            (0, "Mic", "micLevel", 0.8),
            (1, "Chat", "chatLevel", 0.8),
            (2, "Music", "musicLevel", 0.8),
            (3, "Game", "gameLevel", 0.8),
            (4, "System", "systemLevel", 0.8),
            (5, "Samples", "sampleLevel", 0.8),
            (6, "Line In", "lineInLevel", 0.8),
            (7, "Console", "consoleLevel", 0.8),
            (8, "Headphones", "headphoneLevel", 0.8),
            (9, "Monitor", "mic2headphoneSubLevel", 0.8),
            (10, "Line Out", "lineOutLevel", 0.8),
            (11, "Broadcast Stream Mix", null, 1.0),
            (12, "Chat Mic", null, 1.0),
        };

        var channelsArray = new JsonArray();
        foreach (var def in defaultChannels)
        {
            double vol = def.DefaultVol;
            bool muted = false;

            if (mixerTree != null && def.AttrName != null)
            {
                var attrVal = mixerTree.Attribute(def.AttrName)?.Value;
                if (attrVal != null && double.TryParse(attrVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    vol = d / 255.0;
                }
            }
            else
            {
                var chEl = doc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName.Equals(def.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                    e.Attribute("channel")?.Value.Equals(def.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) == true ||
                    e.Attribute("name")?.Value.Equals(def.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) == true);

                if (chEl != null)
                {
                    var volAttr = GetDouble(chEl, "volume") ?? GetDouble(chEl, "vol");
                    if (volAttr.HasValue) vol = volAttr.Value > 1.0 ? volAttr.Value / 255.0 : volAttr.Value;
                    muted = GetBool(chEl, "muted") ?? false;
                }
            }

            if (def.Id == 9) // Monitor is clamped 50% - 100%
            {
                vol = Math.Clamp(vol, 0.5, 1.0);
            }
            else
            {
                vol = Math.Clamp(vol, 0.0, 1.0);
            }

            channelsArray.Add(new JsonObject
            {
                ["id"] = def.Id,
                ["name"] = def.Name,
                ["volume"] = Math.Round(vol, 2),
                ["muted"] = muted,
                ["restore_volume"] = null,
                ["icon"] = null,
            });
        }

        // Faders: A, B, C, D
        var fadersArray = new JsonArray();
        var faderAssignments = new Dictionary<string, uint>
        {
            ["A"] = 0, // Mic
            ["B"] = 1, // Chat
            ["C"] = 2, // Music
            ["D"] = 4, // System
        };

        // GoXLR official listIndex mapping:
        // 0: Mic, 1: Chat, 2: Music, 3: Game, 4: Console, 5: Line In, 6: System, 7: Samples, 8: Headphones, 9: Monitor, 10: Line Out
        var goxlrListToNextXlr = new Dictionary<int, uint>
        {
            [0] = 0, [1] = 1, [2] = 2, [3] = 3, [4] = 7, [5] = 6, [6] = 4, [7] = 5, [8] = 8, [9] = 9, [10] = 10
        };

        var seenFaders = new HashSet<uint>();
        foreach (var letter in new[] { "A", "B", "C", "D" })
        {
            int num = letter[0] - 'A';
            var faderMeterEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals($"FaderMeter{num}", StringComparison.OrdinalIgnoreCase));
            if (faderMeterEl != null)
            {
                var idxStr = faderMeterEl.Attribute($"FaderMeter{num}listIndex")?.Value;
                if (idxStr != null && int.TryParse(idxStr, out var goxlrIdx) && goxlrListToNextXlr.TryGetValue(goxlrIdx, out var mappedId))
                {
                    if (!seenFaders.Contains(mappedId))
                    {
                        faderAssignments[letter] = mappedId;
                        seenFaders.Add(mappedId);
                        continue;
                    }
                }
            }

            // Fallback generic fader element
            int faderNum = letter[0] - 'A' + 1;
            var faderEl = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName.Equals($"fader{letter}", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals($"fader{faderNum}", StringComparison.OrdinalIgnoreCase) ||
                (e.Name.LocalName.Equals("fader", StringComparison.OrdinalIgnoreCase) &&
                 (e.Attribute("id")?.Value.Equals(letter, StringComparison.OrdinalIgnoreCase) == true ||
                  e.Attribute("id")?.Value == faderNum.ToString())));

            if (faderEl != null)
            {
                var targetCh = faderEl.Attribute("channel")?.Value ?? faderEl.Attribute("target")?.Value;
                if (!string.IsNullOrEmpty(targetCh) && TryMapChannelName(targetCh, out var chId) && !seenFaders.Contains(chId))
                {
                    faderAssignments[letter] = chId;
                    seenFaders.Add(chId);
                    continue;
                }
            }

            // Ensure unique
            uint fallbackId = faderAssignments[letter];
            if (seenFaders.Contains(fallbackId))
            {
                foreach (uint candidate in new uint[] { 0, 1, 2, 4, 3, 5, 6, 7 })
                {
                    if (!seenFaders.Contains(candidate))
                    {
                        fallbackId = candidate;
                        break;
                    }
                }
            }
            faderAssignments[letter] = fallbackId;
            seenFaders.Add(fallbackId);
        }

        foreach (var (faderLetter, chId) in faderAssignments)
        {
            fadersArray.Add(new JsonObject
            {
                ["fader"] = faderLetter,
                ["channel"] = chId,
            });
        }

        // Routing
        var routesArray = new JsonArray();
        if (mixerTree != null)
        {
            // Parse GoXLR matrix attributes: micToHP="8192", sampleToStream="8192", etc.
            var srcMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                ["mic"] = 0, ["chat"] = 1, ["music"] = 2, ["game"] = 3,
                ["system"] = 4, ["sample"] = 5, ["linein"] = 6, ["console"] = 7
            };
            var dstMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                ["hp"] = 0, ["headphone"] = 0,
                ["stream"] = 1, ["broadcast"] = 1,
                ["lineout"] = 2,
                ["chat"] = 3, ["chatmic"] = 3,
                ["sampler"] = 4, ["sample"] = 4
            };

            foreach (var attr in mixerTree.Attributes())
            {
                var aname = attr.Name.LocalName;
                int toIndex = aname.IndexOf("To", StringComparison.OrdinalIgnoreCase);
                if (toIndex > 0)
                {
                    var srcPart = aname[..toIndex];
                    var dstPart = aname[(toIndex + 2)..];
                    if (srcMap.TryGetValue(srcPart, out var inId) && dstMap.TryGetValue(dstPart, out var outId))
                    {
                        if (int.TryParse(attr.Value, out var state) && state > 0)
                        {
                            // Skip blocked combinations (sampler to sampler, chat to chatmic)
                            if ((inId == 5 && outId == 4) || (inId == 1 && outId == 3)) continue;
                            routesArray.Add(new JsonObject { ["channel"] = inId, ["bus"] = outId, ["gain"] = 1.0 });
                        }
                    }
                }
            }
        }

        if (routesArray.Count == 0)
        {
            // Default factory routing
            uint[] inputs = [0, 1, 2, 3, 4, 5, 6, 7];
            foreach (var inCh in inputs)
            {
                if (inCh == 0) // Mic
                {
                    routesArray.Add(new JsonObject { ["channel"] = 0, ["bus"] = 0, ["gain"] = 1.0 }); // Headphones (Monitor)
                    routesArray.Add(new JsonObject { ["channel"] = 0, ["bus"] = 1, ["gain"] = 1.0 }); // Broadcast
                    routesArray.Add(new JsonObject { ["channel"] = 0, ["bus"] = 3, ["gain"] = 1.0 }); // Chat Mic
                }
                else
                {
                    routesArray.Add(new JsonObject { ["channel"] = inCh, ["bus"] = 0, ["gain"] = 1.0 }); // Headphones
                    routesArray.Add(new JsonObject { ["channel"] = inCh, ["bus"] = 1, ["gain"] = 1.0 }); // Broadcast
                    routesArray.Add(new JsonObject { ["channel"] = inCh, ["bus"] = 2, ["gain"] = 1.0 }); // Line Out
                }
            }
        }

        // Lighting
        var lightingArray = new JsonArray();
        var f0 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("FaderMeter0", StringComparison.OrdinalIgnoreCase));
        var f1 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("FaderMeter1", StringComparison.OrdinalIgnoreCase));
        var f2 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("FaderMeter2", StringComparison.OrdinalIgnoreCase));
        var f3 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("FaderMeter3", StringComparison.OrdinalIgnoreCase));
        var m1 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("mute1", StringComparison.OrdinalIgnoreCase));
        var m2 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("mute2", StringComparison.OrdinalIgnoreCase));
        var m3 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("mute3", StringComparison.OrdinalIgnoreCase));
        var m4 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("mute4", StringComparison.OrdinalIgnoreCase));
        var mc = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("muteChat", StringComparison.OrdinalIgnoreCase));
        var sw = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("swear", StringComparison.OrdinalIgnoreCase));
        var gc = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("globalColour", StringComparison.OrdinalIgnoreCase));
        var lx = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("logoX", StringComparison.OrdinalIgnoreCase));

        var zoneDefs = new (string Zone, string? ActiveHex, string? InactiveHex, bool Gradient)[]
        {
            ("fader_a", f0?.Attribute("FaderMeter0colour0")?.Value, f0?.Attribute("FaderMeter0colour1")?.Value, f0?.Attribute("FaderMeter0Display")?.Value == "GRADIENT"),
            ("fader_b", f1?.Attribute("FaderMeter1colour0")?.Value, f1?.Attribute("FaderMeter1colour1")?.Value, f1?.Attribute("FaderMeter1Display")?.Value == "GRADIENT"),
            ("fader_c", f2?.Attribute("FaderMeter2colour0")?.Value, f2?.Attribute("FaderMeter2colour1")?.Value, f2?.Attribute("FaderMeter2Display")?.Value == "GRADIENT"),
            ("fader_d", f3?.Attribute("FaderMeter3colour0")?.Value, f3?.Attribute("FaderMeter3colour1")?.Value, f3?.Attribute("FaderMeter3Display")?.Value == "GRADIENT"),
            ("mute_a", m1?.Attribute("mute1colour0")?.Value, m1?.Attribute("mute1colour1")?.Value, false),
            ("mute_b", m2?.Attribute("mute2colour0")?.Value, m2?.Attribute("mute2colour1")?.Value, false),
            ("mute_c", m3?.Attribute("mute3colour0")?.Value, m3?.Attribute("mute3colour1")?.Value, false),
            ("mute_d", m4?.Attribute("mute4colour0")?.Value, m4?.Attribute("mute4colour1")?.Value, false),
            ("cough", mc?.Attribute("muteChatcolour0")?.Value, mc?.Attribute("muteChatcolour1")?.Value, false),
            ("bleep", sw?.Attribute("swearcolour0")?.Value, sw?.Attribute("swearcolour1")?.Value, false),
            ("global", gc?.Attribute("globalColourcolour0")?.Value, gc?.Attribute("globalColourcolour1")?.Value, false),
            ("accent", lx?.Attribute("logoXcolour0")?.Value, lx?.Attribute("logoXcolour1")?.Value, false),
        };

        foreach (var zd in zoneDefs)
        {
            lightingArray.Add(new JsonObject
            {
                ["zone"] = zd.Zone,
                ["active"] = ToHexColor(zd.ActiveHex, "#22D3A6"),
                ["inactive"] = ToHexColor(zd.InactiveHex, "#3A414E"),
                ["gradient"] = zd.Gradient,
            });
        }

        var micSettings = ImportMicProfile(doc);

        return new JsonObject
        {
            ["format"] = 1,
            ["channels"] = channelsArray,
            ["routes"] = routesArray,
            ["lighting"] = lightingArray,
            ["faders"] = fadersArray,
            ["effects"] = null,
            ["mic"] = micSettings,
        };
    }

    private static JsonObject BuildDefaultSetupWithMic(string name, JsonObject micNode)
    {
        var doc = new XDocument(new XElement("profile"));
        var setup = ImportSetupProfile(doc, name);
        setup["mic"] = micNode;
        return setup;
    }

    private static string ToHexColor(string? val, string fallback)
    {
        if (string.IsNullOrWhiteSpace(val)) return fallback;
        val = val.Trim().TrimStart('#');
        if (val.Length == 8 && val.All(Uri.IsHexDigit))
        {
            return "#" + val[2..].ToUpperInvariant();
        }
        if (val.Length == 6 && val.All(Uri.IsHexDigit))
        {
            return "#" + val.ToUpperInvariant();
        }
        return fallback;
    }

    private static int? GetIntAttr(XElement? element, string attrName)
    {
        if (element is null) return null;
        var attr = element.Attribute(attrName)?.Value;
        if (attr != null && double.TryParse(attr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return (int)Math.Round(d);
        }
        return null;
    }

    private static bool TryMapChannelName(string name, out uint id)
    {
        name = name.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
        switch (name)
        {
            case "mic": case "microphone": id = 0; return true;
            case "chat": case "chatmic": id = 1; return true;
            case "music": case "spotify": id = 2; return true;
            case "game": id = 3; return true;
            case "system": id = 4; return true;
            case "sample": case "samples": case "sampler": id = 5; return true;
            case "linein": case "aux": id = 6; return true;
            case "console": id = 7; return true;
            case "headphones": case "headphone": id = 8; return true;
            case "monitor": case "micmonitor": id = 9; return true;
            case "lineout": id = 10; return true;
            case "broadcast": case "stream": case "broadcaststreammix": id = 11; return true;
            default:
                if (uint.TryParse(name, out id) && id <= 12) return true;
                id = 0; return false;
        }
    }

    private static int? GetInt(XElement? element, string name)
    {
        if (element is null) return null;
        var attr = element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value
                ?? element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        if (attr != null && double.TryParse(attr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return (int)Math.Round(d);
        }
        return null;
    }

    private static double? GetDouble(XElement? element, string name)
    {
        if (element is null) return null;
        var attr = element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value
                ?? element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        if (attr != null && double.TryParse(attr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return null;
    }

    private static bool? GetBool(XElement? element, string name)
    {
        if (element is null) return null;
        var attr = element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value
                ?? element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        if (attr != null)
        {
            if (bool.TryParse(attr, out var b)) return b;
            if (attr == "1") return true;
            if (attr == "0") return false;
        }
        return null;
    }
}
