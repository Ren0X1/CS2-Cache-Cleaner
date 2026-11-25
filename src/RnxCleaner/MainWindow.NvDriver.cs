using System.IO;
using System.Windows;
using System.Windows.Controls;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Pestana "Driver NVIDIA": buscar, descargar, elegir componentes e instalar el driver pelado.</summary>
public partial class MainWindow
{
    private NvidiaDriver.Gpu? _nvGpu;
    private List<NvidiaDriver.Release> _nvReleases = [];
    private NvidiaDriver.Release? _nvPick;
    private string? _nvLocalFile;
    private string? _nvDir;                       // paquete descomprimido
    private List<NvidiaDriver.Component> _nvComps = [];
    private readonly HashSet<string> _nvSel = new(StringComparer.OrdinalIgnoreCase);
    private bool _nvBusy, _nvStudio;
    private CancellationTokenSource? _nvCts;

    /// <summary>Descripciones conocidas de los componentes del instalador de NVIDIA.</summary>
    private static string NvDesc(string folder) => folder.ToLowerInvariant() switch
    {
        "display.driver" => T("nvd.c.driver"),
        "physx" => T("nvd.c.physx"),
        "hdaudio" => T("nvd.c.hdaudio"),
        "nvapp" or "gfexperience" or "nvapp.messagebus" => T("nvd.c.app"),
        "nvtelemetry" => T("nvd.c.telemetry"),
        "nvcontainer" => T("nvd.c.container"),
        "shadowplay" => T("nvd.c.shadowplay"),
        "frameviewsdk" => T("nvd.c.frameview"),
        "ppc" => T("nvd.c.usbc"),
        "nvpcf" => T("nvd.c.nvpcf"),
        "nvcpl" => T("nvd.c.cpl"),
        "msvcrt" => T("nvd.c.msvcrt"),
        "nvbackend" => T("nvd.c.backend"),
        "nvdlisr" or "nvdlist" => T("nvd.c.dlisr"),
        "nvvhci" or "nvvad" => T("nvd.c.virtual"),
        _ => T("nvd.c.other"),
    };

    /// <summary>Etiqueta "Nuevo" en el menu + aviso dentro de la app cuando hay un driver mas nuevo.</summary>
    private void OnNvUpdateChanged() => Dispatcher.BeginInvoke(() =>
    {
        var u = App.NvUpdate;
        bool show = u is not null && NavNvDriver.Visibility == Visibility.Visible;
        bool wasHidden = NvBadge.Visibility != Visibility.Visible;
        NvBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdateDrvGroupBadge();
        if (show && wasHidden && IsVisible && PageNvDriver.Visibility != Visibility.Visible)
            ShowToast(T("nvd.toast", u!.Latest.Version));
        if (PageNvDriver.Visibility == Visibility.Visible) RenderNvDriver();
    });

    private async void LoadNvDriver()
    {
        _nvGpu ??= NvidiaDriver.Detect();
        RenderNvDriver();
        if (_nvGpu is not null && _nvReleases.Count == 0 && !_nvBusy) await NvLookupAsync();
    }

