using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Auxline.Controls;

/// <summary>
/// Album art drawn by the current flyout theme (its "CoverArtTemplate"). Layouts set the size and their preferred
/// corner radius; a theme may ignore it and draw its own frame.
/// </summary>
public sealed class CoverArt : Control
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(CoverArt));

    public static readonly DependencyProperty CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner(typeof(CoverArt));

    public CoverArt()
    {
        Focusable = false;
        IsTabStop = false;
        SetResourceReference(TemplateProperty, "CoverArtTemplate");
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }
}
