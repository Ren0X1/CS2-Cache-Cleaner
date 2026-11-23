using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using RnxCleaner.Core;

namespace RnxCleaner;

/// <summary>
/// El HUD dibujado: estilos "a1" (clasico tipo RivaTuner), "c1" (panel RNX) y "m" (mezcla).
/// Lo usan la ventana superpuesta y la vista previa de la pestana HUD.
/// </summary>
public sealed class HudView : Border
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");
    private static readonly FontFamily Sans = new("Segoe UI Variable, Segoe UI");
    private static readonly Brush Blue = B("#6FB3FF"), Green = B("#5FE07A"), Pink = B("#FF8C9E"), Gold = B("#FFD38A"),
                                  White = Brushes.White, Unit = B("#C9CFD8"), Grey = B("#949CA8"), Ok = B("#5FE07A"),
                                  Warm = B("#FFB347"), Hot = B("#FF5E5E");
    private const double TargetMs = 1000.0 / 144;

    public HudView()
    {
        Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.5, Color = Colors.Black };
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    private static SolidColorBrush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static Brush Temp(float? t) => t is null ? Grey : t < 70 ? Ok : t < 85 ? Warm : Hot;
    private static string N(float? v, string fmt = "0") => v is null ? "—" : v.Value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
    private static string N(double v, string fmt = "0") => v <= 0 ? "—" : v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

    public void Render(HudData d, Settings s)
    {
        LayoutTransform = new ScaleTransform(s.HudScale, s.HudScale);
        var accent = new SolidColorBrush(ThemeManager.Swatch(ThemeManager.Palettes.Contains(s.Palette) ? s.Palette : "ambar", dark: true));
        Child = s.HudStyle switch
        {
            "c1" => RenderC1(d, s, accent),
            "m" => RenderM(d, s, accent),
            _ => RenderA1(d, s),
        };
    }

    private void Box(double alpha, Brush border, double radius, Thickness pad)
    {
        Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(alpha, 0, 1) * 255), 0, 0, 0));
        BorderBrush = border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(radius);
        Padding = pad;
    }

    // ------------------------------------------------------------------
    //  A1 · Clasico pulido
    // ------------------------------------------------------------------
    private UIElement RenderA1(HudData d, Settings s)
    {
        Box(s.HudOpacity, B("#14FFFFFF"), 8, new Thickness(14, 10, 14, 8));
        var root = new StackPanel();
        var g = new Grid();
        for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int row = 0;
        void Row(string label, Brush color, params (string V, string U, Brush C)[] cells)
        {
            g.RowDefinitions.Add(new RowDefinition());
            Add(g, Txt(label, color, 14.5), row, 0, new Thickness(0, 1, 20, 1), HorizontalAlignment.Left);
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].V.Length > 0) Add(g, Val(cells[i].V, cells[i].U, cells[i].C), row, i + 1, new Thickness(16, 1, 0, 1), HorizontalAlignment.Right);
            row++;
        }
        if (s.HudCpu) Row(d.CpuName, Blue, (N(d.CpuLoad), "%", Gold), (N(d.CpuClock), "MHz", Gold), (N(d.CpuTemp), "°C", Temp(d.CpuTemp)));
        if (s.HudRam) Row(d.RamName, Blue, (N(d.RamUsedMb), "MB", Gold));
        if (s.HudGpu) Row(d.GpuName, Green, (N(d.GpuLoad), "%", Gold), (N(d.GpuClock), "MHz", Gold), (N(d.GpuTemp), "°C", Temp(d.GpuTemp)));
        if (s.HudFps)
        {
            Row("FPS", Pink, (N(d.Fps), "", White), (N(d.AvgFps), "AVG", White), (N(d.Low1), "1%", White));
            Row(d.FrameMs > 0 ? $"{d.FrameMs:0.0} ms" : "— ms", Pink, ("", "", White), ("", "", White), (N(d.Low01), "0.1%", White));
            ((TextBlock)g.Children[^2]).FontSize = 11;
        }
        root.Children.Add(g);
        if (s.HudGraph)
        {
            root.Children.Add(Graph(d.FrameTimes, 318, 34, Pink, B("#73FF8C9E")));
            var leg = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var r = Txt("— 144 fps (6.9 ms)", Grey, 9.5, FontWeights.SemiBold); DockPanel.SetDock(r, Dock.Right); leg.Children.Add(r);
            leg.Children.Add(Txt("frametime", Grey, 9.5, FontWeights.SemiBold));
            root.Children.Add(leg);
        }
        return root;
    }

    // ------------------------------------------------------------------
    //  C1 · Panel RNX
    // ------------------------------------------------------------------
    private UIElement RenderC1(HudData d, Settings s, Brush accent)
    {
        var ac = ((SolidColorBrush)accent).Color;
        Box(Math.Min(1, s.HudOpacity + 0.16), new SolidColorBrush(Color.FromArgb(82, ac.R, ac.G, ac.B)), 12, new Thickness(14, 12, 14, 12));
        Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(s.HudOpacity + 0.16, 0, 1) * 255), 13, 15, 19));
        var root = new StackPanel { Width = 234 };
        if (s.HudFps)
        {
            var top = new DockPanel();
            var lows = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
            lows.ColumnDefinitions.Add(new ColumnDefinition()); lows.ColumnDefinitions.Add(new ColumnDefinition());
            int r = 0;
            foreach (var (k, v) in new[] { ("AVG", d.AvgFps), ("1%", d.Low1), ("0.1%", d.Low01) })
            {
                lows.RowDefinitions.Add(new RowDefinition());
                Add(lows, Txt(k, Grey, 11, FontWeights.Normal, Sans), r, 0, new Thickness(0, 0, 8, 0), HorizontalAlignment.Right);
                Add(lows, Txt(N(v), White, 11, FontWeights.Bold, Sans), r++, 1, new Thickness(0), HorizontalAlignment.Right);
            }
            DockPanel.SetDock(lows, Dock.Right);
            top.Children.Add(lows);
            var big = new TextBlock { FontFamily = Sans, VerticalAlignment = VerticalAlignment.Bottom };
            big.Inlines.Add(new Run(N(d.Fps)) { FontSize = 38, FontWeight = FontWeights.ExtraBold, Foreground = accent });
            big.Inlines.Add(new Run(" FPS") { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Unit });
            top.Children.Add(big);
            root.Children.Add(top);
        }
        if (s.HudGraph)
        {
            var gr = Graph(d.FrameTimes, 234, 30, accent, new SolidColorBrush(Color.FromArgb(115, ac.R, ac.G, ac.B)));
            gr.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(gr);
            var ms = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var v = Txt(d.FrameMs > 0 ? $"{d.FrameMs:0.0} ms" : "—", Unit, 11, FontWeights.Normal, Sans); DockPanel.SetDock(v, Dock.Right); ms.Children.Add(v);
            ms.Children.Add(Txt("frametime", Unit, 11, FontWeights.Normal, Sans));
            root.Children.Add(ms);
        }
        void Comp(string tag, string main, string sub, float? temp, double pct)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            Add(g, Txt(tag, Grey, 10.5, FontWeights.ExtraBold, Sans), 0, 0, new Thickness(0), HorizontalAlignment.Left);
            var line = new DockPanel();
            if (temp is not null)
            {
                var chip = new Border { CornerRadius = new CornerRadius(999), Background = B("#14FFFFFF"), Padding = new Thickness(6, 0, 6, 0), Child = Txt($"{temp:0}°", Temp(temp), 10.5, FontWeights.Bold, Sans) };
                DockPanel.SetDock(chip, Dock.Right); line.Children.Add(chip);
            }
            var t = new TextBlock { FontFamily = Sans };
            t.Inlines.Add(new Run(main) { Foreground = White, FontWeight = FontWeights.Bold, FontSize = 12 });
            t.Inlines.Add(new Run("  " + sub) { Foreground = Grey, FontWeight = FontWeights.SemiBold, FontSize = 11 });
            line.Children.Add(t);
            Add(g, line, 0, 1, new Thickness(0), HorizontalAlignment.Stretch);
            var bar = new Grid { Height = 4, Margin = new Thickness(0, 3, 0, 0) };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = B("#1AFFFFFF") });
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 190 * Math.Clamp(pct, 0, 1) });
            Add(g, bar, 1, 1, new Thickness(0), HorizontalAlignment.Stretch);
            root.Children.Add(g);
        }
        if (s.HudCpu) Comp("CPU", d.CpuLoad is null ? "—" : $"{d.CpuLoad:0}%", d.CpuClock is null ? "" : $"{d.CpuClock:0} MHz", d.CpuTemp, (d.CpuLoad ?? 0) / 100.0);
        if (s.HudGpu) Comp("GPU", d.GpuLoad is null ? "—" : $"{d.GpuLoad:0}%", d.GpuClock is null ? "" : $"{d.GpuClock:0} MHz", d.GpuTemp, (d.GpuLoad ?? 0) / 100.0);
        if (s.HudGpu && d.VramUsedMb is not null)
            Comp("VRAM", $"{d.VramUsedMb / 1024:0.0}", d.VramTotalMb is > 0 ? $"/ {d.VramTotalMb / 1024:0} GB" : "GB", null, d.VramTotalMb is > 0 ? (d.VramUsedMb ?? 0) / d.VramTotalMb!.Value : 0);
        if (s.HudRam)
            Comp("RAM", d.RamUsedMb is null ? "—" : $"{d.RamUsedMb / 1024:0.0}", d.RamTotalMb > 0 ? $"/ {d.RamTotalMb / 1024:0} GB" : "GB", null, d.RamTotalMb > 0 ? (d.RamUsedMb ?? 0) / d.RamTotalMb : 0);
        return root;
    }

    // ------------------------------------------------------------------
    //  M · Mezcla (FPS grande + tabla clasica)
    // ------------------------------------------------------------------
    private UIElement RenderM(HudData d, Settings s, Brush accent)
    {
        var ac = ((SolidColorBrush)accent).Color;
        Box(s.HudOpacity, B("#14FFFFFF"), 10, new Thickness(0));
        var root = new StackPanel { Orientation = Orientation.Horizontal };
        if (s.HudFps || s.HudGraph)
        {
            var left = new StackPanel { Margin = new Thickness(0), MinWidth = 180 };
            var lb = new Border { Background = new SolidColorBrush(Color.FromArgb(15, ac.R, ac.G, ac.B)), Padding = new Thickness(14, 10, 14, 10), Child = left,
                                  BorderBrush = B("#14FFFFFF"), BorderThickness = new Thickness(0, 0, s.HudCpu || s.HudGpu || s.HudRam ? 1 : 0, 0) };
            if (s.HudFps)
            {
                var big = new TextBlock { FontFamily = Sans };
                big.Inlines.Add(new Run(N(d.Fps)) { FontSize = 40, FontWeight = FontWeights.ExtraBold, Foreground = accent });
                big.Inlines.Add(new Run(" FPS") { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Unit });
                left.Children.Add(big);
                var lows = new TextBlock { FontFamily = Sans, FontSize = 11, Foreground = Grey, Margin = new Thickness(0, 4, 0, 0) };
                lows.Inlines.Add(new Run("AVG ")); lows.Inlines.Add(new Run(N(d.AvgFps)) { Foreground = White, FontWeight = FontWeights.Bold });
                lows.Inlines.Add(new Run(" · 1% ")); lows.Inlines.Add(new Run(N(d.Low1)) { Foreground = White, FontWeight = FontWeights.Bold });
                lows.Inlines.Add(new Run(" · 0.1% ")); lows.Inlines.Add(new Run(N(d.Low01)) { Foreground = White, FontWeight = FontWeights.Bold });
                left.Children.Add(lows);
            }
            if (s.HudGraph)
            {
                var gr = Graph(d.FrameTimes, 180, 22, accent, null);
                gr.Margin = new Thickness(0, 6, 0, 0);
                left.Children.Add(gr);
                var ft = new TextBlock { FontFamily = Sans, FontSize = 11, Foreground = Grey, Margin = new Thickness(0, 3, 0, 0) };
                ft.Inlines.Add(new Run("frametime ")); ft.Inlines.Add(new Run(d.FrameMs > 0 ? $"{d.FrameMs:0.0} ms" : "—") { Foreground = White, FontWeight = FontWeights.Bold });
                left.Children.Add(ft);
            }
            root.Children.Add(lb);
        }
        if (s.HudCpu || s.HudGpu || s.HudRam)
        {
            var g = new Grid { Margin = new Thickness(14, 9, 14, 9), VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int row = 0;
            void Row(string label, Brush color, params (string V, string U, Brush C)[] cells)
            {
                g.RowDefinitions.Add(new RowDefinition());
                Add(g, Txt(label, color, 13.5), row, 0, new Thickness(0, 1, 6, 1), HorizontalAlignment.Left);
                for (int i = 0; i < cells.Length; i++)
                    Add(g, Val(cells[i].V, cells[i].U, cells[i].C, 13.5), row, i + 1, new Thickness(14, 1, 0, 1), HorizontalAlignment.Right);
                row++;
            }
            if (s.HudCpu) Row(d.CpuName, Blue, (N(d.CpuLoad), "%", Gold), (N(d.CpuClock), "MHz", Gold), (N(d.CpuTemp), "°C", Temp(d.CpuTemp)));
            if (s.HudGpu)
            {
                Row(d.GpuName, Green, (N(d.GpuLoad), "%", Gold), (N(d.GpuClock), "MHz", Gold), (N(d.GpuTemp), "°C", Temp(d.GpuTemp)));
                if (d.VramUsedMb is not null) Row("VRAM", Green, ((d.VramUsedMb.Value / 1024).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), "GB", Gold));
            }
            if (s.HudRam) Row(d.RamName, Blue, (d.RamUsedMb is null ? "—" : (d.RamUsedMb.Value / 1024).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), "GB", Gold));
            root.Children.Add(g);
        }
        return root;
    }

    // ------------------------------------------------------------------
    //  Piezas
    // ------------------------------------------------------------------
    private static TextBlock Txt(string text, Brush color, double size, FontWeight? weight = null, FontFamily? font = null) =>
        new() { Text = text, Foreground = color, FontSize = size, FontWeight = weight ?? FontWeights.Bold, FontFamily = font ?? Mono };

    private static TextBlock Val(string value, string unit, Brush color, double size = 14.5)
    {
        var t = new TextBlock { FontFamily = Mono, FontWeight = FontWeights.Bold, FontSize = size };
        t.Inlines.Add(new Run(value) { Foreground = color });
        if (unit.Length > 0) t.Inlines.Add(new Run(unit) { Foreground = Unit, FontSize = size * 0.6, FontWeight = FontWeights.SemiBold, BaselineAlignment = BaselineAlignment.Superscript });
        return t;
    }

    private static void Add(Grid g, UIElement e, int row, int col, Thickness margin, HorizontalAlignment h)
    {
        if (e is FrameworkElement fe) { fe.Margin = margin; fe.HorizontalAlignment = h; fe.VerticalAlignment = VerticalAlignment.Center; }
        Grid.SetRow(e, row); Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    /// <summary>Grafica de frametime (ms) con linea discontinua del objetivo de 144 FPS.</summary>
    private static FrameworkElement Graph(float[] ft, double w, double h, Brush line, Brush? fill)
    {
        var c = new Canvas { Width = w, Height = h, ClipToBounds = true };
        double max = Math.Max(TargetMs * 2.2, ft.Length > 0 ? Math.Min(ft.Max() * 1.1, 60) : 0);
        double Y(double ms) => h - 2 - Math.Min(ms, max) / max * (h - 4);
        c.Children.Add(new Line { X1 = 0, X2 = w, Y1 = Y(TargetMs), Y2 = Y(TargetMs), Stroke = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)), StrokeDashArray = [3, 3] });
        if (ft.Length > 1)
        {
            var pts = new PointCollection();
            for (int i = 0; i < ft.Length; i++) pts.Add(new Point(i * w / (ft.Length - 1), Y(ft[i])));
            if (fill is SolidColorBrush fb)
            {
                var poly = new PointCollection(pts) { new Point(w, h), new Point(0, h) };
                var col = fb.Color;
                c.Children.Add(new Polygon
                {
                    Points = poly,
                    Fill = new LinearGradientBrush(col, Color.FromArgb(0, col.R, col.G, col.B), 90),
                });
            }
            c.Children.Add(new Polyline { Points = pts, Stroke = line, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round });
        }
        return c;
    }

    /// <summary>Datos de ejemplo para la vista previa cuando el HUD esta apagado.</summary>
    public static HudData Demo()
    {
        var rnd = new Random(3);
        var ft = Enumerable.Range(0, 200).Select(i => (float)(3.4 + rnd.NextDouble() * 0.6 + (i % 63 == 0 ? 6 : 0))).ToArray();
        return new HudData("i5-11600K", 45, 4600, 62, "16GB DDR4", 11562, 16384, "RTX 3060", 98, 1890, 74, 5222, 12288,
                           "cs2", 287, 264, 181, 142, 3.5, ft);
    }
}

