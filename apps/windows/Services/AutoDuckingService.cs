using System;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.ViewModels;

namespace NextXLR.Services;

/// <summary>
/// Intelligent auto-ducking engine (sidechain speech detection).
/// Automatically lowers background music/game volume when speaking into the microphone or during chat.
/// </summary>
public sealed partial class AutoDuckingService : ObservableObject
{
    private static readonly Lazy<AutoDuckingService> _instance = new(() => new AutoDuckingService());
    public static AutoDuckingService Instance => _instance.Value;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _triggerChannel = "Mic";
    [ObservableProperty] private string _targetChannel = "Music";
    [ObservableProperty] private double _thresholdDb = -32.0;   // -50..-10 dB
    [ObservableProperty] private double _duckAmountDb = -12.0;  // -30..-6 dB
    [ObservableProperty] private double _holdMs = 600.0;        // 100..2000 ms
    [ObservableProperty] private double _releaseMs = 500.0;     // 100..2000 ms

    [ObservableProperty] private bool _isDuckingActive;
    [ObservableProperty] private double _currentAttenuationDb;
    [ObservableProperty] private string _statusText = "Inaktiv (Bypass)";

    private double _holdRemainingMs;

    public string ThresholdLabel => $"{Math.Round(ThresholdDb)} dB";
    public string DuckAmountLabel => $"{Math.Round(DuckAmountDb)} dB";
    public string HoldLabel => $"{Math.Round(HoldMs)} ms";

    private AutoDuckingService() { }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!value)
        {
            IsDuckingActive = false;
            CurrentAttenuationDb = 0;
            StatusText = "Inaktiv (Bypass)";
        }
        else
        {
            StatusText = "Bereit (Wartet auf Sprache)";
        }
    }

    partial void OnThresholdDbChanged(double value) => OnPropertyChanged(nameof(ThresholdLabel));
    partial void OnDuckAmountDbChanged(double value) => OnPropertyChanged(nameof(DuckAmountLabel));
    partial void OnHoldMsChanged(double value) => OnPropertyChanged(nameof(HoldLabel));

    /// <summary>Called per frame from the UI/meter loop to update sidechain state.</summary>
    public void Tick(double dtMs, MainViewModel mainVm)
    {
        if (!IsEnabled || mainVm == null) return;

        // Find trigger channel (Mic or Chat)
        var trigger = mainVm.Channels.FirstOrDefault(c => c.Name.Equals(TriggerChannel, StringComparison.OrdinalIgnoreCase))
                      ?? mainVm.MicChannel;

        double meter = trigger?.Meter ?? 0.0;
        double inputDb = meter > 0.0005 ? 20.0 * Math.Log10(meter) : -90.0;

        bool speaking = inputDb > ThresholdDb;

        if (speaking)
        {
            _holdRemainingMs = HoldMs;
            // Ramp in smoothly
            double attackStep = (Math.Abs(DuckAmountDb) / Math.Max(10.0, 30.0)) * dtMs;
            CurrentAttenuationDb = Math.Max(DuckAmountDb, CurrentAttenuationDb - attackStep);
            IsDuckingActive = true;
            StatusText = $"Aktiv: Musik gedämpft um {CurrentAttenuationDb:0.0} dB";
        }
        else if (_holdRemainingMs > 0)
        {
            _holdRemainingMs -= dtMs;
            StatusText = $"Hold: Dämpfung wird gehalten ({Math.Round(_holdRemainingMs)} ms)";
        }
        else if (CurrentAttenuationDb < -0.1)
        {
            // Release smoothly back to 0 dB
            double releaseStep = (Math.Abs(DuckAmountDb) / Math.Max(10.0, ReleaseMs)) * dtMs;
            CurrentAttenuationDb = Math.Min(0.0, CurrentAttenuationDb + releaseStep);
            if (CurrentAttenuationDb >= -0.1)
            {
                CurrentAttenuationDb = 0;
                IsDuckingActive = false;
                StatusText = "Bereit (Wartet auf Sprache)";
            }
        }
        else
        {
            IsDuckingActive = false;
            StatusText = "Bereit (Wartet auf Sprache)";
        }
    }
}

