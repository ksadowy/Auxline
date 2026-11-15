using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Auxline.ViewModels;

namespace Auxline.Views.FlyoutStyles;

/// <summary>
/// One line for progress or volume: the speaker toggle or the mouse wheel shows the volume, which returns to the
/// progress after a few seconds without interaction.
/// </summary>
public partial class MinimalistStyle : UserControl
{
    private static readonly TimeSpan VolumeShownFor = TimeSpan.FromSeconds(3);

    /// <summary>Percent per wheel notch (120 delta units = one notch).</summary>
    private const double VolumeStepPerNotch = 2;

    private readonly DispatcherTimer _backToProgressTimer;

    public MinimalistStyle()
    {
        InitializeComponent();
        _backToProgressTimer = new DispatcherTimer { Interval = VolumeShownFor };
        _backToProgressTimer.Tick += (_, _) =>
        {
            // Not while the user is still dragging the volume.
            if (VolumeSlider.IsMouseCaptureWithin) return;
            VolumeToggle.IsChecked = false;
        };

        PreviewMouseWheel += OnPreviewMouseWheel;
        // Any interaction with the volume line keeps it shown a little longer.
        VolumeLine.PreviewMouseMove += (_, _) => RestartTimerIfShown();
        Unloaded += (_, _) => _backToProgressTimer.Stop();
    }

    private void OnVolumeToggleChanged(object sender, RoutedEventArgs e)
    {
        var isVolumeShown = VolumeToggle.IsChecked == true;
        VolumeLine.Visibility = isVolumeShown ? Visibility.Visible : Visibility.Collapsed;
        ProgressSlider.Opacity = isVolumeShown ? 0 : 1;
        ProgressSlider.IsHitTestVisible = !isVolumeShown;
        RestartTimerIfShown();
    }

    /// <summary>Wheel anywhere over the flyout: change the volume and show it.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not PlayerViewModel { HasAudioSession: true } player) return;

        player.VolumePercent = Math.Clamp(player.VolumePercent + e.Delta / 120.0 * VolumeStepPerNotch, 0, 100);
        VolumeToggle.IsChecked = true;
        RestartTimerIfShown();
        e.Handled = true;
    }

    private void RestartTimerIfShown()
    {
        _backToProgressTimer.Stop();
        if (VolumeToggle.IsChecked == true) _backToProgressTimer.Start();
    }
}
