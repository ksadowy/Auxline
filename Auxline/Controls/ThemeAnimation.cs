using System.Windows;

namespace Auxline.Controls;

/// <summary>
/// Signals for flyout theme animations (Views/Themes). Looping animations run only while <c>IsActive</c> is true
/// (flyout visible and music playing), so nothing ticks in the background.
/// </summary>
public static class ThemeAnimation
{
    /// <summary>Inherited: set once on the flyout window, theme templates trigger on it.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(ThemeAnimation),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Raised on the flyout's theme frame when another track starts, for one-shot transitions.</summary>
    public static readonly RoutedEvent TrackChangedEvent = EventManager.RegisterRoutedEvent(
        "TrackChanged", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(ThemeAnimation));

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);
}
