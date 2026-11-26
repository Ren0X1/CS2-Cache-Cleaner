using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Grupo "Drivers": Driver NVIDIA (ya existente) y Drivers PC (todos los demas, al estilo de Driver Booster).</summary>
public partial class MainWindow
{
    private List<PcDrivers.Device>? _pcDevices;
    private List<PcDrivers.DriverUpdate> _pcUpdates = [];
    private List<PcDrivers.Component> _pcComps = [];
    private readonly HashSet<string> _pcSel = new(StringComparer.OrdinalIgnoreCase);
    private bool _pcBusy, _pcShowUpToDate, _pcShowIgnored;
    private string _pcStatus = "";
    private double? _pcProgress;

    // ---- Menu: grupo "Drivers" (acordeon con CS2 y Windows)
    private void NavDrvGroup_Click(object s, RoutedEventArgs e) => SetDrvGroup(NavDrvGroup.IsChecked == true);

    private void SetDrvGroup(bool open)
    {
        if (open)
        {
            if (!(NavCs2.IsChecked == true || NavVideo.IsChecked == true || NavFonts.IsChecked == true)) { NavCs2Group.IsChecked = false; NavCs2Items.Visibility = Visibility.Collapsed; }
            if (!(NavWinTweaks.IsChecked == true || NavWinApps.IsChecked == true || NavWinStartup.IsChecked == true || NavWinTasks.IsChecked == true || NavWinMods.IsChecked == true)) { NavWinGroup.IsChecked = false; NavWinItems.Visibility = Visibility.Collapsed; }
        }
        NavDrvGroup.IsChecked = open;
        NavDrvItems.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        UpdateDrvGroupBadge();
    }

    /// <summary>Con el grupo cerrado, el aviso "Nuevo" del driver NVIDIA se ve en la cabecera del grupo.</summary>
    private void UpdateDrvGroupBadge() =>
        DrvGroupBadge.Visibility = NvBadge.Visibility == Visibility.Visible && NavDrvItems.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

    private bool InDrvGroup(object sender) => sender == NavNvDriver || sender == NavPcDrivers;

    // ------------------------------------------------------------------
    //  Drivers PC
    // ------------------------------------------------------------------
    /// <summary>Al entrar no se analiza nada: solo cuando el usuario pulsa "Analizar".</summary>
    private void LoadPcDrivers() => RenderPcDrivers();

    private async Task ScanPcDriversAsync()
    {
        _pcBusy = true;
        _pcStatus = T("pcd.scan.devices"); _pcProgress = null; RenderPcDrivers();
        try
        {
            _pcDevices = await Task.Run(PcDrivers.Devices);
            _pcStatus = T("pcd.scan.wu"); RenderPcDrivers();
            var wu = await Task.Run(() => { try { return PcDrivers.WindowsUpdate(_pcDevices); } catch (Exception ex) { Logger.Detail("WU: " + ex.Message); return []; } });
            _pcStatus = T("pcd.scan.catalog"); _pcProgress = 0; RenderPcDrivers();
            var cat = await PcDrivers.CatalogAsync(_pcDevices, new Progress<double>(p => { _pcProgress = p; PcBar.Value = p; }), CancellationToken.None);
            // Si Windows Update ya ofrece el mismo dispositivo, se queda esa opcion (es la que Windows elige)
            _pcUpdates = [.. wu, .. cat.Where(c => !wu.Any(w => w.DeviceName.Equals(c.DeviceName, StringComparison.OrdinalIgnoreCase)))];
            _pcStatus = T("pcd.scan.comps"); _pcProgress = null; RenderPcDrivers();
            try { _pcComps = await PcDrivers.ComponentsAsync(CancellationToken.None); } catch (Exception ex) { Logger.Detail("WINGET: " + ex.Message); _pcComps = []; }
            _pcSel.Clear();
            foreach (var u in _pcUpdates) _pcSel.Add("d:" + u.Id);
            foreach (var c in _pcComps) _pcSel.Add("c:" + c.Id);
            Logger.Info(T("lg.pcd.scan", VisibleUpdates.Count, VisibleComps.Count, _pcDevices.Count));
            _pcStatus = "";
        }
        catch (Exception ex) { _pcStatus = T("lg.wt.fail", ex.Message); Logger.Error(_pcStatus); }
        finally { _pcBusy = false; _pcProgress = null; RenderPcDrivers(); }
    }

