using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NextXLR.Protocol;
using NextXLR.Services;

namespace NextXLR.ViewModels;

/// <summary>
/// The sampler, laid out like the hardware: pages A/B/C, four pads per page and Clear in the
/// middle. Pads play into the GoXLR's Sample channel (see <see cref="SamplePlayer"/>). The
/// GoXLR's own sampler buttons arrive from the daemon (<see cref="HandleButton"/>) and their LEDs
/// follow this state (<see cref="PushLeds"/>), so the app and the device behave as one.
/// Clear works like on the device: arm it, then press a pad to empty that pad.
/// </summary>
public partial class SamplerViewModel : ObservableObject
{
    private static readonly string[] PadButtons = ["sampler_top_left", "sampler_top_right", "sampler_bottom_left", "sampler_bottom_right"];
    private static readonly string[] PageButtons = ["sampler_select_a", "sampler_select_b", "sampler_select_c"];

    private readonly PipeTransport _transport;
    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _cursorTimer;
    private readonly SamplePlayer[,] _voices = new SamplePlayer[SamplerStore.Pages, SamplerStore.PadsPerPage];
    private readonly SamplePlayer _preview = new(toSampleChannel: false);

    public SamplerStore Store { get; } = SamplerStore.Load();

    public ObservableCollection<PageViewModel> Pages { get; }
    public ObservableCollection<PadViewModel> Pads { get; }
    public ObservableCollection<SampleItemViewModel> Library { get; } = new();

    [ObservableProperty] private int _activePage;
    [ObservableProperty] private bool _clearArmed;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private PadViewModel? _selectedPad;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _isImporting;

