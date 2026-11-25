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
    private bool _pcBusy, _pcShowUpToDate;
    private string _pcStatus = "";
    private double? _pcProgress;

    // ---- Menu: grupo "Drivers" (acordeon con CS2 y Windows)
    private void NavDrvGroup_Click(object s, RoutedEventArgs e) => SetDrvGroup(NavDrvGroup.IsChecked == true);

    private void SetDrvGroup(bool open)
    {
        if (open)
        {
            if (!(NavCs2.IsChecked == true || NavVideo.IsChecked == true || NavFonts.IsChecked == true)) { NavCs2Group.IsChecked = false; NavCs2Items.Visibility = Visibility.Collapsed; }
            if (!(NavWinTweaks.IsChecked == true || NavWinApps.IsChecked == true || NavWinStartup.IsChecked == true || NavWinTasks.IsChecked == true)) { NavWinGroup.IsChecked = false; NavWinItems.Visibility = Visibility.Collapsed; }
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
    private void LoadPcDrivers()
    {
        RenderPcDrivers();
        if (_pcDevices is null && !_pcBusy) _ = ScanPcDriversAsync();
    }

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
            Logger.Info(T("lg.pcd.scan", _pcUpdates.Count, _pcComps.Count, _pcDevices.Count));
            _pcStatus = "";
        }
        catch (Exception ex) { _pcStatus = T("lg.wt.fail", ex.Message); Logger.Error(_pcStatus); }
        finally { _pcBusy = false; _pcProgress = null; RenderPcDrivers(); }
    }

    private void RenderPcDrivers()
    {
        PcHost.Children.Clear();
        PcActions.Children.Clear();
        int nd = _pcUpdates.Count, nc = _pcComps.Count;

        // Resumen arriba (como Driver Booster)
        if (_pcBusy || _pcDevices is null)
            PcSummary.Text = _pcStatus.Length > 0 ? _pcStatus : T("pcd.scan.devices");
        else
            PcSummary.Text = nd + nc == 0 ? T("pcd.allgood") : T("pcd.summary", nd, nc);
        PcSummary.SetResourceReference(TextBlock.ForegroundProperty, !_pcBusy && nd + nc > 0 ? "Accent" : "Ink");
        PcBar.Visibility = _pcBusy ? Visibility.Visible : Visibility.Collapsed;
        PcBar.IsIndeterminate = _pcBusy && _pcProgress is null;
        if (_pcProgress is { } p) PcBar.Value = p;

        var rescan = new Button { Content = T("pcd.rescan"), Style = (Style)FindResource("GhostButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = !_pcBusy };
        rescan.Click += async (_, _) => { _pcDevices = null; await ScanPcDriversAsync(); };
        PcActions.Children.Add(rescan);
        var go = new Button { Content = T("pcd.update", _pcSel.Count), Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = !_pcBusy && _pcSel.Count > 0 };
        go.Click += PcUpdate_Click;
        PcActions.Children.Add(go);

        if (_pcDevices is null) return;

        if (nd > 0)
        {
            PcHost.Children.Add(Header(T("pcd.outdated", nd)));
            var sp = new StackPanel();
            for (int i = 0; i < _pcUpdates.Count; i++)
            {
                if (i > 0) Sep(sp);
                var u = _pcUpdates[i];
                sp.Children.Add(PcRow("d:" + u.Id, u.DeviceName,
                    (u.Current.Length > 0 ? u.Current + "  →  " : "") + u.NewVersion + (u.NewDate.Year > 2000 ? "  ·  " + u.NewDate.ToString("d", Culture) : "") + (u.Size > 0 ? "  ·  " + FormatSize(u.Size) : ""),
                    u.Source == PcDrivers.Source.WindowsUpdate ? "Windows Update" : T("pcd.src.catalog")));
            }
            PcHost.Children.Add(Card(sp, 8));
        }
        if (nc > 0)
        {
            PcHost.Children.Add(Header(T("pcd.components", nc)));
            var sp = new StackPanel();
            for (int i = 0; i < _pcComps.Count; i++)
            {
                if (i > 0) Sep(sp);
                var c = _pcComps[i];
                sp.Children.Add(PcRow("c:" + c.Id, c.Name, c.Current + "  →  " + c.Available, "winget"));
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
        PcHost.Children.Add(new TextBlock { Text = T("pcd.note"), Style = (Style)FindResource("Sub"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0) });

        TextBlock Header(string t) => new() { Text = t, Style = (Style)FindResource("Overline"), Margin = new Thickness(0, 22, 0, 0) };
    }

    private FrameworkElement PcRow(string key, string name, string detail, string source)
    {
        var cb = new CheckBox { IsChecked = _pcSel.Contains(key), IsEnabled = !_pcBusy, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        cb.Click += (_, _) => { if (cb.IsChecked == true) _pcSel.Add(key); else _pcSel.Remove(key); RenderPcDrivers(); };
        var dp = new DockPanel();
        var src = new TextBlock { Text = source, Style = (Style)FindResource("Sub"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(src, Dock.Right);
        dp.Children.Add(src);
        DockPanel.SetDock(cb, Dock.Left);
        dp.Children.Add(cb);
        var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        left.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        left.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("Sub"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) });
        dp.Children.Add(left);
        return dp;
    }

    private static string FormatSize(long b) => b >= 1 << 30 ? $"{b / (double)(1 << 30):0.0} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.0} MB" : $"{b / 1024.0:0} KB";

    private async void PcUpdate_Click(object s, RoutedEventArgs e)
    {
        var drivers = _pcUpdates.Where(u => _pcSel.Contains("d:" + u.Id)).ToList();
        var comps = _pcComps.Where(c => _pcSel.Contains("c:" + c.Id)).ToList();
        if (drivers.Count + comps.Count == 0) return;
        var list = string.Join("\n", drivers.Select(d => "· " + d.DeviceName + "  " + d.NewVersion).Concat(comps.Select(c => "· " + c.Name + "  " + c.Available)));
        if (!await Confirm(T("pcd.title").ToUpper(Culture), T("pcd.confirm.t", drivers.Count + comps.Count), T("pcd.confirm.x"), T("pcd.update", drivers.Count + comps.Count), T("dlg.cancel"), list)) return;

        _pcBusy = true; _pcProgress = null; RenderPcDrivers();
        int ok = 0, fail = 0; bool reboot = false;
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
                _pcStatus = T("pcd.installing.wu", wu.Count); RenderPcDrivers();
                var (n, rb) = await Task.Run(() => PcDrivers.InstallWindowsUpdate(wu.Select(w => w.Id)));
                ok += n; fail += wu.Count - n; reboot |= rb;
            }
            foreach (var d in drivers.Where(d => d.Source == PcDrivers.Source.Catalog))
            {
                _pcStatus = T("pcd.installing", d.DeviceName); RenderPcDrivers();
                try
                {
                    int code = await PcDrivers.InstallCatalogAsync(d, CancellationToken.None);
                    if (code is 0 or 3010) ok++; else fail++;
                    reboot |= code == 3010;
                }
                catch (Exception ex) { fail++; Logger.Error(T("lg.wt.fail", d.DeviceName + ": " + ex.Message)); }
            }
            foreach (var c in comps)
            {
                _pcStatus = T("pcd.installing", c.Name); RenderPcDrivers();
                if (await PcDrivers.UpgradeComponentAsync(c, CancellationToken.None)) ok++; else fail++;
            }
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
