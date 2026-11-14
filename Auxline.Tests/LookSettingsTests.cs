using System.IO;
using Auxline.Models;
using Auxline.Services;

namespace Auxline.Tests;

public sealed class LookSettingsTests
{
    [Theory]
    [InlineData("#ff8800", "#FF8800")]
    [InlineData("#80FF8800", "#80FF8800")]
    [InlineData("ff8800", null)]
    [InlineData("#ff88", null)]
    [InlineData("#gg8800", null)]
    [InlineData("", null)]
    public void Normalize_TextColor_KeepsOnlyValidHex(string input, string? expected)
    {
        Assert.Equal(expected, new LookSettings { TextColor = input }.Normalize().TextColor);
    }

    [Fact]
    public void Normalize_ClampsFontSizes()
    {
        var look = new LookSettings { TitleFontSize = 100, ArtistFontSize = 1 }.Normalize();

        Assert.Equal(LookSettings.MaxFontSize, look.TitleFontSize);
        Assert.Equal(LookSettings.MinFontSize, look.ArtistFontSize);
    }

    [Fact]
    public void Normalize_BlankFont_FallsBackToDefault()
    {
        Assert.Equal(LookSettings.DefaultFontFamily, new LookSettings { FontFamily = "  " }.Normalize().FontFamily);
    }

    [Fact]
    public void Normalize_UndefinedEnums_FallBackToDefaults()
    {
        var look = new LookSettings { Theme = (ThemePreference)42, FlyoutCorners = (CornerStyle)42, FlyoutTheme = (FlyoutTheme)42 }.Normalize();

        Assert.Equal(ThemePreference.System, look.Theme);
        Assert.Equal(CornerStyle.Round, look.FlyoutCorners);
        Assert.Equal(FlyoutTheme.Default, look.FlyoutTheme);
    }

    [Fact]
    public void SaveAndLoad_WritesEnumsAsNames()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Auxline.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            using (var settings = SettingsService.Load(path))
            {
                settings.Update(s => s with { Look = s.Look with { Theme = ThemePreference.Dark, FlyoutCorners = CornerStyle.Square } });
                settings.Flush();
            }

            Assert.Contains("\"Dark\"", File.ReadAllText(path));
            using var reloaded = SettingsService.Load(path);
            Assert.Equal(ThemePreference.Dark, reloaded.Current.Look.Theme);
            Assert.Equal(CornerStyle.Square, reloaded.Current.Look.FlyoutCorners);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
