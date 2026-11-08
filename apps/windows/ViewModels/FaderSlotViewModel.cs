using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.Protocol;
using NextXLR.Services;

namespace NextXLR.ViewModels;

/// <summary>
/// One physical fader (A–D) and the channel it controls. Choosing a channel sends
/// <c>assign_fader</c>; the daemon rebinds the GoXLR fader (and swaps if that channel was on
/// another fader), drives the motor to the channel's level and broadcasts the new mapping.
/// </summary>
public partial class FaderSlotViewModel(string letter) : ObservableObject
{
    private PipeTransport? _transport;
    private bool _applyingServer;

    public string Letter { get; } = letter;

    /// <summary>Inputs (Mic … Samples) and outputs (Headphones, Mic Monitor, Line Out).</summary>
    public ObservableCollection<ChannelViewModel> Options { get; } = new();

    [ObservableProperty] private ChannelViewModel? _selected;

    internal void SyncOptions(IEnumerable<ChannelViewModel> channels, PipeTransport transport)
    {
        _transport = transport;
        var list = channels.ToList();
        if (Options.Select(o => o.Id).SequenceEqual(list.Select(c => c.Id))) return;
        _applyingServer = true;
        Options.Clear();
        foreach (var c in list) Options.Add(c);
        _applyingServer = false;
    }

    internal void ApplyFromServer(uint? channel)
    {
        _applyingServer = true;
        Selected = Options.FirstOrDefault(o => o.Id == channel);
        _applyingServer = false;
    }

    partial void OnSelectedChanged(ChannelViewModel? value)
    {
        if (!_applyingServer && value is not null) _transport?.Send(new AssignFader(Letter, value.Id));
    }
}
