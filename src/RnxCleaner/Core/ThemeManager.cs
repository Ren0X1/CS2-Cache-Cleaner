using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace RnxCleaner.Core;

/// <summary>
/// Paleta (ambar, cobalto...) x modo (claro/oscuro). "system" sigue el modo de
/// aplicaciones de Windows en directo. Los colores se ponen como pinceles en
/// Application.Resources y la interfaz los usa con DynamicResource.
/// </summary>
public static class ThemeManager
{
    public static readonly string[] Palettes = ["ambar", "cobalto", "naranja", "frambuesa", "oceano", "grafito"];
    public static readonly string[] Modes = ["system", "light", "dark"];

    // acento claro, acento oscuro, texto sobre acento (claro/oscuro), tinte (claro), tinte texto (claro), tinte (oscuro), tinte texto (oscuro)
    private static readonly Dictionary<string, string[]> Pal = new()
    {
        ["ambar"]     = ["#B86E00", "#FFB224", "#FFFFFF", "#2A1A00", "#FBEFD6", "#5E3700", "#3A2C10", "#FFE2A8"],
        ["cobalto"]   = ["#2453E0", "#6D8FFF", "#FFFFFF", "#0B1636", "#E3EAFE", "#16307F", "#1C2A52", "#C5D3FF"],
        ["naranja"]   = ["#E2560B", "#FF8A47", "#FFFFFF", "#2A1003", "#FDE7DA", "#7A2A03", "#3B2014", "#FFD2B8"],
        ["frambuesa"] = ["#C30052", "#FF5C9A", "#FFFFFF", "#33000F", "#FBE0EA", "#6E0030", "#3D1424", "#FFC4DA"],
        ["oceano"]    = ["#0A6F94", "#4CC0E8", "#FFFFFF", "#022130", "#DBF0F8", "#06384C", "#123444", "#BFE9F8"],
        ["grafito"]   = ["#14171C", "#E8EBF0", "#FFFFFF", "#14171C", "#E9EBEE", "#14171C", "#2B3039", "#E8EBF0"],
    };

    private static readonly Dictionary<string, string> Light = new()
    {
        ["Frame"] = "#EEF0F3", ["Sheet"] = "#FFFFFF", ["Edge"] = "#E2E5EA", ["Line"] = "#E5E8ED", ["Line2"] = "#EFF1F4",
        ["Ink"] = "#14171C", ["Muted"] = "#626973", ["Faint"] = "#8A919B", ["Nav"] = "#454B55", ["Active"] = "#DDE1E8",
        ["Scope"] = "#E3E6EB", ["Seg"] = "#F1F3F6", ["SegOn"] = "#FFFFFF", ["SegRing"] = "#E0E4EA", ["Track"] = "#ECEEF2",
        ["Bar"] = "#D3D9E4", ["PillG"] = "#EFF1F4", ["PillGInk"] = "#4A515C", ["Warn"] = "#B4410C", ["Error"] = "#C42B1C",
        ["Shade"] = "#99070A0D",
    };
    private static readonly Dictionary<string, string> Dark = new()
    {
        ["Frame"] = "#0D0F13", ["Sheet"] = "#15181D", ["Edge"] = "#23272E", ["Line"] = "#262B33", ["Line2"] = "#20242B",
        ["Ink"] = "#E8EBF0", ["Muted"] = "#949CA8", ["Faint"] = "#6E7681", ["Nav"] = "#A9B0BA", ["Active"] = "#232831",
        ["Scope"] = "#1B1F25", ["Seg"] = "#1D2128", ["SegOn"] = "#2B3039", ["SegRing"] = "#343A44", ["Track"] = "#232831",
        ["Bar"] = "#363D49", ["PillG"] = "#232831", ["PillGInk"] = "#B3BAC4", ["Warn"] = "#FB923C", ["Error"] = "#FF6B5E",
        ["Shade"] = "#B3000000",
    };

    public static string Palette { get; private set; } = "ambar";
    public static string Mode { get; private set; } = "system";
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    private static bool _hooked;

    public static void Apply(string palette, string mode)
    {
        Palette = Pal.ContainsKey(palette) ? palette : "ambar";
        Mode = Modes.Contains(mode) ? mode : "system";
        IsDark = Mode == "dark" || (Mode == "system" && SystemIsDark());

        var res = Application.Current.Resources;
        foreach (var (k, v) in IsDark ? Dark : Light) res[k] = Brush(v);
        var p = Pal[Palette];
        res["Accent"] = Brush(IsDark ? p[1] : p[0]);
        res["OnAccent"] = Brush(IsDark ? p[3] : p[2]);
        res["Tint"] = Brush(IsDark ? p[6] : p[4]);
        res["TintInk"] = Brush(IsDark ? p[7] : p[5]);

        if (!_hooked)
        {
            _hooked = true;
            // Windows cambia de claro a oscuro: si estamos en "Sistema", seguirlo al momento
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (Mode == "system" && e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                    Application.Current?.Dispatcher.BeginInvoke(() => Apply(Palette, Mode));
            };
        }
        Changed?.Invoke();
    }

    /// <summary>Color de muestra de una paleta (para los circulos de seleccion).</summary>
    public static Color Swatch(string palette, bool dark) => (Color)ColorConverter.ConvertFromString(Pal[palette][dark ? 1 : 0]);

    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
