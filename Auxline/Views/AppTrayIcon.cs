using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using H.NotifyIcon;
using Auxline.Interop;
using Auxline.Services;
using static Auxline.Interop.NativeMethods;
using Icon = System.Drawing.Icon;

namespace Auxline.Views;

/// <summary>Notification-area icon: the app's only entry point for settings, autostart, the debug window and exit.</summary>
public sealed class AppTrayIcon : IDisposable
{
    private enum MenuCommand
    {
        Dismissed = 0,
        ToggleAutostart = 1,
        ShowDebugWindow = 2,
        Exit = 3,
        ShowSettings = 4,
    }

    private readonly TaskbarIcon _icon;
    private readonly AutostartService _autostart;

    /// <summary>Invisible window that owns the native menu; it must be the foreground window while the menu is open.</summary>
    private readonly HwndSource _menuOwner;

    public event EventHandler? SettingsRequested;
    public event EventHandler? DebugWindowRequested;
    public event EventHandler? ExitRequested;

    public AppTrayIcon(AutostartService autostart, bool isLightTheme)
    {
        _autostart = autostart;
        _menuOwner = new HwndSource(new HwndSourceParameters("Auxline tray menu owner")
        {
            WindowStyle = WS_POPUP,
            Width = 0,
            Height = 0,
        });

        _icon = new TaskbarIcon
        {
            ToolTipText = "Auxline",
            Icon = LoadIcon(isLightTheme),
        };
        _icon.TrayRightMouseUp += (_, _) => ShowMenu();
        // Created in code (not in a visual tree), so it must be created explicitly.
        // Efficiency mode would throttle the process and delay media/progress updates.
        _icon.ForceCreate(enablesEfficiencyMode: false);
        NativeMenuTheme.Apply(isDark: !isLightTheme);
    }

    /// <summary>Swaps the icon and re-themes the menu when the taskbar switches between light and dark.</summary>
    public void ApplyTheme(bool isTaskbarLight)
    {
        var previous = _icon.Icon;
        _icon.Icon = LoadIcon(isTaskbarLight);
        previous?.Dispose();
        // Like Windows' own tray menus, follow the taskbar (system theme).
        NativeMenuTheme.Apply(isDark: !isTaskbarLight);
    }

    /// <summary>Removes the icon right away; otherwise a dead icon lingers until hovered.</summary>
    public void Dispose()
    {
        var icon = _icon.Icon;
        _icon.Dispose();
        icon?.Dispose();
        _menuOwner.Dispose();
    }

    /// <summary>
    /// The logo in one colour, like Windows' own tray icons: white on a dark taskbar, near-black on a light one
    /// (Assets/Tray*.ico, made by tools/build-icons.ps1). The frame drawn for the notification area's icon size at the
    /// current scaling is picked, instead of Windows shrinking a bigger one.
    /// </summary>
    internal static Icon LoadIcon(bool isTaskbarLight)
    {
        var uri = new Uri($"pack://application:,,,/Auxline;component/Assets/{(isTaskbarLight ? "TrayDark" : "TrayLight")}.ico");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
        return new Icon(stream, size, size);
    }

    /// <summary>
    /// Native menu instead of a WPF ContextMenu: when the icon sits in the Windows 11 overflow flyout,
    /// the flyout takes focus back ~150 ms after the click, which closes a WPF popup instantly.
    /// A native menu runs a modal loop that keeps input until it's dismissed.
    /// </summary>
    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING, (UIntPtr)MenuCommand.ShowSettings, "Settings…");
            var autostartFlags = MF_STRING | (_autostart.IsEnabled() ? MF_CHECKED : 0);
            AppendMenu(menu, autostartFlags, (UIntPtr)MenuCommand.ToggleAutostart, "Start with Windows");
            AppendMenu(menu, MF_STRING, (UIntPtr)MenuCommand.ShowDebugWindow, "Debug window");
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)MenuCommand.Exit, "Exit");

            GetCursorPos(out var cursor);
            var owner = _menuOwner.Handle;
            // Documented requirement for notification-icon menus: the owner must be in the foreground, or the
            // menu won't close on outside clicks; posting WM_NULL afterwards lets the menu loop end cleanly.
            SetForegroundWindow(owner);
            var command = (MenuCommand)TrackPopupMenuEx(menu,
                TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON | TPM_RIGHTALIGN | TPM_BOTTOMALIGN,
                cursor.X, cursor.Y, owner, IntPtr.Zero);
            PostMessage(owner, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            Execute(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void Execute(MenuCommand command)
    {
        switch (command)
        {
            case MenuCommand.ShowSettings:
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MenuCommand.ToggleAutostart:
                ToggleAutostart();
                break;
            case MenuCommand.ShowDebugWindow:
                DebugWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MenuCommand.Exit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void ToggleAutostart()
    {
        try
        {
            if (_autostart.IsEnabled()) _autostart.Disable();
            else _autostart.Enable();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            MessageBox.Show($"Could not change \"Start with Windows\".\n\n{ex.Message}", "Auxline",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
