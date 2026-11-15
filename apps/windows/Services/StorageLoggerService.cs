using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace NextXLR.Services;

/// <summary>
/// A single audited save event in the storage log.
/// </summary>
public sealed class StorageLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    public string TimeOnlyText => Timestamp.ToString("HH:mm:ss");
    public string DateOnlyText => Timestamp.ToString("dd.MM.yyyy");
    public string Trigger { get; set; } = "Manuell"; // "Autosave", "Manuell", "System", "Import"
    public bool IsAutosave => string.Equals(Trigger, "Autosave", StringComparison.OrdinalIgnoreCase);
    public Microsoft.UI.Xaml.Visibility AutosaveBadge =>
        IsAutosave ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility ManualBadge =>
        !IsAutosave ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string Target { get; set; } = "";        // "Was" (z. B. "Profil: Standard", "Mikrofon-DSP")
    public string Value { get; set; } = "";         // "Wert" (z. B. "Gain: +54dB, Gate: -36dB", "12 Kanäle aktiv")
    public string? FilePath { get; set; }           // Dateipfad
    public string DisplayFile => string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileName(FilePath);
}

/// <summary>
/// Central storage audit logging service for NEXT : XLR.
/// Logs when, what, and which value was saved (including manual and Autosave operations).
/// </summary>
public static class StorageLoggerService
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "nextxlr",
        "logs");

    private static readonly string LogFilePath = Path.Combine(LogDir, "storage_log.json");

    private static readonly object _syncLock = new();
    private static readonly List<StorageLogEntry> _entries = new();
    private static Timer? _saveDebounceTimer;
    private static bool _isDirty;
    private static DispatcherQueue? _uiDispatcher;

    public static event EventHandler? EntriesChanged;

    static StorageLoggerService()
    {
        Load();
    }

    public static void Initialize(DispatcherQueue dispatcher)
    {
        _uiDispatcher = dispatcher;
    }

    public static IReadOnlyList<StorageLogEntry> GetEntries()
    {
        lock (_syncLock)
        {
            return _entries.OrderByDescending(e => e.Timestamp).Take(300).ToList();
        }
    }

    /// <summary>
    /// Records a save event in the storage audit log.
    /// </summary>
    /// <param name="target">Was wurde gespeichert (z. B. "Profil: Gaming", "Mikrofon-EQ")</param>
    /// <param name="value">Wert / Detailbeschreibung (z. B. "10-Band EQ aktiviert", "Volume 75%")</param>
    /// <param name="trigger">Quelle (z. B. "Manuell", "Autosave", "System", "Import")</param>
    /// <param name="filePath">Optionaler Pfad der gespeicherten Datei</param>
    public static void Log(string target, string value, string trigger = "Manuell", string? filePath = null)
    {
        var entry = new StorageLogEntry
        {
            Timestamp = DateTime.Now,
            Target = target,
            Value = value,
            Trigger = trigger,
            FilePath = filePath
        };

        lock (_syncLock)
        {
            _entries.Insert(0, entry);
            // Prune to 500 entries max
            if (_entries.Count > 500)
            {
                _entries.RemoveRange(500, _entries.Count - 500);
            }
            _isDirty = true;
        }

        ScheduleDiskSave();

        // Notify UI
        if (_uiDispatcher != null)
        {
            _uiDispatcher.TryEnqueue(() => EntriesChanged?.Invoke(null, EventArgs.Empty));
        }
        else
        {
            EntriesChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Convenience helper for AutoSave triggers.</summary>
    public static void LogAutosave(string target, string value, string? filePath = null)
    {
        Log(target, value, "Autosave", filePath);
    }

    public static void Clear()
    {
        lock (_syncLock)
        {
            _entries.Clear();
            _isDirty = true;
        }
        ScheduleDiskSave();
        EntriesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static async Task ExportAsync(string destinationFilePath)
    {
        string json;
        lock (_syncLock)
        {
            json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        }
        await File.WriteAllTextAsync(destinationFilePath, json);
    }

    public static void OpenLogDirectory()
    {
        try
        {
            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = LogDir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static void Load()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                var json = File.ReadAllText(LogFilePath);
                var loaded = JsonSerializer.Deserialize<List<StorageLogEntry>>(json);
                if (loaded != null)
                {
                    lock (_syncLock)
                    {
                        _entries.Clear();
                        _entries.AddRange(loaded.OrderByDescending(e => e.Timestamp).Take(500));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StorageLogger] Error loading log: {ex.Message}");
        }
    }

    private static void ScheduleDiskSave()
    {
        lock (_syncLock)
        {
            if (_saveDebounceTimer == null)
            {
                _saveDebounceTimer = new Timer(_ => SaveToDisk(), null, 1500, Timeout.Infinite);
            }
            else
            {
                _saveDebounceTimer.Change(1500, Timeout.Infinite);
            }
        }
    }

    private static void SaveToDisk()
    {
        try
        {
            if (!_isDirty) return;
            string json;
            lock (_syncLock)
            {
                _isDirty = false;
                json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            }

            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
            File.WriteAllText(LogFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[StorageLogger] Error saving log to disk: {ex.Message}");
        }
    }
}
