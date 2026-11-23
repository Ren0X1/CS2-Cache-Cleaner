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
    public static readonly string[] Palettes = ["ambar", "cobalto", "naranja", "frambuesa", "oceano", "lavanda", "grafito"];
    public static readonly string[] Modes = ["system", "light", "dark"];

    // acento claro, acento oscuro, texto sobre acento (claro/oscuro), tinte (claro), tinte texto (claro), tinte (oscuro), tinte texto (oscuro)
    private static readonly Dictionary<string, string[]> Pal = new()
    {
        ["ambar"]     = ["#B86E00", "#FFB224", "#FFFFFF", "#2A1A00", "#FBEFD6", "#5E3700", "#3A2C10", "#FFE2A8"],
        ["cobalto"]   = ["#2453E0", "#6D8FFF", "#FFFFFF", "#0B1636", "#E3EAFE", "#16307F", "#1C2A52", "#C5D3FF"],
        ["naranja"]   = ["#E2560B", "#FF8A47", "#FFFFFF", "#2A1003", "#FDE7DA", "#7A2A03", "#3B2014", "#FFD2B8"],
        ["frambuesa"] = ["#C30052", "#FF5C9A", "#FFFFFF", "#33000F", "#FBE0EA", "#6E0030", "#3D1424", "#FFC4DA"],
        ["oceano"]    = ["#0A6F94", "#4CC0E8", "#FFFFFF", "#022130", "#DBF0F8", "#06384C", "#123444", "#BFE9F8"],
        ["lavanda"]   = ["#7A3FD1", "#B174F7", "#FFFFFF", "#1E0B3A", "#EFE6FD", "#4B1C8F", "#2D2140", "#E3CCFF"],
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
        Palette = Pal.ContainsKey(palette) || TryCustom(palette, out _) ? palette : "ambar";
        Mode = Modes.Contains(mode) ? mode : "system";
        IsDark = Mode == "dark" || (Mode == "system" && SystemIsDark());

        var res = Application.Current.Resources;
        foreach (var (k, v) in IsDark ? Dark : Light) res[k] = Brush(v);
        var p = TryCustom(Palette, out var cc) ? Derive(cc) : Pal[Palette];
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
    public static Color Swatch(string palette, bool dark) =>
        (Color)ColorConverter.ConvertFromString((TryCustom(palette, out var c) ? Derive(c) : Pal[palette])[dark ? 1 : 0]);

    // ------------------------------------------------------------------
    //  Color personalizado: la "paleta" es el codigo del color (#RRGGBB)
    // ------------------------------------------------------------------
    public static bool IsCustom => TryCustom(Palette, out _);

    public static bool TryCustom(string palette, out Color c)
    {
        c = default;
        if (palette.Length != 7 || palette[0] != '#') return false;
        try { c = (Color)ColorConverter.ConvertFromString(palette); return true; } catch { return false; }
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static readonly Color SheetDark = Color.FromRgb(0x15, 0x18, 0x1D), White = Colors.White, InkLight = Color.FromRgb(0x14, 0x17, 0x1C);

    /// <summary>
    /// Genera la paleta completa a partir de un color, con el mismo formato que las fijas.
    /// El acento se aclara u oscurece lo justo para tener buen contraste en cada modo.
    /// </summary>
    public static string[] Derive(Color c)
    {
        // Modo oscuro: el acento tiene que destacar sobre el fondo oscuro
        var dark = c;
        for (int i = 0; i < 20 && Contrast(dark, SheetDark) < 4.5; i++) dark = Mix(dark, White, 0.12);
        // Modo claro: el acento tiene que leerse sobre blanco
        var light = c;
        for (int i = 0; i < 20 && Contrast(light, White) < 4.5; i++) light = Mix(light, Colors.Black, 0.12);
        var onLight = Contrast(White, light) >= 3 ? White : InkLight;
        var onDark = Contrast(Mix(dark, Colors.Black, 0.85), dark) >= 4.5 ? Mix(dark, Colors.Black, 0.85) : (Contrast(White, dark) > Contrast(InkLight, dark) ? White : InkLight);
        return
        [
            Hex(light), Hex(dark), Hex(onLight), Hex(onDark),
            Hex(Mix(c, White, 0.88)), Hex(Mix(light, Colors.Black, 0.45)),
            Hex(Mix(dark, SheetDark, 0.8)), Hex(Mix(dark, White, 0.6)),
        ];
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static double Lum(Color c)
    {
        static double Ch(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Lum(a), lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

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
