using Microsoft.Win32;

namespace Auxline.Services;

/// <summary>"Start with Windows" via the per-user Run key (no admin rights needed).</summary>
public sealed class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Task Manager's Startup tab keeps the Run entry and stores its on/off toggle here
    /// (binary value; odd first byte = disabled, missing value = enabled).
    /// </summary>
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "Auxline";

    private readonly string _command;

    /// <param name="executablePath">Exe to start at logon; quoted because paths may contain spaces.</param>
    public AutostartService(string executablePath)
    {
        _command = $"\"{executablePath}\"";
    }

    /// <summary>
    /// True only when the entry points at this exe (a moved or rebuilt copy elsewhere counts as off)
    /// and Task Manager hasn't disabled it.
    /// </summary>
    public bool IsEnabled() => IsRegistered() && !IsDisabledInTaskManager();

    public void Enable()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            key.SetValue(ValueName, _command);
        }
        // Otherwise a "disabled" toggle left by Task Manager would keep it from starting.
        ClearTaskManagerState();
    }

    public void Disable()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        ClearTaskManagerState();
    }

    private bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string == _command;
    }

    private static bool IsDisabledInTaskManager()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }

    private static void ClearTaskManagerState()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
