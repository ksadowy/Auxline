using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Auxline.Controls;

/// <summary>HSV colour wheel: angle = hue, distance from the centre = saturation, slider = brightness.</summary>
public partial class ColorWheel : UserControl
{
    private const int WheelSize = 160;

    /// <summary>Same for every instance and never changes, so it's rendered once.</summary>
    private static readonly BitmapSource WheelBitmap = CreateWheelBitmap(WheelSize);

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorWheel),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    private HsvColor _hsv = new(0, 0, 1);

    /// <summary>
    /// Set while the wheel itself changes <see cref="SelectedColor"/>: keeps the exact hue/saturation instead of
    /// re-deriving them from RGB (which loses the hue of greys and very dark colours).
    /// </summary>
    private bool _isPicking;

    /// <summary>Set while the slider is moved programmatically, so that doesn't count as a user change.</summary>
    private bool _isSyncingSlider;

    public ColorWheel()
    {
        InitializeComponent();
        WheelImage.Source = WheelBitmap;
        ShowHsv();
    }

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var wheel = (ColorWheel)sender;
        if (wheel._isPicking) return;

        var color = (Color)e.NewValue;
        wheel._hsv = HsvColor.FromRgb(color.R, color.G, color.B);
        wheel.ShowHsv();
    }

    private void OnWheelMouseDown(object sender, MouseButtonEventArgs e)
    {
        WheelArea.CaptureMouse();
        PickAt(e.GetPosition(WheelArea));
    }

    private void OnWheelMouseMove(object sender, MouseEventArgs e)
    {
        if (WheelArea.IsMouseCaptured) PickAt(e.GetPosition(WheelArea));
    }

    private void OnWheelMouseUp(object sender, MouseButtonEventArgs e) => WheelArea.ReleaseMouseCapture();

    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isSyncingSlider) Pick(_hsv with { Value = e.NewValue });
    }

    /// <summary>Dragging outside the wheel keeps the hue and clamps to full saturation at the rim.</summary>
    private void PickAt(Point point)
    {
        var (hue, distance) = ToPolar(point.X, point.Y);
        Pick(_hsv with { Hue = hue, Saturation = Math.Min(1, distance) });
    }

    private void Pick(HsvColor hsv)
    {
        _hsv = hsv;
        ShowHsv();
        var (red, green, blue) = hsv.ToRgb();
        _isPicking = true;
        SelectedColor = Color.FromRgb(red, green, blue);
        _isPicking = false;
    }

    private void ShowHsv()
    {
        const double radius = WheelSize / 2.0;
        var angle = _hsv.Hue * Math.PI / 180;
        Canvas.SetLeft(Thumb, radius + Math.Cos(angle) * _hsv.Saturation * radius - Thumb.Width / 2);
        Canvas.SetTop(Thumb, radius - Math.Sin(angle) * _hsv.Saturation * radius - Thumb.Height / 2);
        DarkOverlay.Opacity = 1 - _hsv.Value;

        _isSyncingSlider = true;
        BrightnessSlider.Value = _hsv.Value;
        _isSyncingSlider = false;
    }

    /// <returns>Hue in degrees (0 = right, counter-clockwise) and distance from the centre (1 = rim).</returns>
    private static (double Hue, double Distance) ToPolar(double x, double y)
    {
        const double radius = WheelSize / 2.0;
        double dx = x - radius, dy = y - radius;
        var hue = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
        return (hue, Math.Sqrt(dx * dx + dy * dy) / radius);
    }

    private static BitmapSource CreateWheelBitmap(int size)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var (hue, distance) = ToPolar(x + 0.5, y + 0.5);
                // Soft one-pixel rim instead of a jagged edge.
                var alpha = Math.Clamp((1 - distance) * size / 2 + 0.5, 0, 1);
                var (red, green, blue) = new HsvColor(hue, Math.Min(1, distance), 1).ToRgb();

                var offset = (y * size + x) * 4;
                pixels[offset] = blue;
                pixels[offset + 1] = green;
                pixels[offset + 2] = red;
                pixels[offset + 3] = (byte)Math.Round(alpha * 255);
            }
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
