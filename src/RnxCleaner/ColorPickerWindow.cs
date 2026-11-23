using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>
/// Selector de color personalizado: cuadro de saturacion/brillo, barra de tono y codigo hexadecimal.
/// La app se previsualiza en directo; "Cancelar" deja el color que habia.
/// </summary>
public sealed class ColorPickerWindow : Window
{
    private const double SvW = 300, SvH = 180, HueH = 16;
    private double _h, _s, _v;
    private bool _syncing;

    private readonly Rectangle _svBase;
    private readonly Ellipse _svThumb;
    private readonly Border _hueThumb;
    private readonly TextBox _hex;
    private readonly Border _preview;
    private readonly TextBlock _contrast;
    private readonly Action<string> _live;

    public string? Result { get; private set; }

    public ColorPickerWindow(Color start, Action<string> live)
    {
        _live = live;
        Title = T("cp.title");
        Width = 400; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Sheet");
        SetResourceReference(ForegroundProperty, "Ink");
        FontFamily = (FontFamily)FindResource("UiFont");

        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        root.Children.Add(new TextBlock { Text = T("cp.title"), Style = (Style)FindResource("H2"), FontSize = 18 });
        root.Children.Add(new TextBlock { Text = T("cp.sub"), Style = (Style)FindResource("Sub"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) });

        // Cuadro saturacion (horizontal) / brillo (vertical)
        var sv = new Grid { Width = SvW, Height = SvH, Cursor = Cursors.Cross, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        _svBase = new Rectangle { RadiusX = 8, RadiusY = 8 };
        sv.Children.Add(_svBase);
        sv.Children.Add(new Rectangle { RadiusX = 8, RadiusY = 8, Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) });
        sv.Children.Add(new Rectangle { RadiusX = 8, RadiusY = 8, Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
        _svThumb = new Ellipse { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2.5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                                 Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.6 } };
        sv.Children.Add(_svThumb);
        sv.MouseLeftButtonDown += (_, e) => { sv.CaptureMouse(); PickSv(e.GetPosition(sv)); };
        sv.MouseMove += (_, e) => { if (sv.IsMouseCaptured) PickSv(e.GetPosition(sv)); };
        sv.MouseLeftButtonUp += (_, _) => sv.ReleaseMouseCapture();
        root.Children.Add(sv);

        // Barra de tono
        var hue = new Grid { Width = SvW, Height = HueH + 6, Margin = new Thickness(0, 12, 0, 0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(FromHsv(i * 60 % 360, 1, 1), i / 6.0));
        hue.Children.Add(new Border { Height = HueH, CornerRadius = new CornerRadius(8), Background = rainbow, VerticalAlignment = VerticalAlignment.Center });
        _hueThumb = new Border { Width = 8, Height = HueH + 6, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2), BorderBrush = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false,
                                 Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.6 } };
        hue.Children.Add(_hueThumb);
        hue.MouseLeftButtonDown += (_, e) => { hue.CaptureMouse(); PickHue(e.GetPosition(hue).X); };
        hue.MouseMove += (_, e) => { if (hue.IsMouseCaptured) PickHue(e.GetPosition(hue).X); };
        hue.MouseLeftButtonUp += (_, _) => hue.ReleaseMouseCapture();
        root.Children.Add(hue);

        // Hex + vista previa
        var row = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        _preview = new Border { Width = 44, Height = 34, CornerRadius = new CornerRadius(8), Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(_preview, Dock.Right);
        row.Children.Add(_preview);
        row.Children.Add(new TextBlock { Text = "HEX", Style = (Style)FindResource("Sub"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        _hex = new TextBox { FontSize = 15, Padding = new Thickness(8, 6, 8, 6), MaxLength = 7, BorderThickness = new Thickness(1), FontFamily = (FontFamily)FindResource("Mono") };
        _hex.SetResourceReference(BackgroundProperty, "Scope");
        _hex.SetResourceReference(ForegroundProperty, "Ink");
        _hex.SetResourceReference(BorderBrushProperty, "Edge");
        _hex.SetResourceReference(TextBox.CaretBrushProperty, "Accent");
        _hex.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            var t = _hex.Text.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            if (ThemeManager.TryCustom(t.ToUpperInvariant(), out var c)) { SetFromColor(c, updateHex: false); }
        };
        row.Children.Add(_hex);
        root.Children.Add(row);

        _contrast = new TextBlock { Style = (Style)FindResource("Sub"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        root.Children.Add(_contrast);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = T("dlg.cancel"), Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; };
        var ok = new Button { Content = T("ws.apply"), Style = (Style)FindResource("PrimaryButton"), IsDefault = true };
        ok.Click += (_, _) => { Result = ThemeManager.Hex(FromHsv(_h, _s, _v)); DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => SetFromColor(start, updateHex: true, preview: false);
    }

    private void PickSv(Point p)
    {
        _s = Math.Clamp(p.X / SvW, 0, 1);
        _v = 1 - Math.Clamp(p.Y / SvH, 0, 1);
        Update(updateHex: true);
    }

    private void PickHue(double x)
    {
        _h = Math.Clamp(x / SvW, 0, 1) * 359.9;
        Update(updateHex: true);
    }

    private void SetFromColor(Color c, bool updateHex, bool preview = true)
    {
        (_h, _s, _v) = ToHsv(c);
        Update(updateHex, preview);
    }

    private void Update(bool updateHex, bool preview = true)
    {
        var c = FromHsv(_h, _s, _v);
        _svBase.Fill = new SolidColorBrush(FromHsv(_h, 1, 1));
        _svThumb.Margin = new Thickness(_s * SvW - 8, (1 - _v) * SvH - 8, 0, 0);
        _hueThumb.Margin = new Thickness(_h / 360 * SvW - 4, 0, 0, 0);
        _preview.Background = new SolidColorBrush(c);
        if (updateHex) { _syncing = true; _hex.Text = ThemeManager.Hex(c); _syncing = false; }
        // Aviso si el color se ajusta para que se lea bien
        var d = ThemeManager.Derive(c);
        _contrast.Text = d[0] != ThemeManager.Hex(c) || d[1] != ThemeManager.Hex(c) ? T("cp.adjusted") : T("cp.ok");
        if (preview) _live(ThemeManager.Hex(c));
    }

    public static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = (h % 360) switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }
}
