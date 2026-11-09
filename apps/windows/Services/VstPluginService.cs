using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NextXLR.ViewModels;
using Windows.Graphics;

namespace NextXLR.Services;

/// <summary>
/// Data model for an active VST3, CLAP, or integrated studio DSP plugin in the microphone rack.
/// </summary>
public partial class VstPluginItem : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _vendor = "NEXT : XLR Studio DSP";
    [ObservableProperty] private string _format = "Studio DSP";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _tagCode = "[DSP]";
    [ObservableProperty] private string _tagColorHex = "#1FC79A";
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isBypassed = false;
    [ObservableProperty] private double _inputGainDb = 0.0;
    [ObservableProperty] private double _outputGainDb = 0.0;
    [ObservableProperty] private double _mixPercent = 100.0;
    [ObservableProperty] private string _oversampling = "1x (Echtzeit)";
    [ObservableProperty] private string _processingMode = "Stereo";
    [ObservableProperty] private string _presetName = "Standard";

    // --- Specialized DSP Properties ---
    // [EQ] NEXT : Pro-EQ 6
    [ObservableProperty] private double _lowCutFreq = 80.0;      // 20 - 300 Hz
    [ObservableProperty] private double _lowGain = 0.0;         // -12 to +12 dB
    [ObservableProperty] private double _mid1Gain = -1.5;       // -12 to +12 dB (450 Hz)
    [ObservableProperty] private double _mid2Gain = 2.0;        // -12 to +12 dB (2.5 kHz)
    [ObservableProperty] private double _highShelfGain = 1.0;   // -12 to +12 dB (8 kHz)
    [ObservableProperty] private double _airBandGain = 2.5;     // -12 to +12 dB (14 kHz)

    // [DE-ESS] NEXT : Precision DeEsser
    [ObservableProperty] private double _detectionFreq = 6200.0; // 3000 - 9500 Hz
    [ObservableProperty] private double _deEssThreshold = -18.0; // -40 to 0 dB
    [ObservableProperty] private double _deEssDepth = -9.0;      // -24 to 0 dB
    [ObservableProperty] private bool _deEssListen = false;      // Sibilance monitor

    // [COMP] NEXT : Broadcast Opto
    [ObservableProperty] private double _optoPeakReduction = 45.0; // 0 - 100 %
    [ObservableProperty] private double _optoGain = 6.0;           // 0 - 24 dB makeup
    [ObservableProperty] private string _optoSpeed = "Broadcast Smooth";
    [ObservableProperty] private double _optoWarmth = 25.0;        // 0 - 100 %

    // [WARMTH] NEXT : Tube & Tape Sat
    [ObservableProperty] private double _satDrive = 22.0;          // 0 - 100 %
    [ObservableProperty] private string _satHarmonics = "Even / Röhre 2nd";
    [ObservableProperty] private string _satTapeSpeed = "15 IPS (Warm Broadcast)";
    [ObservableProperty] private double _satHeadroom = 0.0;        // -6 to +6 dB

    // [GATE] NEXT : Smart Expander
    [ObservableProperty] private double _expanderThreshold = -38.0; // -60 to 0 dB
    [ObservableProperty] private double _expanderRatio = 2.5;       // 1.2 to 8.0 :1
    [ObservableProperty] private double _expanderAttack = 4.0;      // 0.5 to 50 ms
    [ObservableProperty] private double _expanderRelease = 140.0;   // 20 to 800 ms
    [ObservableProperty] private double _expanderHysteresis = 4.0;  // 1 to 12 dB

    // [LIMITER] NEXT : True-Peak Master
    [ObservableProperty] private double _limiterCeiling = -1.0;    // -3.0 to 0.0 dBTP
    [ObservableProperty] private double _limiterLookahead = 2.0;   // 0.5 to 5.0 ms
    [ObservableProperty] private double _limiterRelease = 75.0;    // 10 to 500 ms
    [ObservableProperty] private double _targetLufs = -14.0;       // -24 to -9 LUFS

    // Generic / VST3 fallback parameters
    [ObservableProperty] private double _threshold = -18.0;
    [ObservableProperty] private double _frequency = 120.0;
    [ObservableProperty] private double _qFactor = 1.0;
    [ObservableProperty] private double _ratio = 3.0;
    [ObservableProperty] private double _attack = 15.0;
    [ObservableProperty] private double _release = 120.0;
    [ObservableProperty] private double _drive = 0.0;

    public string StatusText => IsBypassed ? "Bypass" : (IsEnabled ? "Aktiv" : "Aus");
    public SolidColorBrush StatusBrush => new(IsBypassed ? ColorHelper.FromArgb(255, 0xEF, 0x8C, 0x34) : (IsEnabled ? ColorHelper.FromArgb(255, 0x22, 0xD3, 0xA6) : ColorHelper.FromArgb(255, 0x88, 0x88, 0x88)));

    public SolidColorBrush TagBrush
    {
        get
        {
            try
            {
                var hex = TagColorHex.TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex[..2], 16);
                    byte g = Convert.ToByte(hex[2..4], 16);
                    byte b = Convert.ToByte(hex[4..6], 16);
                    return new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b));
                }
            }
            catch { }
            return new SolidColorBrush(ColorHelper.FromArgb(255, 0x1F, 0xC7, 0x9A));
        }
    }

    private void NotifyHardwareSync()
    {
        VstPluginService.SyncToHardware(this);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        NotifyHardwareSync();
        VstPluginService.Instance.SaveRack();
    }

    partial void OnIsBypassedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        NotifyHardwareSync();
        VstPluginService.Instance.SaveRack();
    }

    partial void OnLowCutFreqChanged(double value) => NotifyHardwareSync();
    partial void OnLowGainChanged(double value) => NotifyHardwareSync();
    partial void OnMid1GainChanged(double value) => NotifyHardwareSync();
    partial void OnMid2GainChanged(double value) => NotifyHardwareSync();
    partial void OnHighShelfGainChanged(double value) => NotifyHardwareSync();
    partial void OnAirBandGainChanged(double value) => NotifyHardwareSync();

    partial void OnDetectionFreqChanged(double value) => NotifyHardwareSync();
    partial void OnDeEssThresholdChanged(double value) => NotifyHardwareSync();
    partial void OnDeEssDepthChanged(double value) => NotifyHardwareSync();

    partial void OnOptoPeakReductionChanged(double value) => NotifyHardwareSync();
    partial void OnOptoGainChanged(double value) => NotifyHardwareSync();
    partial void OnOptoSpeedChanged(string value) => NotifyHardwareSync();
    partial void OnOptoWarmthChanged(double value) => NotifyHardwareSync();

    partial void OnSatDriveChanged(double value) => NotifyHardwareSync();
    partial void OnSatHarmonicsChanged(string value) => NotifyHardwareSync();
    partial void OnSatTapeSpeedChanged(string value) => NotifyHardwareSync();
    partial void OnSatHeadroomChanged(double value) => NotifyHardwareSync();

    partial void OnExpanderThresholdChanged(double value) => NotifyHardwareSync();
    partial void OnExpanderRatioChanged(double value) => NotifyHardwareSync();
    partial void OnExpanderAttackChanged(double value) => NotifyHardwareSync();
    partial void OnExpanderReleaseChanged(double value) => NotifyHardwareSync();
    partial void OnExpanderHysteresisChanged(double value) => NotifyHardwareSync();

    partial void OnLimiterCeilingChanged(double value) => NotifyHardwareSync();
    partial void OnLimiterLookaheadChanged(double value) => NotifyHardwareSync();
    partial void OnLimiterReleaseChanged(double value) => NotifyHardwareSync();
    partial void OnTargetLufsChanged(double value) => NotifyHardwareSync();

    public string[] GetAvailablePresets()
    {
        if (Name.Contains("Pro-EQ", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Broadcast Voice Pro",
                "Podcast Speech Clarity",
                "Warm Radio Announcer",
                "Deep Voice Presence",
                "Acoustic / Flat Neutral"
            ];
        }
        if (Name.Contains("DeEsser", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Clean Broadcast (6.2 kHz)",
                "Gentle Vocal (7.5 kHz)",
                "Aggressive Sibilance (5.8 kHz)",
                "Heavy De-Esser (6.0 kHz)"
            ];
        }
        if (Name.Contains("Opto", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Classic LA-2A Leveler",
                "Podcast Vocal Polish",
                "Dynamic Streamer Punch",
                "Vintage Tube Compression"
            ];
        }
        if (Name.Contains("Tube", StringComparison.OrdinalIgnoreCase) || Name.Contains("Sat", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Warm Analog Tube Preamplifier",
                "Vintage Studio Tape 15 IPS",
                "Modern Crisp Exciter",
                "Radio Broadcast Saturation"
            ];
        }
        if (Name.Contains("Expander", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Gentle Speech Expander",
                "Podcast Room Cleanup",
                "Noisy Keyboard & Fan Gate",
                "Transparent Downward Expander"
            ];
        }
        if (Name.Contains("Limiter", StringComparison.OrdinalIgnoreCase) || Name.Contains("Master", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Broadcast EBU R128 (-14 LUFS)",
                "Streaming Clean (-1.0 dBTP)",
                "Loud & Punchy (-0.5 dBTP)",
                "Zero-Overshoot True-Peak Limit"
            ];
        }

        return
        [
            "Standard / Flat",
            "Warm Broadcast Radio",
            "Podcast Voice Clarity",
            "Streamer Dynamic Punch",
            "Clean De-Esser",
            "Heavy Broadcast Mastering"
        ];
    }

    public void ApplyPreset(string preset)
    {
        PresetName = preset;

        // Pro-EQ 6 presets
        if (Name.Contains("Pro-EQ", StringComparison.OrdinalIgnoreCase))
        {
            switch (preset)
            {
                case "Broadcast Voice Pro":
                    LowCutFreq = 80; LowGain = 1.5; Mid1Gain = -2.0; Mid2Gain = 2.5; HighShelfGain = 1.5; AirBandGain = 3.0;
                    break;
                case "Podcast Speech Clarity":
                    LowCutFreq = 100; LowGain = 0.0; Mid1Gain = -3.0; Mid2Gain = 3.0; HighShelfGain = 2.0; AirBandGain = 2.0;
                    break;
                case "Warm Radio Announcer":
                    LowCutFreq = 65; LowGain = 3.5; Mid1Gain = -1.0; Mid2Gain = 1.5; HighShelfGain = 0.5; AirBandGain = 1.5;
                    break;
                case "Deep Voice Presence":
                    LowCutFreq = 70; LowGain = 2.0; Mid1Gain = -1.5; Mid2Gain = 3.5; HighShelfGain = 2.0; AirBandGain = 3.5;
                    break;
                case "Acoustic / Flat Neutral":
                default:
                    LowCutFreq = 40; LowGain = 0.0; Mid1Gain = 0.0; Mid2Gain = 0.0; HighShelfGain = 0.0; AirBandGain = 0.0;
                    break;
            }
            return;
        }

        // DeEsser presets
        if (Name.Contains("DeEsser", StringComparison.OrdinalIgnoreCase))
        {
            switch (preset)
            {
                case "Clean Broadcast (6.2 kHz)":
                    DetectionFreq = 6200; DeEssThreshold = -18; DeEssDepth = -9;
                    break;
                case "Gentle Vocal (7.5 kHz)":
                    DetectionFreq = 7500; DeEssThreshold = -14; DeEssDepth = -6;
                    break;
                case "Aggressive Sibilance (5.8 kHz)":
                    DetectionFreq = 5800; DeEssThreshold = -22; DeEssDepth = -14;
                    break;
                case "Heavy De-Esser (6.0 kHz)":
                default:
                    DetectionFreq = 6000; DeEssThreshold = -26; DeEssDepth = -16;
                    break;
            }
            return;
        }

        // Broadcast Opto presets
        if (Name.Contains("Opto", StringComparison.OrdinalIgnoreCase))
        {
            switch (preset)
            {
                case "Classic LA-2A Leveler":
                    OptoPeakReduction = 45; OptoGain = 6.0; OptoWarmth = 30; OptoSpeed = "Broadcast Smooth";
                    break;
                case "Podcast Vocal Polish":
                    OptoPeakReduction = 35; OptoGain = 4.0; OptoWarmth = 15; OptoSpeed = "Broadcast Smooth";
                    break;
                case "Dynamic Streamer Punch":
                    OptoPeakReduction = 60; OptoGain = 8.5; OptoWarmth = 40; OptoSpeed = "Fast Limiting";
                    break;
                case "Vintage Tube Compression":
                default:
                    OptoPeakReduction = 50; OptoGain = 7.0; OptoWarmth = 50; OptoSpeed = "Vintage Slow";
                    break;
            }
            return;
        }

        // Tube & Tape Sat presets
        if (Name.Contains("Tube", StringComparison.OrdinalIgnoreCase) || Name.Contains("Sat", StringComparison.OrdinalIgnoreCase))
        {
            switch (preset)
            {
                case "Warm Analog Tube Preamplifier":
                    SatDrive = 28; SatHarmonics = "Even / Röhre 2nd"; SatTapeSpeed = "15 IPS (Warm Broadcast)"; SatHeadroom = 0.0;
                    break;
                case "Vintage Studio Tape 15 IPS":
                    SatDrive = 35; SatHarmonics = "Odd / Tape 3rd"; SatTapeSpeed = "15 IPS (Warm Broadcast)"; SatHeadroom = -1.0;
                    break;
                case "Modern Crisp Exciter":
                    SatDrive = 18; SatHarmonics = "Even / Röhre 2nd"; SatTapeSpeed = "30 IPS (Hi-Fi Ultra)"; SatHeadroom = 1.0;
                    break;
                case "Radio Broadcast Saturation":
                default:
                    SatDrive = 40; SatHarmonics = "Mixed Broadcast"; SatTapeSpeed = "15 IPS (Warm Broadcast)"; SatHeadroom = 0.0;
                    break;
            }
            return;
        }

        // Smart Expander presets
        if (Name.Contains("Expander", StringComparison.OrdinalIgnoreCase))
        {
            switch (preset)
            {
                case "Gentle Speech Expander":
                    ExpanderThreshold = -36; ExpanderRatio = 2.0; ExpanderAttack = 4.0; ExpanderRelease = 150; ExpanderHysteresis = 4.0;
                    break;
                case "Podcast Room Cleanup":
                    ExpanderThreshold = -32; ExpanderRatio = 3.0; ExpanderAttack = 3.0; ExpanderRelease = 120; ExpanderHysteresis = 5.0;
                    break;
                case "Noisy Keyboard & Fan Gate":
                    ExpanderThreshold = -28; ExpanderRatio = 5.0; ExpanderAttack = 1.5; ExpanderRelease = 80; ExpanderHysteresis = 6.0;
                    break;
                case "Transparent Downward Expander":
                default:
                    ExpanderThreshold = -40; ExpanderRatio = 1.8; ExpanderAttack = 5.0; ExpanderRelease = 200; ExpanderHysteresis = 3.0;
                    break;
            }
            return;
        }

        // True-Peak Master presets
        if (Name.Contains("Limiter", StringComparison.OrdinalIgnoreCase) || Name.Contains("Master", StringComparison.OrdinalIgnoreCase))
        {
            switch (preset)
            {
                case "Broadcast EBU R128 (-14 LUFS)":
                    LimiterCeiling = -1.0; LimiterLookahead = 2.0; LimiterRelease = 80; TargetLufs = -14.0;
                    break;
                case "Streaming Clean (-1.0 dBTP)":
                    LimiterCeiling = -1.0; LimiterLookahead = 1.5; LimiterRelease = 60; TargetLufs = -16.0;
                    break;
                case "Loud & Punchy (-0.5 dBTP)":
                    LimiterCeiling = -0.5; LimiterLookahead = 1.0; LimiterRelease = 45; TargetLufs = -12.0;
                    break;
                case "Zero-Overshoot True-Peak Limit":
                default:
                    LimiterCeiling = -1.5; LimiterLookahead = 3.0; LimiterRelease = 100; TargetLufs = -18.0;
                    break;
            }
            return;
        }

        // Generic fallback presets
        switch (preset)
        {
            case "Warm Broadcast Radio":
                InputGainDb = 1.5; OutputGainDb = 2.0; MixPercent = 100; Threshold = -22.0; Frequency = 90.0; QFactor = 1.2; Ratio = 4.0; Attack = 20.0; Release = 100.0; Drive = 15.0;
                break;
            case "Podcast Voice Clarity":
                InputGainDb = 0.0; OutputGainDb = 1.0; MixPercent = 100; Threshold = -16.0; Frequency = 140.0; QFactor = 0.8; Ratio = 3.2; Attack = 12.0; Release = 90.0; Drive = 0.0;
                break;
            case "Streamer Dynamic Punch":
                InputGainDb = 2.0; OutputGainDb = 2.5; MixPercent = 100; Threshold = -24.0; Frequency = 80.0; QFactor = 1.5; Ratio = 5.0; Attack = 5.0; Release = 80.0; Drive = 25.0;
                break;
            case "Clean De-Esser":
                InputGainDb = 0.0; OutputGainDb = 0.0; MixPercent = 90; Threshold = -12.0; Frequency = 6200.0; QFactor = 2.5; Ratio = 6.0; Attack = 2.0; Release = 40.0; Drive = 0.0;
                break;
            case "Heavy Broadcast Mastering":
                InputGainDb = 3.0; OutputGainDb = 3.0; MixPercent = 100; Threshold = -28.0; Frequency = 75.0; QFactor = 1.8; Ratio = 8.0; Attack = 3.0; Release = 60.0; Drive = 35.0;
                break;
            case "Standard / Flat":
            default:
                InputGainDb = 0.0; OutputGainDb = 0.0; MixPercent = 100; Threshold = -18.0; Frequency = 120.0; QFactor = 1.0; Ratio = 3.0; Attack = 15.0; Release = 120.0; Drive = 0.0;
                break;
        }
    }
}