    private async Task NvLookupAsync()
    {
        if (_nvGpu is null) return;
        NvStatus.Text = T("nvd.checking");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _nvReleases = await NvidiaDriver.LookupAsync(_nvGpu, _nvStudio, cts.Token);
            _nvPick = _nvReleases.FirstOrDefault();
            _nvLocalFile = null;
            if (!_nvStudio)
            {
                int behind = NvidiaDriver.Behind(_nvReleases, _nvGpu.InstalledVersion);
                App.SetNvUpdate(behind > 0 ? new NvidiaDriver.UpdateInfo(_nvGpu, _nvReleases[0], behind) : null);
            }
        }
        catch (Exception ex) { Logger.Warn(T("lg.nvd.lookupFail", ex.Message)); NvStatus.Text = T("lg.nvd.lookupFail", ex.Message); }
        RenderNvDriver();
    }

    private void RenderNvDriver()
    {
        bool has = _nvGpu is not null;
        NvNoGpu.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        NvBody.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has) return;

        NvGpuName.Text = _nvGpu!.Name;
        NvInstalled.Text = T("nvd.installed", _nvGpu.InstalledVersion);
        var latest = _nvReleases.FirstOrDefault();
        if (latest is not null)
        {
            bool newer = Version.TryParse(latest.Version, out var lv) && Version.TryParse(_nvGpu.InstalledVersion, out var iv) && lv > iv;
            NvStatus.Text = newer ? T("nvd.update", latest.Version, latest.Date.ToString("d", Culture)) : T("nvd.uptodate", latest.Version);
            NvStatus.SetResourceReference(TextBlock.ForegroundProperty, newer ? "Accent" : "Ink");
        }
        NvNotesBtn.IsEnabled = _nvPick?.DetailsUrl.Length > 0;

        // Aviso destacado: driver desactualizado
        if (App.NvUpdate is { } u)
        {
            NvAlert.Visibility = Visibility.Visible;
            NvAlertTitle.Text = T("nvd.alert.t", u.Latest.Version);
            int days = Math.Max(0, (int)(DateTime.Now - u.Latest.Date).TotalDays);
            NvAlertText.Text = T(u.Behind == 1 ? "nvd.alert.x1" : "nvd.alert.x", u.Gpu.InstalledVersion, u.Behind, u.Latest.Date.ToString("d", Culture), days);
        }
        else NvAlert.Visibility = Visibility.Collapsed;

        // Version a instalar
        Seg(NvKind, "nvkind", [("grd", T("nvd.grd")), ("studio", T("nvd.studio"))], _nvStudio ? "studio" : "grd", v =>
        {
            _nvStudio = v == "studio"; _nvReleases = []; _ = NvLookupAsync();
        });
        NvVersions.Content = NvVersionDropdown();
        NvPickText.Text = _nvLocalFile is not null ? T("nvd.local", Path.GetFileName(_nvLocalFile))
                        : _nvPick is not null ? T("nvd.pick", _nvPick.Version, _nvPick.Date.ToString("d", Culture), _nvPick.Size) : "-";

        // Paso 2: componentes
        NvCompsCard.Visibility = _nvDir is not null ? Visibility.Visible : Visibility.Collapsed;
        NvCompList.Children.Clear();
        foreach (var c in _nvComps)
        {
            var cb = new CheckBox { IsChecked = c.Required || _nvSel.Contains(c.Folder), IsEnabled = !c.Required && !_nvBusy, Margin = new Thickness(0, 4, 0, 4) };
            cb.SetResourceReference(Control.ForegroundProperty, "Ink");
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = c.Folder + (c.Required ? "  ·  " + T("nvd.required") : ""), FontWeight = FontWeights.SemiBold });
            sp.Children.Add(new TextBlock { Text = NvDesc(c.Folder), Style = (Style)FindResource("Sub"), FontSize = 12 });
            cb.Content = sp;
            var comp = c;
            cb.Click += (_, _) => { if (cb.IsChecked == true) _nvSel.Add(comp.Folder); else _nvSel.Remove(comp.Folder); };
            NvCompList.Children.Add(cb);
        }

        NvDownloadBtn.IsEnabled = !_nvBusy && (_nvPick is not null || _nvLocalFile is not null);
        NvInstallBtn.IsEnabled = NvSaveBtn.IsEnabled = !_nvBusy && _nvDir is not null;
        NvCancelBtn.Visibility = _nvBusy ? Visibility.Visible : Visibility.Collapsed;
        NvLocalBtn.IsEnabled = !_nvBusy;
    }

    /// <summary>Desplegable vertical con las versiones publicadas.</summary>
    private FrameworkElement NvVersionDropdown()
    {
        string Row(NvidiaDriver.Release r)
        {
            var t = $"{r.Version}   ·   {r.Date.ToString("d", Culture)}   ·   {r.Size}";
            if (r == _nvReleases.FirstOrDefault()) t += "   ·   " + T("nvd.latest");
            if (r.Version == _nvGpu?.InstalledVersion) t += "   ·   " + T("nvd.current");
            return t;
        }
        var list = new StackPanel();
        var popup = new System.Windows.Controls.Primitives.Popup { StaysOpen = false, AllowsTransparency = true, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, VerticalOffset = 6 };
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), MinWidth = 320 };
        border.SetResourceReference(Border.BackgroundProperty, "Sheet");
        border.SetResourceReference(Border.BorderBrushProperty, "Edge");
        border.Child = new ScrollViewer { MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        popup.Child = border;
        foreach (var r in _nvReleases)
        {
            var rr = r;
            list.Children.Add(MenuRow(Row(r), _nvLocalFile is null && r == _nvPick, null, () => { _nvPick = rr; _nvLocalFile = null; _nvDir = null; RenderNvDriver(); }, popup));
        }
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = _nvLocalFile is not null ? T("nvd.localShort") : _nvPick?.Version ?? "-", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = "", Style = (Style)FindResource("Icon"), FontSize = 10, Margin = new Thickness(12, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Style = (Style)FindResource("GhostButton"), Content = content, MinWidth = 160, IsEnabled = !_nvBusy && _nvReleases.Count > 0 };
        popup.PlacementTarget = b;
        b.Click += (_, _) => popup.IsOpen = true;
        var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(b);
        host.Children.Add(popup);
        return host;
    }

    private void NvPreset_Click(object s, RoutedEventArgs e)
    {
        var preset = (string)((FrameworkElement)s).Tag;
        _nvSel.Clear();
        foreach (var c in _nvComps) if (NvidiaDriver.InPreset(c.Folder, preset)) _nvSel.Add(c.Folder);
        RenderNvDriver();
    }

    private void NvNotes_Click(object s, RoutedEventArgs e)
    {
        if (_nvPick?.DetailsUrl is { Length: > 0 } u) OpenUnelevated(u);
    }

    private async void NvRefresh_Click(object s, RoutedEventArgs e) { _nvGpu = NvidiaDriver.Detect(); _nvReleases = []; await NvLookupAsync(); }

    private void NvLocal_Click(object s, RoutedEventArgs e)
    {
        if (PickFile(T("nvd.filter") + " (*.exe)|*.exe", null) is not { } f) return;
        _nvLocalFile = f; _nvDir = null;
        RenderNvDriver();
    }

    private void NvCancel_Click(object s, RoutedEventArgs e) => _nvCts?.Cancel();

    /// <summary>Paso 1: descargar (o usar el archivo local) y descomprimir.</summary>
    private async void NvDownload_Click(object s, RoutedEventArgs e)
    {
        _nvBusy = true; _nvCts = new CancellationTokenSource();
        NvBar.Visibility = Visibility.Visible; NvBar.Value = 0;
        RenderNvDriver();
        try
        {
            string installer;
            if (_nvLocalFile is not null) installer = _nvLocalFile;
            else
            {
                bool cached = NvidiaDriver.IsDownloaded(_nvPick!.Url);
                NvProgress.Text = cached ? T("nvd.cached", _nvPick.Version) : T("nvd.downloading", _nvPick.Version, 0);
                var prog = new Progress<double>(p => { NvBar.Value = p; NvProgress.Text = T("nvd.downloading", _nvPick!.Version, (int)(p * 100)); });
                installer = await NvidiaDriver.DownloadAsync(_nvPick.Url, prog, _nvCts.Token);
                if (!cached) Logger.Ok(T("lg.nvd.downloaded", Path.GetFileName(installer)));
            }
            NvProgress.Text = T("nvd.extracting");
            NvBar.IsIndeterminate = true;
            _nvDir = await NvidiaDriver.ExtractAsync(installer, _nvCts.Token);
            _nvComps = NvidiaDriver.Components(_nvDir);
            _nvSel.Clear();
            foreach (var c in _nvComps) if (NvidiaDriver.InPreset(c.Folder, "rec")) _nvSel.Add(c.Folder);
            NvProgress.Text = T("nvd.ready", _nvComps.Count);
        }
        catch (OperationCanceledException) { NvProgress.Text = T("nvd.cancelled"); }
        catch (Exception ex) { Logger.Error(T("lg.nvd.fail", ex.Message)); NvProgress.Text = T("lg.nvd.fail", ex.Message); }
        finally { _nvBusy = false; NvBar.IsIndeterminate = false; NvBar.Visibility = Visibility.Collapsed; RenderNvDriver(); }
    }

    /// <summary>Paso 2a: dejar solo lo elegido y abrir el instalador de NVIDIA.</summary>
    private async void NvInstall_Click(object s, RoutedEventArgs e)
    {
        if (_nvDir is null) return;
        if (!await Confirm(T("nvd.title").ToUpper(Culture), T("nvd.install.t"), T("nvd.install.x"), T("nvd.install.btn"), T("dlg.cancel"))) return;
        _nvBusy = true; RenderNvDriver();
        try
        {
            NvidiaDriver.Prepare(_nvDir, _nvSel);
            NvProgress.Text = T("nvd.installing");
            // Nada debe leer la GPU mientras se cambia el driver (el HUD y Digital Vibrance se pausan)
            await App.PauseGpuAsync();
            int code;
            try { code = await NvidiaDriver.InstallAsync(_nvDir, NvClean.IsChecked == true, CancellationToken.None); }
            finally { _ = App.ResumeGpuAsync(); }
            _nvGpu = NvidiaDriver.Detect();
            if (_nvGpu is not null) App.SetNvUpdate(NvidiaDriver.Behind(_nvReleases.Where(r => !r.Studio), _nvGpu.InstalledVersion) is int b && b > 0 && _nvReleases.Count > 0 ? new NvidiaDriver.UpdateInfo(_nvGpu, _nvReleases[0], b) : null);
            NvProgress.Text = T("nvd.installedMsg", _nvGpu?.InstalledVersion ?? "?", code);
            if (code == 0 || code == 1 || (_nvPick is not null && _nvGpu?.InstalledVersion == _nvPick.Version))
            {
                NvidiaDriver.CleanWork();
                _nvDir = null; _nvComps = [];
                if (await Confirm(T("dlg.restart.o"), T("dlg.restart.t"), T("nvd.reboot"), T("dlg.restart.yes"), T("dlg.restart.no")))
                {
                    await Sys.RunAsync("shutdown.exe", $"/r /t 10 /c \"{T("restart.comment")}\"", 30);
                    App.ExitApp();
                }
            }
        }
        catch (Exception ex) { Logger.Error(T("lg.nvd.fail", ex.Message)); NvProgress.Text = T("lg.nvd.fail", ex.Message); }
        finally { _nvBusy = false; RenderNvDriver(); }
    }

    /// <summary>Paso 2b: guardar el paquete limpio en una carpeta para instalarlo mas tarde.</summary>
    private void NvSave_Click(object s, RoutedEventArgs e)
    {
        if (_nvDir is null) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = T("nvd.save") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            NvidiaDriver.Prepare(_nvDir, _nvSel);
            var dest = Path.Combine(dlg.FolderName, "NVIDIA " + (_nvPick?.Version ?? Path.GetFileName(_nvDir)) + " RNX");
            CopyDir(_nvDir, dest);
            Logger.Ok(T("lg.nvd.saved", dest));
            NvProgress.Text = T("lg.nvd.saved", dest);
            OpenUnelevated(dest);
        }
        catch (Exception ex) { Logger.Error(T("lg.nvd.fail", ex.Message)); NvProgress.Text = T("lg.nvd.fail", ex.Message); }
        RenderNvDriver();
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
