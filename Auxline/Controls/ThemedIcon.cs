using System.Windows;
using System.Windows.Controls;

namespace Auxline.Controls;

public enum IconKind
{
    Previous,
    Next,
    Play,
    Pause,
    Volume,
    VolumeLow,
    VolumeHigh,
    Music,
}

/// <summary>
/// An icon drawn by the current flyout theme (its "ThemedIconTemplate"): Fluent glyphs by default, pixel art in
/// pixel themes. Sized by FontSize and coloured by Foreground, both inherited like text.
/// </summary>
public sealed class ThemedIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(IconKind), typeof(ThemedIcon), new PropertyMetadata(IconKind.Music));

    public ThemedIcon()
    {
        Focusable = false;
        IsTabStop = false;
        SetResourceReference(TemplateProperty, "ThemedIconTemplate");
    }

    public IconKind Kind
    {
        get => (IconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }
}
