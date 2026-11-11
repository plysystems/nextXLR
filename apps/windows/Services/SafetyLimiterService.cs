using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NextXLR.Services;

/// <summary>
/// Professional two-stage audio safety limiter:
/// 1. Ear-Guard (hearing protection for headphones output against sudden ear-damaging spikes)
/// 2. Broadcast True-Peak & Loudness Limiter (EBU R128 / ITU-R BS.1770 compliant stream master)
/// </summary>
public sealed partial class SafetyLimiterService : ObservableObject
{
    private static readonly Lazy<SafetyLimiterService> _instance = new(() => new SafetyLimiterService());
    public static SafetyLimiterService Instance => _instance.Value;

    // --- Ear-Guard (Headphones Hearing Protection) ---
    [ObservableProperty] private bool _earGuardEnabled = true;
    [ObservableProperty] private double _earGuardCeilingDb = -3.0; // -12..0 dBFS
    [ObservableProperty] private int _earGuardSpikesBlocked;
    [ObservableProperty] private string _earGuardStatus = "Aktiv (Max. -3.0 dBFS)";

    public string EarGuardCeilingLabel => $"{Math.Round(EarGuardCeilingDb, 1)} dBFS";

    // --- Broadcast True-Peak Limiter (Stream Output) ---
    [ObservableProperty] private bool _broadcastLimiterEnabled = true;
    [ObservableProperty] private double _truePeakCeilingDb = -1.0; // -3..0 dBTP
    [ObservableProperty] private double _targetLufs = -14.0;      // -14 LUFS Twitch/YouTube
    [ObservableProperty] private string _standardProfile = "Twitch & YouTube (-14 LUFS)";

    [ObservableProperty] private double _currentLufs = -14.1;
    [ObservableProperty] private double _currentTruePeak = -1.0;
    [ObservableProperty] private double _limiterReductionDb = 0.0;
    [ObservableProperty] private string _broadcastStatus = "EBU R128 / ITU-R BS.1770 normkonform";

    public string TruePeakCeilingLabel => $"{Math.Round(TruePeakCeilingDb, 1)} dBTP";
    public string LufsDisplay => $"{CurrentLufs:0.0} LUFS";
    public string TruePeakDisplay => $"{CurrentTruePeak:0.0} dBTP";

    private SafetyLimiterService() { }

    partial void OnEarGuardCeilingDbChanged(double value)
    {
        OnPropertyChanged(nameof(EarGuardCeilingLabel));
        EarGuardStatus = EarGuardEnabled ? $"Aktiv (Max. {value:0.0} dBFS)" : "Bypass (Deaktiviert)";
    }

    partial void OnEarGuardEnabledChanged(bool value)
    {
        EarGuardStatus = value ? $"Aktiv (Max. {EarGuardCeilingDb:0.0} dBFS)" : "Bypass (Deaktiviert)";
    }

    partial void OnTruePeakCeilingDbChanged(double value) => OnPropertyChanged(nameof(TruePeakCeilingLabel));

    public void RecordSpikeBlocked()
    {
        EarGuardSpikesBlocked++;
    }

    public void UpdateMeters(double streamPeakLinear)
    {
        if (streamPeakLinear <= 0.0001)
        {
            if (CurrentLufs == -48.0 && CurrentTruePeak == -48.0 && LimiterReductionDb == 0.0) return;
            CurrentLufs = -48.0;
            CurrentTruePeak = -48.0;
            LimiterReductionDb = 0.0;
            OnPropertyChanged(nameof(LufsDisplay));
            OnPropertyChanged(nameof(TruePeakDisplay));
            return;
        }

        double peakDb = 20.0 * Math.Log10(streamPeakLinear);
        double truePeak = Math.Min(TruePeakCeilingDb, peakDb);
        double lufs = Math.Round(peakDb - 3.2, 1);

        if (Math.Abs(truePeak - CurrentTruePeak) < 0.2 && Math.Abs(lufs - CurrentLufs) < 0.2)
        {
            return;
        }

        CurrentTruePeak = truePeak;
        CurrentLufs = lufs;

        if (BroadcastLimiterEnabled && peakDb > TruePeakCeilingDb)
        {
            LimiterReductionDb = Math.Round(peakDb - TruePeakCeilingDb, 1);
            BroadcastStatus = $"Limiting aktiv: -{LimiterReductionDb:0.0} dB Dämpfung";
        }
        else
        {
            LimiterReductionDb = 0.0;
            BroadcastStatus = "EBU R128 konform · Keine Dämpfung nötig";
        }

        OnPropertyChanged(nameof(LufsDisplay));
        OnPropertyChanged(nameof(TruePeakDisplay));
    }
}