/// <summary>Ventana superpuesta: siempre encima, transparente y que deja pasar los clics.</summary>
public sealed class HudWindow : Window
{
    public HudView View { get; } = new();
    private string _corner = "tl";
    private bool _locked = true;
    private readonly Border _frame;
    private IntPtr _hwnd;

    /// <summary>El usuario ha soltado el HUD en otra posicion (modo libre).</summary>
    public event Action<double, double>? Moved;

    /// <summary>Posiciones fijas: tl, tc, tr, bl, bc, br. "free" = donde lo deje el raton.</summary>
    public static readonly string[] Corners = ["tl", "tc", "tr", "bl", "bc", "br"];
    public double FreeX { get; set; } = 40;
    public double FreeY { get; set; } = 40;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);

    public HudWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        _frame = new Border { Padding = new Thickness(12), Child = View, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12) };   // margen para la sombra
        Content = _frame;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyLock();
        };
        SizeChanged += (_, _) => Place();
        // El juego cambia la resolucion (p. ej. 1352x1080 estirado): recolocar en la pantalla nueva
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        // Candado abierto: se arrastra con el raton y se guarda la posicion al soltar
        MouseLeftButtonDown += (_, e) =>
        {
            if (_locked) return;
            try { DragMove(); } catch { }
            _corner = "free";
            FreeX = Left; FreeY = Top;
            Moved?.Invoke(Left, Top);
        };
    }

    public void SetCorner(string corner, double x, double y)
    {
        if (corner == _corner && (corner != "free" || (x == FreeX && y == FreeY))) return;
        _corner = corner; FreeX = x; FreeY = y;
        Place();
    }

    /// <summary>Candado cerrado: deja pasar los clics al juego. Abierto: se puede mover.</summary>
    public void SetLocked(bool locked)
    {
        if (locked == _locked) return;
        _locked = locked;
        ApplyLock();
    }

    private void ApplyLock()
    {
        const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        if (_hwnd != IntPtr.Zero)
        {
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            ex = _locked ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
        }
        // Sin candado se ve un marco discontinuo para saber que se puede mover
        _frame.BorderBrush = _locked ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(200, 255, 178, 36));
        _frame.Background = _locked ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Cursor = _locked ? null : System.Windows.Input.Cursors.SizeAll;
    }

    private void OnDisplayChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(() => { Place(); KeepOnTop(); });

    private void Place()
    {
        double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
        if (_corner == "free")
        {
            Left = Math.Clamp(FreeX, 0, Math.Max(0, sw - ActualWidth));
            Top = Math.Clamp(FreeY, 0, Math.Max(0, sh - ActualHeight));
            return;
        }
        Left = _corner[1] switch { 'r' => sw - ActualWidth, 'c' => (sw - ActualWidth) / 2, _ => 0 };
        Top = _corner[0] == 'b' ? sh - ActualHeight : 0;
    }

    /// <summary>Los juegos a veces se ponen encima: se reafirma cada poco.</summary>
    public void KeepOnTop()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
    }
}
