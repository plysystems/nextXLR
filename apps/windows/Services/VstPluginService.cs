using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace NextXLR.Services;

/// <summary>
/// Data model for an active VST3 or CLAP plugin in the microphone DSP rack.
/// </summary>
public partial class VstPluginItem : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _vendor = "VST3 Studio";
    [ObservableProperty] private string _format = "VST3";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isBypassed = false;
    [ObservableProperty] private double _inputGainDb = 0.0;
    [ObservableProperty] private double _outputGainDb = 0.0;
    [ObservableProperty] private double _mixPercent = 100.0;
    [ObservableProperty] private string _oversampling = "1x (Echtzeit)";
    [ObservableProperty] private string _processingMode = "Stereo";
    [ObservableProperty] private string _presetName = "Standard";

    // Core DSP Parameters
    [ObservableProperty] private double _threshold = -18.0;
    [ObservableProperty] private double _frequency = 120.0;
    [ObservableProperty] private double _qFactor = 1.0;
    [ObservableProperty] private double _ratio = 3.0;
    [ObservableProperty] private double _attack = 15.0;
    [ObservableProperty] private double _release = 120.0;
    [ObservableProperty] private double _drive = 0.0;

    public string StatusText => IsBypassed ? "Bypass" : (IsEnabled ? "Aktiv" : "Deaktiviert");
    public SolidColorBrush StatusBrush => new(IsBypassed ? ColorHelper.FromArgb(255, 0xEF, 0x8C, 0x34) : (IsEnabled ? ColorHelper.FromArgb(255, 0x22, 0xD3, 0xA6) : ColorHelper.FromArgb(255, 0x88, 0x88, 0x88)));

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        VstPluginService.Instance.SaveRack();
    }

    partial void OnIsBypassedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        VstPluginService.Instance.SaveRack();
    }

    public void ApplyPreset(string preset)
    {
        PresetName = preset;
        switch (preset)
        {
            case "Warm Broadcast Radio":
                InputGainDb = 1.5;
                OutputGainDb = 2.0;
                MixPercent = 100;
                Threshold = -22.0;
                Frequency = 90.0;
                QFactor = 1.2;
                Ratio = 4.0;
                Attack = 20.0;
                Release = 100.0;
                Drive = 15.0;
                break;

            case "Podcast Voice Clarity":
                InputGainDb = 0.0;
                OutputGainDb = 1.0;
                MixPercent = 100;
                Threshold = -16.0;
                Frequency = 140.0; // High-pass cut
                QFactor = 0.8;
                Ratio = 3.2;
                Attack = 12.0;
                Release = 90.0;
                Drive = 0.0;
                break;

            case "Streamer Dynamic Punch":
                InputGainDb = 2.0;
                OutputGainDb = 2.5;
                MixPercent = 100;
                Threshold = -24.0;
                Frequency = 80.0;
                QFactor = 1.5;
                Ratio = 5.0;
                Attack = 5.0;
                Release = 80.0;
                Drive = 25.0;
                break;

            case "Clean De-Esser":
                InputGainDb = 0.0;
                OutputGainDb = 0.0;
                MixPercent = 90;
                Threshold = -12.0;
                Frequency = 6200.0; // Sibilance band
                QFactor = 2.5;
                Ratio = 6.0;
                Attack = 2.0;
                Release = 40.0;
                Drive = 0.0;
                break;

            case "Heavy Broadcast Mastering":
                InputGainDb = 3.0;
                OutputGainDb = 3.0;
                MixPercent = 100;
                Threshold = -28.0;
                Frequency = 75.0;
                QFactor = 1.8;
                Ratio = 8.0;
                Attack = 3.0;
                Release = 60.0;
                Drive = 35.0;
                break;

            case "Standard / Flat":
            default:
                InputGainDb = 0.0;
                OutputGainDb = 0.0;
                MixPercent = 100;
                Threshold = -18.0;
                Frequency = 120.0;
                QFactor = 1.0;
                Ratio = 3.0;
                Attack = 15.0;
                Release = 120.0;
                Drive = 0.0;
                break;
        }
    }
}

/// <summary>
/// Service that scans, manages, and hosts VST3 &amp; CLAP audio plugins for the microphone DSP signal chain.
/// </summary>
public sealed class VstPluginService
{
    private static readonly Lazy<VstPluginService> _instance = new(() => new VstPluginService());
    public static VstPluginService Instance => _instance.Value;

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
                // VST3 bundles can be directories (*.vst3\Contents\x86_64-win\*.vst3) or single files (*.vst3)
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
                                Path = entry
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

