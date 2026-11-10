using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NextXLR.Services;

public sealed class VirtualChannelItem : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public static readonly string[] AvailableTargets = ["Chat", "Music", "Game", "System", "Sample", "Line In"];
    public string[] TargetOptions => AvailableTargets;

    private string _name = "Virtueller Kanal";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _targetChannel = "Music";
    public string TargetChannel
    {
        get => _targetChannel;
        set
        {
            if (SetProperty(ref _targetChannel, value))
            {
                OnPropertyChanged(nameof(TargetText));
            }
        }
    }

    private int _sampleRate = 48000;
    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            if (SetProperty(ref _sampleRate, value))
            {
                OnPropertyChanged(nameof(LatencyMs));
                OnPropertyChanged(nameof(FormatText));
            }
        }
    }

    private int _bufferSize = 128;
    public int BufferSize
    {
        get => _bufferSize;
        set
        {
            if (SetProperty(ref _bufferSize, value))
            {
                OnPropertyChanged(nameof(LatencyMs));
            }
        }
    }

    private int _bitDepth = 24;
    public int BitDepth
    {
        get => _bitDepth;
        set
        {
            if (SetProperty(ref _bitDepth, value))
            {
                OnPropertyChanged(nameof(FormatText));
            }
        }
    }

    private bool _isActive = true;
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, Math.Clamp(value, 0.0, 1.5)))
            {
                OnPropertyChanged(nameof(VolumePercent));
                OnPropertyChanged(nameof(VolumeLabel));
            }
        }
    }

    public double VolumePercent
    {
        get => Math.Round(_volume * 100);
        set => Volume = value / 100.0;
    }

    public string VolumeLabel => $"{Math.Round(_volume * 100)} %";

    private double _meter = 0.0;
    public double Meter
    {
        get => _meter;
        set => SetProperty(ref _meter, value);
    }

    private string _glyph = "\uE7F4";
    public string Glyph
    {
        get => _glyph;
        set => SetProperty(ref _glyph, value);
    }

    public double LatencyMs => Math.Round(_bufferSize * 1000.0 / Math.Max(1, _sampleRate), 1);
    public string LatencyText => $"Latenz: {LatencyMs} ms";
    public string TargetText => $"Ziel: GoXLR {TargetChannel}";
    public string FormatText => $"{_sampleRate / 1000.0:0.0} kHz · {_bitDepth}-Bit";
}

public sealed class VirtualChannelsService
{
    private static readonly Lazy<VirtualChannelsService> _instance = new(() => new VirtualChannelsService());
    public static VirtualChannelsService Instance => _instance.Value;

    private static readonly string ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "virtual_channels.json");

    public ObservableCollection<VirtualChannelItem> Channels { get; } = [];

    private VirtualChannelsService()
    {
        Load();
    }

    private void AttachItem(VirtualChannelItem it)
    {
        it.PropertyChanged += (_, _) => Save();
        Channels.Add(it);
    }

    public void Load()
    {
        try
        {
            Channels.Clear();
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                var items = JsonSerializer.Deserialize<List<VirtualChannelItem>>(json);
                if (items != null && items.Count > 0)
                {
                    foreach (var it in items) AttachItem(it);
                    return;
                }
            }
        }
        catch { }

        // Default standard virtual cables
        AttachItem(new VirtualChannelItem
        {
            Name = "Virtueller Browser & Medien",
            TargetChannel = "Music",
            SampleRate = 48000,
            BufferSize = 128,
            BitDepth = 24,
            Glyph = "\uE774"
        });
        AttachItem(new VirtualChannelItem
        {
            Name = "Gaming Secondary (Aux)",
            TargetChannel = "Game",
            SampleRate = 48000,
            BufferSize = 64,
            BitDepth = 24,
            Glyph = "\uE7FC"
        });
        AttachItem(new VirtualChannelItem
        {
            Name = "VoIP / Discord Alt",
            TargetChannel = "Chat",
            SampleRate = 48000,
            BufferSize = 256,
            BitDepth = 24,
            Glyph = "\uE717"
        });
        AttachItem(new VirtualChannelItem
        {
            Name = "Soundboard Aux Cable",
            TargetChannel = "Sample",
            SampleRate = 48000,
            BufferSize = 128,
            BitDepth = 16,
            Glyph = "\uE93C"
        });

        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Channels.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch { }
    }

    public VirtualChannelItem CreateChannel(string name, string target = "Music", int sampleRate = 48000, int bufferSize = 128)
    {
        var item = new VirtualChannelItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Virtueller Kanal {Channels.Count + 1}" : name,
            TargetChannel = target,
            SampleRate = sampleRate,
            BufferSize = bufferSize,
            BitDepth = 24
        };
        Channels.Add(item);
        Save();
        return item;
    }

    public void RemoveChannel(VirtualChannelItem item)
    {
        if (Channels.Remove(item))
        {
            Save();
        }
    }
}
