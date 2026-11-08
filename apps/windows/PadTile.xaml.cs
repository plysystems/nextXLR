using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NextXLR.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.UI;

namespace NextXLR;

/// <summary>
/// A sampler pad: press/release play it by its mode (hold-to-play needs the release), the gear
/// or edit button opens its settings, and samples can be dropped on it from the browser or from Explorer.
/// </summary>
public sealed partial class PadTile : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(PadViewModel), typeof(PadTile),
        new PropertyMetadata(null, (d, _) => ((PadTile)d).Bindings.Update()));

    public PadViewModel ViewModel
    {
        get => (PadViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Right-hand column: content aligns right, gear on the right.</summary>
    public bool Mirror { get; set; }
    public HorizontalAlignment Side => Mirror ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public int GearColumn => Mirror ? 2 : 0;

    private bool _down;

    public PadTile()
    {
        InitializeComponent();
        // handledEventsToo: the inner settings/edit buttons must not swallow pad presses elsewhere.
        Body.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        Body.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        Body.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnReleased), true);
        Body.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnReleased), true);
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        KeyDown += (_, e) => { if (e.Key is Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter && !e.KeyStatus.WasKeyDown) Down(); };
        KeyUp += (_, e) => { if (e.Key is Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter) Up(); };
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsInside(e.OriginalSource as DependencyObject, SettingsButton) ||
            IsInside(e.OriginalSource as DependencyObject, EditPadButton))
            return;
        Body.CapturePointer(e.Pointer);
        Down();
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e) => Up();

    private void Down()
    {
        if (_down || ViewModel is null) return;
        _down = true;
        ViewModel.Owner.Press(ViewModel);
    }

    private void Up()
    {
        if (!_down || ViewModel is null) return;
        _down = false;
        ViewModel.Owner.Release(ViewModel);
    }

    private static bool IsInside(DependencyObject? o, DependencyObject container)
    {
        for (; o is not null; o = VisualTreeHelper.GetParent(o)) if (o == container) return true;
        return false;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.Owner.SelectedPad = ViewModel.Owner.SelectedPad == ViewModel ? null : ViewModel;
    }

    private void EditPad_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.Owner.SelectedPad = ViewModel;
        }
    }

    private async void ReplaceSample_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        foreach (var ext in Services.SamplerStore.Extensions) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.Owner.ImportAsync([file.Path], assignTo: ViewModel);
        }
    }

    private void ClearPad_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.Owner.ClearArmed = true;
        ViewModel.Owner.Press(ViewModel);
    }

    // ---- drag & drop: "sample:<id>" from the browser, or audio files from Explorer ----

    private void Pad_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = ViewModel.Label;
        }
    }

    private async void Pad_Drop(object sender, DragEventArgs e)
    {
        var owner = ViewModel.Owner;
        if (e.DataView.Contains(StandardDataFormats.Text)
            && await e.DataView.GetTextAsync() is { } text && text.StartsWith("sample:")
            && owner.Store.Find(text[7..]) is { } sample)
        {
            owner.Assign(sample, owner.ActivePage, ViewModel.Index);
        }
        else if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            await owner.ImportAsync(items.Select(i => i.Path), assignTo: ViewModel);
        }
    }

    // ---- x:Bind helpers ----
    private static readonly SolidColorBrush Empty = new(Color.FromArgb(255, 0x1A, 0x1E, 0x25));
    private static readonly SolidColorBrush LoadedBrush = new(Color.FromArgb(255, 0x1D, 0x2B, 0x29));
    private static readonly SolidColorBrush Playing = new(Color.FromArgb(255, 0x17, 0x5C, 0x4B));
    private static readonly SolidColorBrush Idle = new(Color.FromArgb(255, 0x2C, 0x31, 0x3B));
    private static readonly SolidColorBrush Brand = new(Color.FromArgb(255, 0x1F, 0xC7, 0x9A));
    private static readonly SolidColorBrush Select = new(Color.FromArgb(255, 0xF2, 0xF4, 0xF7));

    public Brush PadBackground(bool playing, bool hasSample) => playing ? Playing : hasSample ? LoadedBrush : Empty;
    public Brush PadBorder(bool selected, bool playing) => selected ? Select : playing ? Brand : Idle;
}
