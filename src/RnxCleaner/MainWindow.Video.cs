using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Pestana "Video CS2": editar cs2_video.txt con opciones en lenguaje normal y bloquearlo.</summary>
public partial class MainWindow
{
    private List<Cs2Video.Account> _vidAccounts = [];
    private Cs2Video.Account? _vidAcc;
    private Dictionary<string, string> _vid = new(StringComparer.OrdinalIgnoreCase);

    private void LoadVideo()
    {
        try { _vidAccounts = Cs2Video.Accounts(); }
        catch (Exception ex) { Logger.Error(T("lg.vid.fail", ex.Message)); _vidAccounts = []; }
        if (_vidAcc is null || !_vidAccounts.Any(a => a.Id == _vidAcc.Id))
            _vidAcc = _vidAccounts.FirstOrDefault(a => a.Id == _settings.VideoAccount) ?? _vidAccounts.FirstOrDefault();
        RenderVideo();
    }

    private void RenderVideo()
    {
        // Cuentas (solo si hay mas de una)
        VidAccounts.Children.Clear();
        foreach (var a in _vidAccounts)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = a.Name, IsChecked = a.Id == _vidAcc?.Id, GroupName = "vidacc", ToolTip = a.File };
            var acc = a;
            rb.Click += (_, _) => { _vidAcc = acc; _settings.VideoAccount = acc.Id; _settings.Save(); RenderVideo(); };
            VidAccounts.Children.Add(rb);
        }
        VidAccountsBox.Visibility = _vidAccounts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        bool has = _vidAcc is not null;
        VidEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        VidLockCard.Visibility = VidForm.Visibility = VidRnxCard.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        VidCs2Warn.Visibility = has && SystemInfo.IsRunning("cs2") ? Visibility.Visible : Visibility.Collapsed;
        RenderRes();
        if (!has) return;

        try { _vid = Cs2Video.Read(_vidAcc!.File); }
        catch (Exception ex) { ShowToast(T("lg.vid.fail", ex.Message)); return; }

        VidRnxRestore.Visibility = Cs2Video.HasConfigBackup(_vidAcc!) ? Visibility.Visible : Visibility.Collapsed;
        bool locked = Cs2Video.IsLocked(_vidAcc!.File);
        VidLock.IsChecked = locked;
        VidLockTitle.Text = T(locked ? "vid.lock.on.t" : "vid.lock.off.t");
        VidLockSub.Text = T(locked ? "vid.lock.on.x" : "vid.lock.off.x");
        VidLockTitle.SetResourceReference(TextBlock.ForegroundProperty, locked ? "Accent" : "Ink");
        VidForm.IsEnabled = !locked;
        VidForm.Opacity = locked ? 0.5 : 1;

        // Presets
        VidPresets.Children.Clear();
        foreach (var p in Cs2Video.Presets)
        {
            var preset = Cs2Video.Preset(p);
            bool on = IsVid(preset);
            var sp = new StackPanel { Margin = new Thickness(2) };
            var t = new TextBlock { Text = T("vid.preset." + p), FontWeight = FontWeights.SemiBold, FontSize = 14.5 };
            if (on) t.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            sp.Children.Add(t);
            sp.Children.Add(new TextBlock { Text = T("vid.preset." + p + ".d"), Style = (Style)FindResource("Sub"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
            var b = new Button { Style = (Style)FindResource("GhostButton"), Content = sp, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 10, 0), Height = double.NaN };
            if (on) b.SetResourceReference(Control.BorderBrushProperty, "Accent");
            b.Click += (_, _) => ApplyVid(preset, T("vid.preset." + p));
            VidPresets.Children.Add(b);
        }

        // Pantalla
        VidScreen.Children.Clear();
        var (modes, hzs) = Cs2Video.DisplayModes();
        int.TryParse(Get("setting.defaultres"), out var w);
        int.TryParse(Get("setting.defaultresheight"), out var h);
        if (w > 0 && h > 0 && !modes.Contains((w, h))) modes.Insert(0, (w, h));
        AddVidRow(VidScreen, "vid.res", "vid.res.h", PickButton($"{w} × {h}",
            modes.Select(m => ($"{m.W} × {m.H}", m.W == w && m.H == h, D(("setting.defaultres", m.W.ToString()), ("setting.defaultresheight", m.H.ToString())))).ToList()));
        AddVidRow(VidScreen, "vid.aspect", "vid.aspect.h", Seg(
            ("4:3", D(("setting.aspectratiomode", "0"))), ("16:9", D(("setting.aspectratiomode", "1"))), ("16:10", D(("setting.aspectratiomode", "2")))));
        AddVidRow(VidScreen, "vid.mode", "vid.mode.h", Seg(
            (T("vid.mode.full"), D(("setting.fullscreen", "1"), ("setting.nowindowborder", "0"))),
            (T("vid.mode.borderless"), D(("setting.fullscreen", "0"), ("setting.nowindowborder", "1"))),
            (T("vid.mode.window"), D(("setting.fullscreen", "0"), ("setting.nowindowborder", "0")))));
        double curHz = double.TryParse(Get("setting.refreshrate_numerator"), out var num) && double.TryParse(Get("setting.refreshrate_denominator"), out var den) && den > 0 ? num / den : 0;
        int curHzR = (int)Math.Round(curHz);
        if (curHzR > 0 && !hzs.Contains(curHzR)) hzs.Insert(0, curHzR);
        AddVidRow(VidScreen, "vid.hz", "vid.hz.h", PickButton(curHzR > 0 ? $"{curHzR} Hz" : "-",
            hzs.Select(z => ($"{z} Hz", z == curHzR, z == curHzR
                ? new Dictionary<string, string>()
                : D(("setting.refreshrate_numerator", (z * 1000).ToString()), ("setting.refreshrate_denominator", "1000")))).ToList()));
        AddVidRow(VidScreen, "vid.vsync", "vid.vsync.h", Seg(
            (T("vid.off"), D(("setting.mat_vsync", "0"))), (T("vid.on"), D(("setting.mat_vsync", "1")))), last: true);

        // Calidad
        VidQuality.Children.Clear();
        string[] lmhv = [T("vid.low"), T("vid.medium"), T("vid.high"), T("vid.veryhigh")];
        AddVidRow(VidQuality, "vid.shadows", "vid.shadows.h", Levels("setting.videocfg_shadow_quality", lmhv));
        AddVidRow(VidQuality, "vid.dynshadows", "vid.dynshadows.h", Seg(
            (T("vid.dyn.sun"), D(("setting.videocfg_dynamic_shadows", "0"))), (T("vid.dyn.all"), D(("setting.videocfg_dynamic_shadows", "1")))));
        AddVidRow(VidQuality, "vid.textures", "vid.textures.h", Levels("setting.videocfg_texture_detail", lmhv[..3]));
        AddVidRow(VidQuality, "vid.filter", "vid.filter.h", Levels("setting.r_texturefilteringquality",
            [T("vid.filter.bi"), T("vid.filter.tri"), "2x", "4x", "8x", "16x"]));
        AddVidRow(VidQuality, "vid.shader", "vid.shader.h", Seg(
            (T("vid.low"), D(("setting.shaderquality", "0"))), (T("vid.high"), D(("setting.shaderquality", "1")))));
        AddVidRow(VidQuality, "vid.particles", "vid.particles.h", Levels("setting.videocfg_particle_detail", lmhv));
        AddVidRow(VidQuality, "vid.ao", "vid.ao.h", Levels("setting.videocfg_ao_detail", [T("vid.off"), T("vid.medium"), T("vid.high")]));
        AddVidRow(VidQuality, "vid.hdr", "vid.hdr.h", Seg(
            (T("vid.hdr.perf"), D(("setting.videocfg_hdr_detail", "-1"))), (T("vid.hdr.quality"), D(("setting.videocfg_hdr_detail", "3")))));
        AddVidRow(VidQuality, "vid.aa", "vid.aa.h", Seg(
            (T("vid.off"), D(("setting.msaa_samples", "0"), ("setting.r_csgo_cmaa_enable", "0"))),
            ("CMAA2", D(("setting.msaa_samples", "0"), ("setting.r_csgo_cmaa_enable", "1"))),
            ("2x", D(("setting.msaa_samples", "2"), ("setting.r_csgo_cmaa_enable", "0"))),
            ("4x", D(("setting.msaa_samples", "4"), ("setting.r_csgo_cmaa_enable", "0"))),
            ("8x", D(("setting.msaa_samples", "8"), ("setting.r_csgo_cmaa_enable", "0")))));
        AddVidRow(VidQuality, "vid.fsr", "vid.fsr.h", Levels("setting.videocfg_fsr_detail",
            [T("vid.off"), T("vid.fsr.ultra"), T("vid.fsr.quality"), T("vid.fsr.balanced"), T("vid.fsr.perf")]));
        AddVidRow(VidQuality, "vid.reflex", "vid.reflex.h", Levels("setting.r_low_latency",
            [T("vid.off"), T("vid.on"), T("vid.reflex.boost")]), last: true);
    }

    private string Get(string key) => _vid.TryGetValue(key, out var v) ? v : "";

    private bool IsVid(IReadOnlyDictionary<string, string> ch) =>
        ch.Count > 0 && ch.All(kv => _vid.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static Dictionary<string, string> D(params (string K, string V)[] p) =>
        p.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase);

    /// <summary>Opciones 0..n-1 de una misma clave.</summary>
    private FrameworkElement Levels(string key, string[] labels) =>
        Seg(labels.Select((l, i) => (l, D((key, i.ToString())))).ToArray());

    private FrameworkElement Seg(params (string Label, Dictionary<string, string> Changes)[] opts)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        string group = "vid" + Guid.NewGuid().ToString("N")[..8];
        foreach (var (label, ch) in opts)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = label, IsChecked = IsVid(ch), GroupName = group };
            rb.Click += (_, _) => ApplyVid(ch, label);
            sp.Children.Add(rb);
        }
        return new Border { Style = (Style)FindResource("SegBox"), Child = sp, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Boton con el valor actual que abre una lista (resolucion, Hz).</summary>
    private FrameworkElement PickButton(string current, List<(string Label, bool On, Dictionary<string, string> Changes)> items)
    {
        var list = new StackPanel();
        var popup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 6 };
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), MinWidth = 190 };
        border.SetResourceReference(Border.BackgroundProperty, "Sheet");
        border.SetResourceReference(Border.BorderBrushProperty, "Edge");
        border.Child = new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        popup.Child = border;
        foreach (var (label, on, ch) in items)
        {
            var changes = ch;
            list.Children.Add(MenuRow(label, on, null, () => { if (changes.Count > 0) ApplyVid(changes, label); }, popup));
        }
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = current, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = "\uE70D", Style = (Style)FindResource("Icon"), FontSize = 10, Margin = new Thickness(10, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Style = (Style)FindResource("GhostButton"), Content = content, MinWidth = 150 };
        popup.PlacementTarget = b;
        b.Click += (_, _) => popup.IsOpen = true;
        var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(b);
        host.Children.Add(popup);
        return host;
    }

    private void AddVidRow(Panel parent, string titleKey, string hintKey, FrameworkElement control, bool last = false)
    {
        var dp = new DockPanel();
        DockPanel.SetDock(control, Dock.Right);
        dp.Children.Add(control);
        var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = T(titleKey), FontWeight = FontWeights.SemiBold, FontSize = 14 });
        left.Children.Add(new TextBlock { Text = T(hintKey), Style = (Style)FindResource("Sub"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
        dp.Children.Add(left);
        parent.Children.Add(dp);
        if (!last)
        {
            var sep = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) };
            sep.SetResourceReference(Border.BackgroundProperty, "Line2");
            parent.Children.Add(sep);
        }
    }

    /// <summary>Guarda al momento (solo si no esta bloqueado).</summary>
    private void ApplyVid(IReadOnlyDictionary<string, string> changes, string what)
    {
        if (_vidAcc is null) return;
        try
        {
            if (Cs2Video.IsLocked(_vidAcc.File)) { ShowToast(T("vid.toast.locked")); RenderVideo(); return; }
            Cs2Video.BackupOnce(_vidAcc.File);
            Cs2Video.Write(_vidAcc.File, changes);
            Logger.Detail(T("lg.vid.saved", _vidAcc.Name, what));
            ShowToast(T("vid.toast.saved", what));
        }
        catch (Exception ex) { Logger.Error(T("lg.vid.fail", ex.Message)); ShowToast(T("lg.vid.fail", ex.Message)); }
        RenderVideo();
    }

    private void VidLock_Click(object s, RoutedEventArgs e)
    {
        if (_vidAcc is null) return;
        bool lockIt = VidLock.IsChecked == true;
        try
        {
            Cs2Video.SetLocked(_vidAcc.File, lockIt);
            Logger.Info(T(lockIt ? "lg.vid.locked" : "lg.vid.unlocked", _vidAcc.Name));
            ShowToast(T(lockIt ? "vid.toast.lockOn" : "vid.toast.lockOff"));
        }
        catch (Exception ex) { Logger.Error(T("lg.vid.fail", ex.Message)); ShowToast(T("lg.vid.fail", ex.Message)); }
        RenderVideo();
    }

    private void VidRefresh_Click(object s, RoutedEventArgs e) => LoadVideo();

    // ---- Config completa de RNX
    private async Task<bool> CloseCs2ForConfigAsync()
    {
        if (!SystemInfo.IsRunning("cs2")) return true;
        if (!await Confirm(T("dlg.warn"), T("dlg.cs2.t"), T("vid.rnx.cs2"), T("dlg.cs2.yes"), T("dlg.cancel"))) return false;
        Sys.KillProcess("cs2");
        await Task.Delay(1500);
        return true;
    }

    private async void VidRnxApply_Click(object s, RoutedEventArgs e)
    {
        if (_vidAcc is null) return;
        if (!await Confirm(T("vid.rnx.title").ToUpper(Culture), T("vid.rnx.dlg.t", _vidAcc.Name), T("vid.rnx.dlg.x"), T("vid.rnx.btn"), T("dlg.cancel"))) return;
        if (!await CloseCs2ForConfigAsync()) return;
        try
        {
            int n = Cs2Video.ApplyRnxConfig(_vidAcc);
            Logger.Ok(T("lg.vid.rnx", _vidAcc.Name, n));
            ShowToast(T("vid.rnx.toast"));
        }
        catch (Exception ex) { Logger.Error(T("lg.vid.fail", ex.Message)); ShowToast(T("lg.vid.fail", ex.Message)); }
        RenderVideo();
    }

    private async void VidRnxRestore_Click(object s, RoutedEventArgs e)
    {
        if (_vidAcc is null || !Cs2Video.HasConfigBackup(_vidAcc)) return;
        if (!await Confirm(T("vid.rnx.title").ToUpper(Culture), T("vid.rnx.restore.t", _vidAcc.Name), T("vid.rnx.restore.x"), T("vid.rnx.restore"), T("dlg.cancel"))) return;
        if (!await CloseCs2ForConfigAsync()) return;
        try
        {
            int n = Cs2Video.RestoreConfig(_vidAcc);
            Logger.Ok(T("lg.vid.rnxRestored", _vidAcc.Name, n));
            ShowToast(T("vid.rnx.restored"));
        }
        catch (Exception ex) { Logger.Error(T("lg.vid.fail", ex.Message)); ShowToast(T("lg.vid.fail", ex.Message)); }
        RenderVideo();
    }

    // ===================================================================
    //  Resoluciones personalizadas (escaladas por la grafica)
    // ===================================================================
    private bool _resChanged;

    private void RenderRes()
    {
        var gpu = CustomRes.Gpus().FirstOrDefault();
        ResCard.Visibility = Visibility.Visible;
        ResList.Children.Clear();
        ResQuick.Children.Clear();
        ResReboot.Visibility = _resChanged ? Visibility.Visible : Visibility.Collapsed;
        if (gpu is null)
        {
            ResSub.Text = T("res.nogpu");
            ResRestoreBtn.Visibility = Visibility.Collapsed;
            ResW.IsEnabled = ResH.IsEnabled = false;
            return;
        }
        ResSub.Text = T("res.sub", gpu.Name);
        ResRestoreBtn.Visibility = CustomRes.HasBackup(gpu) ? Visibility.Visible : Visibility.Collapsed;
        List<CustomRes.Res> list;
        try { list = CustomRes.Read(gpu); }
        catch (Exception ex) { ResSub.Text = T("lg.res.fail", ex.Message); return; }

        foreach (var r in list)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = $"{r.W} × {r.H}", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            var del = new Button { Style = (Style)FindResource("LinkButton"), Content = "\u2715", FontSize = 11, Margin = new Thickness(8, 0, 0, 0), ToolTip = T("res.remove"), VerticalAlignment = VerticalAlignment.Center };
            var res = r;
            del.Click += async (_, _) => await ChangeResAsync(() => CustomRes.Remove(gpu, res));
            sp.Children.Add(del);
            var chip = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 8, 5), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1) };
            chip.SetResourceReference(Border.BackgroundProperty, "Track");
            chip.SetResourceReference(Border.BorderBrushProperty, "Line2");
            ResList.Children.Add(chip);
        }

        foreach (var q in new[] { new CustomRes.Res(1352, 1080), new CustomRes.Res(1440, 1080), new CustomRes.Res(1280, 960), new CustomRes.Res(1024, 768) })
        {
            if (list.Contains(q)) continue;
            var b = new Button { Style = (Style)FindResource("GhostButton"), Content = $"+ {q.W}×{q.H}", Margin = new Thickness(8, 0, 0, 0) };
            var res = q;
            b.Click += async (_, _) => await ChangeResAsync(() => CustomRes.Add(gpu, res));
            ResQuick.Children.Add(b);
        }
    }

    private async Task ChangeResAsync(Action change)
    {
        try
        {
            await Task.Run(change);
            _resChanged = true;
            ShowToast(T("res.toast"));
        }
        catch (Exception ex) { Logger.Error(T("lg.res.fail", ex.Message)); ShowToast(T("lg.res.fail", ex.Message)); }
        RenderRes();
    }

    private async void ResAdd_Click(object s, RoutedEventArgs e)
    {
        var gpu = CustomRes.Gpus().FirstOrDefault();
        if (gpu is null) return;
        if (!int.TryParse(ResW.Text.Trim(), out var w) || !int.TryParse(ResH.Text.Trim(), out var h) || !CustomRes.Valid(w, h))
        {
            ShowToast(T("res.invalid", CustomRes.Min, CustomRes.Max));
            return;
        }
        ResW.Text = ResH.Text = "";
        await ChangeResAsync(() => CustomRes.Add(gpu, new CustomRes.Res(w, h)));
    }

    private async void ResRestore_Click(object s, RoutedEventArgs e)
    {
        var gpu = CustomRes.Gpus().FirstOrDefault();
        if (gpu is null) return;
        if (!await Confirm(T("res.title").ToUpper(Culture), T("res.restore.t"), T("res.restore.x"), T("res.restore"), T("dlg.cancel"))) return;
        await ChangeResAsync(() => { if (CustomRes.RestoreDefault(gpu)) Logger.Ok(T("lg.res.restored", gpu.Name)); });
    }

    private async void ResReboot_Click(object s, RoutedEventArgs e)
    {
        if (!await Confirm(T("dlg.restart.o"), T("dlg.restart.t"), T("res.reboot"), T("dlg.restart.yes"), T("dlg.restart.no"))) return;
        Logger.Info(T("lg.restart"));
        await Sys.RunAsync("shutdown.exe", $"/r /t 10 /c \"{T("restart.comment")}\"", 30);
        Application.Current.Shutdown();
    }
}
