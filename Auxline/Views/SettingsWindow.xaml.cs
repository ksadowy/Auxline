using System.Windows;
using Auxline.Models;

namespace Auxline.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>Follows the theme preference live (Fluent theme: light, dark or the Windows app theme).</summary>
    public void ApplyTheme(ThemePreference theme)
    {
        // WPF's Fluent theme is still flagged experimental in .NET 10. Suppressed only here: at worst a future
        // .NET changes this setter and the settings window falls back to the classic look.
#pragma warning disable WPF0001
        var mode = theme switch
        {
            ThemePreference.Light => ThemeMode.Light,
            ThemePreference.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        // Setting it reloads the window's theme resources, which rebuilds the controls and resets scrolling;
        // this runs on every settings change, so only touch it when the theme really differs.
        if (ThemeMode != mode) ThemeMode = mode;
#pragma warning restore WPF0001
    }
}
