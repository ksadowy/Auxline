using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
/// pixel themes. Sized by FontSize; coloured by <see cref="DisplayBrush"/> (templates bind to it).
/// </summary>
public sealed class ThemedIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(IconKind), typeof(ThemedIcon), new PropertyMetadata(IconKind.Music));

    /// <summary>
    /// Inherited colour that wins over Foreground: a theme whose buttons have their own face (aluminium keys, arcade
    /// caps) sets it on the button, because layouts set the buttons' Foreground for the default look. Null = Foreground.
    /// </summary>
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.RegisterAttached(
        "IconBrush", typeof(Brush), typeof(ThemedIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    private static readonly DependencyPropertyKey DisplayBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayBrush), typeof(Brush), typeof(ThemedIcon), new PropertyMetadata(null));

    public static readonly DependencyProperty DisplayBrushProperty = DisplayBrushPropertyKey.DependencyProperty;

    public ThemedIcon()
    {
        Focusable = false;
        IsTabStop = false;
        SetResourceReference(TemplateProperty, "ThemedIconTemplate");
        UpdateDisplayBrush();
    }

    public IconKind Kind
    {
        get => (IconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>The colour to draw with: <see cref="IconBrushProperty"/> if set, otherwise Foreground.</summary>
    public Brush? DisplayBrush => (Brush?)GetValue(DisplayBrushProperty);

    public static Brush? GetIconBrush(DependencyObject element) => (Brush?)element.GetValue(IconBrushProperty);

    public static void SetIconBrush(DependencyObject element, Brush? value) => element.SetValue(IconBrushProperty, value);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty || e.Property == IconBrushProperty) UpdateDisplayBrush();
    }

    private void UpdateDisplayBrush() => SetValue(DisplayBrushPropertyKey, GetIconBrush(this) ?? Foreground);
}
