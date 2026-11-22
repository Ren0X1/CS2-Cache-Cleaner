using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Pestana "Color en juegos": vibrance por juego como vibranceGUI.</summary>
public partial class MainWindow
{
    private IVibranceDriver? Driver => App.Vibrance?.Driver ?? _vibDriverProbe;
    private IVibranceDriver? _vibDriverProbe;

    private void LoadVibrance()
    {
        if (App.Vibrance is null && _vibDriverProbe is null) _vibDriverProbe = VibranceEngine.DetectDriver();
        RenderVibrance();
    }

    private void RenderVibrance()
    {
        var d = Driver;
        VibNoGpu.Visibility = d is null ? Visibility.Visible : Visibility.Collapsed;
        VibBody.Visibility = d is null ? Visibility.Collapsed : Visibility.Visible;
        if (d is null) return;

        bool running = App.Vibrance?.Running == true;
        VibToggle.IsChecked = _settings.VibranceEnabled;
        VibState.Text = !_settings.VibranceEnabled ? T("vib.state.off")
                      : running ? (App.Vibrance!.ActiveGame is { } g ? T("vib.state.game", g) : T("vib.state.on"))
                      : App.Vibrance is null && !SnapMode ? T("vib.state.error") : T("vib.state.starting");
        VibState.SetResourceReference(TextBlock.ForegroundProperty, _settings.VibranceEnabled && running ? "Accent" : "Muted");
        VibGpu.Text = T("vib.gpu", d.Vendor);

        bool other = VibranceEngine.VibranceGuiRunning;
        VibOtherWarn.Visibility = other ? Visibility.Visible : Visibility.Collapsed;
        VibImportBtn.Visibility = File.Exists(Path.Combine(VibranceEngine.VibranceGuiDir, "applicationData.xml")) ? Visibility.Visible : Visibility.Collapsed;

        // Nivel de Windows y monitor principal
        VibDefault.Minimum = d.MinPercent;
        VibDefault.Maximum = d.MaxPercent;
        VibDefault.Value = Math.Clamp(_settings.VibranceDefault, d.MinPercent, d.MaxPercent);
        VibDefaultText.Text = $"{(int)VibDefault.Value} %";
        VibPrimary.IsChecked = _settings.VibrancePrimaryOnly;

        // Juegos
        VibList.Children.Clear();
        VibEmpty.Visibility = _settings.VibranceProfiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var (modes, hzs) = Cs2Video.DisplayModes();
        foreach (var p in _settings.VibranceProfiles.ToList())
            VibList.Children.Add(VibRow(p, d, modes, hzs));
    }

