using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Auxline.Services;

/// <summary>
/// Per-app volume and loudness via Core Audio sessions on the default playback device.
/// Volume calls look sessions up fresh, so app restarts and default-device changes need no extra handling; the
/// loudness meters are cached briefly because they're read many times a second. Call from the UI thread (COM apartment).
/// </summary>
public sealed class VolumeService : IDisposable
{
    /// <summary>How long one app's session meters are reused before looking again (new browser tabs, restarts).</summary>
    private static readonly TimeSpan MeterLookupInterval = TimeSpan.FromSeconds(2);

    private readonly MMDeviceEnumerator _enumerator = new();

    /// <summary>Kept alive while its sessions' meters are in use.</summary>
    private MMDevice? _meterDevice;
    private List<AudioMeterInformation> _meters = [];
    private string? _meterProcessName;
    private long _metersLookedUpAt;

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

    /// <returns>The loudest current peak of the app's sessions, 0..1; 0 without a session or on audio errors.</returns>
    public float GetPeak(string processName)
    {
        try
        {
            var isStale = Environment.TickCount64 - _metersLookedUpAt > MeterLookupInterval.TotalMilliseconds;
            if (isStale || !string.Equals(processName, _meterProcessName, StringComparison.OrdinalIgnoreCase)) LookUpMeters(processName);
            return _meters.Count == 0 ? 0 : _meters.Max(meter => meter.MasterPeakValue);
        }
        catch (COMException)
        {
            // Device or session gone mid-read; the next lookup (at most 2 s later) finds the new ones. Not logged:
            // this runs many times a second.
            ReleaseMeters();
            return 0;
        }
    }

    public void Dispose()
    {
        ReleaseMeters();
        _enumerator.Dispose();
    }

    private void LookUpMeters(string processName)
    {
        ReleaseMeters();
        // Set first, so a failing lookup (no playback device) is retried after the interval, not on every read.
        _meterProcessName = processName;
        _metersLookedUpAt = Environment.TickCount64;
        _meterDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _meters = OwnedSessions(_meterDevice, processName).Select(session => session.AudioMeterInformation).ToList();
    }

    private void ReleaseMeters()
    {
        _meters = [];
        _meterDevice?.Dispose();
        _meterDevice = null;
    }

    /// <summary>Runs <paramref name="use"/> while the device is alive; default(T) on audio failure.</summary>
    private T UseSessions<T>(string processName, Func<IReadOnlyList<AudioSessionControl>, T> use)
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return use(OwnedSessions(device, processName));
        }
        catch (COMException ex)
        {
            // No playback device, or device/session removed mid-call.
            Trace.TraceWarning($"Audio session access failed: {ex.Message}");
            return default!;
        }
    }

    private static List<AudioSessionControl> OwnedSessions(MMDevice device, string processName)
    {
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var all = manager.Sessions;
        return Enumerable.Range(0, all.Count)
            .Select(i => all[i])
            .Where(session => !session.IsSystemSoundsSession && IsOwnedBy(session, processName))
            .ToList();
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