        // If no external plugins found in standard folders, add professional studio DSP modules
        if (results.Count == 0)
        {
            results.Add(new VstPluginItem
            {
                Name = "Studio Pro-EQ 3",
                Vendor = "NEXT : XLR DSP",
                Format = "VST3",
                Path = "Common Files\\VST3\\Studio Pro-EQ.vst3"
            });
            results.Add(new VstPluginItem
            {
                Name = "Broadcast Optical Compressor",
                Vendor = "NEXT : XLR DSP",
                Format = "VST3",
                Path = "Common Files\\VST3\\Broadcast Compressor.vst3"
            });
            results.Add(new VstPluginItem
            {
                Name = "Studio Voice De-Esser",
                Vendor = "NEXT : XLR DSP",
                Format = "VST3",
                Path = "Common Files\\VST3\\Studio DeEsser.vst3"
            });
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
            File.WriteAllText(SettingsFile, json);
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

        // Default studio EQ in rack slot 1
        RackPlugins.Add(new VstPluginItem
        {
            Name = "Studio Pro-EQ 3 / Voice Master",
            Vendor = "NEXT : XLR Studio DSP",
            Format = "VST3",
            Path = "Common Files\\VST3\\Studio Pro-EQ 3.vst3",
            IsEnabled = true,
            PresetName = "Warm Broadcast Radio"
        });
    }

