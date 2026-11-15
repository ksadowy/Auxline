using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Auxline.Controls;
using Auxline.Models;
using Auxline.Views;
using Auxline.Views.Themes;

namespace Auxline.Tests;

/// <summary>
/// Renders every flyout style in every theme with sample data, in the real (never shown) flyout window. XAML
/// mistakes (bad resource keys, templates, bindings that throw) only surface at runtime, so this catches them for
/// all combinations, not just the one the app starts with.
/// Set AUXLINE_RENDER_DIR to also save the renders as PNGs for a visual check.
/// </summary>
public sealed class FlyoutStyleRenderTests
{
    public static TheoryData<FlyoutStyle, FlyoutTheme, bool> Cases()
    {
        var data = new TheoryData<FlyoutStyle, FlyoutTheme, bool>();
        foreach (var style in Enum.GetValues<FlyoutStyle>())
        foreach (var theme in Enum.GetValues<FlyoutTheme>())
        {
            data.Add(style, theme, false);
            data.Add(style, theme, true);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Renders(FlyoutStyle style, FlyoutTheme theme, bool isLight)
    {
        double layoutWidth = 0, height = 0;
        RunOnStaThread(() => (layoutWidth, height) = Render(style, theme, isLight));

        Assert.Equal(style == FlyoutStyle.Minimalist ? 280 : 320, layoutWidth);
        Assert.InRange(height, 60, 620);
    }

    /// <summary>Storyboards resolve their targets only when they start: start every theme's animations once.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void AnimationsStart(FlyoutTheme theme)
    {
        RunOnStaThread(() =>
        {
            var window = CreateWindow(FlyoutStyle.Classic, theme, isLight: false);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            content.Arrange(new Rect(content.DesiredSize));

            ThemeAnimation.SetIsActive(window, true);
            window.ThemeFrame.RaiseEvent(new RoutedEventArgs(ThemeAnimation.TrackChangedEvent));
            content.UpdateLayout();
            ThemeAnimation.SetIsActive(window, false);
            window.Close();
        });
    }

    public static TheoryData<FlyoutTheme> Themes() => new(Enum.GetValues<FlyoutTheme>());

    /// <summary>The widget's music meter in each theme's look, mid-song, on a dark taskbar.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void MeterRenders(FlyoutTheme theme)
    {
        double width = 0;
        RunOnStaThread(() =>
        {
            var meter = new LevelMeter { Look = FlyoutThemes.MeterOf(theme), Level = 0.8, Height = 14, Foreground = Brushes.White };
            meter.Measure(new Size(double.PositiveInfinity, 14));
            meter.Arrange(new Rect(meter.DesiredSize));
            SaveIfRequested(meter, new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)), $"Meter-{theme}.png");
            width = meter.ActualWidth;
        });

        Assert.InRange(width, 8, 40);
    }

    private static FlyoutWindow CreateWindow(FlyoutStyle style, FlyoutTheme theme, bool isLight)
    {
        if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Application.Current!.Resources["BoolToVisibility"] = new BooleanToVisibilityConverter();

        // The styles bind to their window's Foreground and read its theme resources.
        var window = new FlyoutWindow { DataContext = new SamplePlayer() };
        window.ApplyLook(new LookSettings { FlyoutStyle = style, FlyoutTheme = theme }, isLight);
        return window;
    }

    /// <returns>The layout's width and the whole content's height (layout plus the theme's frame).</returns>
    private static (double LayoutWidth, double Height) Render(FlyoutStyle style, FlyoutTheme theme, bool isLight)
    {
        var window = CreateWindow(style, theme, isLight);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Arrange(new Rect(content.DesiredSize));
        content.UpdateLayout();
        SaveIfRequested(content, (Brush)window.FindResource("FlyoutBackgroundBrush"), $"{style}-{theme}-{(isLight ? "light" : "dark")}.png");

        var layout = (FrameworkElement)window.StyleHost.Content;
        var size = (layout.ActualWidth, content.ActualHeight);
        window.Close();
        return size;
    }

    private static void SaveIfRequested(FrameworkElement element, Brush background, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("AUXLINE_RENDER_DIR");
        if (string.IsNullOrEmpty(directory)) return;

        var (width, height) = ((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight));
        var backdrop = new DrawingVisual();
        using (var context = backdrop.RenderOpen()) context.DrawRectangle(background, null, new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(backdrop);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, fileName));
        encoder.Save(file);
    }

    /// <summary>WPF needs an STA thread; xUnit runs tests on MTA threads.</summary>
    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Rendering failed", failure);
    }

    /// <summary>The properties the styles bind to, with realistic values (bindings work by name on any object).</summary>
    private sealed class SamplePlayer
    {
        public string Title => "Blinding Lights";
        public string Artist => "The Weeknd";
        public string Album => "After Hours";
        public string SourceName => "Spotify";
        public BitmapSource Cover { get; } = SolidCover(Color.FromRgb(196, 106, 46));
        public bool IsPlaying => true;
        public bool HasTimeline => true;
        public bool CanSeek => true;
        public double PositionSeconds { get; set; } = 83;
        public double DurationSeconds => 200;
        public string PositionText => "1:23";
        public string DurationText => "3:20";
        public double VolumePercent { get; set; } = 62;
        public bool HasAudioSession => true;

        private static BitmapSource SolidCover(Color color)
        {
            const int size = 64;
            var pixels = Enumerable.Range(0, size * size).SelectMany(_ => new[] { color.B, color.G, color.R, (byte)255 }).ToArray();
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
