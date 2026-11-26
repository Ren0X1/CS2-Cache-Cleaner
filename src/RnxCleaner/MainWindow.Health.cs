using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Historial de rendimiento del HUD (pestana HUD) y salud de los discos (Mods y arreglos).</summary>
public partial class MainWindow
{
    private string? _hhGame;
    private bool _healthHooked;
    private List<DiskHealth.Disk>? _disks;

    private void HookHealth()
    {
        if (_healthHooked) return;
        _healthHooked = true;
        HudHistory.Changed += () => Dispatcher.BeginInvoke(() => { if (PageHud.Visibility == Visibility.Visible) RenderHudHistory(); });
        App.DiskAlertsChanged += () => Dispatcher.BeginInvoke(() =>
        {
            DiskBadge.Visibility = App.DiskAlerts is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
            if (App.DiskAlerts is { Count: > 0 } a && IsVisible)
                ShowToast(T("dh.toast", a[0].Name, T(a[0].Issues[0].Key, a[0].Issues[0].Arg)));
        });
    }

    // ------------------------------------------------------------------
    //  Historial de rendimiento (HUD)
    // ------------------------------------------------------------------
    private void RenderHudHistory()
    {
        var all = HudHistory.Load();
        HhGames.Children.Clear();
        HhChart.Children.Clear();
        HhTable.Children.Clear();
        HhClearBtn.Visibility = all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            HhTable.Children.Add(new TextBlock { Text = T("hh.empty"), Style = (Style)FindResource("Sub"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            HhChart.Visibility = Visibility.Collapsed;
            return;
        }
        HhChart.Visibility = Visibility.Visible;
        var games = all.GroupBy(s => s.Game, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Max(s => s.Start)).Select(g => g.Key).ToList();
        if (_hhGame is null || !games.Contains(_hhGame, StringComparer.OrdinalIgnoreCase)) _hhGame = games[0];
        Seg(HhGames, "hhgame", games.Take(6).Select(g => (g, g + ".exe")).ToArray(), _hhGame, v => { _hhGame = v; RenderHudHistory(); });

        var list = all.Where(s => s.Game.Equals(_hhGame, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Start).TakeLast(30).ToList();
        DrawHhChart(list);

        // Tabla: ultimas partidas de ese juego
        var head = HhRow(T("hist.col.date"), T("hh.col.time"), T("hh.col.avg"), "1 % low", "0.1 % low", "CPU / GPU °C", true);
        HhTable.Children.Add(head);
        foreach (var s in list.AsEnumerable().Reverse().Take(12))
            HhTable.Children.Add(HhRow(s.Start.ToString("g", Culture), $"{s.Seconds / 60} min", $"{s.AvgFps:0}", $"{s.Low1:0}", s.Low01 > 0 ? $"{s.Low01:0}" : "-",
                                       $"{(s.CpuTempMax is { } c ? c.ToString("0") : "-")} / {(s.GpuTempMax is { } g ? g.ToString("0") : "-")}", false));
    }

    private FrameworkElement HhRow(string a, string b, string c, string d, string e, string f, bool header)
    {
        var g = new Grid { Margin = new Thickness(0, header ? 0 : 3, 0, header ? 4 : 3) };
        foreach (var w in new[] { 2.2, 1, 1, 1, 1, 1.4 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        var cells = new[] { a, b, c, d, e, f };
        for (int i = 0; i < cells.Length; i++)
        {
            var tb = new TextBlock { Text = cells[i], FontSize = 12.5, FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal, TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right };
            tb.SetResourceReference(TextBlock.ForegroundProperty, header ? "Muted" : "Ink");
            Grid.SetColumn(tb, i);
            g.Children.Add(tb);
        }
        return g;
    }

    /// <summary>Barras de FPS medios por partida y linea del 1 % low.</summary>
    private void DrawHhChart(List<HudHistory.Session> list)
    {
        HhChart.UpdateLayout();
        double w = HhChart.ActualWidth > 0 ? HhChart.ActualWidth : 800, h = HhChart.Height - 18;
        double max = Math.Max(30, list.Max(s => s.AvgFps) * 1.1);
        double bw = Math.Max(6, Math.Min(36, w / Math.Max(1, list.Count) - 6));
        var acc = (Brush)FindResource("Accent");
        var line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        line.SetResourceReference(Shape.StrokeProperty, "Warn");
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            double x = i * (w / list.Count) + 3, bh = h * s.AvgFps / max;
            var r = new Rectangle { Width = bw, Height = Math.Max(2, bh), RadiusX = 3, RadiusY = 3, Fill = acc, Opacity = 0.85,
                                    ToolTip = $"{s.Start:g} · {s.AvgFps:0} FPS · 1 % low {s.Low1:0}" };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, h - bh);
            HhChart.Children.Add(r);
            line.Points.Add(new Point(x + bw / 2, h - h * s.Low1 / max));
        }
        HhChart.Children.Add(line);
        var legend = new TextBlock { Text = T("hh.legend", Math.Round(list.Average(s => s.AvgFps)), Math.Round(list.Average(s => s.Low1))), FontSize = 11.5 };
        legend.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Canvas.SetLeft(legend, 0); Canvas.SetTop(legend, h + 2);
        HhChart.Children.Add(legend);
    }

    private async void HhClear_Click(object s, RoutedEventArgs e)
    {
        if (!await Confirm(T("hh.title").ToUpper(Culture), T("hh.clear.t"), T("hh.clear.x"), T("hh.clear"), T("dlg.cancel"))) return;
        HudHistory.Clear();
        RenderHudHistory();
    }

    // ------------------------------------------------------------------
    //  Salud de los discos (Mods y arreglos)
    // ------------------------------------------------------------------
    private async void DiskCheck_Click(object s, RoutedEventArgs e)
    {
        DiskBtn.IsEnabled = false;
        DiskList.Children.Clear();
        DiskList.Children.Add(new TextBlock { Text = T("dh.checking"), Style = (Style)FindResource("Sub"), FontSize = 12.5 });
        try
        {
            _disks = await DiskHealth.ReadAsync(CancellationToken.None);
            App.SetDiskAlerts(_disks);
            DiskHealth.Log(_disks.Where(d => d.Issues.Count > 0).ToList());
        }
        catch (Exception ex) { Logger.Error(T("lg.wt.fail", ex.Message)); }
        finally { DiskBtn.IsEnabled = true; RenderDisks(); }
    }

    private void RenderDisks()
    {
        DiskList.Children.Clear();
        if (_disks is null) return;
        foreach (var d in _disks)
        {
            var lvl = d.Worst;
            var brush = lvl switch { DiskHealth.Level.Critical => "Error", DiskHealth.Level.Warning => "Warn", _ => "Accent" };
            var box = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 8, 0, 0) };
            box.SetResourceReference(Border.BorderBrushProperty, lvl == DiskHealth.Level.Ok ? "Line" : brush);
            var sp = new StackPanel();
            var head = new DockPanel();
            var state = new TextBlock { Text = T(lvl switch { DiskHealth.Level.Critical => "dh.s.critical", DiskHealth.Level.Warning => "dh.s.warning", _ => "dh.s.ok" }), FontWeight = FontWeights.SemiBold, FontSize = 12.5 };
            if (lvl == DiskHealth.Level.Ok) state.Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)); else state.SetResourceReference(TextBlock.ForegroundProperty, brush);
            DockPanel.SetDock(state, Dock.Right);
            head.Children.Add(state);
            head.Children.Add(new TextBlock { Text = $"{d.Name}  ·  {d.Media}  ·  {d.SizeGb:0} GB", FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(head);
            // Salud traducida (Windows la da en ingles: Healthy / Warning / Unhealthy)
            var health = d.Health.ToLowerInvariant() switch { "healthy" => T("dh.s.ok"), "warning" => T("dh.s.warning"), "unhealthy" => T("dh.s.critical"), _ => d.Health };
            var facts = new List<string> { T("dh.f.health", health) };
            if (d.Wear is { } w) facts.Add(T("dh.f.wear", w));
            if (d.Temp is { } t) facts.Add(T("dh.f.temp", t));
            if (d.Hours is { } hrs) facts.Add(T("dh.f.hours", hrs));
            if (d.Errors > 0) facts.Add(T("dh.f.errors", d.Errors));
            sp.Children.Add(new TextBlock { Text = string.Join("  ·  ", facts), Style = (Style)FindResource("Sub"), FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            foreach (var v in d.Volumes)
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var lbl = new TextBlock { Text = T("dh.f.free", v.Letter, v.FreeGb.ToString("0.0", Culture), v.SizeGb.ToString("0", Culture), (int)v.FreePct), FontSize = 12, Width = 260 };
                DockPanel.SetDock(lbl, Dock.Left);
                row.Children.Add(lbl);
                var bar = new ProgressBar { Style = (Style)FindResource("ThinBar"), Maximum = 100, Value = 100 - v.FreePct, VerticalAlignment = VerticalAlignment.Center };
                if (v.FreePct < 10) bar.SetResourceReference(ForegroundProperty, "Warn");
                row.Children.Add(bar);
                sp.Children.Add(row);
            }
            foreach (var (l, key, arg) in d.Issues)
            {
                var it = new TextBlock { Text = "⚠  " + T(key, arg), FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
                it.SetResourceReference(TextBlock.ForegroundProperty, l == DiskHealth.Level.Critical ? "Error" : "Warn");
                sp.Children.Add(it);
            }
            box.Child = sp;
            DiskList.Children.Add(box);
        }
    }
}