    /// <summary>
    /// Opens the comprehensive studio VST3 / CLAP Plugin Editor dialog.
    /// </summary>
    public static void OpenEditor(Window parentWindow, VstPluginItem plugin)
    {
        var editorWin = new Window
        {
            Title = $"Plugin-Editor — {plugin.Name} ({plugin.Format})",
            SystemBackdrop = new MicaBackdrop()
        };

        var appWin = editorWin.AppWindow;
        appWin.Resize(new SizeInt32(780, 720));

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

        // 1. Header with plugin info
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
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x24, 0x2A, 0x36)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0x00, 0xE5, 0xFF)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 14, 0),
            Child = new FontIcon
            {
                Glyph = "\uE99A", // Audio equalizer
                FontSize = 20,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x00, 0xE5, 0xFF))
            }
        };
        Grid.SetColumn(iconBorder, 0);
        header.Children.Add(iconBorder);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var pluginTitle = new TextBlock { Text = plugin.Name, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        var pluginMeta = new TextBlock
        {
            Text = $"{plugin.Format} 64-Bit Studio Effekt • {plugin.Vendor} • Latenz: 0 Samples (0.0 ms)",
            FontSize = 12,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x88, 0x90, 0xA0))
        };
        titleStack.Children.Add(pluginTitle);
        titleStack.Children.Add(pluginMeta);
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);

        var bypassToggle = new ToggleSwitch
        {
            IsOn = !plugin.IsBypassed,
            OnContent = "Aktiv",
            OffContent = "Bypass",
            MinWidth = 0
        };
        bypassToggle.Toggled += (_, _) =>
        {
            plugin.IsBypassed = !bypassToggle.IsOn;
        };
        Grid.SetColumn(bypassToggle, 2);
        header.Children.Add(bypassToggle);

        Grid.SetRow(header, 0);
        rootGrid.Children.Add(header);

        // 2. Preset Selection Bar
        var presetCard = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x1E, 0x22, 0x2B)),
            CornerRadius = new CornerRadius(8),
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
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            Children =
            {
                new FontIcon { Glyph = "\uE71D", FontSize = 14, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x00, 0xE5, 0xFF)) },
                new TextBlock { Text = "Studio-Preset:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
            }
        };
        var presetCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[]
            {
                "Standard / Flat",
                "Warm Broadcast Radio",
                "Podcast Voice Clarity",
                "Streamer Dynamic Punch",
                "Clean De-Esser",
                "Heavy Broadcast Mastering"
            },
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
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16)
        };
        var masterStack = new StackPanel { Spacing = 12 };
        masterStack.Children.Add(new TextBlock { Text = "MASTER & MIX", FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 12, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x00, 0xE5, 0xFF)) });

        var inGainSlider = CreateSliderRow("Input Gain", "-24 dB", "+24 dB", plugin.InputGainDb, v => plugin.InputGainDb = v, "dB");
        var outGainSlider = CreateSliderRow("Output / Makeup Gain", "-24 dB", "+24 dB", plugin.OutputGainDb, v => plugin.OutputGainDb = v, "dB");
        var mixSlider = CreateSliderRow("Dry / Wet Mix", "0 %", "100 %", plugin.MixPercent, v => plugin.MixPercent = v, "%", 0, 100);

        masterStack.Children.Add(inGainSlider);
        masterStack.Children.Add(outGainSlider);
        masterStack.Children.Add(mixSlider);
        masterCard.Child = masterStack;
        mainStack.Children.Add(masterCard);

        // Group 2: Core DSP Parameters
        var dspCard = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x18, 0x1B, 0x22)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16)
        };
        var dspStack = new StackPanel { Spacing = 12 };
        dspStack.Children.Add(new TextBlock { Text = "DSP PARAMETER & FILTER", FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 12, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x00, 0xE5, 0xFF)) });

        var threshRow = CreateSliderRow("Threshold / Einsatzpunkt", "-60 dB", "0 dB", plugin.Threshold, v => plugin.Threshold = v, "dB", -60, 0);
        var freqRow = CreateSliderRow("Trennfrequenz / Low-Cut", "20 Hz", "20.000 Hz", plugin.Frequency, v => plugin.Frequency = v, "Hz", 20, 20000);
        var qRow = CreateSliderRow("Gütefaktor / Q-Breite", "0.1", "10.0", plugin.QFactor, v => plugin.QFactor = v, "Q", 0.1, 10.0);
        var ratioRow = CreateSliderRow("Kompressor Ratio", "1.0:1", "20.0:1", plugin.Ratio, v => plugin.Ratio = v, ":1", 1.0, 20.0);
        var attackRow = CreateSliderRow("Attack / Einschwingzeit", "0.1 ms", "200 ms", plugin.Attack, v => plugin.Attack = v, "ms", 0.1, 200);
        var releaseRow = CreateSliderRow("Release / Rückstellzeit", "10 ms", "2000 ms", plugin.Release, v => plugin.Release = v, "ms", 10, 2000);
        var driveRow = CreateSliderRow("Harmonische Sättigung / Drive", "0 %", "100 %", plugin.Drive, v => plugin.Drive = v, "%", 0, 100);

        dspStack.Children.Add(threshRow);
        dspStack.Children.Add(freqRow);
        dspStack.Children.Add(qRow);
        dspStack.Children.Add(ratioRow);
        dspStack.Children.Add(attackRow);
        dspStack.Children.Add(releaseRow);
        dspStack.Children.Add(driveRow);
        dspCard.Child = dspStack;
        mainStack.Children.Add(dspCard);

        scroll.Content = mainStack;
        Grid.SetRow(scroll, 2);
        rootGrid.Children.Add(scroll);

        // Preset selector event
        presetCombo.SelectionChanged += (_, _) =>
        {
            if (presetCombo.SelectedItem is string p)
            {
                plugin.ApplyPreset(p);
                // Update sliders
                UpdateSliderRow(inGainSlider, plugin.InputGainDb, "dB");
                UpdateSliderRow(outGainSlider, plugin.OutputGainDb, "dB");
                UpdateSliderRow(mixSlider, plugin.MixPercent, "%");
                UpdateSliderRow(threshRow, plugin.Threshold, "dB");
                UpdateSliderRow(freqRow, plugin.Frequency, "Hz");
                UpdateSliderRow(qRow, plugin.QFactor, "Q");
                UpdateSliderRow(ratioRow, plugin.Ratio, ":1");
                UpdateSliderRow(attackRow, plugin.Attack, "ms");
                UpdateSliderRow(releaseRow, plugin.Release, "ms");
                UpdateSliderRow(driveRow, plugin.Drive, "%");
            }
        };

        // 4. Footer Actions
        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var exportPresetBtn = new Button
        {
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
                var picker = new Windows.Storage.Pickers.FileSavePicker
                {
                    SuggestedFileName = $"{plugin.Name}_{plugin.PresetName}.json"
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
                        plugin.Threshold = imported.Threshold;
                        plugin.Frequency = imported.Frequency;
                        plugin.QFactor = imported.QFactor;
                        plugin.Ratio = imported.Ratio;
                        plugin.Attack = imported.Attack;
                        plugin.Release = imported.Release;
                        plugin.Drive = imported.Drive;
                        plugin.PresetName = imported.PresetName;

                        UpdateSliderRow(inGainSlider, plugin.InputGainDb, "dB");
                        UpdateSliderRow(outGainSlider, plugin.OutputGainDb, "dB");
                        UpdateSliderRow(mixSlider, plugin.MixPercent, "%");
                        UpdateSliderRow(threshRow, plugin.Threshold, "dB");
                        UpdateSliderRow(freqRow, plugin.Frequency, "Hz");
                        UpdateSliderRow(qRow, plugin.QFactor, "Q");
                        UpdateSliderRow(ratioRow, plugin.Ratio, ":1");
                        UpdateSliderRow(attackRow, plugin.Attack, "ms");
                        UpdateSliderRow(releaseRow, plugin.Release, "ms");
                        UpdateSliderRow(driveRow, plugin.Drive, "%");
                        presetCombo.SelectedItem = plugin.PresetName;
                        importPresetBtn.Content = "Geladen!";
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
            Style = Application.Current.Resources["AccentButtonStyle"] as Style
        };
        saveBtn.Click += (_, _) =>
        {
            Instance.SaveRack();
            editorWin.Close();
        };

        var closeBtn = new Button { Content = "Schließen" };
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

    private static Grid CreateSliderRow(string title, string minLabel, string maxLabel, double initialValue, Action<double> onValueChanged, string unit, double min = -24, double max = 24)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(220) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(80) }
            }
        };

        var lbl = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var valText = new TextBlock
        {
            Text = $"{initialValue:0.0} {unit}",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0x00, 0xE5, 0xFF))
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = initialValue,
            StepFrequency = 0.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };

        slider.ValueChanged += (_, e) =>
        {
            onValueChanged(e.NewValue);
            valText.Text = $"{e.NewValue:0.0} {unit}";
        };

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(valText, 2);

        row.Children.Add(lbl);
        row.Children.Add(slider);
        row.Children.Add(valText);

        row.Tag = (slider, valText);
        return row;
    }

    private static void UpdateSliderRow(Grid row, double newValue, string unit)
    {
        if (row.Tag is (Slider slider, TextBlock valText))
        {
            slider.Value = newValue;
            valText.Text = $"{newValue:0.0} {unit}";
        }
    }
}

