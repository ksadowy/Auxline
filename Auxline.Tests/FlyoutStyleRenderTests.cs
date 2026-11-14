using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Auxline.Models;
using Auxline.Views.FlyoutStyles;

namespace Auxline.Tests;

/// <summary>
/// Renders every flyout style with sample data. XAML mistakes (bad resource keys, templates, bindings that throw)
/// only surface at runtime, so this catches them for all styles, not just the one the app starts with.
/// Set AUXLINE_RENDER_DIR to also save the renders as PNGs for a visual check.
/// </summary>
public sealed class FlyoutStyleRenderTests
{
    public static TheoryData<FlyoutStyle, bool> Cases()
    {
        var data = new TheoryData<FlyoutStyle, bool>();
        foreach (var style in Enum.GetValues<FlyoutStyle>())
        {
            data.Add(style, false);
            data.Add(style, true);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Renders(FlyoutStyle style, bool isLight)
    {
        Size size = default;
        RunOnStaThread(() => size = Render(style, isLight));

        Assert.Equal(style == FlyoutStyle.Minimalist ? 280 : 320, size.Width);
        Assert.InRange(size.Height, 60, 600);
    }

    private static Size Render(FlyoutStyle style, bool isLight)
    {
        if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Application.Current!.Resources["BoolToVisibility"] = new BooleanToVisibilityConverter();

        var content = FlyoutStyleFactory.Create(style);

        // A never-shown window as host: the styles bind to their Window's Foreground and read its palette.
        var host = new Border
        {
            Background = new SolidColorBrush(isLight ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20)),
            Child = content,
        };
        var window = new Window
        {
            Content = host,
            Foreground = isLight ? Brushes.Black : Brushes.White,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            DataContext = new SamplePlayer(),
        };
        FlyoutPalette.Apply(window.Resources, isLight);
        FlyoutPalette.ApplyCover(window.Resources, Color.FromRgb(196, 106, 46), isLight);

        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        SaveIfRequested(host, $"{style}-{(isLight ? "light" : "dark")}.png");
        window.Close();
        return host.RenderSize;
    }

    private static void SaveIfRequested(FrameworkElement element, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("AUXLINE_RENDER_DIR");
        if (string.IsNullOrEmpty(directory)) return;

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
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
        public string Title => "Make Me Bad";
        public string Artist => "Korn";
        public string Album => "Issues";
        public string SourceName => "Spotify";
        public BitmapSource Cover { get; } = SolidCover(Color.FromRgb(196, 106, 46));
        public bool IsPlaying => true;
        public bool HasTimeline => true;
        public bool CanSeek => true;
        public double PositionSeconds { get; set; } = 83;
        public double DurationSeconds => 232;
        public string PositionText => "1:23";
        public string DurationText => "3:52";
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