    public IEnumerable<SampleItemViewModel> FilteredLibrary =>
        string.IsNullOrWhiteSpace(Search) ? Library : Library.Where(l => l.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase));
    public bool LibraryEmpty => Library.Count == 0;

    public SamplerViewModel(PipeTransport transport, DispatcherQueue ui)
    {
        _transport = transport;
        _ui = ui;
        _saveTimer = ui.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => Store.Save();
        _cursorTimer = ui.CreateTimer();
        _cursorTimer.Interval = TimeSpan.FromMilliseconds(33);
        _cursorTimer.Tick += (_, _) => SelectedPad?.UpdateCursor();

        Pages = new(new[] { "A", "B", "C" }.Select((l, i) => new PageViewModel(i, l, this)));
        Pads = new(Enumerable.Range(0, SamplerStore.PadsPerPage).Select(i => new PadViewModel(i, this)));
        for (int p = 0; p < SamplerStore.Pages; p++)
            for (int i = 0; i < SamplerStore.PadsPerPage; i++)
            {
                var (page, pad) = (p, i);
                var voice = _voices[p, i] = new SamplePlayer(toSampleChannel: true);
                voice.PlayingChanged += playing => _ui.TryEnqueue(() => OnVoiceChanged(page, pad, playing));
            }
        foreach (var e in Store.Library) Library.Add(new SampleItemViewModel(e, this));
        Library.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(FilteredLibrary)); OnPropertyChanged(nameof(LibraryEmpty)); };
        _preview.PlayingChanged += playing => _ui.TryEnqueue(() => { if (!playing) foreach (var l in Library) l.IsPreviewing = false; });

        Pages[0].SetActiveSilently(true);
        Refresh();
    }

    partial void OnSearchChanged(string value) => OnPropertyChanged(nameof(FilteredLibrary));

    internal PadConfig Config(int page, int pad) => Store.Pads[page][pad];
    internal SamplePlayer Voice(int page, int pad) => _voices[page, pad];
    internal void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }
    public string PageLetter => Pages[ActivePage].Letter;

    // ---------------- pages ----------------

    internal void SelectPage(PageViewModel page)
    {
        foreach (var p in Pages) p.SetActiveSilently(p == page);
        ActivePage = page.Index;
        ClearArmed = false;
        Refresh();
    }

    // ---------------- pad presses ----------------

    /// <summary>Pad pressed (app or hardware): clear if armed, else play by the pad's mode.</summary>
    public void Press(PadViewModel pad)
    {
        var cfg = pad.Config;
        if (ClearArmed)
        {
            Voice(ActivePage, pad.Index).Stop();
            cfg.SampleId = null;
            cfg.Start = cfg.End = 0;
            ClearArmed = false;
            Status = Loc.Format("SamplerCleared", PageLetter, pad.Number);
            ScheduleSave();
            Refresh();
            return;
        }
        if (Store.Find(cfg.SampleId) is not { } sample)
        {
            SelectedPad = pad;
            Status = Loc.Format("SamplerPadEmpty", pad.Number);
            return;
        }
        var voice = Voice(ActivePage, pad.Index);
        if (cfg.Mode == PadMode.PlayStop && voice.IsPlaying) { voice.Stop(); return; }
        voice.Play(sample.File, cfg.Start, cfg.End);
        Status = "";
    }

    /// <summary>Pad released: only "hold to play" reacts.</summary>
    public void Release(PadViewModel pad)
    {
        if (pad.Config.Mode == PadMode.Hold) Voice(ActivePage, pad.Index).Stop();
    }

    public void StopAll()
    {
        foreach (var v in _voices) v.Stop();
        _preview.Stop();
    }

    /// <summary>The GoXLR's sampler buttons (forwarded by the daemon).</summary>
    public void HandleButton(string id, bool pressed)
    {
        if (Array.IndexOf(PadButtons, id) is var pad and >= 0)
        {
            if (pressed) Press(Pads[pad]); else Release(Pads[pad]);
        }
        else if (pressed && Array.IndexOf(PageButtons, id) is var page and >= 0)
        {
            if (page == 0 && ActivePage == 0)
            {
                // Sampler Bank A is already active and pressed again: Toggle Mixer Page on GoXLR!
                MainViewModel.Instance?.ToggleMixerPage();
            }
            SelectPage(Pages[page]);
        }
        else if (pressed && id == "sampler_clear") ClearArmed = !ClearArmed;
    }

    partial void OnClearArmedChanged(bool value)
    {
        if (value) Status = Loc.Get("SamplerClearArmed");
        PushLeds();
    }

    private void OnVoiceChanged(int page, int pad, bool playing)
    {
        if (page == ActivePage) Pads[pad].IsPlaying = playing;
        UpdateCursorTimer();
    }

    private void UpdateCursorTimer()
    {
        var any = SelectedPad is { } p && Voice(ActivePage, p.Index).IsPlaying;
        if (any) _cursorTimer.Start(); else { _cursorTimer.Stop(); SelectedPad?.UpdateCursor(); }
    }

    partial void OnSelectedPadChanged(PadViewModel? oldValue, PadViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) { newValue.IsSelected = true; newValue.Refresh(); }
        UpdateCursorTimer();
    }

    // ---------------- library ----------------

    public async Task ImportAsync(IEnumerable<string> paths, PadViewModel? assignTo = null)
    {
        IsImporting = true;
        var failed = new List<string>();
        SampleEntry? first = null;
        try
        {
            foreach (var path in paths.Where(p => SamplerStore.Extensions.Contains(Path.GetExtension(p).ToLowerInvariant())))
            {
                try
                {
                    var e = await Store.ImportAsync(path);
                    first ??= e;
                    Library.Insert(0, new SampleItemViewModel(e, this));
                }
                catch { failed.Add(Path.GetFileName(path)); }
            }
        }
        finally { IsImporting = false; }
        if (assignTo is not null && first is not null) Assign(first, ActivePage, assignTo.Index);
        Status = failed.Count > 0 ? Loc.Format("SamplerImportFailed", string.Join(", ", failed)) : "";
    }

    public void Assign(SampleEntry sample, int page, int pad)
    {
        Voice(page, pad).Stop();
        var cfg = Config(page, pad);
        cfg.SampleId = sample.Id;
        cfg.Start = 0;
        cfg.End = 0;
        Store.Save();
        Status = Loc.Format("SamplerAssigned", sample.Name, Pages[page].Letter, pad + 1);
        Refresh();
    }

    public void TogglePreview(SampleItemViewModel item)
    {
        var wasPlaying = item.IsPreviewing;
        _preview.Stop();
        foreach (var l in Library) l.IsPreviewing = false;
        if (wasPlaying) return;
        _preview.Play(item.Entry.File);
        item.IsPreviewing = true;
    }

    public void Delete(SampleItemViewModel item)
    {
        if (item.IsPreviewing) _preview.Stop();
        foreach (var v in _voices) v.Stop();
        Store.Remove(item.Entry);
        Library.Remove(item);
        Refresh();
    }

    // ---------------- view refresh + LEDs ----------------

    internal void Refresh()
    {
        foreach (var pad in Pads) pad.Refresh();
        foreach (var l in Library) l.RefreshUsage();
        PushLeds();
    }

    /// <summary>Page buttons: active page lit. Pads: lit when loaded. Clear: lit while armed.</summary>
    public void PushLeds()
    {
        var leds = new List<ButtonLedState>();
        for (int i = 0; i < PageButtons.Length; i++) leds.Add(new(PageButtons[i], i == ActivePage));
        for (int i = 0; i < PadButtons.Length; i++) leds.Add(new(PadButtons[i], Pads[i].HasSample));
        leds.Add(new("sampler_clear", ClearArmed));
        _transport.Send(new SetButtonLeds(leds));
    }
}

public partial class PageViewModel(int index, string letter, SamplerViewModel owner) : ObservableObject
{
    private bool _silent;
    public int Index { get; } = index;
    public string Letter { get; } = letter;
    public string Label => Loc.Format("SamplerPage", Letter);

    [ObservableProperty] private bool _isActive;

    partial void OnIsActiveChanged(bool value)
    {
        if (_silent) return;
        if (!value) { SetActiveSilently(true); return; }   // one page is always active
        owner.SelectPage(this);
    }

    internal void SetActiveSilently(bool active)
    {
        _silent = true;
        IsActive = active;
        _silent = false;
    }
}

