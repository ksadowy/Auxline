using System.Diagnostics;
using System.IO;

namespace Auxline.Diagnostics;

/// <summary>
/// Writes <see cref="Trace"/> output to a size-capped log file. When the file reaches <see cref="MaxBytes"/>
/// it becomes <c>auxline.1.log</c> (replacing the previous one), so at most ~2 × MaxBytes stay on disk.
/// </summary>
public sealed class RollingFileTraceListener : TraceListener
{
    private const long MaxBytes = 1024 * 1024;

    private readonly string _path;
    private readonly string _previousPath;
    private readonly object _gate = new();
    private StreamWriter _writer;

    /// <exception cref="IOException">The log directory or file can't be created.</exception>
    /// <exception cref="UnauthorizedAccessException">No write access to the log directory.</exception>
    public RollingFileTraceListener(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "auxline.log");
        _previousPath = Path.Combine(directory, "auxline.1.log");
        _writer = OpenWriter();
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) =>
        WriteEntry(eventType, message ?? "");

    public override void TraceEvent(
        TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) =>
        WriteEntry(eventType, format is not null && args is { Length: > 0 } ? string.Format(format, args) : format ?? "");

    public override void Write(string? message) => WriteEntry(TraceEventType.Verbose, message ?? "");

    public override void WriteLine(string? message) => WriteEntry(TraceEventType.Verbose, message ?? "");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate) _writer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void WriteEntry(TraceEventType level, string message)
    {
        lock (_gate)
        {
            try
            {
                if (_writer.BaseStream.Length >= MaxBytes) Roll();
                _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {LevelName(level),-5} {message}");
            }
            catch (IOException)
            {
                // Logging must never take the app down (e.g. disk full); drop the entry.
            }
        }
    }

    private void Roll()
    {
        _writer.Dispose();
        File.Move(_path, _previousPath, overwrite: true);
        _writer = OpenWriter();
    }

    /// <summary>Shared read access, so the log can be opened while the app runs.</summary>
    private StreamWriter OpenWriter() =>
        new(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };

    private static string LevelName(TraceEventType level) => level switch
    {
        TraceEventType.Critical => "FATAL",
        TraceEventType.Error => "ERROR",
        TraceEventType.Warning => "WARN",
        TraceEventType.Information => "INFO",
        _ => "DEBUG",
    };
}
