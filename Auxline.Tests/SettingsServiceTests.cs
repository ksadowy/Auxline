using System.IO;
using Auxline.Models;
using Auxline.Services;

namespace Auxline.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Auxline.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "settings.json");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var settings = SettingsService.Load(_path);

        AssertDefaults(settings.Current);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsDefaultsAndKeepsBrokenFile()
    {
        File.WriteAllText(_path, "{ not json");

        using var settings = SettingsService.Load(_path);

        AssertDefaults(settings.Current);
        Assert.True(File.Exists(_path + ".bad"));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Load_OutOfRangeValues_AreClamped()
    {
        File.WriteAllText(_path, """{ "Layout": { "LeftOffset": -50, "MaxWidgetWidth": 5000, "CoverSize": 1 } }""");

        using var settings = SettingsService.Load(_path);

        Assert.Equal(LayoutSettings.MinLeftOffset, settings.Current.Layout.LeftOffset);
        Assert.Equal(LayoutSettings.MaxWidgetWidthLimit, settings.Current.Layout.MaxWidgetWidth);
        Assert.Equal(LayoutSettings.MinCoverSize, settings.Current.Layout.CoverSize);
    }

    [Fact]
    public void Load_MissingSection_UsesDefaultsForIt()
    {
        File.WriteAllText(_path, """{ "Layout": null }""");

        using var settings = SettingsService.Load(_path);

        Assert.Equal(new LayoutSettings(), settings.Current.Layout);
    }

    [Fact]
    public void Update_RaisesChangedWithNormalizedSettings()
    {
        using var settings = SettingsService.Load(_path);
        AppSettings? received = null;
        settings.Changed += (_, changed) => received = changed;

        settings.Update(s => s with { Layout = s.Layout with { CoverSize = 999 } });

        Assert.Equal(LayoutSettings.MaxCoverSize, received?.Layout.CoverSize);
        Assert.Equal(received, settings.Current);
    }

    [Fact]
    public void UpdateThenFlush_PersistsAcrossLoads()
    {
        using (var settings = SettingsService.Load(_path))
        {
            settings.Update(s => s with { Layout = s.Layout with { LeftOffset = 120 } });
            settings.Flush();
        }

        using var reloaded = SettingsService.Load(_path);

        Assert.Equal(120, reloaded.Current.Layout.LeftOffset);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void UpdateThenFlush_PersistsSources()
    {
        using (var settings = SettingsService.Load(_path))
        {
            settings.Update(s => s with { Sources = [new("Chrome", IsEnabled: false), new("Spotify.exe")] });
            settings.Flush();
        }

        using var reloaded = SettingsService.Load(_path);

        Assert.Equal([new SourceEntry("Chrome", IsEnabled: false), new SourceEntry("Spotify.exe")], reloaded.Current.Sources);
    }

    /// <summary>Records compare lists by reference, so the source list is compared element by element.</summary>
    private static void AssertDefaults(AppSettings settings)
    {
        var defaults = new AppSettings();
        Assert.Equal(defaults.Layout, settings.Layout);
        Assert.Equal(defaults.Look, settings.Look);
        Assert.Equal(defaults.Sources, settings.Sources);
    }

    [Fact]
    public void Normalize_NonFiniteValues_FallBackToDefaults()
    {
        var layout = new LayoutSettings { LeftOffset = double.NaN, CoverSize = double.PositiveInfinity }.Normalize();

        Assert.Equal(new LayoutSettings().LeftOffset, layout.LeftOffset);
        Assert.Equal(new LayoutSettings().CoverSize, layout.CoverSize);
    }
}
