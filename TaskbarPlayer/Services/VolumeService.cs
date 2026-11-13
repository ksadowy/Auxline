using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace TaskbarPlayer.Services;

/// <summary>
/// Per-app volume via Core Audio sessions on the default playback device.
/// Sessions are looked up fresh on every call, so app restarts and default-device
/// changes need no extra handling. Call from the UI thread (COM apartment).
/// </summary>
public sealed class VolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    /// <returns>Volume 0..1, or null when the app has no audio session.</returns>
    public float? GetVolume(string processName) =>
        UseSessions(processName, sessions =>
            sessions.Count > 0 ? sessions[0].SimpleAudioVolume.Volume : (float?)null);

    /// <returns>False when the app has no audio session.</returns>
    public bool SetVolume(string processName, float volume) =>
        UseSessions(processName, sessions =>
        {
            foreach (var session in sessions)
            {
                session.SimpleAudioVolume.Volume = volume;
            }
            return sessions.Count > 0;
        });

    public void Dispose() => _enumerator.Dispose();

    /// <summary>Runs <paramref name="use"/> while the device is alive; default(T) on audio failure.</summary>
    private T UseSessions<T>(string processName, Func<IReadOnlyList<AudioSessionControl>, T> use)
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var all = manager.Sessions;
            var owned = Enumerable.Range(0, all.Count)
                .Select(i => all[i])
                .Where(session => !session.IsSystemSoundsSession && IsOwnedBy(session, processName))
                .ToList();
            return use(owned);
        }
        catch (COMException ex)
        {
            // No playback device, or device/session removed mid-call.
            Trace.TraceWarning($"Audio session access failed: {ex.Message}");
            return default!;
        }
    }

    private static bool IsOwnedBy(AudioSessionControl session, string processName)
    {
        try
        {
            using var process = Process.GetProcessById((int)session.GetProcessID);
            return process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Process already exited; its session is stale.
            return false;
        }
    }
}
