using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Windows → Mods y arreglos: reloj de la barra de tareas (estilo Windhawk), arreglos y Open NAT.</summary>
public partial class MainWindow
{
    private bool _modsHooked, _clockLoading;
    private TextBox? _clockFocus;

    private static readonly string[] ClockFonts = ["Segoe UI Variable Text", "Segoe UI", "Segoe UI Semibold", "Bahnschrift", "Cascadia Mono", "Consolas", "Bebas Neue", "Arial"];

    private void LoadMods()
    {
        if (!_modsHooked)
        {
            _modsHooked = true;
            App.ClockTick += () => { if (PageMods.Visibility == Visibility.Visible) RenderClockPreview(); };
            foreach (var tb in new[] { ClockTopBox, ClockBottomBox }) tb.GotFocus += (_, _) => _clockFocus = tb;
            foreach (var tb in new[] { ClockTopBox, ClockBottomBox, ClockTimeBox, ClockDateBox })
            {
                tb.SetResourceReference(BackgroundProperty, "Scope");
                tb.SetResourceReference(ForegroundProperty, "Ink");
                tb.SetResourceReference(BorderBrushProperty, "Edge");
            }
        }
        var s = _settings;
        _clockLoading = true;
        ClockToggle.IsChecked = s.ClockEnabled;
        ClockTopBox.Text = s.ClockTop; ClockBottomBox.Text = s.ClockBottom;
        ClockTimeBox.Text = s.ClockTimeFormat; ClockDateBox.Text = s.ClockDateFormat;
        ClockBoldBox.IsChecked = s.ClockBold;
        _clockLoading = false;

        // Plantillas
        ClockPresets.Children.Clear();
        foreach (var p in TaskbarClock.Presets)
        {
            var b = new Button { Content = T("clock.preset." + p.Id), Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 8) };
            var pp = p;
            b.Click += (_, _) => { s.ClockTop = pp.Top; s.ClockBottom = pp.Bottom; s.ClockTimeFormat = pp.Time; s.ClockDateFormat = pp.Date; SaveClock(); LoadMods(); };
            ClockPresets.Children.Add(b);
        }
        // Variables: clic = se inserta en la linea que se este editando
        ClockVars.Children.Clear();
        foreach (var v in TaskbarClock.Variables)
        {
            var b = new Button { Content = v, Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 3, 8, 3), FontSize = 11.5,
                                 FontFamily = (FontFamily)FindResource("Mono"), ToolTip = T("clock.var." + v.Trim('%')) };
            var vv = v;
            b.Click += (_, _) =>
            {
                var tb = _clockFocus ?? ClockBottomBox;
                int at = Math.Clamp(tb.CaretIndex, 0, tb.Text.Length);
                tb.Text = tb.Text.Insert(at, (at > 0 && tb.Text[at - 1] != ' ' ? " " : "") + vv);
                tb.CaretIndex = Math.Min(tb.Text.Length, at + vv.Length + 1);
                tb.Focus();
            };
            ClockVars.Children.Add(b);
        }
        // Fuente
        var list = new StackPanel();
        var popup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 6 };
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), MinWidth = 220, Child = list };
        border.SetResourceReference(Border.BackgroundProperty, "Sheet");
        border.SetResourceReference(Border.BorderBrushProperty, "Edge");
        popup.Child = border;
        foreach (var f in ClockFonts.Where(f => Fonts.SystemFontFamilies.Any(x => x.Source.Equals(f, StringComparison.OrdinalIgnoreCase)) || f.StartsWith("Segoe")))
        {
            var ff = f;
            var row = MenuRow(f, f.Equals(s.ClockFont, StringComparison.OrdinalIgnoreCase), null, () => { s.ClockFont = ff; SaveClock(); LoadMods(); }, popup);
            try { ((DockPanel)row.Content).Children.OfType<TextBlock>().Last().FontFamily = new FontFamily(f); } catch { }
            list.Children.Add(row);
        }
        var fb = new Button { Style = (Style)FindResource("GhostButton"), MinWidth = 190, Content = s.ClockFont + "  ▾" };
        popup.PlacementTarget = fb;
        fb.Click += (_, _) => popup.IsOpen = true;
        var host = new Grid();
        host.Children.Add(fb); host.Children.Add(popup);
        ClockFontHost.Content = host;
        // Tamano
        Seg(ClockSizes, "clocksize", [("10", "10"), ("11", "11"), ("12", "12"), ("13", "13"), ("14", "14")],
            ((int)Math.Round(s.ClockSize)).ToString(), v => { s.ClockSize = double.Parse(v); SaveClock(); });
        // Color: automatico, blanco, negro, acento y personalizado
        ClockColors.Children.Clear();
        void Swatch(string value, Brush fill, string tip)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SwatchPick"), Background = fill, IsChecked = s.ClockColor.Equals(value, StringComparison.OrdinalIgnoreCase), GroupName = "clockcolor", ToolTip = tip, Margin = new Thickness(0, 0, 2, 0) };
            var val = value;
            rb.Click += (_, _) => { s.ClockColor = val; SaveClock(); LoadMods(); };
            ClockColors.Children.Add(rb);
        }
        var auto = new LinearGradientBrush(Colors.White, Color.FromRgb(0x20, 0x20, 0x20), 45);
        Swatch("", auto, T("clock.color.auto"));
        Swatch("#FFFFFF", Brushes.White, "#FFFFFF");
        Swatch("#000000", Brushes.Black, "#000000");
        var acc = ((SolidColorBrush)FindResource("Accent")).Color;
        Swatch(ThemeManager.Hex(acc), new SolidColorBrush(acc), T("clock.color.accent"));
        bool custom = s.ClockColor.Length == 7 && !new[] { "#FFFFFF", "#000000", ThemeManager.Hex(acc) }.Contains(s.ClockColor, StringComparer.OrdinalIgnoreCase);
        var cb = new RadioButton { Style = (Style)FindResource("SwatchPick"), GroupName = "clockcolor", IsChecked = custom, ToolTip = T("pal.custom"),
                                   Background = custom ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.ClockColor)) : new LinearGradientBrush(Colors.Red, Colors.Blue, 45) };
        cb.Click += (_, _) =>
        {
            var start = custom ? (Color)ColorConverter.ConvertFromString(s.ClockColor) : Colors.White;
            var prev = s.ClockColor;
            var w = new ColorPickerWindow(start, hex => { s.ClockColor = hex; App.ApplyClock(); RenderClockPreview(); }) { Owner = this };
            if (w.ShowDialog() == true && w.Result is { } r) s.ClockColor = r; else s.ClockColor = prev;
            SaveClock(); LoadMods();
        };
        ClockColors.Children.Add(cb);
        RenderClockPreview();
    }

    private void RenderClockPreview()
    {
        var s = _settings;
        var (top, bottom, tip) = App.ClockRunning ? (App.ClockPreviewTop, App.ClockPreviewBottom, "") : App.ClockText(s, null);
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        bool light = k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        ClockPreviewBar.Background = new SolidColorBrush(light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20));
        Brush fg = s.ClockColor.Length == 7 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.ClockColor)) : light ? Brushes.Black : Brushes.White;
        foreach (var (tb, text) in new[] { (ClockPrevTop, top), (ClockPrevBottom, bottom) })
        {
            tb.Text = text;
            tb.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            try { tb.FontFamily = new FontFamily(s.ClockFont); } catch { }
            tb.FontSize = s.ClockSize;
            tb.FontWeight = s.ClockBold ? FontWeights.SemiBold : FontWeights.Normal;
            tb.Foreground = fg;
        }
    }

    private void SaveClock()
    {
        _settings.Save();
        if (_settings.ClockEnabled) App.ApplyClock();
        RenderClockPreview();
    }

    private void ClockToggle_Click(object s, RoutedEventArgs e)
    {
        _settings.ClockEnabled = ClockToggle.IsChecked == true;
        _settings.Save();
        App.ApplyClock();
        ShowToast(T(_settings.ClockEnabled ? "clock.toast.on" : "clock.toast.off"));
        RenderClockPreview();
    }

    private void ClockText_Changed(object s, TextChangedEventArgs e)
    {
        if (_clockLoading || ClockTopBox is null) return;
        _settings.ClockTop = ClockTopBox.Text;
        _settings.ClockBottom = ClockBottomBox.Text;
        if (ClockTimeBox.Text.Trim().Length > 0) _settings.ClockTimeFormat = ClockTimeBox.Text.Trim();
        if (ClockDateBox.Text.Trim().Length > 0) _settings.ClockDateFormat = ClockDateBox.Text.Trim();
        SaveClock();
    }

    private void ClockBold_Click(object s, RoutedEventArgs e)
    {
        _settings.ClockBold = ClockBoldBox.IsChecked == true;
        SaveClock();
    }
}
