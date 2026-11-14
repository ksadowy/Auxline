using System.Windows.Controls;
using Auxline.Models;

namespace Auxline.Views.FlyoutStyles;

internal static class FlyoutStyleFactory
{
    public static UserControl Create(FlyoutStyle style) => style switch
    {
        FlyoutStyle.Native => new NativeStyle(),
        FlyoutStyle.BigCover => new BigCoverStyle(),
        FlyoutStyle.CoverTinted => new CoverTintedStyle(),
        FlyoutStyle.Compact => new CompactStyle(),
        FlyoutStyle.Minimalist => new MinimalistStyle(),
        _ => new ClassicStyle(),
    };
}
