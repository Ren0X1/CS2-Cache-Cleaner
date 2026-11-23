using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>
/// Grupo "Windows": ajustes de optimizacion (seleccion y riesgo de optimizerDuck, implementacion propia),
/// apps preinstaladas, programas de inicio y tareas programadas.
/// </summary>
public partial class MainWindow
{
    private string _winMode = "tweaks";
    private bool _winBusy;
    private Dictionary<string, bool> _wtState = [];
    private List<AppxApps.App>? _apps;
    private readonly HashSet<string> _appSel = [];

    private static readonly SolidColorBrush SafeGreen = Frozen(Color.FromRgb(0x3F, 0xB9, 0x50));
    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static readonly TweakCat[] CatOrder = [TweakCat.Performance, TweakCat.Gpu, TweakCat.Privacy, TweakCat.Ai, TweakCat.Experience, TweakCat.Services];

    private void NavWinGroup_Click(object s, RoutedEventArgs e) => SetWinGroup(NavWinGroup.IsChecked == true);

    private void SetWinGroup(bool open)
    {
        // Acordeon: al abrir Windows se cierra CS2 (salvo que haya una pestana de CS2 abierta)
        if (open && !(NavCs2.IsChecked == true || NavVideo.IsChecked == true || NavFonts.IsChecked == true))
        { NavCs2Group.IsChecked = false; NavCs2Items.Visibility = Visibility.Collapsed; }
        NavWinGroup.IsChecked = open;
        NavWinItems.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void LoadWindows(string mode)
    {
        _winMode = mode;
        WinTitle.Text = T("ws." + mode + ".title");
        WinSub.Text = T("ws." + mode + ".sub");
        WinHost.Children.Clear();
        WinActions.Children.Clear();
        WinHost.Children.Add(new TextBlock { Text = T("ws.loading"), Style = (Style)FindResource("Sub") });
        switch (mode)
        {
            case "tweaks":
                _wtState = await Task.Run(() => WinTweaks.Visible.ToDictionary(t => t.Id, t => WinTweaks.IsApplied(t)));
                SyncTweakSettings();
                if (_winMode == "tweaks") RenderTweaks();
                break;
            case "apps":
                try { _apps = await AppxApps.ListAsync(CancellationToken.None); } catch (Exception ex) { Logger.Detail("APPX: " + ex.Message); _apps = []; }
                _appSel.Clear();
                if (_winMode == "apps") RenderApps();
                break;
            case "startup":
                var st = await Task.Run(StartupApps.List);
                if (_winMode == "startup") RenderStartup(st);
                break;
            case "tasks":
                var tk = await Task.Run(ThirdPartyTasks.List);
                if (_winMode == "tasks") RenderTasks(tk);
                break;
        }
    }

    /// <summary>La config guarda que ajustes tiene el usuario aplicados (si se deshace fuera de RNX, se quita).</summary>
    private void SyncTweakSettings()
    {
        int before = _settings.WinTweaks.Count;
        _settings.WinTweaks.RemoveAll(id => _wtState.TryGetValue(id, out var on) && !on);
        if (_settings.WinTweaks.Count != before) _settings.Save();
    }

    // ------------------------------------------------------------------
    //  Elementos comunes
    // ------------------------------------------------------------------
    private Border RiskBadge(TweakRisk? r)
    {
        var (key, brush) = r switch
        {
            TweakRisk.Safe => ("ws.risk.safe", "PillGInk"),
            TweakRisk.Moderate => ("ws.risk.moderate", "Warn"),
            TweakRisk.Risky => ("ws.risk.risky", "Error"),
            _ => ("ws.risk.unknown", "Muted"),
        };
        var tb = new TextBlock { Text = T(key), FontSize = 11, FontWeight = FontWeights.SemiBold };
        var b = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = tb };
        if (r == TweakRisk.Safe)
        {
            // Verde fijo: se distingue del color de acento en cualquier paleta y tema
            tb.Foreground = b.BorderBrush = SafeGreen;
        }
        else
        {
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            b.SetResourceReference(Border.BorderBrushProperty, brush);
        }
        return b;
    }

