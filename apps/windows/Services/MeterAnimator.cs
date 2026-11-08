using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using NextXLR.ViewModels;

namespace NextXLR.Services;

/// <summary>
/// Drives every channel meter from the compositor's frame clock (CompositionTarget.Rendering),
/// so meters animate at display rate (60–144 Hz) regardless of the daemon's ~30 Hz telemetry.
/// </summary>
public sealed class MeterAnimator : IDisposable
{
    private readonly IList<ChannelViewModel> _channels;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _last;

    public MeterAnimator(IList<ChannelViewModel> channels)
    {
        _channels = channels;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(now - _last, 0.1);
        if (dt <= 0) return;
        _last = now;

        var count = _channels.Count;
        for (int i = 0; i < count; i++)
        {
            if (i < _channels.Count)
            {
                _channels[i].Tick(dt);
            }
        }
    }

    public void Dispose() => CompositionTarget.Rendering -= OnRendering;
}
