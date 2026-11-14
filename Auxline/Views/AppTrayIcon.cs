using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using H.NotifyIcon;
using Auxline.Services;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Views;

/// <summary>Notification-area icon: the app's only entry point for settings, autostart, the debug window and exit.</summary>
public sealed class AppTrayIcon : IDisposable
{
    private const string MusicNoteGlyph = "";
    private const string FluentIconsFont = "Segoe Fluent Icons";
    private const string LegacyIconsFont = "Segoe MDL2 Assets";

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
            IconSource = new GeneratedIconSource
            {
                Text = MusicNoteGlyph,
                FontFamily = new FontFamily(PickIconFont()),
                Foreground = isLightTheme ? Brushes.Black : Brushes.White,
            },
        };
        _icon.TrayRightMouseUp += (_, _) => ShowMenu();
        // Created in code (not in a visual tree), so it must be created explicitly.
        // Efficiency mode would throttle the process and delay media/progress updates.
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>Removes the icon right away; otherwise a dead icon lingers until hovered.</summary>
    public void Dispose()
    {
        _icon.Dispose();
        _menuOwner.Dispose();
    }

    /// <summary>
    /// The icon is rendered via System.Drawing, which needs exactly one installed font name
    /// (a WPF fallback list like "A, B" crashes it). Windows 11 has Fluent Icons; Windows 10 only MDL2.
    /// </summary>
    private static string PickIconFont() =>
        Fonts.SystemFontFamilies.Any(font => font.Source == FluentIconsFont) ? FluentIconsFont : LegacyIconsFont;

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
