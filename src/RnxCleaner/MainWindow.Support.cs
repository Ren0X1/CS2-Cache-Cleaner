using System.IO;
using System.Windows;
using System.Windows.Controls;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Mods y arreglos → Limpieza del firewall e Informe del PC.</summary>
public partial class MainWindow
{
    private List<FirewallCleaner.Rule>? _fwRules;
    private readonly HashSet<string> _fwSel = new(StringComparer.OrdinalIgnoreCase);
    private bool _fwBusy;

    private void RenderFirewall()
    {
        FwList.Children.Clear();
        var last = FirewallCleaner.LastBackup();
        FwRestoreBtn.IsEnabled = !_fwBusy && last is not null;
        FwRestoreBtn.ToolTip = last is null ? null : T("fw.restore.tip", File.GetLastWriteTime(last).ToString("g", Culture));
        FwScanBtn.IsEnabled = !_fwBusy;
        FwRemoveBtn.Visibility = _fwRules is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        FwRemoveBtn.Content = T("fw.remove", _fwSel.Count);
        FwRemoveBtn.IsEnabled = !_fwBusy && _fwSel.Count > 0;
        if (_fwRules is null) { FwState.Text = _fwBusy ? T("fw.scanning") : T("fw.idle"); return; }
        if (_fwRules.Count == 0) { FwState.Text = T("fw.clean"); return; }
        int o = _fwRules.Count(r => r.Kind == FirewallCleaner.Kind.Orphan), d = _fwRules.Count - o;
        FwState.Text = T("fw.found", o, d);
        foreach (var grp in _fwRules.GroupBy(r => r.Kind))
        {
            FwList.Children.Add(new TextBlock { Text = T(grp.Key == FirewallCleaner.Kind.Orphan ? "fw.g.orphan" : "fw.g.dup", grp.Count()), Style = (Style)FindResource("Overline"), Margin = new Thickness(0, 14, 0, 6) });
            foreach (var r in grp)
            {
                var cb = new CheckBox { IsChecked = _fwSel.Contains(r.Id), IsEnabled = !_fwBusy, Margin = new Thickness(0, 3, 0, 3) };
                cb.SetResourceReference(Control.ForegroundProperty, "Ink");
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = r.Name + "   ·   " + r.Direction + (r.Protocol.Length > 0 ? " " + r.Protocol : ""), FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                if (r.Program.Length > 0) sp.Children.Add(new TextBlock { Text = r.Program, Style = (Style)FindResource("Sub"), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
                cb.Content = sp;
                var id = r.Id;
                cb.Click += (_, _) => { if (cb.IsChecked == true) _fwSel.Add(id); else _fwSel.Remove(id); FwRemoveBtn.Content = T("fw.remove", _fwSel.Count); FwRemoveBtn.IsEnabled = _fwSel.Count > 0; };
                FwList.Children.Add(cb);
            }
        }
    }

    private async void FwScan_Click(object s, RoutedEventArgs e)
    {
        _fwBusy = true; _fwRules = null; RenderFirewall();
        try
        {
            _fwRules = await FirewallCleaner.ScanAsync(CancellationToken.None);
            _fwSel.Clear();
            foreach (var r in _fwRules) _fwSel.Add(r.Id);
            Logger.Info(T("lg.fw.scan", _fwRules.Count(r => r.Kind == FirewallCleaner.Kind.Orphan), _fwRules.Count(r => r.Kind == FirewallCleaner.Kind.Duplicate)));
        }
        catch (Exception ex) { Logger.Error(T("lg.wt.fail", ex.Message)); FwState.Text = T("lg.wt.fail", ex.Message); }
        finally { _fwBusy = false; RenderFirewall(); }
    }

    private async void FwRemove_Click(object s, RoutedEventArgs e)
    {
        var sel = (_fwRules ?? []).Where(r => _fwSel.Contains(r.Id)).ToList();
        if (sel.Count == 0) return;
        if (!await Confirm(T("fw.title").ToUpper(Culture), T("fw.confirm.t", sel.Count), T("fw.confirm.x"), T("fw.remove", sel.Count), T("dlg.cancel"),
                           string.Join("\n", sel.GroupBy(r => r.Name).Select(g => "· " + g.Key + (g.Count() > 1 ? $" ×{g.Count()}" : ""))))) return;
        _fwBusy = true; RenderFirewall();
        try
        {
            var (ok, fail, _) = await FirewallCleaner.RemoveAsync(sel, CancellationToken.None);
            ShowToast(T("fw.toast.removed", ok, fail));
            _fwRules = await FirewallCleaner.ScanAsync(CancellationToken.None);
            _fwSel.Clear();
            foreach (var r in _fwRules) _fwSel.Add(r.Id);
        }
        catch (Exception ex) { Logger.Error(T("lg.wt.fail", ex.Message)); ShowToast(T("lg.wt.fail", ex.Message)); }
        finally { _fwBusy = false; RenderFirewall(); }
    }

    private async void FwRestore_Click(object s, RoutedEventArgs e)
    {
        if (FirewallCleaner.LastBackup() is not { } file) return;
        if (!await Confirm(T("fw.title").ToUpper(Culture), T("fw.restore.t"), T("fw.restore.x", File.GetLastWriteTime(file).ToString("g", Culture)), T("fw.restore"), T("dlg.cancel"))) return;
        _fwBusy = true; RenderFirewall();
        try
        {
            await FirewallCleaner.RestoreAsync(file, CancellationToken.None);
            ShowToast(T("fw.toast.restored"));
            _fwRules = null;
        }
        catch (Exception ex) { Logger.Error(T("lg.wt.fail", ex.Message)); ShowToast(T("lg.wt.fail", ex.Message)); }
        finally { _fwBusy = false; RenderFirewall(); }
    }

    private async void Report_Click(object s, RoutedEventArgs e)
    {
        ReportBtn.IsEnabled = false;
        ReportState.Text = T("rep.building");
        try
        {
            var file = await PcReport.SaveAsync(_settings, CancellationToken.None);
            try { Clipboard.SetText(await File.ReadAllTextAsync(file)); } catch { }
            ReportState.Text = T("rep.done", Path.GetFileName(file));
            ShowToast(T("rep.toast"));
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\""); } catch { }
        }
        catch (Exception ex) { Logger.Error(T("lg.wt.fail", ex.Message)); ReportState.Text = T("lg.wt.fail", ex.Message); }
        finally { ReportBtn.IsEnabled = true; }
    }
}
