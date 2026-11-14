using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Auxline.Models;

namespace Auxline.Services;

/// <summary>
/// Loads, holds and saves <see cref="AppSettings"/>. Changes apply live via <see cref="Changed"/>;
/// saving is debounced so dragging a slider doesn't rewrite the file on every tick.
/// </summary>
public sealed class SettingsService : IDisposable
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Auxline", "settings.json");

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly Timer _saveTimer;
    private readonly object _saveGate = new();
    private bool _isSavePending;

    public AppSettings Current { get; private set; }

    /// <summary>Raised on the thread that called <see cref="Update"/> (the UI thread in the app).</summary>
    public event EventHandler<AppSettings>? Changed;

    private SettingsService(string path, AppSettings initial)
    {
        _path = path;
        Current = initial;
        _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Never throws for file problems: falls back to defaults and logs a warning.</summary>
    public static SettingsService Load(string path) => new(path, ReadOrDefault(path));

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current).Normalize();
        Changed?.Invoke(this, Current);
        ScheduleSave();
    }

    /// <summary>Writes a pending change immediately (e.g. on exit).</summary>
    public void Flush()
    {
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SaveNow();
    }

    public void Dispose()
    {
        Flush();
        _saveTimer.Dispose();
    }

    private static AppSettings ReadOrDefault(string path)
    {
        if (!File.Exists(path)) return new AppSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            return (settings ?? new AppSettings()).Normalize();
        }
        catch (JsonException ex)
        {
            // Keep the broken file for inspection instead of silently overwriting the user's edits.
            Trace.TraceWarning($"Settings file is invalid, using defaults (kept as .bad): {ex.Message}");
            TryKeepBrokenFile(path);
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Settings file unreadable, using defaults: {ex.Message}");
            return new AppSettings();
        }
    }

    private static void TryKeepBrokenFile(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not keep the invalid settings file: {ex.Message}");
        }
    }

    private void ScheduleSave()
    {
        lock (_saveGate) _isSavePending = true;
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Atomic: write a temp file, then replace, so a crash mid-write can't leave a corrupt file.</summary>
    private void SaveNow()
    {
        lock (_saveGate)
        {
            if (!_isSavePending) return;
            _isSavePending = false;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tempPath = _path + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(Current, JsonOptions));
                File.Move(tempPath, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Saving settings failed: {ex.Message}");
            }
        }
    }
}
