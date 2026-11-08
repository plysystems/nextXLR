using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.Protocol;
using NextXLR.Services;

namespace NextXLR.ViewModels;

/// <summary>
/// UI state manager for the attached device. The daemon detects the model from the USB product
/// id (0x8FE0 = GoXLR, 0x8FE4 = GoXLR Mini) and sends its <see cref="Capabilities"/>; every
/// view binds to these properties instead of checking model names. When a Mini is attached,
/// Effects and Sampler disappear, the EQ shows 6 bands and lighting shows the Mini's zones.
/// </summary>
public partial class HardwareState : ObservableObject
{
    private string? _appliedKey;

    [ObservableProperty] private bool _hasDevice;
    [ObservableProperty] private bool _isMini;
    [ObservableProperty] private string _deviceName = Loc.Get("DeviceNone");
    [ObservableProperty] private string _deviceDetail = Loc.Get("DeviceSoftwareMixer");
    [ObservableProperty] private bool _hasEffects;
    [ObservableProperty] private bool _hasSampler;
    [ObservableProperty] private bool _motorizedFaders;

    public ObservableCollection<EqBandViewModel> EqBands { get; } = new();
    /// <summary>Lighting zone ids this device has (drives the Lighting page).</summary>
    public IReadOnlyList<string> LightingZoneIds { get; private set; } = [];

    private static readonly int[] FullEq = [31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public HardwareState() => Apply(null);

    public void Apply(HardwareInfo? hw)
    {
        // Only rebuild collections when the device identity actually changes.
        var key = hw is null ? "none" : $"{hw.Kind}|{hw.Serial}|{hw.Model}";
        if (key == _appliedKey) return;
        _appliedKey = key;

        HasDevice = hw is not null;
        IsMini = hw?.Kind == "mini";
        MainViewModel.IsCurrentMini = IsMini;
        DeviceName = hw?.Model ?? Loc.Get("DeviceNone");
        DeviceDetail = hw is null ? Loc.Get("DeviceSoftwareMixer")
            : hw.Serial is { } s ? Loc.Format("DeviceSerial", s) : Loc.Get("StatusConnected");
        HasEffects = hw?.Capabilities.Effects ?? false;
        HasSampler = hw?.Capabilities.Sampler ?? false;
        MotorizedFaders = hw?.Capabilities.MotorizedFaders ?? false;

        EqBands.Clear();
        foreach (var f in (IEnumerable<int>?)hw?.Capabilities.EqBands ?? FullEq)
            EqBands.Add(new EqBandViewModel(f));

        LightingZoneIds = hw?.Capabilities.LightingZones ?? [];
        OnPropertyChanged(nameof(LightingZoneIds));
    }
}

/// <summary>One EQ band (-9..9 dB). <paramref name="changed"/> runs on user edits only.</summary>
public partial class EqBandViewModel(int frequency, Action? changed = null) : ObservableObject
{
    private bool _silent;
    public int Frequency { get; } = frequency;
    public string Label { get; } = frequency >= 1000 ? $"{frequency / 1000.0:0.#}k" : $"{frequency}";
    [ObservableProperty] private double _gain;
    partial void OnGainChanged(double value)
    {
        OnPropertyChanged(nameof(GainLabel));
        if (!_silent) changed?.Invoke();
    }
    public string GainLabel => $"{Gain:+0;-0;0} dB";

    internal void SetSilently(double gain)
    {
        _silent = true;
        Gain = gain;
        _silent = false;
    }
}

