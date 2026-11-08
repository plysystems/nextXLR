using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.Protocol;
using NextXLR.Services;

namespace NextXLR.ViewModels;

public partial class RouteCellViewModel : ObservableObject
{
    private readonly PipeTransport _transport;
    private bool _applyingServer;

    public uint ChannelId { get; }
    public uint BusId { get; }
    public string Tooltip { get; }
    /// <summary>Not allowed on the GoXLR (would feed a signal back into itself).</summary>
    public bool IsBlocked { get; init; }
    /// <summary>Permanently routed (e.g. Bleep to Broadcast, Chat Mic &amp; Monitor).</summary>
    public bool IsLocked { get; init; }
    public bool IsEnabled => !IsBlocked;

    [ObservableProperty] private bool _isRouted;

    public RouteCellViewModel(uint channel, uint bus, string tooltip, PipeTransport transport)
    {
        ChannelId = channel;
        BusId = bus;
        Tooltip = tooltip;
        _transport = transport;
    }

    public void ApplyFromServer(bool routed)
    {
        _applyingServer = true;
        try { IsRouted = routed; } finally { _applyingServer = false; }
    }

    partial void OnIsRoutedChanged(bool value)
    {
        if (IsLocked)
        {
            if (!value) IsRouted = true;
            return;
        }
        if (!_applyingServer) _transport.Send(new SetRoute(ChannelId, BusId, value ? 1.0 : 0.0));
    }
}

public sealed class RouteRowViewModel
{
    public required uint ChannelId { get; init; }
    public required string ChannelName { get; init; }
    public string Glyph { get; init; } = "\uE767";
    public Microsoft.UI.Xaml.Media.Brush? Accent { get; init; }
    /// <summary>Headphones, Broadcast Stream Mix, Line Out, Chat Mic.</summary>
    public ObservableCollection<RouteCellViewModel> Cells { get; } = new();
    /// <summary>The Sampler recording input, shown in its own highlighted column.</summary>
    public RouteCellViewModel? SamplerCell { get; set; }
}
