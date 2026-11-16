using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using NextXLR.ViewModels;

namespace NextXLR.Services;

/// <summary>
/// High-performance, debounced background auto-save service.
/// Automatically saves modifications to the currently active setup profile or mic profile
/// when <see cref="AppSettings.AutoSaveProfiles"/> is enabled (Default: OFF).
/// </summary>
public sealed class AutoSaveService
{
    private static readonly Lazy<AutoSaveService> _instance = new(() => new AutoSaveService());
    public static AutoSaveService Instance => _instance.Value;

    private readonly Timer _setupTimer;
    private readonly Timer _micTimer;
    private readonly SemaphoreSlim _setupLock = new(1, 1);
    private readonly SemaphoreSlim _micLock = new(1, 1);

    private volatile bool _setupPending;
    private volatile bool _micPending;

    private MainViewModel? _mainViewModel;
    private DispatcherQueue? _dispatcherQueue;

    private const int DebounceDelayMs = 750;

    private AutoSaveService()
    {
        _setupTimer = new Timer(OnSetupTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        _micTimer = new Timer(OnMicTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Initialize(MainViewModel vm, DispatcherQueue dispatcherQueue)
    {
        _mainViewModel = vm;
        _dispatcherQueue = dispatcherQueue;
    }

    /// <summary>
    /// Notifies that a setup-related change has occurred (volumes, routing, lighting, effects, mixer pages).
    /// </summary>
    public void NotifySetupChanged()
    {
        if (!AppSettings.Current.AutoSaveProfiles) return;
        _setupPending = true;
        _setupTimer.Change(DebounceDelayMs, Timeout.Infinite);
    }

    /// <summary>
    /// Notifies that a mic-related change has occurred (gate, compressor, EQ, de-esser, morph, VST).
    /// </summary>
    public void NotifyMicChanged()
    {
        if (!AppSettings.Current.AutoSaveProfiles) return;
        _micPending = true;
        _micTimer.Change(DebounceDelayMs, Timeout.Infinite);
    }

    private void OnSetupTimerCallback(object? state)
    {
        if (!_setupPending || !AppSettings.Current.AutoSaveProfiles) return;
        if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(async () =>
            {
                await SaveSetupAsync();
            });
        }
        else
        {
            _ = Task.Run(SaveSetupAsync);
        }
    }

    private void OnMicTimerCallback(object? state)
    {
        if (!_micPending || !AppSettings.Current.AutoSaveProfiles) return;
        if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(async () =>
            {
                await SaveMicAsync();
            });
        }
        else
        {
            _ = Task.Run(SaveMicAsync);
        }
    }

    public async Task SaveSetupAsync()
    {
        if (!AppSettings.Current.AutoSaveProfiles) return;
        if (_mainViewModel == null || !_mainViewModel.IsConnected) return;

        if (!await _setupLock.WaitAsync(0))
        {
            _setupTimer.Change(300, Timeout.Infinite);
            return;
        }

        try
        {
            _setupPending = false;
            var profile = await _mainViewModel.GetProfileAsync();
            var targetName = ProfileService.ActiveSetupProfileName;
            if (!string.IsNullOrWhiteSpace(targetName) && profile != null)
            {
                var entry = ProfileService.Setups.Save(targetName, profile, isAutosave: true);
                MainViewModel.HasPendingChanges = false;
                ChangeDeltaTracker.Instance.DetectAndCommitSetupChanges(_mainViewModel, isAutosave: true, entry.Path);
                Debug.WriteLine($"[AutoSave] Active setup profile '{targetName}' saved successfully.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoSave] Error saving setup profile: {ex.Message}");
        }
        finally
        {
            _setupLock.Release();
        }
    }

    public async Task SaveMicAsync()
    {
        if (!AppSettings.Current.AutoSaveProfiles) return;
        if (_mainViewModel == null) return;

        if (!await _micLock.WaitAsync(0))
        {
            _micTimer.Change(300, Timeout.Infinite);
            return;
        }

        try
        {
            _micPending = false;
            var micSettings = _mainViewModel.Mic.Build();
            var micNode = JsonSerializer.SerializeToNode(micSettings, Protocol.Json.Options);
            var targetName = ProfileService.ActiveMicProfileName;
            if (micNode != null && !string.IsNullOrWhiteSpace(targetName))
            {
                var entry = ProfileService.Mic.Save(targetName, micNode, isAutosave: true);
                VstPluginService.Instance?.SaveRackForProfile(targetName);
                MainViewModel.HasPendingChanges = false;
                ChangeDeltaTracker.Instance.DetectAndCommitMicChanges(_mainViewModel, isAutosave: true, entry.Path);
                Debug.WriteLine($"[AutoSave] Active mic profile '{targetName}' saved successfully.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoSave] Error saving mic profile: {ex.Message}");
        }
        finally
        {
            _micLock.Release();
        }
    }

    /// <summary>
    /// Flushes any pending auto-saves immediately (e.g. before exiting the app).
    /// </summary>
    public async Task FlushAsync()
    {
        _setupTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _micTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (AppSettings.Current.AutoSaveProfiles)
        {
            if (_setupPending)
            {
                await SaveSetupAsync();
            }
            if (_micPending)
            {
                await SaveMicAsync();
            }
        }

        StorageLoggerService.Flush();
    }
}
