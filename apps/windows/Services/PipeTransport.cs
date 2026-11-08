using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using NextXLR.Protocol;

namespace NextXLR.Services;

public enum ConnectionState { Connecting, Connected, Offline }

/// <summary>
/// Connects to mixforged over \\.\pipe\mixforge (JSON lines) and keeps reconnecting.
/// Events are raised on a background thread — consumers marshal to the UI thread.
/// </summary>
public sealed class PipeTransport : IAsyncDisposable
{
    private const string PipeName = "mixforge";
    private static readonly TimeSpan CoalesceInterval = TimeSpan.FromMilliseconds(16);

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<string, ClientMessage> _pending = new();
    private readonly object _pendingLock = new();
    private StreamWriter? _writer;
    private Task? _loop;
    private Task? _flusher;

    public event Action<ServerMessage>? MessageReceived;
    public event Action<ConnectionState>? StateChanged;

    public void Start()
    {
        _loop = Task.Run(() => RunAsync(_cts.Token));
        _flusher = Task.Run(() => FlushLoopAsync(_cts.Token));
    }

    /// <summary>Send immediately (mute, route, …).</summary>
    public void Send(ClientMessage msg) => _ = WriteAsync(msg);

    /// <summary>
    /// Keep only the latest message per key and flush every ~16 ms: a fast slider drag sends at
    /// most ~60 updates/s instead of hundreds.
    /// </summary>
    public void SendCoalesced(string key, ClientMessage msg)
    {
        lock (_pendingLock) _pending[key] = msg;
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(CoalesceInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            ClientMessage[] batch;
            lock (_pendingLock)
            {
                if (_pending.Count == 0) continue;
                batch = _pending.Values.ToArray();
                _pending.Clear();
            }
            foreach (var m in batch) await WriteAsync(m).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(ClientMessage msg)
    {
        var writer = _writer;
        if (writer is null) return;
        var line = JsonSerializer.Serialize(msg, Json.Options);
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try { await writer.WriteLineAsync(line).ConfigureAwait(false); }
        catch (IOException) { /* reader loop notices the disconnect */ }
        catch (ObjectDisposedException) { }
        finally { _writeLock.Release(); }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromMilliseconds(250);
        while (!ct.IsCancellationRequested)
        {
            StateChanged?.Invoke(ConnectionState.Connecting);
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000, ct).ConfigureAwait(false);
                backoff = TimeSpan.FromMilliseconds(250);

                using var reader = new StreamReader(pipe, new UTF8Encoding(false));
                _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                StateChanged?.Invoke(ConnectionState.Connected);

                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    ServerMessage? msg;
                    try { msg = JsonSerializer.Deserialize<ServerMessage>(line, Json.Options); }
                    // Unknown/newer message: skip it, don't drop the link.
                    catch (Exception e) when (e is JsonException or NotSupportedException) { continue; }
                    if (msg is not null) MessageReceived?.Invoke(msg);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { /* daemon not running / pipe broken → retry */ }
            finally { _writer = null; }

            StateChanged?.Invoke(ConnectionState.Offline);
            try { await Task.Delay(backoff, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, 3000));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { if (_loop is not null) await _loop.ConfigureAwait(false); } catch { }
        try { if (_flusher is not null) await _flusher.ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }
}