/// <summary>A pad of the active page, plus its settings (mode, trim) when selected.</summary>
public partial class PadViewModel(int index, SamplerViewModel owner) : ObservableObject
{
    private bool _loading;

    public int Index { get; } = index;
    public int Number => Index + 1;
    public string Label => Loc.Format("SamplerPlay", Number);
    public SamplerViewModel Owner { get; } = owner;
    internal PadConfig Config => Owner.Config(Owner.ActivePage, Index);

    [ObservableProperty] private bool _hasSample;
    [ObservableProperty] private string _sampleName = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private byte[] _peaks = [];
    [ObservableProperty] private double _duration;
    /// <summary>Trim points, 0..1 of the file.</summary>
    [ObservableProperty] private double _trimStart;
    [ObservableProperty] private double _trimEnd = 1;
    /// <summary>Playback cursor 0..1, or -1 when stopped.</summary>
    [ObservableProperty] private double _cursor = -1;

    public string Title => Loc.Format("SamplerPadTitle", Number, Owner.PageLetter);
    public string ModeGlyph => (PadMode)ModeIndex switch { PadMode.PlayRestart => "", PadMode.Hold => "", _ => "" };
    public string ModeText => Loc.Get($"SamplerMode{(PadMode)ModeIndex}");
    public string StartText => Time(TrimStart * Duration);
    public string EndText => Time(TrimEnd * Duration);
    public string LengthText => Time((TrimEnd - TrimStart) * Duration);

    private static string Time(double s) => TimeSpan.FromSeconds(Math.Max(0, s)).ToString(s >= 60 ? @"m\:ss\.f" : @"s\.ff") + (s >= 60 ? "" : " s");

    internal void Refresh()
    {
        _loading = true;
        var cfg = Config;
        var sample = Owner.Store.Find(cfg.SampleId);
        HasSample = sample is not null;
        SampleName = sample?.Name ?? Loc.Get("SamplerNoSamples");
        Peaks = sample?.Peaks ?? [];
        Duration = sample?.Duration ?? 0;
        ModeIndex = (int)cfg.Mode;
        TrimStart = Duration > 0 ? Math.Clamp(cfg.Start / Duration, 0, 1) : 0;
        TrimEnd = Duration > 0 && cfg.End > 0 ? Math.Clamp(cfg.End / Duration, 0, 1) : 1;
        IsPlaying = Owner.Voice(Owner.ActivePage, Index).IsPlaying;
        OnPropertyChanged(nameof(Title));
        _loading = false;
        UpdateCursor();
    }

    internal void UpdateCursor()
    {
        var voice = Owner.Voice(Owner.ActivePage, Index);
        Cursor = voice.IsPlaying && Duration > 0 ? Math.Clamp(voice.Position / Duration, 0, 1) : -1;
    }

    partial void OnModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ModeGlyph));
        OnPropertyChanged(nameof(ModeText));
        if (_loading) return;
        Config.Mode = (PadMode)value;
        Owner.ScheduleSave();
    }

    partial void OnTrimStartChanged(double value) => TrimChanged();
    partial void OnTrimEndChanged(double value) => TrimChanged();

    private void TrimChanged()
    {
        OnPropertyChanged(nameof(StartText));
        OnPropertyChanged(nameof(EndText));
        OnPropertyChanged(nameof(LengthText));
        if (_loading || Duration <= 0) return;
        Config.Start = Math.Round(TrimStart * Duration, 3);
        Config.End = TrimEnd >= 0.999 ? 0 : Math.Round(TrimEnd * Duration, 3);
        Owner.ScheduleSave();
    }

    public void ResetTrim()
    {
        TrimStart = 0;
        TrimEnd = 1;
    }
}

/// <summary>A file in the sampler browser.</summary>
public partial class SampleItemViewModel(SampleEntry entry, SamplerViewModel owner) : ObservableObject
{
    public SampleEntry Entry { get; } = entry;
    public SamplerViewModel Owner { get; } = owner;
    public string Name => Entry.Name;
    public byte[] Peaks => Entry.Peaks;
    public string Detail => $"{TimeSpan.FromSeconds(Entry.Duration):m\\:ss} · {Path.GetExtension(Entry.File).TrimStart('.').ToUpperInvariant()}";

    [ObservableProperty] private bool _isPreviewing;
    [ObservableProperty] private string _usage = "";

    public string PreviewGlyph => IsPreviewing ? "" : "";
    partial void OnIsPreviewingChanged(bool value) => OnPropertyChanged(nameof(PreviewGlyph));

    /// <summary>"A1 · C3": where this sample is assigned.</summary>
    internal void RefreshUsage()
    {
        var used = new List<string>();
        for (int p = 0; p < SamplerStore.Pages; p++)
            for (int i = 0; i < SamplerStore.PadsPerPage; i++)
                if (Owner.Store.Pads[p][i].SampleId == Entry.Id) used.Add($"{(char)('A' + p)}{i + 1}");
        Usage = string.Join(" · ", used);
    }
}
