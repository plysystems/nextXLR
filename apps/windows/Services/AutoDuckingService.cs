using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.ViewModels;

namespace NextXLR.Services;

/// <summary>
/// Auto-Ducking engine (Sidechain speech detection).
/// Automatically lowers background music/game volume when speaking into the microphone or during chat.
/// </summary>
public sealed partial class AutoDuckingService : ObservableObject
{
    private static readonly Lazy<AutoDuckingService> _instance = new(() =>
    {
        var svc = new AutoDuckingService();
        svc.LoadSettings();
        return svc;
    });
    public static AutoDuckingService Instance => _instance.Value;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _triggerChannel = "Mic";
    [ObservableProperty] private string _targetChannel = "Music";
    [ObservableProperty] private double _thresholdDb = -32.0;   // -50..-15 dB
    [ObservableProperty] private double _duckAmountDb = -12.0;  // -30..-6 dB
    [ObservableProperty] private double _holdMs = 600.0;        // 100..2000 ms
    [ObservableProperty] private double _releaseMs = 500.0;     // 100..1500 ms

    [ObservableProperty] private bool _isDuckingActive;
    [ObservableProperty] private double _currentAttenuationDb;
    [ObservableProperty] private double _duckReductionPercent;
    [ObservableProperty] private string _statusText = "Inaktiv (Bypass)";

    public IReadOnlyList<string> AvailableTriggerChannels { get; } = new[] { "Mic", "Chat", "Line In", "Console" };
    public IReadOnlyList<string> AvailableTargetChannels { get; } = new[] { "Music", "Game", "System", "Chat", "Sample" };

    private double _holdRemainingMs;
    private double _lastNotifiedAttenuation;

    public string ThresholdLabel => $"{Math.Round(ThresholdDb)} dB";
    public string DuckAmountLabel => $"{Math.Round(DuckAmountDb)} dB";
    public string HoldLabel => $"{Math.Round(HoldMs)} ms";
    public string ReleaseLabel => $"{Math.Round(ReleaseMs)} ms";

    private AutoDuckingService() { }

    public void LoadSettings()
    {
        try
        {
            var s = AppSettings.Load();
            IsEnabled = s.AutoDuckingEnabled;
            TriggerChannel = string.IsNullOrWhiteSpace(s.AutoDuckingTrigger) ? "Mic" : s.AutoDuckingTrigger;
            TargetChannel = string.IsNullOrWhiteSpace(s.AutoDuckingTarget) ? "Music" : s.AutoDuckingTarget;
            ThresholdDb = s.AutoDuckingThresholdDb != 0 ? s.AutoDuckingThresholdDb : -32.0;
            DuckAmountDb = s.AutoDuckingDuckAmountDb != 0 ? s.AutoDuckingDuckAmountDb : -12.0;
            HoldMs = s.AutoDuckingHoldMs != 0 ? s.AutoDuckingHoldMs : 600.0;
            ReleaseMs = s.AutoDuckingReleaseMs != 0 ? s.AutoDuckingReleaseMs : 500.0;
        }
        catch { }
    }

    public void SaveSettings()
    {
        try
        {
            var s = AppSettings.Load();
            s.AutoDuckingEnabled = IsEnabled;
            s.AutoDuckingTrigger = TriggerChannel;
            s.AutoDuckingTarget = TargetChannel;
            s.AutoDuckingThresholdDb = ThresholdDb;
            s.AutoDuckingDuckAmountDb = DuckAmountDb;
            s.AutoDuckingHoldMs = HoldMs;
            s.AutoDuckingReleaseMs = ReleaseMs;
            s.Save();
        }
        catch { }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!value)
        {
            IsDuckingActive = false;
            CurrentAttenuationDb = 0;
            DuckReductionPercent = 0;
            StatusText = "Inaktiv (Bypass)";
        }
        else
        {
            StatusText = "Bereit (Wartet auf Sprache)";
        }
        SaveSettings();
    }

    partial void OnTriggerChannelChanged(string value) => SaveSettings();
    partial void OnTargetChannelChanged(string value) => SaveSettings();
    partial void OnThresholdDbChanged(double value) { OnPropertyChanged(nameof(ThresholdLabel)); SaveSettings(); }
    partial void OnDuckAmountDbChanged(double value) { OnPropertyChanged(nameof(DuckAmountLabel)); SaveSettings(); }
    partial void OnHoldMsChanged(double value) { OnPropertyChanged(nameof(HoldLabel)); SaveSettings(); }
    partial void OnReleaseMsChanged(double value) { OnPropertyChanged(nameof(ReleaseLabel)); SaveSettings(); }

    /// <summary>Called per frame from the UI/meter loop to update sidechain state.</summary>
    public void Tick(double dtMs, MainViewModel mainVm)
    {
        if (!IsEnabled || mainVm == null)
        {
            if (_lastNotifiedAttenuation < -0.05)
            {
                _lastNotifiedAttenuation = 0;
                var target = mainVm?.Channels.FirstOrDefault(c => c.Name.Equals(TargetChannel, StringComparison.OrdinalIgnoreCase));
                target?.UpdateRouterLevels();
            }
            return;
        }

        // Find trigger channel (Mic or Chat)
        var trigger = mainVm.Channels.FirstOrDefault(c => c.Name.Equals(TriggerChannel, StringComparison.OrdinalIgnoreCase))
                      ?? mainVm.MicChannel;

        double meter = trigger?.Meter ?? 0.0;
        double inputDb = meter > 0.0005 ? 20.0 * Math.Log10(meter) : -90.0;

        bool speaking = inputDb > ThresholdDb;

        if (speaking)
        {
            _holdRemainingMs = HoldMs;
            // Smooth attack ramp
            double attackStep = (Math.Abs(DuckAmountDb) / Math.Max(10.0, 30.0)) * dtMs;
            CurrentAttenuationDb = Math.Max(DuckAmountDb, CurrentAttenuationDb - attackStep);
            IsDuckingActive = true;
            StatusText = $"Aktiv: {TargetChannel} gedämpft um {Math.Abs(CurrentAttenuationDb):0.0} dB";
        }
        else if (_holdRemainingMs > 0)
        {
            _holdRemainingMs -= dtMs;
            StatusText = $"Hold: Dämpfung wird gehalten ({Math.Round(_holdRemainingMs)} ms)";
        }
        else if (CurrentAttenuationDb < -0.1)
        {
            // Smooth release ramp back to 0 dB
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
            CurrentAttenuationDb = 0;
            IsDuckingActive = false;
            StatusText = "Bereit (Wartet auf Sprache)";
        }

        // Calculate 0..100% reduction meter for UI bar
        double maxDuck = Math.Abs(DuckAmountDb);
        DuckReductionPercent = maxDuck > 0.01 ? Math.Clamp((Math.Abs(CurrentAttenuationDb) / maxDuck) * 100.0, 0.0, 100.0) : 0;

        // Apply audio attenuation when it shifts
        if (Math.Abs(CurrentAttenuationDb - _lastNotifiedAttenuation) > 0.1 || (CurrentAttenuationDb == 0 && _lastNotifiedAttenuation != 0))
        {
            _lastNotifiedAttenuation = CurrentAttenuationDb;
            var target = mainVm.Channels.FirstOrDefault(c => c.Name.Equals(TargetChannel, StringComparison.OrdinalIgnoreCase));
            target?.UpdateRouterLevels();
        }
    }
}
