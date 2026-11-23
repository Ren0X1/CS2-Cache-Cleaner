using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Pestana "HUD": activar, tecla, diseno, posicion (6 + libre con candado), tamano, opacidad y filas.</summary>
public partial class MainWindow
{
    private bool _capturingKey;
    private bool _hudHooked;

    private void LoadHud()
    {
        if (!_hudHooked)
        {
            _hudHooked = true;
            App.HudTick += () => { if (PageHud.Visibility == Visibility.Visible) RenderHudPreview(); };
            PreviewKeyDown += HudKeyCapture;
        }
        RenderHud();
    }

    private void RenderHud()
    {
        var s = _settings;
        HudToggle.IsChecked = s.HudEnabled;
        HudState.Text = !s.HudEnabled ? T("hud.state.off") : s.HudVisible ? T("hud.state.visible") : T("hud.state.hidden");
        HudState.SetResourceReference(TextBlock.ForegroundProperty, s.HudEnabled ? "Accent" : "Muted");

        HudKeyText.Text = _capturingKey ? T("hud.key.press") : s.HudHotkeyVk == 0 ? T("hud.key.none") : App.HotkeyText(s.HudHotkeyMods, s.HudHotkeyVk);
        HudKeyText.SetResourceReference(TextBlock.ForegroundProperty, _capturingKey ? "Accent" : s.HudHotkeyVk == 0 ? "Muted" : "Ink");
        HudKeyClear.IsEnabled = s.HudHotkeyVk != 0;

        Seg(HudStyles, "hudstyle", [("a1", T("hud.style.a1")), ("c1", T("hud.style.c1")), ("m", T("hud.style.m"))], s.HudStyle, v => { s.HudStyle = v; SaveHud(); });
        Seg(HudSizes, "hudsize", [("0.85", T("hud.size.s")), ("1", T("hud.size.m")), ("1.2", T("hud.size.l"))],
            s.HudScale < 0.95 ? "0.85" : s.HudScale > 1.1 ? "1.2" : "1",
            v => { s.HudScale = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); SaveHud(); });
        Seg(HudOpacities, "hudop", [("0.4", "40 %"), ("0.58", "58 %"), ("0.75", "75 %"), ("0.9", "90 %")],
            new[] { 0.4, 0.58, 0.75, 0.9 }.OrderBy(x => Math.Abs(x - s.HudOpacity)).First().ToString(System.Globalization.CultureInfo.InvariantCulture),
            v => { s.HudOpacity = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); SaveHud(); });

        // 6 posiciones en una "pantalla" pequena
        HudPositions.Children.Clear();
        foreach (var c in HudWindow.Corners)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = T("hud.pos." + c), IsChecked = s.HudCorner == c, GroupName = "hudpos", Margin = new Thickness(3), Height = 34,
                                       VerticalAlignment = c[0] == 'b' ? VerticalAlignment.Bottom : VerticalAlignment.Top };
            var corner = c;
            rb.Click += (_, _) => { s.HudCorner = corner; SaveHud(); };
            Grid.SetRow(rb, c[0] == 'b' ? 1 : 0);
            Grid.SetColumn(rb, c[1] switch { 'l' => 0, 'c' => 1, _ => 2 });
            HudPositions.Children.Add(rb);
        }
        HudFreeText.Visibility = s.HudCorner == "free" ? Visibility.Visible : Visibility.Collapsed;
        HudUnlock.IsChecked = !s.HudLocked;
        HudUnlock.IsEnabled = s.HudEnabled;

        HudRowCpu.IsChecked = s.HudCpu; HudRowRam.IsChecked = s.HudRam; HudRowGpu.IsChecked = s.HudGpu;
        HudRowFps.IsChecked = s.HudFps; HudRowGraph.IsChecked = s.HudGraph;

        HudPawnio.Visibility = App.HudRunning && App.HudLast is { CpuTemp: null } && App.HudLast.CpuLoad is not null ? Visibility.Visible : Visibility.Collapsed;
        RenderHudPreview();
    }

    private void RenderHudPreview()
    {
        HudPreview.Render(App.HudLast ?? HudView.Demo(), _settings);
        HudPreviewNote.Text = App.HudLast is null ? T("hud.preview.demo") : T("hud.preview.live", App.HudLast.Process.Length > 0 ? App.HudLast.Process : "-");
        HudPawnio.Visibility = App.HudRunning && App.HudLast is { CpuTemp: null } && App.HudLast.CpuLoad is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.HudEnabled) HudState.Text = _settings.HudVisible ? T("hud.state.visible") : T("hud.state.hidden");
    }

    private void Seg(Panel panel, string group, (string V, string Label)[] items, string current, Action<string> pick)
    {
        panel.Children.Clear();
        foreach (var (v, label) in items)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = label, IsChecked = v == current, GroupName = group };
            var val = v;
            rb.Click += (_, _) => pick(val);
            panel.Children.Add(rb);
        }
    }

    private void SaveHud()
    {
        _settings.Save();
        RenderHud();
    }

    private void HudToggle_Click(object s, RoutedEventArgs e)
    {
        _settings.HudEnabled = HudToggle.IsChecked == true;
        if (_settings.HudEnabled) _settings.HudVisible = true;
        if (!_settings.HudEnabled) _settings.HudLocked = true;   // al apagarlo, siempre vuelve bloqueado
        _settings.Save();
        App.ApplyHud();
        ShowToast(T(_settings.HudEnabled ? "hud.toast.on" : "hud.toast.off"));
        RenderHud();
    }

    private void HudUnlock_Click(object s, RoutedEventArgs e)
    {
        _settings.HudLocked = HudUnlock.IsChecked != true;
        if (!_settings.HudLocked) _settings.HudVisible = true;
        _settings.Save();
        ShowToast(T(_settings.HudLocked ? "hud.toast.locked" : "hud.toast.unlocked"));
        RenderHud();
    }

    private void HudRow_Click(object s, RoutedEventArgs e)
    {
        _settings.HudCpu = HudRowCpu.IsChecked == true;
        _settings.HudRam = HudRowRam.IsChecked == true;
        _settings.HudGpu = HudRowGpu.IsChecked == true;
        _settings.HudFps = HudRowFps.IsChecked == true;
        _settings.HudGraph = HudRowGraph.IsChecked == true;
        SaveHud();
    }

    // ---- Tecla: se pulsa la combinacion que se quiera (Esc cancela)
    private void HudKeyAssign_Click(object s, RoutedEventArgs e)
    {
        _capturingKey = true;
        Focus();
        RenderHud();
    }

    private void HudKeyClear_Click(object s, RoutedEventArgs e)
    {
        _settings.HudHotkeyVk = 0;
        _settings.HudHotkeyMods = 0;
        _settings.Save();
        App.RegisterHudHotkey();
        RenderHud();
    }

    private void HudKeyCapture(object s, KeyEventArgs e)
    {
        if (!_capturingKey) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { _capturingKey = false; RenderHud(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        int mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= 1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 2;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= 4;
        _capturingKey = false;
        _settings.HudHotkeyMods = mods;
        _settings.HudHotkeyVk = KeyInterop.VirtualKeyFromKey(key);
        _settings.Save();
        bool ok = App.RegisterHudHotkey();
        ShowToast(ok ? T("hud.toast.key", App.HotkeyText(mods, _settings.HudHotkeyVk)) : T("hud.toast.keyBusy", App.HotkeyText(mods, _settings.HudHotkeyVk)));
        RenderHud();
    }

    private async void HudPawnio_Click(object s, RoutedEventArgs e)
    {
        if (!await Confirm("PAWNIO", T("hud.pawnio.t"), T("hud.pawnio.x"), T("hud.pawnio.btn"), T("dlg.cancel"))) return;
        HudPawnioBtn.IsEnabled = false;
        try
        {
            ShowToast(T("hud.pawnio.downloading"));
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var file = await PawnIo.DownloadAsync(cts.Token);
            var signer = await PawnIo.VerifyAsync(file, cts.Token);
            if (signer is null)
            {
                try { System.IO.File.Delete(file); } catch { }
                Logger.Error(T("lg.pawnio.badsig"));
                ShowToast(T("lg.pawnio.badsig"));
                return;
            }
            Logger.Info(T("lg.pawnio.signed", signer));
            int code = await PawnIo.RunInstallerAsync(file, cts.Token);
            Logger.Info(T("lg.pawnio.done", code));
            App.RestartHud();
            await Task.Delay(3000);
            ShowToast(App.HudLast?.CpuTemp is not null ? T("hud.pawnio.ok") : T("hud.pawnio.reboot"));
        }
        catch (Exception ex) { Logger.Error(T("lg.pawnio.fail", ex.Message)); ShowToast(T("lg.pawnio.fail", ex.Message)); }
        finally { HudPawnioBtn.IsEnabled = true; RenderHud(); }
    }
}