    private Border Card(UIElement child, double top = 10)
    {
        var c = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, top, 0, 0), Child = child };
        return c;
    }

    private void Sep(Panel p)
    {
        var sep = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
        sep.SetResourceReference(Border.BackgroundProperty, "Line2");
        p.Children.Add(sep);
    }

    private Button ActionButton(string text, bool primary, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = !_winBusy };
        b.Click += click;
        return b;
    }

    // ------------------------------------------------------------------
    //  Ajustes
    // ------------------------------------------------------------------
    private void RenderTweaks()
    {
        WinHost.Children.Clear();
        WinActions.Children.Clear();
        var visible = WinTweaks.Visible.ToList();
        int applied = visible.Count(t => _wtState.GetValueOrDefault(t.Id));
        WinActions.Children.Add(ActionButton(T("ws.applySafe"), true, ApplySafe_Click));

        // Resumen y leyenda
        var head = new StackPanel();
        var sum = new TextBlock { Text = T("ws.summary", applied, visible.Count), FontSize = 15, FontWeight = FontWeights.SemiBold };
        head.Children.Add(sum);
        var legend = new WrapPanel { Margin = new Thickness(-10, 10, 0, 0) };
        foreach (var r in new[] { TweakRisk.Safe, TweakRisk.Moderate, TweakRisk.Risky })
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 4) };
            sp.Children.Add(RiskBadge(r));
            sp.Children.Add(new TextBlock { Text = T("ws.risk." + r.ToString().ToLowerInvariant() + ".h"), Style = (Style)FindResource("Sub"), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(sp);
        }
        head.Children.Add(legend);
        WinHost.Children.Add(Card(head, 0));

        foreach (var cat in CatOrder)
        {
            var items = visible.Where(t => t.Cat == cat).ToList();
            if (items.Count == 0) continue;
            WinHost.Children.Add(new TextBlock { Text = T("ws.cat." + cat.ToString().ToLowerInvariant()), Style = (Style)FindResource("Overline"), Margin = new Thickness(0, 22, 0, 0) });
            var list = new StackPanel();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) Sep(list);
                list.Children.Add(TweakRow(items[i]));
            }
            WinHost.Children.Add(Card(list, 8));
        }
        var credit = new TextBlock { Text = T("ws.credit"), Style = (Style)FindResource("Sub"), FontSize = 12, Margin = new Thickness(0, 18, 0, 0), TextWrapping = TextWrapping.Wrap };
        WinHost.Children.Add(credit);
    }

    private FrameworkElement TweakRow(WinTweaks.Tweak t)
    {
        bool on = _wtState.GetValueOrDefault(t.Id);
        bool byRnx = _settings.WinTweaks.Contains(t.Id) && WinTweaks.CanUndo(t);
        var toggle = new CheckBox { Style = (Style)FindResource("Toggle"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center, IsEnabled = !_winBusy };
        toggle.Click += async (_, _) => await ToggleTweakAsync(t, toggle.IsChecked == true);
        var dp = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        dp.Children.Add(toggle);
        var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = T("wt." + t.Id), FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(RiskBadge(t.Risk));
        left.Children.Add(line);
        left.Children.Add(new TextBlock { Text = T("wt." + t.Id + ".d"), Style = (Style)FindResource("Sub"), FontSize = 12.5, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        var state = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0),
                                    Text = on ? (byRnx ? T("ws.state.rnx") : T("ws.state.on")) : T("ws.state.off") };
        state.SetResourceReference(TextBlock.ForegroundProperty, on ? "Accent" : "Muted");
        left.Children.Add(state);
        dp.Children.Add(left);
        return dp;
    }

    private async Task ToggleTweakAsync(WinTweaks.Tweak t, bool enable)
    {
        if (_winBusy) return;
        if (enable && t.Risk == TweakRisk.Risky &&
            !await Confirm(T("ws.risk.risky").ToUpper(Culture), T("ws.risky.t", T("wt." + t.Id)), T("ws.risky.x"), T("ws.apply"), T("dlg.cancel")))
        { RenderTweaks(); return; }
        if (!enable && !WinTweaks.CanUndo(t))
        {
            await Confirm(T("dlg.warn"), T("ws.noundo.t"), T("ws.noundo.x"), T("ws.ok"), T("dlg.cancel"));
            RenderTweaks();
            return;
        }
        _winBusy = true; RenderTweaks();
        try
        {
            var r = await Task.Run(() => enable ? WinTweaks.Apply(t) : WinTweaks.Undo(t));
            _wtState[t.Id] = await Task.Run(() => WinTweaks.IsApplied(t));
            if (enable) { if (!_settings.WinTweaks.Contains(t.Id)) _settings.WinTweaks.Add(t.Id); }
            else _settings.WinTweaks.Remove(t.Id);
            _settings.Save();
            ShowToast(r.Failed > 0 ? T("ws.toast.partial", T("wt." + t.Id), r.Failed) : T(enable ? "ws.toast.on" : "ws.toast.off", T("wt." + t.Id)) + (NeedsRestart(t) ? " " + T("ws.restart") : ""));
        }
        catch (Exception ex) { Logger.Error(T("lg.wt.fail", ex.Message)); ShowToast(T("lg.wt.fail", ex.Message)); }
        finally { _winBusy = false; RenderTweaks(); }
    }

    private static bool NeedsRestart(WinTweaks.Tweak t) => t.Cat is TweakCat.Gpu or TweakCat.Services || t.Id is "svchost" or "priority" or "mmcss" or "hibernate" or "telemetry" or "autologger";

    private async void ApplySafe_Click(object s, RoutedEventArgs e)
    {
        var todo = WinTweaks.Visible.Where(t => t.Risk == TweakRisk.Safe && !_wtState.GetValueOrDefault(t.Id)).ToList();
        if (todo.Count == 0) { ShowToast(T("ws.toast.allSafe")); return; }
        if (!await Confirm(T("ws.tweaks.title").ToUpper(Culture), T("ws.safe.t", todo.Count), T("ws.safe.x"), T("ws.apply"), T("dlg.cancel"),
                           string.Join("\n", todo.Select(t => "· " + T("wt." + t.Id))))) return;
        _winBusy = true; RenderTweaks();
        try
        {
            if (_settings.RestorePoint) await RestorePoint.CreateAsync(T("ws.tweaks.title"), CancellationToken.None);
            int failed = 0;
            foreach (var t in todo)
            {
                var r = await Task.Run(() => WinTweaks.Apply(t));
                failed += r.Failed;
                if (!_settings.WinTweaks.Contains(t.Id)) _settings.WinTweaks.Add(t.Id);
            }
            _settings.Save();
            _wtState = await Task.Run(() => WinTweaks.Visible.ToDictionary(t => t.Id, t => WinTweaks.IsApplied(t)));
            ShowToast(T("ws.toast.safe", todo.Count) + " " + T("ws.restart"));
        }
        catch (Exception ex) { Logger.Error(T("lg.wt.fail", ex.Message)); }
        finally { _winBusy = false; RenderTweaks(); }
    }

    // ------------------------------------------------------------------
    //  Apps preinstaladas
    // ------------------------------------------------------------------
    private void RenderApps()
    {
        WinHost.Children.Clear();
        WinActions.Children.Clear();
        var apps = _apps ?? [];
        WinActions.Children.Add(ActionButton(T("ws.apps.selSafe"), false, (_, _) =>
        {
            _appSel.Clear();
            foreach (var a in apps.Where(a => a.Risk == TweakRisk.Safe)) _appSel.Add(a.FullName);
            RenderApps();
        }));
        var remove = ActionButton(T("ws.apps.remove", _appSel.Count), true, RemoveApps_Click);
        remove.IsEnabled = !_winBusy && _appSel.Count > 0;
        WinActions.Children.Add(remove);

        if (apps.Count == 0) { WinHost.Children.Add(new TextBlock { Text = T("ws.apps.none"), Style = (Style)FindResource("Sub") }); return; }
        var warn = new TextBlock { Text = T("ws.apps.warn"), Style = (Style)FindResource("Sub"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        WinHost.Children.Add(Card(warn, 0));
        foreach (var grp in apps.GroupBy(a => a.Risk))
        {
            WinHost.Children.Add(new TextBlock { Text = T(grp.Key switch { TweakRisk.Safe => "ws.apps.g.safe", TweakRisk.Moderate => "ws.apps.g.caution", _ => "ws.apps.g.other" }, grp.Count()),
                                                 Style = (Style)FindResource("Overline"), Margin = new Thickness(0, 22, 0, 0) });
            var list = new StackPanel();
            foreach (var a in grp)
            {
                var cb = new CheckBox { IsChecked = _appSel.Contains(a.FullName), Margin = new Thickness(0, 5, 0, 5), IsEnabled = !_winBusy };
                cb.SetResourceReference(Control.ForegroundProperty, "Ink");
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = a.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = "  " + a.Version, Style = (Style)FindResource("Sub"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(RiskBadge(a.Risk));
                cb.Content = sp;
                var app = a;
                cb.Click += (_, _) => { if (cb.IsChecked == true) _appSel.Add(app.FullName); else _appSel.Remove(app.FullName); RenderAppsActions(); };
                list.Children.Add(cb);
            }
            WinHost.Children.Add(Card(list, 8));
        }
    }

    private void RenderAppsActions()
    {
        if (WinActions.Children.Count == 2 && WinActions.Children[1] is Button b)
        {
            b.Content = T("ws.apps.remove", _appSel.Count);
            b.IsEnabled = !_winBusy && _appSel.Count > 0;
        }
    }

    private async void RemoveApps_Click(object s, RoutedEventArgs e)
    {
        var sel = (_apps ?? []).Where(a => _appSel.Contains(a.FullName)).ToList();
        if (sel.Count == 0) return;
        if (!await Confirm(T("ws.apps.title").ToUpper(Culture), T("ws.apps.t", sel.Count), T("ws.apps.x"), T("ws.apps.btn"), T("dlg.cancel"),
                           string.Join("\n", sel.Select(a => "· " + a.Name)))) return;
        _winBusy = true; RenderApps();
        int ok = 0;
        foreach (var a in sel) if (await AppxApps.RemoveAsync(a, CancellationToken.None)) ok++;
        _winBusy = false;
        ShowToast(T("ws.apps.toast", ok, sel.Count - ok));
        LoadWindows("apps");
    }

    // ------------------------------------------------------------------
    //  Inicio
    // ------------------------------------------------------------------
    private void RenderStartup(List<StartupApps.Entry> list)
    {
        WinHost.Children.Clear();
        WinActions.Children.Clear();
        WinActions.Children.Add(ActionButton(T("sv.refresh"), false, (_, _) => LoadWindows("startup")));
        if (list.Count == 0) { WinHost.Children.Add(new TextBlock { Text = T("ws.startup.none"), Style = (Style)FindResource("Sub") }); return; }
        var sp = new StackPanel();
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) Sep(sp);
            var en = list[i];
            var toggle = new CheckBox { Style = (Style)FindResource("Toggle"), IsChecked = en.Enabled, VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) =>
            {
                try { StartupApps.SetEnabled(en, toggle.IsChecked == true); ShowToast(T(toggle.IsChecked == true ? "ws.startup.on" : "ws.startup.off", en.Name)); }
                catch (Exception ex) { ShowToast(T("lg.wt.fail", ex.Message)); toggle.IsChecked = !toggle.IsChecked; }
            };
            var dp = new DockPanel();
            DockPanel.SetDock(toggle, Dock.Right);
            dp.Children.Add(toggle);
            var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            left.Children.Add(new TextBlock { Text = en.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            left.Children.Add(new TextBlock { Text = en.Where + "  ·  " + en.Command, Style = (Style)FindResource("Sub"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = en.Command });
            dp.Children.Add(left);
            sp.Children.Add(dp);
        }
        WinHost.Children.Add(Card(sp, 0));
    }

    // ------------------------------------------------------------------
    //  Tareas programadas
    // ------------------------------------------------------------------
    private void RenderTasks(List<ThirdPartyTasks.Item> list)
    {
        WinHost.Children.Clear();
        WinActions.Children.Clear();
        WinActions.Children.Add(ActionButton(T("sv.refresh"), false, (_, _) => LoadWindows("tasks")));
        if (list.Count == 0) { WinHost.Children.Add(new TextBlock { Text = T("ws.tasks.none"), Style = (Style)FindResource("Sub") }); return; }
        var sp = new StackPanel();
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) Sep(sp);
            var it = list[i];
            var toggle = new CheckBox { Style = (Style)FindResource("Toggle"), IsChecked = it.Enabled, VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) =>
            {
                try { ThirdPartyTasks.SetEnabled(it, toggle.IsChecked == true); ShowToast(T(toggle.IsChecked == true ? "ws.tasks.on" : "ws.tasks.off", it.Name)); }
                catch (Exception ex) { ShowToast(T("lg.wt.fail", ex.Message)); toggle.IsChecked = !toggle.IsChecked; }
            };
            var dp = new DockPanel();
            DockPanel.SetDock(toggle, Dock.Right);
            dp.Children.Add(toggle);
            var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            left.Children.Add(new TextBlock { Text = it.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            var meta = it.Path + (it.Author.Length > 0 ? "  ·  " + it.Author : "") + (it.NextRun.Length > 0 ? "  ·  " + T("ws.tasks.next", it.NextRun) : "");
            left.Children.Add(new TextBlock { Text = meta, Style = (Style)FindResource("Sub"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            dp.Children.Add(left);
            sp.Children.Add(dp);
        }
        WinHost.Children.Add(Card(sp, 0));
    }
}