    private void RenderPcDrivers()
    {
        PcHost.Children.Clear();
        PcActions.Children.Clear();
        var vUpd = VisibleUpdates; var vComp = VisibleComps;
        int nd = vUpd.Count, nc = vComp.Count;
        int selCount = vUpd.Count(u => _pcSel.Contains("d:" + u.Id)) + vComp.Count(c => _pcSel.Contains("c:" + c.Id));

        // Resumen arriba (como Driver Booster)
        if (_pcBusy)
            PcSummary.Text = _pcStatus.Length > 0 ? _pcStatus : T("pcd.scan.devices");
        else if (_pcDevices is null)
            PcSummary.Text = T("pcd.idle");
        else
            PcSummary.Text = nd + nc == 0 ? T("pcd.allgood") : T("pcd.summary", nd, nc);
        PcSummary.SetResourceReference(TextBlock.ForegroundProperty, !_pcBusy && nd + nc > 0 ? "Accent" : "Ink");
        PcBar.Visibility = _pcBusy ? Visibility.Visible : Visibility.Collapsed;
        PcBar.IsIndeterminate = _pcBusy && _pcProgress is null;
        if (_pcProgress is { } p) PcBar.Value = p;

        bool scanned = _pcDevices is not null;
        var rescan = new Button { Content = T(scanned ? "pcd.rescan" : "pcd.scan"), Style = (Style)FindResource(scanned ? "GhostButton" : "PrimaryButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = !_pcBusy };
        rescan.Click += async (_, _) => { _pcDevices = null; await ScanPcDriversAsync(); };
        PcActions.Children.Add(rescan);
        if (scanned)
        {
            var go = new Button { Content = T("pcd.update", selCount), Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = !_pcBusy && selCount > 0 };
            go.Click += PcUpdate_Click;
            PcActions.Children.Add(go);
        }

        if (_pcDevices is null) return;

        if (nd > 0)
        {
            PcHost.Children.Add(Header(T("pcd.outdated", nd)));
            var sp = new StackPanel();
            for (int i = 0; i < vUpd.Count; i++)
            {
                if (i > 0) Sep(sp);
                var u = vUpd[i];
                sp.Children.Add(PcRow("d:" + u.Id, u.DeviceName,
                    (u.Current.Length > 0 ? u.Current + "  →  " : "") + u.NewVersion + (u.NewDate.Year > 2000 ? "  ·  " + u.NewDate.ToString("d", Culture) : "") + (u.Size > 0 ? "  ·  " + FormatSize(u.Size) : ""),
                    u.Source == PcDrivers.Source.WindowsUpdate ? "Windows Update" : T("pcd.src.catalog"), () => Discard(IgnKey(u), u.DeviceName, "d:" + u.Id)));
            }
            PcHost.Children.Add(Card(sp, 8));
        }
        if (nc > 0)
        {
            PcHost.Children.Add(Header(T("pcd.components", nc)));
            var sp = new StackPanel();
            for (int i = 0; i < vComp.Count; i++)
            {
                if (i > 0) Sep(sp);
                var c = vComp[i];
                sp.Children.Add(PcRow("c:" + c.Id, c.Name, c.Current + "  →  " + c.Available, "winget", () => Discard(IgnKey(c), c.Name, "c:" + c.Id)));
            }
            PcHost.Children.Add(Card(sp, 8));
        }

        // Al dia (plegable)
        var upToDate = _pcDevices.Where(d => !_pcUpdates.Any(u => u.DeviceName.Equals(d.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        var hdr = new Button { Style = (Style)FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 22, 0, 0),
                               Content = (_pcShowUpToDate ? "▾  " : "▸  ") + T("pcd.uptodate", upToDate.Count) };
        hdr.Click += (_, _) => { _pcShowUpToDate = !_pcShowUpToDate; RenderPcDrivers(); };
        PcHost.Children.Add(hdr);
        if (_pcShowUpToDate)
        {
            var sp = new StackPanel();
            foreach (var d in upToDate)
            {
                var dp = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
                var right = new TextBlock { Text = (d.Date.Year > 2000 ? d.Date.ToString("d", Culture) + "  ·  " : "") + d.Version, Style = (Style)FindResource("Sub"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(right, Dock.Right);
                dp.Children.Add(right);
                var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
                left.Children.Add(new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                left.Children.Add(new TextBlock { Text = d.Class + (d.Provider.Length > 0 ? "  ·  " + d.Provider : ""), Style = (Style)FindResource("Sub"), FontSize = 11.5 });
                dp.Children.Add(left);
                sp.Children.Add(dp);
            }
            PcHost.Children.Add(Card(sp, 8));
        }
        // Descartados (plegable): se pueden volver a mostrar
        if (_settings.PcIgnore.Count > 0)
        {
            var ih = new Button { Style = (Style)FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0),
                                  Content = (_pcShowIgnored ? "▾  " : "▸  ") + T("pcd.ignored", _settings.PcIgnore.Count) };
            ih.Click += (_, _) => { _pcShowIgnored = !_pcShowIgnored; RenderPcDrivers(); };
            PcHost.Children.Add(ih);
            if (_pcShowIgnored)
            {
                var sp = new StackPanel();
                foreach (var entry in _settings.PcIgnore.ToList())
                {
                    var label = entry.Contains('|') ? entry[(entry.IndexOf('|') + 1)..] : entry;
                    var dp = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                    var back = new Button { Content = T("pcd.unignore"), Style = (Style)FindResource("GhostButton"), IsEnabled = !_pcBusy };
                    var e2 = entry;
                    back.Click += (_, _) => Restore(e2);
                    DockPanel.SetDock(back, Dock.Right);
                    dp.Children.Add(back);
                    var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
                    left.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                    left.Children.Add(new TextBlock { Text = T(entry.StartsWith("comp:") ? "pcd.ign.comp" : "pcd.ign.dev"), Style = (Style)FindResource("Sub"), FontSize = 11.5 });
                    dp.Children.Add(left);
                    sp.Children.Add(dp);
                }
                PcHost.Children.Add(Card(sp, 8));
            }
        }
        PcHost.Children.Add(new TextBlock { Text = T("pcd.note"), Style = (Style)FindResource("Sub"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0) });

        TextBlock Header(string t) => new() { Text = t, Style = (Style)FindResource("Overline"), Margin = new Thickness(0, 22, 0, 0) };
    }

    private FrameworkElement PcRow(string key, string name, string detail, string source, Action discard)
    {
        var cb = new CheckBox { IsChecked = _pcSel.Contains(key), IsEnabled = !_pcBusy, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        cb.Click += (_, _) => { if (cb.IsChecked == true) _pcSel.Add(key); else _pcSel.Remove(key); RenderPcDrivers(); };
        var dp = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = source, Style = (Style)FindResource("Sub"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var dis = new Button { Content = T("pcd.ignore"), Style = (Style)FindResource("GhostButton"), IsEnabled = !_pcBusy, ToolTip = T("pcd.ignore.tip") };
        dis.Click += (_, _) => discard();
        right.Children.Add(dis);
        DockPanel.SetDock(right, Dock.Right);
        dp.Children.Add(right);
        DockPanel.SetDock(cb, Dock.Left);
        dp.Children.Add(cb);
        var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        left.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        left.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("Sub"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) });
        dp.Children.Add(left);
        return dp;
    }

    // ---- Descartados (se guardan en la config)
    private static string IgnKey(PcDrivers.DriverUpdate u) => "dev:" + u.DeviceName.Trim().ToLowerInvariant();
    private static string IgnKey(PcDrivers.Component c) => "comp:" + c.Id.Trim().ToLowerInvariant();
    private bool Ignored(string key) => _settings.PcIgnore.Any(x => x.Split('|')[0] == key);
    private List<PcDrivers.DriverUpdate> VisibleUpdates => _pcUpdates.Where(u => !Ignored(IgnKey(u))).ToList();
    private List<PcDrivers.Component> VisibleComps => _pcComps.Where(c => !Ignored(IgnKey(c))).ToList();

    private void Discard(string key, string label, string selKey)
    {
        if (!Ignored(key)) _settings.PcIgnore.Add(key + "|" + label);
        _settings.Save();
        _pcSel.Remove(selKey);
        Logger.Info(T("lg.pcd.ignore", label));
        ShowToast(T("pcd.toast.ignored", label));
        RenderPcDrivers();
    }

    private void Restore(string entry)
    {
        _settings.PcIgnore.Remove(entry);
        _settings.Save();
        var label = entry.Contains('|') ? entry[(entry.IndexOf('|') + 1)..] : entry;
        Logger.Info(T("lg.pcd.unignore", label));
        // Si estaba en el ultimo analisis, vuelve a aparecer seleccionado
        foreach (var u in _pcUpdates.Where(u => IgnKey(u) == entry.Split('|')[0])) _pcSel.Add("d:" + u.Id);
        foreach (var c in _pcComps.Where(c => IgnKey(c) == entry.Split('|')[0])) _pcSel.Add("c:" + c.Id);
        RenderPcDrivers();
    }

    private static string FormatSize(long b) => b >= 1 << 30 ? $"{b / (double)(1 << 30):0.0} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.0} MB" : $"{b / 1024.0:0} KB";

    private async void PcUpdate_Click(object s, RoutedEventArgs e)
    {
        var drivers = VisibleUpdates.Where(u => _pcSel.Contains("d:" + u.Id)).ToList();
        var comps = VisibleComps.Where(c => _pcSel.Contains("c:" + c.Id)).ToList();
        if (drivers.Count + comps.Count == 0) return;
        var list = string.Join("\n", drivers.Select(d => "· " + d.DeviceName + "  " + d.NewVersion).Concat(comps.Select(c => "· " + c.Name + "  " + c.Available)));
        if (!await Confirm(T("pcd.title").ToUpper(Culture), T("pcd.confirm.t", drivers.Count + comps.Count), T("pcd.confirm.x"), T("pcd.update", drivers.Count + comps.Count), T("dlg.cancel"), list)) return;

        _pcBusy = true; _pcProgress = null; RenderPcDrivers();
        int ok = 0, fail = 0, step = 0, total = drivers.Count + comps.Count; bool reboot = false;
        void Step(string name)
        {
            _pcStatus = T("pcd.installing.n", name, step + 1, total);
            _pcProgress = (double)step / total;
            RenderPcDrivers();
        }
        try
        {
            if (drivers.Count > 0 && _settings.RestorePoint)
            {
                _pcStatus = T("pcd.restorepoint"); RenderPcDrivers();
                await RestorePoint.CreateAsync(T("pcd.title"), CancellationToken.None);
            }
            var wu = drivers.Where(d => d.Source == PcDrivers.Source.WindowsUpdate).ToList();
            if (wu.Count > 0)
            {
                int baseStep = step;
                var (n, rb) = await Task.Run(() => PcDrivers.InstallWindowsUpdate(wu.Select(w => w.Id),
                    (i, title) => Dispatcher.Invoke(() => { step = baseStep + i; Step(wu.FirstOrDefault(w => w.Title == title)?.DeviceName ?? title); })));
                step = baseStep + wu.Count;
                ok += n; fail += wu.Count - n; reboot |= rb;
            }
            foreach (var d in drivers.Where(d => d.Source == PcDrivers.Source.Catalog))
            {
                Step(d.DeviceName);
                try
                {
                    int code = await PcDrivers.InstallCatalogAsync(d, CancellationToken.None);
                    if (code is 0 or 3010) ok++; else fail++;
                    reboot |= code == 3010;
                }
                catch (Exception ex) { fail++; Logger.Error(T("lg.wt.fail", d.DeviceName + ": " + ex.Message)); }
                step++;
            }
            foreach (var c in comps)
            {
                Step(c.Name);
                if (await PcDrivers.UpgradeComponentAsync(c, CancellationToken.None)) ok++; else fail++;
                step++;
            }
            _pcProgress = 1; RenderPcDrivers();
        }
        catch (Exception ex) { Logger.Error(T("lg.wt.fail", ex.Message)); }
        finally { PcDrivers.CleanWork(); _pcBusy = false; }

        ShowToast(T("pcd.done", ok, fail));
        _pcDevices = null;
        await ScanPcDriversAsync();
        if (reboot && await Confirm(T("dlg.restart.o"), T("dlg.restart.t"), T("pcd.reboot"), T("dlg.restart.yes"), T("dlg.restart.no")))
        {
            await Sys.RunAsync("shutdown.exe", $"/r /t 10 /c \"{T("restart.comment")}\"", 30);
            App.ExitApp();
        }
    }
}