    private FrameworkElement VibRow(VibranceProfile p, IVibranceDriver d, List<(int W, int H)> modes, List<int> hzs)
    {
        var grid = new Grid { Margin = new Thickness(16, 10, 16, 10) };
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(250), GridLength.Auto, GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });

        var img = new Image { Width = 32, Height = 32, Margin = new Thickness(0, 0, 14, 0), Source = IconOf(p.Path), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(img);

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        name.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        name.Children.Add(new TextBlock { Text = p.Path, Style = (Style)FindResource("Sub"), FontSize = 11.5, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = p.Path });
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        // Color
        var sp = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var val = new TextBlock { Width = 48, TextAlignment = TextAlignment.Right, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Text = $"{p.Percent} %" };
        DockPanel.SetDock(val, Dock.Right);
        sp.Children.Add(val);
        var slider = new Slider { Minimum = d.MinPercent, Maximum = d.MaxPercent, Value = Math.Clamp(p.Percent, d.MinPercent, d.MaxPercent), IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("vib.color") };
        slider.ValueChanged += (_, e) => val.Text = $"{(int)e.NewValue} %";
        slider.PreviewMouseUp += (_, _) => { p.Percent = (int)slider.Value; SaveVib(); };
        slider.LostKeyboardFocus += (_, _) => { if (p.Percent != (int)slider.Value) { p.Percent = (int)slider.Value; SaveVib(); } };
        sp.Children.Add(slider);
        Grid.SetColumn(sp, 2);
        grid.Children.Add(sp);

        // Resolucion
        var res = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        var chk = new CheckBox { Content = T("vib.res"), IsChecked = p.ChangeResolution, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        chk.SetResourceReference(Control.ForegroundProperty, "Ink");
        chk.Click += (_, _) =>
        {
            p.ChangeResolution = chk.IsChecked == true;
            if (p.ChangeResolution && p.Width == 0 && modes.Count > 0) { p.Width = modes[0].W; p.Height = modes[0].H; p.Hz = hzs.FirstOrDefault(); }
            SaveVib();
        };
        res.Children.Add(chk);
        if (p.ChangeResolution)
        {
            var label = p.Width > 0 ? $"{p.Width}×{p.Height}" + (p.Hz > 0 ? $" @{p.Hz}" : "") : "-";
            var items = modes.SelectMany(m => hzs.Take(4).Select(z => (m, z)))
                             .Select(x => ($"{x.m.W}×{x.m.H} @{x.z}", x.m.W == p.Width && x.m.H == p.Height && x.z == p.Hz,
                                           new Dictionary<string, string> { ["w"] = x.m.W.ToString(), ["h"] = x.m.H.ToString(), ["z"] = x.z.ToString() })).ToList();
            res.Children.Add(VibPick(label, items, ch => { p.Width = int.Parse(ch["w"]); p.Height = int.Parse(ch["h"]); p.Hz = int.Parse(ch["z"]); SaveVib(); }));
        }
        Grid.SetColumn(res, 3);
        grid.Children.Add(res);

        var del = new Button { Style = (Style)FindResource("GhostButton"), Content = T("vib.remove"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        del.Click += (_, _) => { _settings.VibranceProfiles.Remove(p); SaveVib(); };
        Grid.SetColumn(del, 4);
        grid.Children.Add(del);

        var wrap = new Border { Child = grid, BorderThickness = new Thickness(0, 1, 0, 0) };
        wrap.SetResourceReference(Border.BorderBrushProperty, "Line2");
        return wrap;
    }

    /// <summary>Boton con lista (como el de resolucion de Video CS2) que devuelve el elemento elegido.</summary>
    private FrameworkElement VibPick(string current, List<(string Label, bool On, Dictionary<string, string> Data)> items, Action<Dictionary<string, string>> pick)
    {
        var list = new StackPanel();
        var popup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 6 };
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), MinWidth = 190 };
        border.SetResourceReference(Border.BackgroundProperty, "Sheet");
        border.SetResourceReference(Border.BorderBrushProperty, "Edge");
        border.Child = new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        popup.Child = border;
        foreach (var (label, on, data) in items)
        {
            var dd = data;
            list.Children.Add(MenuRow(label, on, null, () => pick(dd), popup));
        }
        var b = new Button { Style = (Style)FindResource("GhostButton"), Content = current, MinWidth = 140 };
        popup.PlacementTarget = b;
        b.Click += (_, _) => popup.IsOpen = true;
        var host = new Grid();
        host.Children.Add(b);
        host.Children.Add(popup);
        return host;
    }

    private static ImageSource? IconOf(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var ic = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (ic is null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    private void SaveVib()
    {
        _settings.Save();
        App.Vibrance?.Refresh();
        RenderVibrance();
    }

    private void VibToggle_Click(object s, RoutedEventArgs e)
    {
        _settings.VibranceEnabled = VibToggle.IsChecked == true;
        _settings.Save();
        if (_settings.VibranceEnabled) App.StartVibrance(); else App.StopVibrance();
        ShowToast(T(_settings.VibranceEnabled ? "vib.toast.on" : "vib.toast.off"));
        RenderVibrance();
    }

    private void VibDefault_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VibDefaultText is not null) VibDefaultText.Text = $"{(int)e.NewValue} %";
    }

    private void VibDefault_Commit(object s, RoutedEventArgs e)
    {
        if (_settings.VibranceDefault == (int)VibDefault.Value) return;
        _settings.VibranceDefault = (int)VibDefault.Value;
        SaveVib();
    }

    private void VibPrimary_Click(object s, RoutedEventArgs e)
    {
        _settings.VibrancePrimaryOnly = VibPrimary.IsChecked == true;
        SaveVib();
    }

    private void AddVibProfile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (_settings.VibranceProfiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { ShowToast(T("vib.toast.exists", name)); return; }
        var d = Driver;
        _settings.VibranceProfiles.Add(new VibranceProfile { Name = name, Path = path, Percent = d is null ? 80 : Math.Min(d.MaxPercent, d.DefaultPercent + (d.MaxPercent - d.DefaultPercent) * 3 / 5) });
        Logger.Info(T("lg.vib.added", name));
        SaveVib();
    }

    private void VibAddFile_Click(object s, RoutedEventArgs e)
    {
        if (PickFile(T("vib.filter") + " (*.exe)|*.exe", null) is { } f) AddVibProfile(f);
    }

    /// <summary>"Anadir": lista de programas abiertos con ventana.</summary>
    private void VibAddRunning_Click(object s, RoutedEventArgs e)
    {
        var procs = new List<(string Name, string Path, string Title)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || p.Id == Environment.ProcessId || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                var path = p.MainModule?.FileName;
                if (path is null || path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) continue;
                procs.Add((p.ProcessName, path, p.MainWindowTitle));
            }
            catch { }
            finally { p.Dispose(); }
        }
        VibRunningList.Children.Clear();
        foreach (var (name, path, title) in procs.DistinctBy(x => x.Name.ToLowerInvariant()).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var pp = path;
            VibRunningList.Children.Add(MenuRow($"{name}  ·  {title}", false, null, () => AddVibProfile(pp), VibRunningPopup));
        }
        if (procs.Count == 0) { ShowToast(T("vib.toast.noprocs")); return; }
        VibRunningPopup.IsOpen = true;
    }

    private void VibImport_Click(object s, RoutedEventArgs e)
    {
        var d = Driver;
        var (list, winPct, primary) = VibranceEngine.ImportVibranceGui(d?.Vendor != "AMD");
        int added = 0;
        foreach (var p in list)
            if (!_settings.VibranceProfiles.Any(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))) { _settings.VibranceProfiles.Add(p); added++; }
        if (winPct is { } w) _settings.VibranceDefault = w;
        if (primary is { } pr) _settings.VibrancePrimaryOnly = pr;
        Logger.Info(T("lg.vib.imported", added));
        ShowToast(T("vib.toast.imported", added));
        SaveVib();
    }

    private void VibCloseOther_Click(object s, RoutedEventArgs e)
    {
        Sys.KillProcess("vibranceGUI");
        Logger.Info(T("lg.vib.closedOther"));
        App.Vibrance?.Refresh();
        RenderVibrance();
    }
}