/// <summary>
/// Service that scans, manages, and hosts VST3 &amp; CLAP audio plugins and the 6 integrated studio DSP plugins.
/// </summary>
public sealed class VstPluginService
{
    private static readonly Lazy<VstPluginService> _instance = new(() => new VstPluginService());
    public static VstPluginService Instance => _instance.Value;

    public static MicSettingsViewModel? ActiveMicViewModel { get; set; }

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NextXLR",
        "vst_rack.json");

    public ObservableCollection<VstPluginItem> RackPlugins { get; } = new();

    private VstPluginService()
    {
        LoadRack();
    }

    /// <summary>
    /// Creates an instance of one of the 6 integrated Studio DSP plugins.
    /// </summary>
    public static VstPluginItem CreateIntegratedPlugin(string type)
    {
        return type.ToUpperInvariant() switch
        {
            "EQ" => new VstPluginItem
            {
                Name = "NEXT : Pro-EQ 6",
                Vendor = "NEXT : XLR Studio DSP",
                Format = "Studio DSP",
                Path = "Builtin::Pro-EQ6",
                TagCode = "[EQ]",
                TagColorHex = "#1FC79A",
                PresetName = "Broadcast Voice Pro",
                LowCutFreq = 80.0,
                LowGain = 1.5,
                Mid1Gain = -2.0,
                Mid2Gain = 2.5,
                HighShelfGain = 1.5,
                AirBandGain = 3.0
            },
            "DE-ESS" or "DEESS" => new VstPluginItem
            {
                Name = "NEXT : Precision DeEsser",
                Vendor = "NEXT : XLR Studio DSP",
                Format = "Studio DSP",
                Path = "Builtin::PrecisionDeEsser",
                TagCode = "[DE-ESS]",
                TagColorHex = "#33D6AA",
                PresetName = "Clean Broadcast (6.2 kHz)",
                DetectionFreq = 6200.0,
                DeEssThreshold = -18.0,
                DeEssDepth = -9.0
            },
            "COMP" => new VstPluginItem
            {
                Name = "NEXT : Broadcast Opto",
                Vendor = "NEXT : XLR Studio DSP",
                Format = "Studio DSP",
                Path = "Builtin::BroadcastOpto",
                TagCode = "[COMP]",
                TagColorHex = "#F5B942",
                PresetName = "Classic LA-2A Leveler",
                OptoPeakReduction = 45.0,
                OptoGain = 6.0,
                OptoWarmth = 25.0
            },
            "WARMTH" => new VstPluginItem
            {
                Name = "NEXT : Tube & Tape Sat",
                Vendor = "NEXT : XLR Studio DSP",
                Format = "Studio DSP",
                Path = "Builtin::TubeTapeSat",
                TagCode = "[WARMTH]",
                TagColorHex = "#FF7043",
                PresetName = "Warm Analog Tube Preamplifier",
                SatDrive = 22.0,
                SatHarmonics = "Even / Röhre 2nd",
                SatTapeSpeed = "15 IPS (Warm Broadcast)"
            },
            "GATE" => new VstPluginItem
            {
                Name = "NEXT : Smart Expander",
                Vendor = "NEXT : XLR Studio DSP",
                Format = "Studio DSP",
                Path = "Builtin::SmartExpander",
                TagCode = "[GATE]",
                TagColorHex = "#42A5F5",
                PresetName = "Gentle Speech Expander",
                ExpanderThreshold = -36.0,
                ExpanderRatio = 2.2,
                ExpanderAttack = 4.0,
                ExpanderRelease = 140.0
            },
            "LIMITER" => new VstPluginItem
            {
                Name = "NEXT : True-Peak Master",
                Vendor = "NEXT : XLR Studio DSP",
                Format = "Studio DSP",
                Path = "Builtin::TruePeakMaster",
                TagCode = "[LIMITER]",
                TagColorHex = "#AB47BC",
                PresetName = "Broadcast EBU R128 (-14 LUFS)",
                LimiterCeiling = -1.0,
                LimiterLookahead = 2.0,
                LimiterRelease = 75.0,
                TargetLufs = -14.0
            },
            _ => new VstPluginItem
            {
                Name = "Studio DSP Generic",
                TagCode = "[DSP]",
                TagColorHex = "#1FC79A"
            }
        };
    }

    /// <summary>
    /// Returns the complete suite of all 6 integrated Studio DSP plugins in optimal broadcast signal-flow order:
    /// Gate/Expander -> De-Esser -> EQ -> Compressor -> Saturation -> Limiter.
    /// </summary>
    public static List<VstPluginItem> GetIntegratedPlugins()
    {
        return
        [
            CreateIntegratedPlugin("GATE"),
            CreateIntegratedPlugin("DE-ESS"),
            CreateIntegratedPlugin("EQ"),
            CreateIntegratedPlugin("COMP"),
            CreateIntegratedPlugin("WARMTH"),
            CreateIntegratedPlugin("LIMITER")
        ];
    }

    /// <summary>
    /// Resets the active effect rack to the default 6 studio broadcast plugins and synchronizes them with the GoXLR hardware.
    /// </summary>
    public void ResetToIntegratedPlugins()
    {
        RackPlugins.Clear();
        foreach (var p in GetIntegratedPlugins())
        {
            RackPlugins.Add(p);
        }
        SaveRack();
        SyncAllToHardware();
    }

    /// <summary>
    /// Synchronizes a DSP plugin's parameter state directly to the GoXLR hardware via MicSettingsViewModel in real time.
    /// </summary>
    public static void SyncToHardware(VstPluginItem plugin)
    {
        var mic = ActiveMicViewModel;
        if (mic == null) return;

        if (plugin.Name.Contains("Pro-EQ", StringComparison.OrdinalIgnoreCase))
        {
            if (plugin.IsBypassed || !plugin.IsEnabled)
            {
                foreach (var b in mic.EqBands) b.Gain = 0;
            }
            else
            {
                double subCut = plugin.LowCutFreq >= 100 ? -5.0 : (plugin.LowCutFreq >= 60 ? -2.0 : 0.0);
                if (mic.EqBands.Count >= 10)
                {
                    mic.EqBands[0].Gain = Math.Clamp(plugin.LowGain + subCut - 3.0, -9.0, 9.0); // 31 Hz
                    mic.EqBands[1].Gain = Math.Clamp(plugin.LowGain + subCut, -9.0, 9.0);       // 63 Hz
                    mic.EqBands[2].Gain = Math.Clamp(plugin.LowGain, -9.0, 9.0);                // 125 Hz
                    mic.EqBands[3].Gain = Math.Clamp(plugin.Mid1Gain * 0.8, -9.0, 9.0);         // 250 Hz
                    mic.EqBands[4].Gain = Math.Clamp(plugin.Mid1Gain, -9.0, 9.0);               // 500 Hz
                    mic.EqBands[5].Gain = Math.Clamp(plugin.Mid2Gain * 0.7, -9.0, 9.0);         // 1000 Hz
                    mic.EqBands[6].Gain = Math.Clamp(plugin.Mid2Gain, -9.0, 9.0);               // 2000 Hz
                    mic.EqBands[7].Gain = Math.Clamp(plugin.HighShelfGain, -9.0, 9.0);          // 4000 Hz
                    mic.EqBands[8].Gain = Math.Clamp(plugin.HighShelfGain + 0.5, -9.0, 9.0);    // 8000 Hz
                    mic.EqBands[9].Gain = Math.Clamp(plugin.AirBandGain, -9.0, 9.0);            // 16000 Hz
                }
            }
        }
        else if (plugin.Name.Contains("Opto", StringComparison.OrdinalIgnoreCase))
        {
            if (plugin.IsBypassed || !plugin.IsEnabled)
            {
                mic.CompThreshold = 0;
                mic.CompMakeup = 0;
            }
            else
            {
                // Peak Reduction: 0..100% -> -40 dB to 0 dB
                mic.CompThreshold = Math.Clamp(-(plugin.OptoPeakReduction * 0.38), -40, 0);
                mic.CompMakeup = Math.Clamp(plugin.OptoGain, -6, 24);
                mic.CompRatio = 9; // 4.0:1
                if (plugin.OptoSpeed.Contains("Fast", StringComparison.OrdinalIgnoreCase))
                {
                    mic.CompAttack = 4;
                    mic.CompRelease = 5;
                }
                else if (plugin.OptoSpeed.Contains("Slow", StringComparison.OrdinalIgnoreCase))
                {
                    mic.CompAttack = 12;
                    mic.CompRelease = 14;
                }
                else
                {
                    mic.CompAttack = 7;
                    mic.CompRelease = 9;
                }
            }
        }
        else if (plugin.Name.Contains("DeEsser", StringComparison.OrdinalIgnoreCase))
        {
            if (plugin.IsBypassed || !plugin.IsEnabled)
            {
                mic.DeEsser = 0;
            }
            else
            {
                // DeEssDepth: -24 dB to 0 dB -> 0 to 100%
                mic.DeEsser = Math.Clamp(-plugin.DeEssDepth / 24.0 * 100.0, 0, 100);
            }
        }
        else if (plugin.Name.Contains("Expander", StringComparison.OrdinalIgnoreCase))
        {
            if (plugin.IsBypassed || !plugin.IsEnabled)
            {
                mic.GateEnabled = false;
            }
            else
            {
                mic.GateEnabled = true;
                mic.GateThreshold = Math.Clamp(plugin.ExpanderThreshold, -59, 0);
                mic.GateAttenuation = Math.Clamp(plugin.ExpanderRatio * 16.0, 10, 100);
                mic.GateAttack = Math.Clamp(plugin.ExpanderAttack / 1.5, 0, 45);
                mic.GateRelease = Math.Clamp(plugin.ExpanderRelease / 20.0, 0, 45);
            }
        }
        else if (plugin.Name.Contains("Tube", StringComparison.OrdinalIgnoreCase) || plugin.Name.Contains("Sat", StringComparison.OrdinalIgnoreCase))
        {
            if (plugin.IsBypassed || !plugin.IsEnabled)
            {
                mic.VoiceMorphDrive = 0;
            }
            else
            {
                mic.VoiceMorphDrive = Math.Clamp(plugin.SatDrive, 0, 100);
            }
        }
        else if (plugin.Name.Contains("Limiter", StringComparison.OrdinalIgnoreCase) || plugin.Name.Contains("Master", StringComparison.OrdinalIgnoreCase))
        {
            if (plugin.IsBypassed || !plugin.IsEnabled)
            {
                // Limiter bypass
            }
            else
            {
                double boost = (plugin.TargetLufs + 18.0) * 0.4;
                mic.CompMakeup = Math.Clamp(mic.CompMakeup + boost, -6, 24);
            }
        }
    }

    /// <summary>
    /// Synchronizes all active plugins in the rack to the GoXLR hardware DSP.
    /// </summary>
    public static void SyncAllToHardware()
    {
        if (ActiveMicViewModel == null) return;
        foreach (var p in Instance.RackPlugins)
        {
            SyncToHardware(p);
        }
    }

    /// <summary>
    /// Adds all 6 integrated plugins to the active DSP rack if not already present.
    /// </summary>
    public int AddIntegratedPlugins()
    {
        int count = 0;
        foreach (var p in GetIntegratedPlugins())
        {
            if (!RackPlugins.Any(r => r.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                RackPlugins.Add(p);
                count++;
            }
        }
        SaveRack();
        return count;
    }

    /// <summary>
    /// Moves a plugin up in the effect rack sequence.
    /// </summary>
    public void MovePluginUp(VstPluginItem item)
    {
        int idx = RackPlugins.IndexOf(item);
        if (idx > 0)
        {
            RackPlugins.Move(idx, idx - 1);
            SaveRack();
        }
    }

    /// <summary>
    /// Moves a plugin down in the effect rack sequence.
    /// </summary>
    public void MovePluginDown(VstPluginItem item)
    {
        int idx = RackPlugins.IndexOf(item);
        if (idx >= 0 && idx < RackPlugins.Count - 1)
        {
            RackPlugins.Move(idx, idx + 1);
            SaveRack();
        }
    }

    /// <summary>
    /// Scans standard Windows system directories for installed VST3 and CLAP audio plugins.
    /// </summary>
    public static List<VstPluginItem> ScanInstalledPlugins()
    {
        var results = new List<VstPluginItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var searchDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files", "VST3"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Common Files", "VST3"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "VST3"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files", "CLAP"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "CLAP")
        };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(dir, "*.*", SearchOption.AllDirectories))
                {
                    if (entry.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ||
                        entry.EndsWith(".clap", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenPaths.Add(entry))
                        {
                            string format = entry.EndsWith(".clap", StringComparison.OrdinalIgnoreCase) ? "CLAP" : "VST3";
                            string name = Path.GetFileNameWithoutExtension(entry);
                            string vendor = ResolveVendor(entry);

                            results.Add(new VstPluginItem
                            {
                                Name = name,
                                Vendor = vendor,
                                Format = format,
                                Path = entry,
                                TagCode = format == "CLAP" ? "[CLAP]" : "[VST3]",
                                TagColorHex = "#8B93A3"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to scan VST3 dir {dir}: {ex.Message}");
            }
        }

        // Always include the 6 integrated studio plugins in search results
        foreach (var ip in GetIntegratedPlugins())
        {
            results.Add(ip);
        }

        return results.OrderBy(p => p.Name).ToList();
    }

    private static string ResolveVendor(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains("fabfilter")) return "FabFilter";
        if (lower.Contains("izotope")) return "iZotope";
        if (lower.Contains("waves")) return "Waves Audio";
        if (lower.Contains("tokyodawn") || lower.Contains("tdr")) return "Tokyo Dawn Records";
        if (lower.Contains("valhalla")) return "Valhalla DSP";
        if (lower.Contains("meldaproduction")) return "MeldaProduction";
        if (lower.Contains("steinberg")) return "Steinberg";
        if (lower.Contains("cockos") || lower.Contains("reaplugs")) return "Cockos ReaPlugs";
        return "Audio DSP Studio";
    }

    public void AddPlugin(VstPluginItem item)
    {
        RackPlugins.Add(item);
        SaveRack();
    }

    public void RemovePlugin(VstPluginItem item)
    {
        RackPlugins.Remove(item);
        SaveRack();
    }

    public void SaveRack()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            var list = RackPlugins.ToList();
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            string tempFile = SettingsFile + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, SettingsFile, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save VST rack: {ex.Message}");
        }
    }

    public void LoadRack()
    {
        try
        {
            RackPlugins.Clear();
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var items = JsonSerializer.Deserialize<List<VstPluginItem>>(json);
                if (items != null && items.Count > 0)
                {
                    foreach (var it in items) RackPlugins.Add(it);
                    return;
                }
            }
        }
        catch { }

        // Default: Populate with the 6 integrated broadcast plugins
        foreach (var p in GetIntegratedPlugins())
        {
            RackPlugins.Add(p);
        }
    }

    /// <summary>
    /// Opens the comprehensive studio VST3 / CLAP / Studio DSP Plugin Editor dialog.
    /// </summary>
    public static void OpenEditor(Window parentWindow, VstPluginItem plugin)
    {
        var editorWin = new Window
        {
            Title = $"Plugin-Editor — {plugin.Name} {plugin.TagCode}",
            SystemBackdrop = new MicaBackdrop()
        };

        var appWin = editorWin.AppWindow;
        appWin.Resize(new SizeInt32(820, 740));

        // Set official app icon
        try
        {
            var iconCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "nextXLR_icon.ico"),
                Path.Combine(AppContext.BaseDirectory, "nextXLR_icon.ico"),
                Path.Combine(AppContext.BaseDirectory, @"..\..\..\Assets\nextXLR_icon.ico")
            };
            var iconPath = iconCandidates.FirstOrDefault(File.Exists);
            if (iconPath != null) appWin.SetIcon(iconPath);
        }
        catch { }

        var rootGrid = new Grid
        {
            Padding = new Thickness(24),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Header
                new RowDefinition { Height = GridLength.Auto }, // Presets
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }, // Parameters & Knobs
                new RowDefinition { Height = GridLength.Auto }  // Footer actions
            }
        };

        // 1. Header with plugin info, badge, and official logo
        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 16),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var iconBorder = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x1E, 0x22, 0x2B)),
            BorderBrush = plugin.TagBrush,
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(0, 0, 14, 0),
            Child = new FontIcon
            {
                Glyph = plugin.TagCode switch
                {
                    "[EQ]" => "\uE9E9",
                    "[COMP]" => "\uE9CA",
                    "[DE-ESS]" => "\uE8D6",
                    "[WARMTH]" => "\uE7E6",
                    "[GATE]" => "\uE9D9",
                    "[LIMITER]" => "\uE9F5",
                    _ => "\uE99A"
                },
                FontSize = 22,
                Foreground = plugin.TagBrush
            }
        };
        Grid.SetColumn(iconBorder, 0);
        header.Children.Add(iconBorder);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var pluginTitle = new TextBlock { Text = plugin.Name, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        var tagBadge = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(35, 31, 199, 154)),
            BorderBrush = plugin.TagBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = plugin.TagCode, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = plugin.TagBrush }
        };
        headerRow.Children.Add(pluginTitle);
        headerRow.Children.Add(tagBadge);

        var pluginMeta = new TextBlock
        {
            Text = $"{plugin.Format} 64-Bit Studio DSP • {plugin.Vendor} • Latenz: 0 Samples (0.0 ms)",
            FontSize = 12,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x88, 0x90, 0xA0)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        titleStack.Children.Add(headerRow);
        titleStack.Children.Add(pluginMeta);
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);

        var headerRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };

        // Add official logo on right
        try
        {
            var logoCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "nextXLR_logo.png"),
                Path.Combine(AppContext.BaseDirectory, "nextXLR_logo.png"),
                Path.Combine(AppContext.BaseDirectory, @"..\..\..\Assets\nextXLR_logo.png")
            };
            var logoPath = logoCandidates.FirstOrDefault(File.Exists);
            if (logoPath != null)
            {
                var logoImg = new Image
                {
                    Source = new BitmapImage(new Uri(Path.GetFullPath(logoPath))),
                    Height = 32,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerRight.Children.Add(logoImg);
            }
        }
        catch { }

        var bypassToggle = new ToggleSwitch
        {
            IsOn = !plugin.IsBypassed,
            OnContent = "Aktiv",
            OffContent = "Bypass",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        bypassToggle.Toggled += (_, _) =>
        {
            plugin.IsBypassed = !bypassToggle.IsOn;
        };
        headerRight.Children.Add(bypassToggle);

        Grid.SetColumn(headerRight, 2);
        header.Children.Add(headerRight);

        Grid.SetRow(header, 0);
        rootGrid.Children.Add(header);

        // 2. Preset Selection Bar
        var presetCard = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x1E, 0x22, 0x2B)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0x2A, 0x30, 0x3C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var presetRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };
        var presetLabel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            Children =
            {
                new FontIcon { Glyph = "\uE71D", FontSize = 15, Foreground = plugin.TagBrush },
                new TextBlock { Text = "Studio-Preset:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
            }
        };
        var presetCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = plugin.GetAvailablePresets(),
            SelectedItem = plugin.PresetName
        };

        Grid.SetColumn(presetLabel, 0);
        Grid.SetColumn(presetCombo, 1);
        presetRow.Children.Add(presetLabel);
        presetRow.Children.Add(presetCombo);
        presetCard.Child = presetRow;
        Grid.SetRow(presetCard, 1);
        rootGrid.Children.Add(presetCard);

        // 3. Scrollable Controls Area
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var mainStack = new StackPanel { Spacing = 16 };

        // Group 1: Master Gain & Mix
        var masterCard = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x18, 0x1B, 0x22)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0x25, 0x2B, 0x36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16)
        };
        var masterStack = new StackPanel { Spacing = 12 };
        masterStack.Children.Add(new TextBlock
        {
            Text = "MASTER GAIN & DRY/WET MIX",
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 11,
            Foreground = plugin.TagBrush
        });

        var inGainSlider = CreateSliderRow("Input Gain", "-24 dB", "+24 dB", plugin.InputGainDb, v => plugin.InputGainDb = v, "dB");
        var outGainSlider = CreateSliderRow("Output / Makeup Gain", "-24 dB", "+24 dB", plugin.OutputGainDb, v => plugin.OutputGainDb = v, "dB");
        var mixSlider = CreateSliderRow("Dry / Wet Mix", "0 %", "100 %", plugin.MixPercent, v => plugin.MixPercent = v, "%", 0, 100);

        masterStack.Children.Add(inGainSlider);
        masterStack.Children.Add(outGainSlider);
        masterStack.Children.Add(mixSlider);
        masterCard.Child = masterStack;
        mainStack.Children.Add(masterCard);

        // Group 2: Dynamic Tailored DSP Parameters Card
        var dspCard = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x18, 0x1B, 0x22)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0x25, 0x2B, 0x36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16)
        };
        var dspStack = new StackPanel { Spacing = 12 };
        dspStack.Children.Add(new TextBlock
        {
            Text = $"{plugin.Name.ToUpperInvariant()} — DSP PARAMETER & KONTROLLEN",
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 11,
            Foreground = plugin.TagBrush
        });

        List<FrameworkElement> dspControls = new();

        if (plugin.Name.Contains("Pro-EQ", StringComparison.OrdinalIgnoreCase))
        {
            dspControls.Add(CreateSliderRow("Low-Cut Filter (HPF)", "20 Hz", "300 Hz", plugin.LowCutFreq, v => plugin.LowCutFreq = v, "Hz", 20, 300));
            dspControls.Add(CreateSliderRow("Bass / Grundton (120 Hz)", "-12 dB", "+12 dB", plugin.LowGain, v => plugin.LowGain = v, "dB", -12, 12));
            dspControls.Add(CreateSliderRow("Mitten-Entmumpfung (450 Hz)", "-12 dB", "+12 dB", plugin.Mid1Gain, v => plugin.Mid1Gain = v, "dB", -12, 12));
            dspControls.Add(CreateSliderRow("Präsenz & Verständlichkeit (2.5 kHz)", "-12 dB", "+12 dB", plugin.Mid2Gain, v => plugin.Mid2Gain = v, "dB", -12, 12));
            dspControls.Add(CreateSliderRow("High-Shelf Glanz (8 kHz)", "-12 dB", "+12 dB", plugin.HighShelfGain, v => plugin.HighShelfGain = v, "dB", -12, 12));
            dspControls.Add(CreateSliderRow("Air-Band Seide (14 kHz)", "-12 dB", "+12 dB", plugin.AirBandGain, v => plugin.AirBandGain = v, "dB", -12, 12));
        }
        else if (plugin.Name.Contains("DeEsser", StringComparison.OrdinalIgnoreCase))
        {
            dspControls.Add(CreateSliderRow("Zischlaut-Zielfrequenz", "3.000 Hz", "9.500 Hz", plugin.DetectionFreq, v => plugin.DetectionFreq = v, "Hz", 3000, 9500));
            dspControls.Add(CreateSliderRow("De-Esser Threshold", "-40 dB", "0 dB", plugin.DeEssThreshold, v => plugin.DeEssThreshold = v, "dB", -40, 0));
            dspControls.Add(CreateSliderRow("Maximale Reduktion (Depth)", "-24 dB", "0 dB", plugin.DeEssDepth, v => plugin.DeEssDepth = v, "dB", -24, 0));
        }
        else if (plugin.Name.Contains("Opto", StringComparison.OrdinalIgnoreCase))
        {
            dspControls.Add(CreateSliderRow("Peak Reduction", "0 %", "100 %", plugin.OptoPeakReduction, v => plugin.OptoPeakReduction = v, "%", 0, 100));
            dspControls.Add(CreateSliderRow("Opto Makeup Gain", "0 dB", "24 dB", plugin.OptoGain, v => plugin.OptoGain = v, "dB", 0, 24));
            dspControls.Add(CreateSliderRow("Optische Röhrenwärme", "0 %", "100 %", plugin.OptoWarmth, v => plugin.OptoWarmth = v, "%", 0, 100));
        }
        else if (plugin.Name.Contains("Tube", StringComparison.OrdinalIgnoreCase) || plugin.Name.Contains("Sat", StringComparison.OrdinalIgnoreCase))
        {
            dspControls.Add(CreateSliderRow("Sättigungs-Intensität (Drive)", "0 %", "100 %", plugin.SatDrive, v => plugin.SatDrive = v, "%", 0, 100));
            dspControls.Add(CreateSliderRow("Headroom Trim", "-6 dB", "+6 dB", plugin.SatHeadroom, v => plugin.SatHeadroom = v, "dB", -6, 6));
        }
        else if (plugin.Name.Contains("Expander", StringComparison.OrdinalIgnoreCase))
        {
            dspControls.Add(CreateSliderRow("Expander Threshold", "-60 dB", "0 dB", plugin.ExpanderThreshold, v => plugin.ExpanderThreshold = v, "dB", -60, 0));
            dspControls.Add(CreateSliderRow("Expander Ratio", "1.2:1", "8.0:1", plugin.ExpanderRatio, v => plugin.ExpanderRatio = v, ":1", 1.2, 8.0));
            dspControls.Add(CreateSliderRow("Attack Einschwingzeit", "0.5 ms", "50 ms", plugin.ExpanderAttack, v => plugin.ExpanderAttack = v, "ms", 0.5, 50));
            dspControls.Add(CreateSliderRow("Release Rückstellzeit", "20 ms", "800 ms", plugin.ExpanderRelease, v => plugin.ExpanderRelease = v, "ms", 20, 800));
            dspControls.Add(CreateSliderRow("Hysterese-Puffer", "1 dB", "12 dB", plugin.ExpanderHysteresis, v => plugin.ExpanderHysteresis = v, "dB", 1, 12));
        }
        else if (plugin.Name.Contains("Limiter", StringComparison.OrdinalIgnoreCase) || plugin.Name.Contains("Master", StringComparison.OrdinalIgnoreCase))
        {
            dspControls.Add(CreateSliderRow("True-Peak Ceiling", "-3.0 dBTP", "0.0 dBTP", plugin.LimiterCeiling, v => plugin.LimiterCeiling = v, "dBTP", -3.0, 0.0));
            dspControls.Add(CreateSliderRow("Lookahead Puffer", "0.5 ms", "5.0 ms", plugin.LimiterLookahead, v => plugin.LimiterLookahead = v, "ms", 0.5, 5.0));
            dspControls.Add(CreateSliderRow("Release Zeit", "10 ms", "500 ms", plugin.LimiterRelease, v => plugin.LimiterRelease = v, "ms", 10, 500));
            dspControls.Add(CreateSliderRow("Ziel-Lautheit (Target LUFS)", "-24 LUFS", "-9 LUFS", plugin.TargetLufs, v => plugin.TargetLufs = v, "LUFS", -24, -9));
        }
        else
        {
            dspControls.Add(CreateSliderRow("Threshold / Einsatzpunkt", "-60 dB", "0 dB", plugin.Threshold, v => plugin.Threshold = v, "dB", -60, 0));
            dspControls.Add(CreateSliderRow("Trennfrequenz / Low-Cut", "20 Hz", "20.000 Hz", plugin.Frequency, v => plugin.Frequency = v, "Hz", 20, 20000));
            dspControls.Add(CreateSliderRow("Gütefaktor / Q-Breite", "0.1", "10.0", plugin.QFactor, v => plugin.QFactor = v, "Q", 0.1, 10.0));
            dspControls.Add(CreateSliderRow("Kompressor Ratio", "1.0:1", "20.0:1", plugin.Ratio, v => plugin.Ratio = v, ":1", 1.0, 20.0));
            dspControls.Add(CreateSliderRow("Attack / Einschwingzeit", "0.1 ms", "200 ms", plugin.Attack, v => plugin.Attack = v, "ms", 0.1, 200));
            dspControls.Add(CreateSliderRow("Release / Rückstellzeit", "10 ms", "2000 ms", plugin.Release, v => plugin.Release = v, "ms", 10, 2000));
            dspControls.Add(CreateSliderRow("Harmonische Sättigung / Drive", "0 %", "100 %", plugin.Drive, v => plugin.Drive = v, "%", 0, 100));
        }

        foreach (var c in dspControls) dspStack.Children.Add(c);
        dspCard.Child = dspStack;
        mainStack.Children.Add(dspCard);

        scroll.Content = mainStack;
        Grid.SetRow(scroll, 2);
        rootGrid.Children.Add(scroll);

        // Preset selector handler
        presetCombo.SelectionChanged += (_, _) =>
        {
            if (presetCombo.SelectedItem is string p)
            {
                plugin.ApplyPreset(p);
                UpdateSliderRow(inGainSlider, plugin.InputGainDb, "dB");
                UpdateSliderRow(outGainSlider, plugin.OutputGainDb, "dB");
                UpdateSliderRow(mixSlider, plugin.MixPercent, "%");
                // Refresh all DSP sliders
                editorWin.Close();
                OpenEditor(parentWindow, plugin);
            }
        };

        // 4. Footer Actions (Import, Export, Save, Close)
        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var exportPresetBtn = new Button
        {
            Style = Application.Current?.Resources["SmoothButton"] as Style,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new FontIcon { Glyph = "\uE896", FontSize = 14 }, new TextBlock { Text = "Preset exportieren..." } }
            }
        };
        exportPresetBtn.Click += async (_, _) =>
        {
            try
            {
                var safeName = string.Join("_", plugin.Name.Split(Path.GetInvalidFileNameChars()));
                var picker = new Windows.Storage.Pickers.FileSavePicker
                {
                    SuggestedFileName = $"{safeName}_{plugin.PresetName}.json"
                };
                picker.FileTypeChoices.Add("Plugin Preset (*.json)", [".json"]);
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(editorWin);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    var json = JsonSerializer.Serialize(plugin, new JsonSerializerOptions { WriteIndented = true });
                    await Windows.Storage.FileIO.WriteTextAsync(file, json);
                    exportPresetBtn.Content = "Exportiert!";
                }
            }
            catch { }
        };

        var importPresetBtn = new Button
        {
            Style = Application.Current?.Resources["SmoothButton"] as Style,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new FontIcon { Glyph = "\uE898", FontSize = 14 }, new TextBlock { Text = "Preset laden..." } }
            }
        };
        importPresetBtn.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.FileTypeFilter.Add(".json");
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(editorWin);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    var text = await Windows.Storage.FileIO.ReadTextAsync(file);
                    var imported = JsonSerializer.Deserialize<VstPluginItem>(text);
                    if (imported != null)
                    {
                        plugin.InputGainDb = imported.InputGainDb;
                        plugin.OutputGainDb = imported.OutputGainDb;
                        plugin.MixPercent = imported.MixPercent;
                        plugin.LowCutFreq = imported.LowCutFreq;
                        plugin.LowGain = imported.LowGain;
                        plugin.Mid1Gain = imported.Mid1Gain;
                        plugin.Mid2Gain = imported.Mid2Gain;
                        plugin.HighShelfGain = imported.HighShelfGain;
                        plugin.AirBandGain = imported.AirBandGain;
                        plugin.DetectionFreq = imported.DetectionFreq;
                        plugin.DeEssThreshold = imported.DeEssThreshold;
                        plugin.DeEssDepth = imported.DeEssDepth;
                        plugin.OptoPeakReduction = imported.OptoPeakReduction;
                        plugin.OptoGain = imported.OptoGain;
                        plugin.OptoWarmth = imported.OptoWarmth;
                        plugin.SatDrive = imported.SatDrive;
                        plugin.SatHarmonics = imported.SatHarmonics;
                        plugin.SatTapeSpeed = imported.SatTapeSpeed;
                        plugin.ExpanderThreshold = imported.ExpanderThreshold;
                        plugin.ExpanderRatio = imported.ExpanderRatio;
                        plugin.ExpanderAttack = imported.ExpanderAttack;
                        plugin.ExpanderRelease = imported.ExpanderRelease;
                        plugin.LimiterCeiling = imported.LimiterCeiling;
                        plugin.LimiterLookahead = imported.LimiterLookahead;
                        plugin.LimiterRelease = imported.LimiterRelease;
                        plugin.TargetLufs = imported.TargetLufs;
                        plugin.PresetName = imported.PresetName;

                        Instance.SaveRack();
                        SyncToHardware(plugin);
                        editorWin.Close();
                        OpenEditor(parentWindow, plugin);
                    }
                }
            }
            catch { }
        };

        leftBtns.Children.Add(exportPresetBtn);
        leftBtns.Children.Add(importPresetBtn);
        Grid.SetColumn(leftBtns, 0);
        footer.Children.Add(leftBtns);

        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var saveBtn = new Button
        {
            Content = "Speichern & Anwenden",
            Style = Application.Current?.Resources["AccentButtonStyle"] as Style
        };
        saveBtn.Click += (_, _) =>
        {
            Instance.SaveRack();
            SyncToHardware(plugin);
            editorWin.Close();
        };

        var closeBtn = new Button
        {
            Content = "Schließen",
            Style = Application.Current?.Resources["SmoothButton"] as Style
        };
        closeBtn.Click += (_, _) => editorWin.Close();

        rightBtns.Children.Add(saveBtn);
        rightBtns.Children.Add(closeBtn);
        Grid.SetColumn(rightBtns, 1);
        footer.Children.Add(rightBtns);

        Grid.SetRow(footer, 3);
        rootGrid.Children.Add(footer);

        editorWin.Content = rootGrid;
        editorWin.Activate();
    }

    private static FrameworkElement CreateSliderRow(
        string title,
        string minLabel,
        string maxLabel,
        double initialValue,
        Action<double> onValueChanged,
        string unit,
        double min = -24,
        double max = 24)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x14, 0x16, 0x1C)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var stack = new StackPanel { Spacing = 4 };
        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 };
        var valueBlock = new TextBlock
        {
            Text = $"{initialValue:+0.0;-0.0;0.0} {unit}",
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x1F, 0xC7, 0x9A))
        };
        Grid.SetColumn(titleBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        topRow.Children.Add(titleBlock);
        topRow.Children.Add(valueBlock);
        stack.Children.Add(topRow);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            StepFrequency = (max - min) > 100 ? 5 : 0.5,
            Value = initialValue,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 0)
        };
        slider.ValueChanged += (_, e) =>
        {
            onValueChanged(e.NewValue);
            valueBlock.Text = $"{e.NewValue:+0.0;-0.0;0.0} {unit}";
            Instance.SaveRack();
        };
        stack.Children.Add(slider);

        card.Child = stack;
        card.Tag = (slider, valueBlock);
        return card;
    }

    private static void UpdateSliderRow(FrameworkElement card, double newValue, string unit)
    {
        if (card.Tag is ValueTuple<Slider, TextBlock> tuple)
        {
            tuple.Item1.Value = newValue;
            tuple.Item2.Text = $"{newValue:+0.0;-0.0;0.0} {unit}";
        }
    }
}
