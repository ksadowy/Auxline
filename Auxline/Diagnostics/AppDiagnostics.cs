using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace Auxline.Diagnostics;

/// <summary>
/// Process-wide logging (existing <see cref="Trace"/> calls go to a file) and last-resort error handlers,
/// so an unexpected error is recorded instead of silently closing the app.
/// </summary>
public static class AppDiagnostics
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Auxline", "logs");

    private static RollingFileTraceListener? _fileListener;

    public static void Start(Application app)
    {
        StartFileLog();
        RegisterErrorHandlers(app);
        Trace.TraceInformation($"Auxline {Assembly.GetExecutingAssembly().GetName().Version} started");
    }

    public static void Stop()
    {
        Trace.TraceInformation("Auxline exiting");
        if (_fileListener is null) return;

        Trace.Listeners.Remove(_fileListener);
        _fileListener.Dispose();
        _fileListener = null;
    }

    private static void StartFileLog()
    {
        try
        {
            _fileListener = new RollingFileTraceListener(LogDirectory);
            Trace.Listeners.Add(_fileListener);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging is optional: run without a log file rather than not at all.
            Debug.WriteLine($"File log unavailable: {ex.Message}");
        }
    }

    private static void RegisterErrorHandlers(Application app)
    {
        // UI thread: event handlers, bindings, async void continuations. Most are local glitches, so keep running.
        app.DispatcherUnhandledException += (_, e) =>
        {
            Trace.TraceError($"Unhandled UI exception (app keeps running): {e.Exception}");
            e.Handled = true;
        };

        // Fire-and-forget tasks (e.g. media refreshes) that failed with an exception nobody awaited.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Trace.TraceError($"Unobserved background task exception: {e.Exception}");
            e.SetObserved();
        };

        // Crash on a non-UI thread: can't be stopped, but the cause ends up in the log.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Trace.TraceError($"Fatal unhandled exception (terminating: {e.IsTerminating}): {e.ExceptionObject}");
    }
}
