using Auxline.Views;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Tests;

public sealed class TrayIconTests
{
    /// <summary>Both tray icons are embedded and load at the notification area's size for this machine's scaling.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadIcon_EmbeddedIcon_AtSmallIconSize(bool isTaskbarLight)
    {
        // The app's Application registers the pack:// URI scheme; this test may run before any test creates one.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var expectedSize = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());

        using var icon = AppTrayIcon.LoadIcon(isTaskbarLight);

        Assert.Equal(expectedSize, icon.Width);
        Assert.Equal(expectedSize, icon.Height);
    }
}
