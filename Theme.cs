using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace CasualtiesModManager;

/// <summary>Тёмная / светлая / системная тема. Меняет цвета общих кистей из App.xaml — окна перекрашиваются на лету.</summary>
public static class Theme
{
    public const string Dark = "dark", Light = "light", Auto = "system";

    private static readonly Dictionary<string, string> DarkColors = new()
    {
        ["BgBrush"] = "#0E0F12", ["SurfaceBrush"] = "#16181D", ["Surface2Brush"] = "#1D2027",
        ["LineBrush"] = "#2A2E37", ["TextBrush"] = "#E6E8EC", ["MutedBrush"] = "#8B93A1",
        ["AccentBrush"] = "#E5533D", ["AccentDimBrush"] = "#26E5533D",
        ["OkBrush"] = "#4CC38A", ["WarnBrush"] = "#F2B84B", ["DangerBrush"] = "#F0645A",
        ["HoverTintBrush"] = "#FFFFFF", ["OverlayBrush"] = "#E60E0F12",
        ["ScrollThumbBrush"] = "#3A404C", ["ScrollThumbHoverBrush"] = "#596070",
    };

    private static readonly Dictionary<string, string> LightColors = new()
    {
        ["BgBrush"] = "#F3F4F7", ["SurfaceBrush"] = "#FFFFFF", ["Surface2Brush"] = "#ECEEF2",
        ["LineBrush"] = "#D9DCE3", ["TextBrush"] = "#1B1E25", ["MutedBrush"] = "#667085",
        ["AccentBrush"] = "#D6432E", ["AccentDimBrush"] = "#1FD6432E",
        ["OkBrush"] = "#1E9E63", ["WarnBrush"] = "#B7791F", ["DangerBrush"] = "#D64545",
        ["HoverTintBrush"] = "#000000", ["OverlayBrush"] = "#E6F3F4F7",
        ["ScrollThumbBrush"] = "#C5CAD3", ["ScrollThumbHoverBrush"] = "#A9B0BC",
    };

    public static string Normalize(string? mode) => mode is Dark or Light or Auto ? mode : Dark;

    /// <summary>Какая тема реально включена («system» превращается в dark или light).</summary>
    public static string Resolve(string? mode)
    {
        mode = Normalize(mode);
        if (mode != Auto) return mode;
        try
        {
            var v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return v is int i && i == 0 ? Dark : Light;
        }
        catch { return Light; }
    }

    public static void Apply(string? mode)
    {
        var colors = Resolve(mode) == Light ? LightColors : DarkColors;
        var res = Application.Current.Resources;
        foreach (var (key, hex) in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            // Подменяем кисть целиком: стили замораживают свои кисти, поэтому менять Color на месте нельзя.
            // Все ссылки в XAML — DynamicResource, в коде — SetResourceReference, так что всё перекрасится само.
            res[key] = new SolidColorBrush(color);
        }
    }
}
