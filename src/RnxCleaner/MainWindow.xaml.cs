using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RnxCleaner.Core;
using RnxCleaner.Views;
using Path = System.IO.Path;
using Shape = System.Windows.Shapes.Shape;
using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

public partial class MainWindow : Window
{
    private readonly Settings _settings = App.Shared;
    private readonly SystemInfo _sys = SystemInfo.Detect();
    private readonly RunMode? _autoMode;
    private readonly bool _scheduled;
    public bool SnapMode { get; set; }
    private UpdateInfo? _update;

    private readonly ObservableCollection<StepVM> _steps = [];
    private readonly List<CheckBox> _customBoxes = [];
    private CancellationTokenSource? _runCts;
    private bool _running;
    private bool _keepOpen;
    private RunMode _mode = RunMode.Todo;
    private List<RunRecord> _history = [];

    public MainWindow(RunMode? autoMode, bool scheduled = false)
    {
        _autoMode = autoMode;
        _scheduled = scheduled;
        App.NvUpdateChanged += OnNvUpdateChanged;
        // Con el color activado, cerrar = quedarse en la bandeja
        Closing += (_, e) =>
        {
            if (App.Exiting || _autoMode is not null || SnapMode || !App.KeepInTray) return;
            e.Cancel = true;
            App.HideToTray(this);
        };

        // Idioma y tema antes de dibujar nada
        Loc.Set(_settings.Language);
        InitializeComponent();
        ThemeManager.Apply(_settings.Palette, _settings.ThemeMode);

        StepsList.ItemsSource = _steps;
        VersionText.Text = $"v{Updater.Current}";
        Title = "RNX Cache Cleaner";
        _mode = _settings.LastMode switch { "quick" => RunMode.Quick, "full" => RunMode.Full, _ => RunMode.Todo };

        SysPc.Text = Environment.MachineName;
        SysSpec.Text = $"{_sys.Windows} · {_sys.GpuName.Replace("NVIDIA GeForce ", "")}";
        SysSpec.ToolTip = _sys.GpuName;
        if (!_sys.IsNvidia) { NvidiaBtn.IsEnabled = false; CustomNvidia.IsEnabled = false; NavNvDriver.Visibility = Visibility.Collapsed; }
        if (!File.Exists(AtlasRunAsTI)) { FixStoreBtn.IsEnabled = false; FixStoreBtn.ToolTip = T("fix.store.notAtlas"); }

        LoadSettingsIntoUi();
        (_mode switch { RunMode.Quick => ModeQuick, RunMode.Full => ModeFull, _ => ModeTodo }).IsChecked = true;
        RefreshTexts();

        Loc.Changed += RefreshTexts;
        ThemeManager.Changed += () => { RefreshThemeButton(); DrawHomeChart(); DrawHistoryChart(); BuildFirstRun(); if (PageSettings.Visibility == Visibility.Visible) RenderSettings(); };

        StateChanged += (_, _) =>
        {
            // Con WindowChrome, al maximizar Windows recorta ~7px por cada lado
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        };

        Loaded += async (_, _) =>
        {
            if (!_settings.FirstRunDone && _autoMode is null && !SnapMode) ShowFirstRun();
            await RefreshDriveAsync();
            if (_autoMode is null && !SnapMode && _settings.FirstRunDone) _ = CheckUpdatesAsync(silent: true);
            if (_autoMode is { } m)
            {
                // Programada: no se limpia nada con CS2 abierto
                if (_scheduled && SystemInfo.IsRunning("cs2")) { Logger.Info(T("lg.sch.skipCs2")); Close(); return; }
                if (_scheduled) Logger.Info(T("lg.sch.start"));
                await RunAsync(RunPlan.For(m, _settings, _sys, interactive: false), auto: true);
            }
        };
    }

    // ===================================================================
    //  Textos que pone el codigo (se rehacen al cambiar de idioma)
    // ===================================================================
    private void RefreshTexts()
    {
        LangCode.Text = Lang.ToUpperInvariant() == "ZH" ? "中文" : Lang.ToUpperInvariant();
        RefreshThemeButton();
        if (!_sys.IsNvidia) NvidiaSub.Text = T("cs2.nv.none");
        else NvidiaSub.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[cs2.nv.sub]") { Source = Loc.I });
        BuildCustomList();
        UpdateHome();
        BuildFirstRun();
        if (PageHistory.Visibility == Visibility.Visible) RefreshHistory();
        if (PageServers.Visibility == Visibility.Visible) RenderServers();
        if (PageVideo.Visibility == Visibility.Visible) RenderVideo();
        if (PageFonts.Visibility == Visibility.Visible) RenderFonts();
        if (PageVibrance.Visibility == Visibility.Visible) RenderVibrance();
        if (PageCustomRes.Visibility == Visibility.Visible) RenderRes();
        if (PageHud.Visibility == Visibility.Visible) RenderHud();
        if (PageNvDriver.Visibility == Visibility.Visible) RenderNvDriver();
        if (PagePcDrivers.Visibility == Visibility.Visible) RenderPcDrivers();
        if (PageWin.Visibility == Visibility.Visible) LoadWindows(_winMode);
        if (PageSettings.Visibility == Visibility.Visible) RenderSettings();
        _ = RefreshDriveAsync();
    }

    private void RefreshThemeButton()
    {
        ThemeLabel.Text = T("theme." + ThemeManager.Mode);
        ThemeIcon.Text = ThemeManager.Mode switch { "light" => "", "dark" => "", _ => "" };
    }

    // ===================================================================
    //  Pantalla de inicio
    // ===================================================================
    private void UpdateHome()
    {
        // Disco
        long free = SystemInfo.FreeBytesC(), total = SystemInfo.TotalBytesC();
        HomeDiskFree.Text = Sys.FormatBytes(free, 0);
        HomeDiskTotal.Text = Sys.FormatBytes(total, 0);
        HomeDiskBar.Value = total > 0 ? (double)(total - free) / total : 0;
        HomeDiskUsed.Text = T("home.disk.used", Sys.FormatBytes(total - free, 0));
        HomeDiskHint.Text = T("home.disk.hint");

        // Frase de estado: ultima limpieza + backup
        _history = History.Load();
        var last = _history.LastOrDefault(h => !h.Cancelled);
        var s = last is null ? T("home.sub.none") : T("home.sub.last", Ago(last.When), ModeName(last.Mode), Sys.FormatBytes(Math.Max(0, last.FreedBytes)));
        HomeSub.Text = s + " · " + (_settings.BackupToDrive ? T("home.sub.backup.on") : T("home.sub.backup.off"));

        // Que hace el modo elegido
        var names = ModeSteps(_mode);
        HomeStepsTitle.Text = T("home.steps", T(_mode switch { RunMode.Quick => "mode.quick", RunMode.Full => "mode.full", _ => "mode.todo" }));
        HomeStepsMeta.Text = T("home.steps.meta", names.Count);
        HomeSteps.ItemsSource = names.Select(n => StepChip(n.name, n.cs2)).ToList();

        DrawHomeChart();
    }

    private List<(string name, bool cs2)> ModeSteps(RunMode m)
    {
        var plan = RunPlan.For(m, _settings, _sys, true);
        var list = plan.Cleanup.Select(c => (c.Title, false)).ToList();
        if (plan.Nvidia) list.Add((T("mod.nvidia"), true));
        if (plan.Shader) list.Add((T("mod.shader"), true));
        if (plan.Mouse) list.Add((T("mod.mouse"), true));
        if (plan.Power) list.Add((T("mod.power"), true));
        if (plan.Network) list.Add((T("mod.network"), true));
        if (plan.Oem) list.Add((T("mod.oem"), true));
        return list;
    }

    private static FrameworkElement StepChip(string name, bool cs2)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, cs2 ? "Ink" : "Accent");
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 12, 4) };
        sp.Children.Add(dot);
        sp.Children.Add(new TextBlock { Text = name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        return sp;
    }

    private void Mode_Checked(object s, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        _mode = s == ModeQuick ? RunMode.Quick : s == ModeFull ? RunMode.Full : RunMode.Todo;
        _settings.LastMode = _mode switch { RunMode.Quick => "quick", RunMode.Full => "full", _ => "todo" };
        _settings.Save();
        if (HomeSteps is not null) UpdateHome();
    }

    private async void CleanNow_Click(object s, RoutedEventArgs e) => await RunAsync(RunPlan.For(_mode, _settings, _sys, true));
    private void GoBackup_Click(object s, RoutedEventArgs e) => NavBackup.IsChecked = true;
    private void HomeChart_SizeChanged(object s, SizeChangedEventArgs e) => DrawHomeChart();

    private void DrawHomeChart()
    {
        if (HomeChart is null) return;
        var data = _history.Where(h => !h.Cancelled).TakeLast(10).ToList();
        HomeChartEmpty.Visibility = data.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeFreedMeta.Text = data.Count == 0 ? "" : Sys.FormatBytes(data.Sum(d => Math.Max(0, d.FreedBytes)));
        DrawBars(HomeChart, data, 10);
    }

    /// <summary>"hace 3 dias", "hace 2 h"... en el idioma actual.</summary>
    private static string Ago(DateTime when)
    {
        var d = DateTime.Now - when;
        if (d.TotalMinutes < 2) return T("ago.now");
        if (d.TotalHours < 1) return T("ago.min", (int)d.TotalMinutes);
        if (d.TotalDays < 1) return T("ago.h", (int)d.TotalHours);
        return T("ago.d", (int)d.TotalDays);
    }

    /// <summary>Los registros guardan la clave del modo; los de la 5.0 guardaban el nombre.</summary>
    private static string ModeName(string key)
    {
        key = key switch
        {
            "TODO" => "todo", "TODO sin backup" => "nobackup", "Limpieza rápida" => "quick", "Limpieza completa" => "full",
            "Limpieza personalizada" => "custom", "Shadercache" => "shader", "Configuración del ratón" => "mouse",
            "Perfil NVIDIA CS2" => "nvidia", "Backup Steam" => "backup", _ => key
        };
        return Strings.Table.ContainsKey("plan." + key) ? T("plan." + key) : key;
    }

    // ===================================================================
    //  Ajustes en la interfaz
    // ===================================================================
    private void LoadSettingsIntoUi()
    {
        HomeBackupToggle.IsChecked = BackupToggle.IsChecked = _settings.BackupToDrive;
        ShaderPathBox.Text = _settings.ShaderPath;
        DriveFolderBox.Text = _settings.DriveFolder;
        MaxMbBox.Text = _settings.BackupMaxMB.ToString();
        ExcludeBox.Text = string.Join(",", _settings.BackupExclude);
    }

    private void BuildCustomList()
    {
        var sel = _customBoxes.Count > 0
            ? _customBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToHashSet()
            : (_settings.CustomSelection.Count == 0 ? CleanupCatalog.Quick.Select(q => q.Id).ToHashSet() : _settings.CustomSelection.ToHashSet());
        if (_customBoxes.Count == 0)
        {
            CustomShader.IsChecked = sel.Contains("mod:shader");
            CustomMouse.IsChecked = sel.Contains("mod:mouse");
            CustomNvidia.IsChecked = _sys.IsNvidia && sel.Contains("mod:nvidia");
            CustomPower.IsChecked = sel.Contains("mod:power");
            CustomNetwork.IsChecked = sel.Contains("mod:network");
            CustomOem.IsChecked = sel.Contains("mod:oem");
        }
        _customBoxes.Clear();
        foreach (var item in CleanupCatalog.All)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = item.Description, Style = (Style)FindResource("Sub"), FontSize = 12 });
            if (item.Risky)
            {
                var w = new TextBlock { Text = T("custom.caution"), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
                w.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
                panel.Children.Add(w);
            }
            var cb = new CheckBox { Style = (Style)FindResource("CheckCard"), Content = panel, Tag = item.Id, IsChecked = sel.Contains(item.Id) };
            cb.Click += CustomItem_Click;
            _customBoxes.Add(cb);
        }
        CustomList.ItemsSource = null;
        CustomList.ItemsSource = _customBoxes;
        UpdateCustomCount();
    }

    // ===================================================================
    //  Barra de titulo, navegacion, idioma, color y tema
    // ===================================================================
    private void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private async void Close_Click(object s, RoutedEventArgs e)
    {
        if (_running && !await Confirm(T("dlg.warn"), T("dlg.running.t"), T("dlg.running.x"), T("dlg.running.yes"), T("dlg.running.no")))
            return;
        _runCts?.Cancel();
        Close();
    }

    /// <summary>Abre o cierra el grupo "CS2" del menu.</summary>
    private void NavCs2Group_Click(object s, RoutedEventArgs e) => SetCs2Group(NavCs2Group.IsChecked == true);

    private void SetCs2Group(bool open)
    {
        NavCs2Group.IsChecked = open;
        NavCs2Items.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open && NavWinGroup.IsChecked == true && !(NavWinTweaks.IsChecked == true || NavWinApps.IsChecked == true || NavWinStartup.IsChecked == true || NavWinTasks.IsChecked == true))
        { NavWinGroup.IsChecked = false; NavWinItems.Visibility = Visibility.Collapsed; }
        if (open && NavDrvGroup.IsChecked == true && !(NavNvDriver.IsChecked == true || NavPcDrivers.IsChecked == true))
        { NavDrvGroup.IsChecked = false; NavDrvItems.Visibility = Visibility.Collapsed; UpdateDrvGroupBadge(); }
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Si se entra en una pestana del grupo CS2 (p. ej. desde un boton), el grupo se abre
        if (sender == NavCs2 || sender == NavVideo || sender == NavFonts) SetCs2Group(true);
        if (sender == NavWinTweaks || sender == NavWinApps || sender == NavWinStartup || sender == NavWinTasks) SetWinGroup(true);
        if (InDrvGroup(sender)) SetDrvGroup(true);
        // Durante InitializeComponent "Limpieza" se marca antes de que existan las paginas
        if (!IsLoaded) return;
        if (_running) return;
        ShowPage(sender switch
        {
            RadioButton r when r == NavCustom => PageCustom,
            RadioButton r when r == NavCs2 => PageCs2,
            RadioButton r when r == NavBackup => PageBackup,
            RadioButton r when r == NavServers => PageServers,
            RadioButton r when r == NavVideo => PageVideo,
            RadioButton r when r == NavFonts => PageFonts,
            RadioButton r when r == NavVibrance => PageVibrance,
            RadioButton r when r == NavCustomRes => PageCustomRes,
            RadioButton r when r == NavHud => PageHud,
            RadioButton r when r == NavNvDriver => PageNvDriver,
            RadioButton r when r == NavPcDrivers => PagePcDrivers,
            RadioButton r when r == NavWinTweaks || r == NavWinApps || r == NavWinStartup || r == NavWinTasks => PageWin,
            RadioButton r when r == NavSettings => PageSettings,
            RadioButton r when r == NavLog => PageLog,
            RadioButton r when r == NavHistory => PageHistory,
            _ => PageHome
        });
        if (sender == NavLog) LogBox.Text = Logger.ReadAll();
        if (sender == NavHistory) RefreshHistory();
        if (sender == NavHome) UpdateHome();
        if (sender == NavServers) _ = LoadServersAsync();
        if (sender == NavSettings) RenderSettings();
        if (sender == NavCs2) _ = RefreshCs2StateAsync();
        if (sender == NavVideo) LoadVideo();
        if (sender == NavFonts) LoadFonts();
        if (sender == NavVibrance) LoadVibrance();
        if (sender == NavCustomRes) RenderRes();
        if (sender == NavHud) LoadHud();
        if (sender == NavNvDriver) LoadNvDriver();
        if (sender == NavPcDrivers) LoadPcDrivers();
        if (sender == NavWinTweaks) LoadWindows("tweaks");
        if (sender == NavWinApps) LoadWindows("apps");
        if (sender == NavWinStartup) LoadWindows("startup");
        if (sender == NavWinTasks) LoadWindows("tasks");
    }

    private void ShowPage(FrameworkElement page)
    {
        if (PageHome is null) return;
        foreach (var p in new FrameworkElement[] { PageHome, PageCustom, PageCs2, PageVideo, PageFonts, PageCustomRes, PageVibrance, PageHud, PageNvDriver, PagePcDrivers, PageWin, PageServers, PageBackup, PageSettings, PageLog, PageHistory, PageRun })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void LangBtn_Click(object s, RoutedEventArgs e)
    {
        LangList.Children.Clear();
        foreach (var k in Langs)
            LangList.Children.Add(MenuRow(In(k, "lang.native"), k == Lang, null, () => SetLanguage(k), LangPopup));
        LangPopup.IsOpen = true;
    }

    private void ColorBtn_Click(object s, RoutedEventArgs e)
    {
        ColorList.Children.Clear();
        foreach (var p in ThemeManager.Palettes)
            ColorList.Children.Add(MenuRow(T("pal." + p), p == ThemeManager.Palette, ThemeManager.Swatch(p, ThemeManager.IsDark), () => SetPalette(p), ColorPopup));
        ColorList.Children.Add(MenuRow(T("pal.custom"), ThemeManager.IsCustom, ThemeManager.IsCustom ? ThemeManager.Swatch(ThemeManager.Palette, ThemeManager.IsDark) : null,
                                       OpenCustomColor, ColorPopup));
        ColorPopup.IsOpen = true;
    }

    /// <summary>Selector de color personalizado (vista previa en directo; Cancelar deja el anterior).</summary>
    private void OpenCustomColor()
    {
        var prev = ThemeManager.Palette;
        var start = ThemeManager.TryCustom(prev, out var c) ? c : ThemeManager.Swatch(prev, ThemeManager.IsDark);
        var w = new ColorPickerWindow(start, hex => ThemeManager.Apply(hex, ThemeManager.Mode)) { Owner = this };
        if (w.ShowDialog() == true && w.Result is { } r) SetPalette(r);
        else ThemeManager.Apply(prev, ThemeManager.Mode);
        if (PageSettings.Visibility == Visibility.Visible) RenderSettings();
        if (FirstRun.Visibility == Visibility.Visible) RenderFirstRunColors();
    }

    /// <summary>Circulo extra "personalizado" junto a las paletas (arcoiris, o el color elegido).</summary>
    private RadioButton CustomSwatch(string group, Thickness margin)
    {
        Brush bg;
        if (ThemeManager.IsCustom) bg = new SolidColorBrush(ThemeManager.Swatch(ThemeManager.Palette, ThemeManager.IsDark));
        else
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            for (int i = 0; i <= 6; i++) g.GradientStops.Add(new GradientStop(ColorPickerWindow.FromHsv(i * 60 % 360, 0.75, 1), i / 6.0));
            bg = g;
        }
        var rb = new RadioButton { Style = (Style)FindResource("SwatchPick"), Background = bg, IsChecked = ThemeManager.IsCustom, GroupName = group,
                                   ToolTip = T("pal.custom") + (ThemeManager.IsCustom ? " · " + ThemeManager.Palette : ""), Margin = margin };
        rb.Click += (_, _) => OpenCustomColor();
        return rb;
    }

    private void ThemeBtn_Click(object s, RoutedEventArgs e)
    {
        var i = Array.IndexOf(ThemeManager.Modes, ThemeManager.Mode);
        SetThemeMode(ThemeManager.Modes[(i + 1) % ThemeManager.Modes.Length]);
    }

    private Button MenuRow(string text, bool on, Color? swatch, Action act, Popup popup)
    {
        var dp = new DockPanel { LastChildFill = true, MinWidth = 170 };
        var check = new TextBlock { Text = on ? "" : "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        check.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        DockPanel.SetDock(check, Dock.Right);
        dp.Children.Add(check);
        if (swatch is { } c)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(dot, Dock.Left);
            dp.Children.Add(dot);
        }
        dp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal });
        var b = new Button { Style = (Style)FindResource("FootButton"), Content = dp, Height = 34, FontSize = 13.5, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        b.Click += (_, _) => { popup.IsOpen = false; act(); };
        return b;
    }

    private void SetLanguage(string lang)
    {
        _settings.Language = lang;
        _settings.Save();
        Loc.Set(lang);
    }

    private void SetPalette(string pal)
    {
        _settings.Palette = pal;
        _settings.Save();
        ThemeManager.Apply(pal, ThemeManager.Mode);
    }

    private void SetThemeMode(string mode)
    {
        _settings.ThemeMode = mode;
        _settings.Save();
        ThemeManager.Apply(ThemeManager.Palette, mode);
    }

    // ===================================================================
    //  Primer arranque: idioma, color y apariencia
    // ===================================================================
    private void RenderFirstRunColors()
    {
        FrColors.Children.Clear();
        foreach (var p in ThemeManager.Palettes)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SwatchPick"), Background = new SolidColorBrush(ThemeManager.Swatch(p, ThemeManager.IsDark)),
                                       IsChecked = p == ThemeManager.Palette, GroupName = "frpal", ToolTip = T("pal." + p), Margin = new Thickness(0, 0, 10, 0) };
            var pal = p;
            rb.Click += (_, _) => { SetPalette(pal); RenderFirstRunColors(); };
            FrColors.Children.Add(rb);
        }
        FrColors.Children.Add(CustomSwatch("frpal", new Thickness(0, 0, 10, 0)));
    }

    private void ShowFirstRun()
    {
        BuildFirstRun();
        FirstRun.Visibility = Visibility.Visible;
    }

    private void BuildFirstRun()
    {
        if (FrLangs is null) return;
        FrTitle.Text = T("first.title");
        FrSub.Text = T("first.sub");
        FrLangLbl.Text = T("first.lang");
        FrColorLbl.Text = T("first.color");
        FrColorName.Text = T("pal." + ThemeManager.Palette);
        FrLookLbl.Text = T("first.look");
        FrContinue.Content = T("first.continue");

        FrLangs.Children.Clear();
        foreach (var k in Langs)
        {
            var row = new DockPanel();
            var chk = new TextBlock { Text = k == Lang ? "" : "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            chk.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            DockPanel.SetDock(chk, Dock.Right);
            row.Children.Add(chk);
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            name.Inlines.Add(new System.Windows.Documents.Run(In(k, "lang.native")) { FontWeight = FontWeights.SemiBold });
            if (k != "en")
            {
                var en = new System.Windows.Documents.Run("  " + In("en", "lang.native." + k)) { FontSize = 12 };
                en.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
                name.Inlines.Add(en);
            }
            row.Children.Add(name);
            var rb = new RadioButton { Style = (Style)FindResource("PickRow"), Content = row, IsChecked = k == Lang, GroupName = "frlang", Margin = new Thickness(0, 0, 6, 6) };
            var lang = k;
            rb.Click += (_, _) => SetLanguage(lang);
            FrLangs.Children.Add(rb);
        }

        RenderFirstRunColors();

        FrLooks.Children.Clear();
        foreach (var m in ThemeManager.Modes)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = T("theme." + m), IsChecked = m == ThemeManager.Mode, GroupName = "frlook" };
            var mode = m;
            rb.Click += (_, _) => SetThemeMode(mode);
            FrLooks.Children.Add(rb);
        }
    }

    private void FirstRunContinue_Click(object s, RoutedEventArgs e)
    {
        _settings.FirstRunDone = true;
        _settings.Save();
        FirstRun.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)));
        Dispatcher.BeginInvoke(async () => { await Task.Delay(200); FirstRun.Visibility = Visibility.Collapsed; FirstRun.BeginAnimation(OpacityProperty, null); _ = CheckUpdatesAsync(true); });
    }

    // ===================================================================
    //  Modos y modulos
    // ===================================================================
    private async void RunCustom_Click(object s, RoutedEventArgs e)
    {
        SaveCustomSelection();
        var ids = _customBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToHashSet();
        var plan = new RunPlan
        {
            Key = "custom",
            Cleanup = CleanupCatalog.All.Where(i => ids.Contains(i.Id)).ToList(),
            Shader = CustomShader.IsChecked == true,
            Mouse = CustomMouse.IsChecked == true,
            Nvidia = CustomNvidia.IsChecked == true && _sys.IsNvidia,
            Power = CustomPower.IsChecked == true,
            Network = CustomNetwork.IsChecked == true,
            Oem = CustomOem.IsChecked == true,
            Backup = _settings.BackupToDrive,
        };
        if (plan.Cleanup.Count == 0 && !plan.Shader && !plan.Mouse && !plan.Nvidia && !plan.Power && !plan.Network && !plan.Oem)
        {
            ShowToast(T("toast.selectOne"));
            return;
        }
        await RunAsync(plan);
    }

    private async void Shader_Click(object s, RoutedEventArgs e)
    {
        SaveShaderPath();
        var p = RunPlan.For(RunMode.Shader, _settings, _sys, true);
        p.Backup = false; // un modulo suelto desde el menu no necesita backup
        await RunAsync(p);
    }

    private async void Mouse_Click(object s, RoutedEventArgs e)
    {
        var p = RunPlan.For(RunMode.Mouse, _settings, _sys, true);
        p.Backup = false;
        await RunAsync(p);
    }

    private async void Nvidia_Click(object s, RoutedEventArgs e)
    {
        var p = RunPlan.For(RunMode.Nvidia, _settings, _sys, true);
        p.Backup = false;
        await RunAsync(p);
    }

    private async void Power_Click(object s, RoutedEventArgs e) => await RunAsync(RunPlan.For(RunMode.Power, _settings, _sys, true));

    private void OpenInspector_Click(object s, RoutedEventArgs e)
    {
        try { Cs2Modules.OpenNvidiaInspector(); }
        catch (Exception ex) { ShowToast(T("toast.inspectorFail", ex.Message)); }
    }

    private async void BackupNow_Click(object s, RoutedEventArgs e)
    {
        SaveBackupSettings();
        await RunAsync(RunPlan.For(RunMode.BackupOnly, _settings, _sys, true));
    }

    // ===================================================================
    //  Ejecucion
    // ===================================================================
    private async Task RunAsync(RunPlan plan, bool auto = false)
    {
        if (_running) return;

        // Con CS2 abierto el shadercache y el perfil NVIDIA no se aplican bien
        if (plan.Interactive && (plan.Shader || plan.Nvidia) && SystemInfo.IsRunning("cs2"))
        {
            if (!await Confirm(T("dlg.warn"), T("dlg.cs2.t"), T("dlg.cs2.x"), T("dlg.cs2.yes"), T("dlg.cancel")))
                return;
            Sys.KillProcess("cs2");
            Logger.Info(T("lg.cs2.closed"));
        }

        // Con Steam abierto la shadercache puede no borrarse entera
        if (plan.Shader && plan.Interactive && SystemInfo.IsRunning("steam"))
        {
            if (!await Confirm(T("dlg.warn"), T("dlg.steam.t"), T("dlg.steam.x"), T("dlg.steam.yes"), T("dlg.steam.no")))
                plan.Shader = false;
        }

        // Backup activado pero sin sesion de Google: mejor resolverlo antes de empezar
        if (plan.Backup && plan.Interactive && DriveSync.IsConfigured && !await DriveSync.HasTokenAsync())
        {
            if (await Confirm("GOOGLE DRIVE", T("dlg.drive.t"), T("dlg.drive.x"), T("dlg.drive.yes"), T("dlg.drive.no")))
                await ConnectDriveAsync();
        }

        var stepsDef = Runner.BuildSteps(plan, _settings, _sys);
        if (stepsDef.Count == 0) { ShowToast(T("toast.nothing")); return; }

        _running = true;
        _keepOpen = false;
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        Logger.StartSession(plan.Name);
        Logger.Info(T("lg.start", plan.Name.ToUpper(Culture)));
        if (plan.Key == "nobackup") Logger.Info(T("lg.nobackup"));

        _steps.Clear();
        foreach (var st in stepsDef) _steps.Add(new StepVM(st));
        LiveLog.Items.Clear();
        Logger.Written += OnLog;

        RunTitle.Text = plan.Name;
        RunOverline.Text = T("run.overline.running");
        RunOverline.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        RunBar.Value = 0;
        RunPercent.Text = "0%";
        RunFreed.Text = "-";
        RunState.Text = T("run.state.running");
        RunState.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        CancelBtn.Visibility = Visibility.Visible;
        DoneBtn.Visibility = Visibility.Collapsed;
        KeepOpenBtn.Visibility = Visibility.Collapsed;
        Nav.IsEnabled = false;
        ShowPage(PageRun);

        var sw = Stopwatch.StartNew();
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => RunTime.Text = sw.Elapsed.ToString(@"mm\:ss");
        clock.Start();

        long freed = 0;
        int ok = 0, warn = 0, err = 0;
        bool cancelled = false;

        for (int i = 0; i < _steps.Count; i++)
        {
            var vm = _steps[i];
            if (ct.IsCancellationRequested) { vm.Status = StepStatus.Skipped; continue; }

            vm.Status = StepStatus.Running;
            int idx = i;
            var progress = new Progress<double>(p =>
            {
                vm.HasProgress = true;
                vm.Progress = p;
                SetOverall((idx + Math.Clamp(p, 0, 1)) / _steps.Count);
            });
            SetOverall((double)i / _steps.Count);
            ScrollStepIntoView(i);

            long before = SystemInfo.FreeBytesC();
            try
            {
                vm.Status = await Task.Run(() => vm.Step.Work(progress, ct), ct);
            }
            catch (OperationCanceledException)
            {
                vm.Status = StepStatus.Skipped;
                cancelled = true;
                Logger.Warn(T("lg.cancelled"));
            }
            catch (Exception ex)
            {
                vm.Status = StepStatus.Error;
                Logger.Error($"{vm.Title}: {ex.Message}");
            }

            // El backup escribe ZIPs en C:, eso no cuenta como "liberado"
            long delta = SystemInfo.FreeBytesC() - before;
            if (!vm.Step.IsBackup && delta > 0)
            {
                freed += delta;
                if (delta > 1024 * 1024) vm.Detail = T("run.stepFreed", Sys.FormatBytes(delta));
            }
            switch (vm.Status)
            {
                case StepStatus.Ok: ok++; break;
                case StepStatus.Warn: warn++; break;
                case StepStatus.Error: err++; break;
            }
            RunFreed.Text = Sys.FormatBytes(freed);
        }

        sw.Stop();
        clock.Stop();
        RunTime.Text = sw.Elapsed.ToString(@"mm\:ss");
        Logger.Written -= OnLog;
        SetOverall(1);
        cancelled |= ct.IsCancellationRequested;

        History.Add(new RunRecord(DateTime.Now, plan.Key, freed, sw.Elapsed.TotalSeconds, ok, warn, err, cancelled));
        Logger.Write(err > 0 ? LogLevel.Warn : LogLevel.Ok,
            T("lg.end", plan.Name.ToUpper(Culture), Sys.FormatBytes(freed), sw.Elapsed.ToString(@"mm\:ss"), ok, warn, err));

        string stateKey;
        if (cancelled) { RunState.Text = T("run.state.cancelled"); stateKey = "Warn"; RunOverline.Text = T("run.overline.cancelled"); }
        else if (err > 0) { RunState.Text = T("run.state.errors", err); stateKey = "Error"; RunOverline.Text = T("run.overline.errors"); }
        else if (warn > 0) { RunState.Text = T("run.state.warnings", warn); stateKey = "Warn"; RunOverline.Text = T("run.overline.done"); }
        else { RunState.Text = T("run.state.done"); stateKey = "Accent"; RunOverline.Text = T("run.overline.done"); }
        RunState.SetResourceReference(TextBlock.ForegroundProperty, stateKey);
        RunOverline.SetResourceReference(TextBlock.ForegroundProperty, stateKey);

        CancelBtn.Visibility = Visibility.Collapsed;
        DoneBtn.Visibility = Visibility.Visible;
        Nav.IsEnabled = true;
        _running = false;
        _ = RefreshCs2StateAsync();
        _runCts.Dispose();
        _runCts = null;

        // Lanzado con /todo, /rapida...: se cierra solo al terminar
        if (auto && _scheduled)
        {
            await Notifier.ShowAsync("RNX Cache Cleaner", err > 0
                ? T("notify.errors", err)
                : T("notify.done", plan.Name, Sys.FormatBytes(freed), sw.Elapsed.ToString(@"mm\:ss")));
            Close();
            return;
        }
        if (auto) { await AutoCloseAsync(); return; }

        // Tras una limpieza desde la ventana, ofrecer reiniciar
        if (plan.Cleanup.Count > 0 && !cancelled)
            await OfferRestartAsync();
    }

    private async Task OfferRestartAsync()
    {
        if (!await Confirm(T("dlg.restart.o"), T("dlg.restart.t"), T("dlg.restart.x"), T("dlg.restart.yes"), T("dlg.restart.no")))
            return;
        Logger.Info(T("lg.restart"));
        await Sys.RunAsync("shutdown.exe", $"/r /t 10 /c \"{T("restart.comment")}\"", 30);
        App.ExitApp();
    }

    private async Task AutoCloseAsync()
    {
        KeepOpenBtn.Visibility = Visibility.Visible;
        for (int s = 6; s > 0; s--)
        {
            if (_keepOpen) { KeepOpenBtn.Visibility = Visibility.Collapsed; return; }
            RunOverline.Text = T("run.autoclose", s);
            await Task.Delay(1000);
        }
        if (!_keepOpen) Close();
    }

    private void SetOverall(double v)
    {
        RunBar.Value = v;
        RunPercent.Text = $"{(int)Math.Round(v * 100)}%";
    }

    private void ScrollStepIntoView(int i)
    {
        if (StepsList.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe) fe.BringIntoView();
    }

    private void OnLog(LogEntry e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var tb = new TextBlock { Text = $"{e.Time:HH:mm:ss}  {e.Text}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            tb.SetResourceReference(TextBlock.ForegroundProperty, e.Level switch
            {
                LogLevel.Error => "Error", LogLevel.Warn => "Warn", LogLevel.Ok => "Accent", LogLevel.Detail => "Faint", _ => "Muted"
            });
            LiveLog.Items.Add(tb);
            while (LiveLog.Items.Count > 400) LiveLog.Items.RemoveAt(0);
            LiveScroll.ScrollToEnd();
        });
    }

    private void Cancel_Click(object s, RoutedEventArgs e)
    {
        _runCts?.Cancel();
        CancelBtn.IsEnabled = false;
        RunState.Text = T("run.state.cancelling");
        Dispatcher.BeginInvoke(() => CancelBtn.IsEnabled = true, DispatcherPriority.Background);
    }

    private void KeepOpen_Click(object s, RoutedEventArgs e)
    {
        _keepOpen = true;
        KeepOpenBtn.Visibility = Visibility.Collapsed;
        RunOverline.Text = T("run.overline.done");
    }

    private void Done_Click(object s, RoutedEventArgs e)
    {
        NavHome.IsChecked = true;
        ShowPage(PageHome);
        UpdateHome();
    }

    // ===================================================================
    //  Personalizada
    // ===================================================================
    // ---- Reparacion de la Microsoft Store (script de AtlasOS, solo a mano)
    private static string AtlasRunAsTI => Path.Combine(Sys.WinDir, "AtlasModules", "Scripts", "RunAsTI.cmd");

    private async void FixStore_Click(object s, RoutedEventArgs e)
    {
        if (_running) return;
        if (!File.Exists(AtlasRunAsTI)) { ShowToast(T("fix.store.notAtlas")); return; }
        if (!await Confirm(T("dlg.warn"), T("dlg.fix.t"), T("dlg.fix.x"), T("fix.store.btn"), T("dlg.cancel"))) return;
        try
        {
            var cmd = AppPaths.ExtractTool("fixStoreIssues.cmd") ?? throw new FileNotFoundException("fixStoreIssues.cmd");
            // Ventana visible: el script muestra el progreso y espera una tecla al final
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c \"\"{cmd}\"\"")
                { UseShellExecute = true, WorkingDirectory = AppPaths.Tools });
            Logger.Info(T("lg.fix.store"));
            ShowToast(T("toast.fix.store"));
        }
        catch (Exception ex) { Logger.Error(T("lg.fix.fail", ex.Message)); ShowToast(T("lg.fix.fail", ex.Message)); }
    }

    // ---- Dispositivos antiguos y con error (solo a mano, con lista previa)
    private async void Devices_Click(object s, RoutedEventArgs e)
    {
        if (_running) return;
        DevicesBtn.IsEnabled = false;
        try
        {
            ShowToast(T("dev.scanning"));
            var (ghosts, errors) = await Devices.ScanAsync();
            if (ghosts.Count == 0 && errors.Count == 0) { ShowToast(T("dev.none")); return; }

            var sb = new System.Text.StringBuilder();
            foreach (var g in ghosts.GroupBy(d => d.Group))
            {
                sb.AppendLine($"{g.Key} ({g.Count()})");
                foreach (var d in g) sb.AppendLine("   •  " + d.Name);
            }
            if (errors.Count > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine(T("dev.errorsHeader", errors.Count));
                foreach (var d in errors) sb.AppendLine($"   •  {d.Name} ({d.Group})");
            }
            if (!await Confirm(T("dev.title").ToUpper(Culture), T("dev.dlg.t", ghosts.Count, errors.Count), T("dev.dlg.x"),
                               T("dev.dlg.yes"), T("dlg.cancel"), sb.ToString().TrimEnd()))
                return;

            Logger.StartSession(T("dev.title"));
            if (_settings.RestorePoint) await RestorePoint.CreateAsync(T("dev.title"), CancellationToken.None);
            var (removed, failed, fixd) = await Devices.CleanAsync(ghosts, errors);
            ShowToast(T("dev.toast", removed, fixd) + (failed > 0 ? " · " + T("dev.toast.failed", failed) : ""));
        }
        catch (Exception ex) { Logger.Error(T("lg.dev.fail", ex.Message)); ShowToast(T("lg.dev.fail", ex.Message)); }
        finally { DevicesBtn.IsEnabled = true; }
    }

    private void SetSelection(Func<CleanupItem, bool> pick)
    {
        foreach (var cb in _customBoxes) cb.IsChecked = pick(CleanupCatalog.Get((string)cb.Tag));
        SaveCustomSelection();
    }

    private void SelQuick_Click(object s, RoutedEventArgs e) => SetSelection(i => i.InQuick);
    private void SelFull_Click(object s, RoutedEventArgs e) => SetSelection(i => i.InFull);
    private void SelAll_Click(object s, RoutedEventArgs e) => SetSelection(_ => true);
    private void SelNone_Click(object s, RoutedEventArgs e) => SetSelection(_ => false);
    private void CustomItem_Click(object s, RoutedEventArgs e) => SaveCustomSelection();

    private void SaveCustomSelection()
    {
        var sel = _customBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
        if (CustomShader.IsChecked == true) sel.Add("mod:shader");
        if (CustomMouse.IsChecked == true) sel.Add("mod:mouse");
        if (CustomNvidia.IsChecked == true) sel.Add("mod:nvidia");
        if (CustomPower.IsChecked == true) sel.Add("mod:power");
        if (CustomNetwork.IsChecked == true) sel.Add("mod:network");
        if (CustomOem.IsChecked == true) sel.Add("mod:oem");
        if (sel.Count == 0) sel.Add("none"); // distinguir "nada marcado" de "primera vez"
        _settings.CustomSelection = sel;
        _settings.Save();
        UpdateCustomCount();
    }

    private void UpdateCustomCount()
    {
        int n = _customBoxes.Count(c => c.IsChecked == true)
                + new[] { CustomShader, CustomMouse, CustomNvidia, CustomPower, CustomNetwork, CustomOem }.Count(c => c.IsChecked == true);
        CustomCount.Text = T("custom.count", n);
    }

    // ===================================================================
    //  Shadercache
    // ===================================================================
    private void SaveShaderPath()
    {
        var p = ShaderPathBox.Text.Trim().Trim('"');
        _settings.ShaderPath = string.IsNullOrEmpty(p) ? Settings.DefaultShaderPath : p;
        ShaderPathBox.Text = _settings.ShaderPath;
        _settings.Save();
    }

    private void ShaderPath_LostFocus(object s, RoutedEventArgs e) => SaveShaderPath();

    private void ShaderBrowse_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = T("cs2.browse") };
        var parent = Path.GetDirectoryName(_settings.ShaderPath);
        if (Directory.Exists(parent)) dlg.InitialDirectory = parent;
        if (dlg.ShowDialog(this) == true)
        {
            ShaderPathBox.Text = dlg.FolderName;
            SaveShaderPath();
        }
    }

    private void ShaderDefault_Click(object s, RoutedEventArgs e)
    {
        // Busca en todas las bibliotecas de Steam (libraryfolders.vdf)
        var found = Cs2Modules.FindShaderCache();
        if (found is not null)
        {
            ShaderPathBox.Text = found;
            SaveShaderPath();
            ShowToast(T("toast.shaderFound"));
            return;
        }
        var lib = Cs2Modules.Cs2Library();
        if (lib is not null)
        {
            ShaderPathBox.Text = Path.Combine(lib, "steamapps", "shadercache", "730");
            SaveShaderPath();
            ShowToast(T("toast.shaderClean"));
        }
        else ShowToast(T("toast.noCs2"));
    }

    // ===================================================================
    //  Servidores de Valve (bloqueo por firewall)
    // ===================================================================
    private List<ValvePop> _pops = [];
    private Dictionary<string, string> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long?> _pings = new(StringComparer.OrdinalIgnoreCase);
    private string _svRegion = "all";
    private bool _svFromCache, _svLoading;

    private async Task LoadServersAsync()
    {
        if (_svLoading) return;
        _svLoading = true;
        SvEmpty.Text = T("sv.loading");
        SvEmpty.Visibility = _pops.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            (_pops, _svFromCache) = await ServerBlocker.LoadAsync(cts.Token);
            if (!_svFromCache) await Task.Run(() => ServerBlocker.SyncChangedIps(_pops));
            ReadFirewallState();
            RenderServers();
            _ = PingServersAsync();
        }
        catch (Exception ex)
        {
            SvEmpty.Text = T("sv.fail", ex.Message);
            SvEmpty.Visibility = Visibility.Visible;
        }
        finally { _svLoading = false; }
    }

    /// <summary>El estado sale siempre de las reglas que hay en el firewall.</summary>
    private void ReadFirewallState()
    {
        try { _blocked = ServerBlocker.BlockedRules(); }
        catch (Exception ex) { ShowToast(T("sv.toast.fail", ex.Message)); }
    }

    private async Task PingServersAsync()
    {
        await Parallel.ForEachAsync(_pops.ToList(), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (p, _) =>
        {
            var ms = _blocked.ContainsKey(p.Code) ? null : await ServerBlocker.PingAsync(p);
            lock (_pings) _pings[p.Code] = ms;
        });
        if (PageServers.Visibility == Visibility.Visible) RenderServers();
    }

    private void RenderServers()
    {
        // Filtro por region
        SvRegions.Children.Clear();
        foreach (var r in new[] { "all" }.Concat(ServerBlocker.Regions.Where(r => _pops.Any(p => p.Region == r))))
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = T("region." + r), IsChecked = r == _svRegion, GroupName = "svreg" };
            var reg = r;
            rb.Click += (_, _) => { _svRegion = reg; RenderServers(); };
            SvRegions.Children.Add(rb);
        }

        var shown = _pops.Where(p => _svRegion == "all" || p.Region == _svRegion).ToList();
        int blocked = _pops.Count(p => _blocked.ContainsKey(p.Code));
        SvSummary.Text = T("sv.summary", blocked, _pops.Count) + (_svFromCache ? " · " + T("sv.cache") : "");
        SvEmpty.Visibility = _pops.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_pops.Count == 0 && !_svLoading) SvEmpty.Text = T("sv.none");

        SvList.Children.Clear();
        foreach (var p in shown)
        {
            bool isBlocked = _blocked.ContainsKey(p.Code);
            var g = new Grid { Margin = new Thickness(14, 0, 14, 0), MinHeight = 44 };
            foreach (var w in new[] { new GridLength(1, GridUnitType.Star), new GridLength(150), new GridLength(60), new GridLength(80), new GridLength(120), new GridLength(56) })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });

            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Inlines.Add(new System.Windows.Documents.Run(p.Name) { FontWeight = FontWeights.SemiBold });
            var code = new System.Windows.Documents.Run("  " + p.Code) { FontSize = 12 };
            code.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Faint");
            name.Inlines.Add(code);
            g.Children.Add(name);

            AddCell(g, 1, T("region." + p.Region), "Muted", HorizontalAlignment.Left);
            AddCell(g, 2, p.Ips.Count.ToString(Culture), "Muted", HorizontalAlignment.Right);
            long? ms; bool has; lock (_pings) has = _pings.TryGetValue(p.Code, out ms);
            AddCell(g, 3, isBlocked || (has && ms is null) ? "-" : has ? $"{ms} ms" : "…", "Ink", HorizontalAlignment.Right);

            var pill = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 2, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
            pill.SetResourceReference(Border.BackgroundProperty, isBlocked ? "Tint" : "PillG");
            var pt = new TextBlock { Text = isBlocked ? T("sv.blocked") : T("sv.allowed"), FontSize = 12, FontWeight = FontWeights.SemiBold };
            pt.SetResourceReference(TextBlock.ForegroundProperty, isBlocked ? "TintInk" : "PillGInk");
            pill.Child = pt;
            Grid.SetColumn(pill, 4);
            g.Children.Add(pill);

            var tg = new CheckBox { Style = (Style)FindResource("Toggle"), IsChecked = isBlocked, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = isBlocked ? T("sv.tip.unblock") : T("sv.tip.block") };
            var pop = p;
            tg.Click += (_, _) => ToggleServer(pop, tg.IsChecked == true);
            Grid.SetColumn(tg, 5);
            g.Children.Add(tg);

            var row = new Border { Child = g, BorderThickness = new Thickness(0, 1, 0, 0) };
            row.SetResourceReference(Border.BorderBrushProperty, "Line2");
            SvList.Children.Add(row);
        }
    }

    private static void AddCell(Grid g, int col, string text, string brush, HorizontalAlignment align)
    {
        var t = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Grid.SetColumn(t, col);
        g.Children.Add(t);
    }

    private void ToggleServer(ValvePop p, bool block)
    {
        try
        {
            if (block) ServerBlocker.Block(p); else ServerBlocker.Unblock(p.Code, p.Name);
            ShowToast(T(block ? "sv.toast.blocked" : "sv.toast.unblocked", p.Name));
        }
        catch (Exception ex) { ShowToast(T("sv.toast.fail", ex.Message)); }
        ReadFirewallState();
        if (!block) lock (_pings) _pings.Remove(p.Code);
        RenderServers();
        if (!block) _ = PingServersAsync();
    }

    // ---- Presets: dejar solo los N mejores por ping o solo una region
    private void SvPreset_Click(object s, RoutedEventArgs e)
    {
        if (_pops.Count == 0 || _svBusy) return;
        SvPresetList.Children.Clear();
        foreach (var n in new[] { 3, 5, 10 })
        {
            int k = n;
            SvPresetList.Children.Add(MenuRow(T("sv.best", n), false, null, () => _ = KeepBestAsync(k), SvPresetPopup));
        }
        SvPresetList.Children.Add(new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4), Background = (Brush)FindResource("Line2") });
        foreach (var r in ServerBlocker.Regions.Where(r => r != "other" && _pops.Any(p => p.Region == r)))
        {
            var reg = r;
            SvPresetList.Children.Add(MenuRow(T("sv.only", T("region." + r)), false, null, () => _ = KeepRegionAsync(reg), SvPresetPopup));
        }
        SvPresetPopup.IsOpen = true;
    }

    private bool _svBusy;

    /// <summary>Deja abiertas exactamente las ubicaciones de "keep" y bloquea el resto.</summary>
    private void ApplyServerSet(IEnumerable<string> keepCodes) => ServerBlocker.KeepOnly(_pops, keepCodes);

    private async Task KeepBestAsync(int n)
    {
        _svBusy = true;
        var before = ServerBlocker.BlockedRules().Keys.ToList();
        try
        {
            // Para medir el ping de todas hay que abrirlas un momento
            ShowToast(T("sv.measuring"));
            ServerBlocker.UnblockAll();
            ReadFirewallState();
            lock (_pings) _pings.Clear();
            RenderServers();
            await PingServersAsync();

            List<(ValvePop Pop, long Ms)> ranked;
            lock (_pings)
                ranked = _pops.Where(p => _pings.TryGetValue(p.Code, out var ms) && ms is not null)
                              .Select(p => (p, _pings[p.Code]!.Value)).OrderBy(x => x.Item2).ToList();
            if (ranked.Count == 0)
            {
                ApplyServerSet(_pops.Select(p => p.Code).Except(before, StringComparer.OrdinalIgnoreCase));
                ShowToast(T("sv.toast.noPing"));
                return;
            }
            var keep = ranked.Take(n).ToList();
            var list = string.Join(", ", keep.Select(k => $"{k.Pop.Name} ({k.Ms} ms)"));
            if (!await Confirm(T("sv.title").ToUpper(Culture), T("sv.best.t", keep.Count), T("sv.best.x", list, _pops.Count - keep.Count), T("sv.apply"), T("dlg.cancel")))
            {
                ApplyServerSet(_pops.Select(p => p.Code).Except(before, StringComparer.OrdinalIgnoreCase));
                return;
            }
            ApplyServerSet(keep.Select(k => k.Pop.Code));
            ShowToast(T("sv.toast.preset", _pops.Count - keep.Count));
        }
        catch (Exception ex) { ShowToast(T("sv.toast.fail", ex.Message)); }
        finally
        {
            _svBusy = false;
            ReadFirewallState();
            RenderServers();
        }
    }

    private async Task KeepRegionAsync(string region)
    {
        var keep = _pops.Where(p => p.Region == region).Select(p => p.Code).ToList();
        int toBlock = _pops.Count - keep.Count;
        var name = T("region." + region);
        if (!await Confirm(T("sv.title").ToUpper(Culture), T("sv.only.t", name), T("sv.only.x", toBlock, name), T("sv.apply"), T("dlg.cancel")))
            return;
        _svBusy = true;
        try
        {
            ApplyServerSet(keep);
            ShowToast(T("sv.toast.preset", toBlock));
        }
        catch (Exception ex) { ShowToast(T("sv.toast.fail", ex.Message)); }
        finally
        {
            _svBusy = false;
            ReadFirewallState();
            lock (_pings) _pings.Clear();
            RenderServers();
            _ = PingServersAsync();
        }
    }

    private async void SvRefresh_Click(object s, RoutedEventArgs e) { lock (_pings) _pings.Clear(); await LoadServersAsync(); }

    private async void SvUnblockAll_Click(object s, RoutedEventArgs e)
    {
        if (_blocked.Count == 0) { ShowToast(T("sv.toast.none")); return; }
        if (!await Confirm(T("sv.title").ToUpper(Culture), T("sv.dlg.t"), T("sv.dlg.x", _blocked.Count), T("sv.unblockAll"), T("dlg.cancel")))
            return;
        try { ShowToast(T("sv.toast.unblockedAll", ServerBlocker.UnblockAll())); }
        catch (Exception ex) { ShowToast(T("sv.toast.fail", ex.Message)); }
        ReadFirewallState();
        lock (_pings) _pings.Clear();
        RenderServers();
        _ = PingServersAsync();
    }

    // ===================================================================
    //  Ajustes: archivos propios (.nip / .reg) y apariencia
    // ===================================================================
    // ===================================================================
    //  Estado de los modulos de CS2 y deshacer
    // ===================================================================
    private async Task RefreshCs2StateAsync()
    {
        var st = await Cs2Status.ReadAsync(_settings);

        void Set(TextBlock tb, string text, string brush, string? tip = null)
        {
            tb.Text = text;
            tb.ToolTip = tip ?? text;
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }

        if (st.PowerName is null) Set(StPower, "-", "Muted");
        else if (st.PowerIsRnx) Set(StPower, T("st.power.rnx"), "Accent");
        else Set(StPower, T("st.power.other", st.PowerName), "Ink");

        var source = st.MouseSource.Length > 0 ? st.MouseSource : T("st.mouse.defaultName");
        if (st.MouseMatches is null) Set(StMouse, "-", "Muted");
        else if (st.MouseMatches == true) Set(StMouse, st.MouseSource.Length > 0 ? T("st.mouse.custom", source) : T("st.mouse.default"), "Accent");
        else Set(StMouse, T("st.mouse.differs", source), "Ink");

        if (st.ShaderPath is null || st.ShaderBytes == 0) Set(StShader, T("st.shader.none"), "Muted", st.ShaderPath);
        else Set(StShader, Sys.FormatBytes(st.ShaderBytes), "Ink", st.ShaderPath);

        if (!_sys.IsNvidia) Set(StNvidia, T("st.nv.noGpu"), "Muted");
        else if (_settings.NvidiaApplied is { } when) Set(StNvidia, T("st.nv.applied", when.ToString("g", Culture)), "Accent");
        else Set(StNvidia, T("st.nv.never"), "Muted");

        var parts = new List<string>();
        if (Undo.HasMouse) parts.Add(T("undo.part.mouse"));
        if (Undo.SavedPower() is { } p) parts.Add(T("undo.part.power", p.Name));
        if (Undo.HasNvidia) parts.Add(T("undo.part.nvidia"));
        if (Undo.HasDns || Undo.HasTcp) parts.Add(T("undo.part.network"));
        if (Undo.HasOem) parts.Add(T("undo.part.oem"));
        UndoSub.Text = parts.Count > 0 ? T("cs2.undo.saved", string.Join(" · ", parts)) : T("cs2.undo.none");
        UndoBtn.IsEnabled = parts.Count > 0 && !_running;
    }

    private async void Undo_Click(object s, RoutedEventArgs e)
    {
        if (_running) return;
        UndoBtn.IsEnabled = false;
        bool ok = await Undo.RestoreAsync();
        ShowToast(ok ? T("toast.undo.ok") : T("toast.undo.fail"));
        await RefreshCs2StateAsync();
    }

    // ===================================================================
    //  Ajustes: punto de restauracion y limpieza programada
    // ===================================================================
    private void AutostartToggle_Click(object s, RoutedEventArgs e)
    {
        bool on = AutostartToggle.IsChecked == true;
        try
        {
            Scheduler.SetAutostart(on);
            _settings.Autostart = on;
            _settings.Save();
            ShowToast(T(on ? "set.autostart.on" : "set.autostart.off"));
        }
        catch (Exception ex)
        {
            AutostartToggle.IsChecked = !on;
            Logger.Error(T("toast.sch.fail", ex.Message));
            ShowToast(T("toast.sch.fail", ex.Message));
        }
    }

    private void RpToggle_Click(object s, RoutedEventArgs e)
    {
        _settings.RestorePoint = RpToggle.IsChecked == true;
        _settings.Save();
    }

    private void RenderSchedule()
    {
        SchFreqs.Children.Clear();
        foreach (var f in Scheduler.Frequencies)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = T("sch." + f), IsChecked = f == _settings.ScheduleFreq, GroupName = "schfreq" };
            var freq = f;
            rb.Click += (_, _) => ApplySchedule(freq, _settings.ScheduleMode);
            SchFreqs.Children.Add(rb);
        }
        SchModes.Children.Clear();
        foreach (var m in Scheduler.Modes)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = T("plan." + m), IsChecked = m == _settings.ScheduleMode, GroupName = "schmode" };
            var mode = m;
            rb.Click += (_, _) => ApplySchedule(_settings.ScheduleFreq, mode);
            SchModes.Children.Add(rb);
        }
        SchModes.IsEnabled = _settings.ScheduleFreq != "off";
        SchModes.Opacity = SchModes.IsEnabled ? 1 : 0.5;

        var info = Scheduler.Query();
        string text;
        if (_settings.ScheduleFreq == "off" || !info.Exists) text = T("sch.offState");
        else
        {
            text = _settings.ScheduleFreq == "logon" ? T("sch.nextLogon")
                 : info.Next is { } n ? T("sch.next", n.ToString("g", Culture)) : T("sch.on");
            if (info.Last is { } l) text += T("sch.last", l.ToString("g", Culture));
        }
        SchState.Text = text;
        SchState.SetResourceReference(TextBlock.ForegroundProperty, _settings.ScheduleFreq != "off" && !info.Exists ? "Warn" : _settings.ScheduleFreq == "off" ? "Muted" : "Accent");
    }

    private void ApplySchedule(string freq, string mode)
    {
        try
        {
            Scheduler.Apply(freq, mode);
            _settings.ScheduleFreq = freq;
            _settings.ScheduleMode = mode;
            _settings.Save();
            if (freq != "off") ShowToast(T("toast.sch.ok"));
        }
        catch (Exception ex)
        {
            Logger.Error(T("toast.sch.fail", ex.Message));
            ShowToast(T("toast.sch.fail", ex.Message));
        }
        RenderSchedule();
    }

    private void ShowUpdateNotes(string notes)
    {
        UpdateNotesText.Text = notes;
        UpdateNotesBox.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderSettings()
    {
        bool customReg = !string.IsNullOrWhiteSpace(_settings.RegPath);
        RegState.Text = customReg ? T("set.custom", Path.GetFileName(_settings.RegPath)) : T("set.reg.default");
        RegState.ToolTip = customReg ? _settings.RegPath : null;
        RegState.SetResourceReference(TextBlock.ForegroundProperty, customReg && !File.Exists(_settings.RegPath) ? "Warn" : customReg ? "Accent" : "Ink");
        RegDefaultBtn.IsEnabled = customReg;
        RpToggle.IsChecked = _settings.RestorePoint;
        AutostartToggle.IsChecked = _settings.Autostart && Scheduler.AutostartExists();
        RenderSchedule();

        SetLangs.Children.Clear();
        foreach (var k in Langs)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = k == "zh" ? "中文" : k.ToUpperInvariant(), IsChecked = k == Lang, GroupName = "setlang", ToolTip = In(k, "lang.native") };
            var lang = k;
            rb.Click += (_, _) => SetLanguage(lang);
            SetLangs.Children.Add(rb);
        }
        SetColors.Children.Clear();
        foreach (var p in ThemeManager.Palettes)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SwatchPick"), Background = new SolidColorBrush(ThemeManager.Swatch(p, ThemeManager.IsDark)),
                                       IsChecked = p == ThemeManager.Palette, GroupName = "setpal", ToolTip = T("pal." + p), Margin = new Thickness(6, 0, 0, 0) };
            var pal = p;
            rb.Click += (_, _) => SetPalette(pal);
            SetColors.Children.Add(rb);
        }
        SetColors.Children.Add(CustomSwatch("setpal", new Thickness(6, 0, 0, 0)));
        SetLooks.Children.Clear();
        foreach (var m in ThemeManager.Modes)
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = T("theme." + m), IsChecked = m == ThemeManager.Mode, GroupName = "setlook" };
            var mode = m;
            rb.Click += (_, _) => SetThemeMode(mode);
            SetLooks.Children.Add(rb);
        }
    }

    private string? PickFile(string filter, string? current)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter, CheckFileExists = true };
        var dir = string.IsNullOrWhiteSpace(current) ? null : Path.GetDirectoryName(current);
        if (dir is not null && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private void RegChoose_Click(object s, RoutedEventArgs e)
    {
        var f = PickFile(T("set.reg.filter") + " (*.reg)|*.reg", _settings.RegPath);
        if (f is null) return;
        _settings.RegPath = f; _settings.Save();
        ShowToast(T("toast.set.custom", Path.GetFileName(f)));
        RenderSettings();
    }

    private async void RegExport_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = T("set.reg.filter") + " (*.reg)|*.reg",
            FileName = T("set.reg.exportName") + ".reg",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        if (await Cs2Modules.ExportMouseAsync(dlg.FileName))
        {
            // Lo exportado pasa a ser el .reg que usa la app
            _settings.RegPath = dlg.FileName; _settings.Save();
            ShowToast(T("toast.set.exported", Path.GetFileName(dlg.FileName)));
        }
        else ShowToast(T("toast.set.exportFail"));
        RenderSettings();
    }

    private void RegDefault_Click(object s, RoutedEventArgs e) { _settings.RegPath = ""; _settings.Save(); ShowToast(T("toast.set.default")); RenderSettings(); }

    // ===================================================================
    //  Backup / Google Drive
    // ===================================================================
    private async void BackupToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = ((CheckBox)sender).IsChecked == true;
        _settings.BackupToDrive = on;
        HomeBackupToggle.IsChecked = BackupToggle.IsChecked = on;
        _settings.Save();
        UpdateHome();

        if (on && !DriveSync.IsConfigured) { ShowToast(T("toast.noDriveBuild")); return; }
        if (on && !await DriveSync.HasTokenAsync())
            await ConnectDriveAsync();
        await RefreshDriveAsync();
    }

    private async void DriveConnect_Click(object s, RoutedEventArgs e) => await ConnectDriveAsync();

    private async Task ConnectDriveAsync()
    {
        if (!DriveSync.IsConfigured) { ShowToast(T("toast.noDriveBuild")); return; }
        DriveConnectBtn.IsEnabled = false;
        DriveAccount.Text = T("bk.waiting");
        ShowToast(T("toast.browser"));
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await DriveSync.AuthorizeAsync(true, cts.Token);
            ShowToast(T("toast.driveOn"));
        }
        catch (Exception ex)
        {
            Logger.Warn(T("lg.drive.loginFail", ex.Message));
            ShowToast(T("toast.loginFail"));
        }
        finally { DriveConnectBtn.IsEnabled = true; }
        await RefreshDriveAsync();
    }

    private async void DriveDisconnect_Click(object s, RoutedEventArgs e)
    {
        if (!await Confirm("GOOGLE DRIVE", T("dlg.disc.t"), T("dlg.disc.x"), T("dlg.disc.yes"), T("dlg.cancel")))
            return;
        await DriveSync.SignOutAsync();
        await RefreshDriveAsync();
        ShowToast(T("toast.driveOff"));
    }

    private async void RestoreCfg_Click(object s, RoutedEventArgs e)
    {
        if (!DriveSync.IsConfigured) return;
        if (!await DriveSync.HasTokenAsync()) { ShowToast(T("bk.cfg.login")); return; }
        if (!await Confirm(T("nav.backup").ToUpper(Culture), T("bk.cfg.t"), T("bk.cfg.x"), T("bk.cfg.btn"), T("dlg.cancel"))) return;
        RestoreCfgBtn.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var json = await DriveSync.DownloadSettingsAsync(_settings, cts.Token);
            if (json is null) { ShowToast(T("bk.cfg.none")); return; }
            _settings.ImportPortable(json);
            _settings.Save();
            Loc.Set(_settings.Language);
            ThemeManager.Apply(_settings.Palette, _settings.ThemeMode);
            App.StartVibrance();
            RefreshTexts();
            LoadSettingsIntoUi();
            Logger.Ok(T("lg.cfg.restored"));
            ShowToast(T("bk.cfg.done"));
        }
        catch (Exception ex) { Logger.Error(T("lg.cfg.fail", ex.Message)); ShowToast(T("lg.cfg.fail", ex.Message)); }
        finally { RestoreCfgBtn.IsEnabled = true; }
    }

    private async Task RefreshDriveAsync()
    {
        if (!DriveSync.IsConfigured)
        {
            DriveAccount.Text = T("bk.na");
            DriveAccountSub.Text = T("bk.na.sub");
            DriveDot.SetResourceReference(Shape.FillProperty, "Warn");
            DriveConnectBtn.Visibility = DriveDisconnectBtn.Visibility = Visibility.Collapsed;
            HomeBackupStatus.Text = T("bk.na.sub");
            return;
        }
        if (!await DriveSync.HasTokenAsync())
        {
            DriveAccount.Text = T("bk.off");
            DriveAccountSub.Text = T("bk.off.sub");
            DriveDot.SetResourceReference(Shape.FillProperty, "Faint");
            DriveConnectBtn.Visibility = Visibility.Visible;
            DriveDisconnectBtn.Visibility = Visibility.Collapsed;
            HomeBackupStatus.Text = _settings.BackupToDrive ? T("home.bk.needLogin") : T("home.bk.off");
            return;
        }
        DriveConnectBtn.Visibility = Visibility.Collapsed;
        DriveDisconnectBtn.Visibility = Visibility.Visible;
        DriveDot.SetResourceReference(Shape.FillProperty, "Accent");
        string? email = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            email = await DriveSync.GetAccountEmailAsync(cts.Token);
        }
        catch { }
        DriveAccount.Text = email is null ? T("bk.on") : T("bk.on.as", email);
        DriveAccountSub.Text = T("bk.on.sub", _settings.DriveFolder);
        HomeBackupStatus.Text = _settings.BackupToDrive
            ? T("home.bk.on", _settings.DriveFolder) + (email is null ? "" : $" · {email}")
            : T("home.bk.off");
    }

    private void SaveBackupSettings()
    {
        var folder = DriveFolderBox.Text.Trim();
        if (folder.Length > 0) _settings.DriveFolder = folder;
        if (int.TryParse(MaxMbBox.Text.Trim(), out var mb) && mb > 0) _settings.BackupMaxMB = mb;
        _settings.BackupExclude = ExcludeBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        DriveFolderBox.Text = _settings.DriveFolder;
        MaxMbBox.Text = _settings.BackupMaxMB.ToString();
        _settings.Save();
    }

    private void BackupSettings_LostFocus(object s, RoutedEventArgs e) => SaveBackupSettings();
    private void OpenBackups_Click(object s, RoutedEventArgs e) => OpenUnelevated(AppPaths.Backups);
    private void OpenDrive_Click(object s, RoutedEventArgs e) => OpenUnelevated("https://drive.google.com/drive/my-drive");
    private void OpenDataFolder_Click(object s, RoutedEventArgs e) => OpenUnelevated(AppPaths.Root);
    private void LogRefresh_Click(object s, RoutedEventArgs e) => LogBox.Text = Logger.ReadAll();

    /// <summary>Abre carpetas/URLs a traves del Explorador para que no hereden el modo administrador.</summary>
    private static void OpenUnelevated(string target)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = false });
        }
        catch { }
    }

    // ===================================================================
    //  Accesos directos
    // ===================================================================
    private void ShortcutNoBackup_Click(object s, RoutedEventArgs e) => CreateShortcut(T("sc.name.nobackup"), "/nobackup");

    private void CreateShortcut(string name, string args)
    {
        try
        {
            var exe = Environment.ProcessPath!;
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = exe;
            sc.Arguments = args;
            sc.WorkingDirectory = Path.GetDirectoryName(exe);
            sc.IconLocation = exe + ",0";
            sc.Description = "RNX Cache Cleaner " + args;
            sc.Save();
            ShowToast(T("toast.scOk", name));
        }
        catch (Exception ex) { ShowToast(T("toast.scFail", ex.Message)); }
    }

    // ===================================================================
    //  Historial
    // ===================================================================
    private sealed record HistRow(string Date, string Mode, string Freed, string Time, string Result, Brush ResultBrush);

    private void RefreshHistory(List<RunRecord>? demo = null)
    {
        _history = demo ?? History.Load();
        var real = _history.Where(h => !h.Cancelled).ToList();
        long total = real.Sum(h => Math.Max(0, h.FreedBytes));
        HistCount.Text = real.Count.ToString(Culture);
        HistTotal.Text = Sys.FormatBytes(total);
        HistAvg.Text = real.Count == 0 ? Sys.FormatBytes(0) : Sys.FormatBytes(total / real.Count);
        HistLast.Text = _history.Count == 0 ? "-" : _history[^1].When.ToString("g", Culture);

        HistTable.ItemsSource = _history.AsEnumerable().Reverse().Select(h => new HistRow(
            h.When.ToString("g", Culture), ModeName(h.Mode), Sys.FormatBytes(Math.Max(0, h.FreedBytes)),
            TimeSpan.FromSeconds(h.Seconds).ToString(@"mm\:ss"),
            h.Cancelled ? T("hist.r.cancelled") : h.Error > 0 ? T("hist.r.errors", h.Error) : h.Warn > 0 ? T("hist.r.warnings", h.Warn) : T("hist.r.ok"),
            ThemeManager.Res(h.Cancelled || h.Warn > 0 ? "Warn" : h.Error > 0 ? "Error" : "Muted"))).ToList();
        DrawHistoryChart();
    }

    private void HistChart_SizeChanged(object s, SizeChangedEventArgs e) => DrawHistoryChart();

    private void DrawHistoryChart()
    {
        if (HistChart is null) return;
        var data = _history.TakeLast(30).ToList();
        HistEmpty.Visibility = data.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DrawBars(HistChart, data, 30);
    }

    /// <summary>Barras de una serie: espacio liberado por ejecucion; la ultima, en el color de acento.</summary>
    private void DrawBars(Canvas c, List<RunRecord> data, int slots)
    {
        c.Children.Clear();
        if (data.Count == 0 || c.ActualWidth < 50 || c.ActualHeight < 40) return;

        const double axisW = 58, axisH = 18, top = 8;
        double w = c.ActualWidth - axisW, h = c.ActualHeight - axisH - top;
        double maxMb = Math.Max(1, data.Max(d => Math.Max(0, d.FreedBytes)) / 1048576.0);
        double niceMax = NiceCeiling(maxMb);
        var muted = ThemeManager.Res("Faint");

        foreach (var f in new[] { 0.0, 0.5, 1.0 })
        {
            double y = top + h - f * h;
            var line = new System.Windows.Shapes.Line { X1 = axisW, X2 = axisW + w, Y1 = y, Y2 = y, StrokeThickness = 1 };
            line.SetResourceReference(Shape.StrokeProperty, "Line2");
            c.Children.Add(line);
            // Misma unidad en todo el eje: GB si el maximo pasa de 1 GB
            string tick = f == 0 ? "0" : niceMax >= 1024
                ? (niceMax * f / 1024).ToString("0.#", Culture) + " " + T("u.gb")
                : (niceMax * f).ToString("0", Culture) + " " + T("u.mb");
            var lbl = new TextBlock { Text = tick, FontSize = 10.5, Foreground = muted, Width = axisW - 10, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(lbl, 0); Canvas.SetTop(lbl, y - 7);
            c.Children.Add(lbl);
        }

        double slot = w / slots, bw = Math.Max(4, Math.Min(26, slot - 6));
        for (int i = 0; i < data.Count; i++)
        {
            var d = data[i];
            double mb = Math.Max(0, d.FreedBytes) / 1048576.0;
            double bh = Math.Max(d.FreedBytes > 0 ? 2 : 0, mb / niceMax * h);
            bool last = i == data.Count - 1;
            var rect = new Border { Width = bw, Height = bh, CornerRadius = new CornerRadius(Math.Min(4, bw / 2), Math.Min(4, bw / 2), 0, 0) };
            rect.SetResourceReference(Border.BackgroundProperty, last ? "Accent" : "Bar");
            Canvas.SetLeft(rect, axisW + i * slot + (slot - bw) / 2); Canvas.SetTop(rect, top + h - bh);
            c.Children.Add(rect);
            var hit = new Border
            {
                Width = slot, Height = h, Background = Brushes.Transparent,
                ToolTip = $"{d.When.ToString("g", Culture)}\n{ModeName(d.Mode)}\n{T("hist.tt.freed", Sys.FormatBytes(Math.Max(0, d.FreedBytes)))}\n{T("hist.tt.time", TimeSpan.FromSeconds(d.Seconds).ToString(@"mm\:ss"))}"
            };
            Canvas.SetLeft(hit, axisW + i * slot); Canvas.SetTop(hit, top);
            c.Children.Add(hit);
        }

        void DateLabel(int i, bool right)
        {
            var t = new TextBlock { Text = data[i].When.ToString(Culture.DateTimeFormat.MonthDayPattern, Culture), FontSize = 10.5, Foreground = muted };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = axisW + i * slot + slot / 2 - (right ? t.DesiredSize.Width : t.DesiredSize.Width / 2);
            Canvas.SetLeft(t, Math.Max(axisW, x)); Canvas.SetTop(t, top + h + 3);
            c.Children.Add(t);
        }
        DateLabel(0, false);
        if (data.Count > 1) DateLabel(data.Count - 1, true);
    }

    private static double NiceCeiling(double v)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
            if (m * p >= v) return m * p;
        return 10 * p;
    }

    private async void HistoryClear_Click(object s, RoutedEventArgs e)
    {
        if (!await Confirm(T("hist.title").ToUpper(Culture), T("dlg.hist.t"), T("dlg.hist.x"), T("dlg.hist.yes"), T("dlg.cancel")))
            return;
        History.Clear();
        RefreshHistory();
        UpdateHome();
    }

    // ===================================================================
    //  Actualizaciones (GitHub releases)
    // ===================================================================
    private async Task CheckUpdatesAsync(bool silent)
    {
        CheckUpdatesBtn.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _update = await Updater.CheckAsync(cts.Token);
            if (_update is null)
            {
                if (!silent) ShowToast(T("toast.upToDate", Updater.Current));
                return;
            }
            UpdateTitle.Text = T("upd.title", _update.Version);
            UpdateSub.Text = T("upd.sub", Updater.Current);
            ShowUpdateNotes(Updater.WhatsNew(_update.Notes));
            UpdateBar.Visibility = Visibility.Collapsed;
            UpdateNowBtn.IsEnabled = UpdateLaterBtn.IsEnabled = true;
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBanner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            Logger.Detail(T("lg.upd.available", _update.Tag, Updater.Current));
        }
        catch (Exception ex)
        {
            if (!silent) ShowToast(T("toast.updFail"));
            Logger.Detail(T("lg.upd.checkFail", ex.Message));
        }
        finally { CheckUpdatesBtn.IsEnabled = true; }
    }

    private async void CheckUpdates_Click(object s, RoutedEventArgs e) => await CheckUpdatesAsync(silent: false);
    private void UpdateLater_Click(object s, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;
    private void UpdateNotes_Click(object s, RoutedEventArgs e) { if (_update is not null) OpenUnelevated(_update.PageUrl); }

    private async void UpdateNow_Click(object s, RoutedEventArgs e)
    {
        if (_update is null) return;
        if (_running) { ShowToast(T("toast.waitRun")); return; }

        UpdateNowBtn.IsEnabled = UpdateLaterBtn.IsEnabled = false;
        UpdateBar.Visibility = Visibility.Visible;
        UpdateBar.Value = 0;
        UpdateSub.Text = T("upd.downloading", 0);
        var progress = new Progress<double>(p => { UpdateBar.Value = p; UpdateSub.Text = T("upd.downloading", (int)(p * 100)); });
        try
        {
            await Updater.ApplyAsync(_update, progress, CancellationToken.None);
            UpdateSub.Text = T("upd.installing");
            await Task.Delay(800);
            App.ExitApp();
        }
        catch (Exception ex)
        {
            Logger.Error(T("lg.upd.fail", ex.Message));
            UpdateSub.Text = T("upd.fail", ex.Message);
            UpdateBar.Visibility = Visibility.Collapsed;
            UpdateNowBtn.IsEnabled = UpdateLaterBtn.IsEnabled = true;
        }
    }

    // ===================================================================
    //  Captura interna (--snap): para revisar el diseno sin tocar la pantalla
    // ===================================================================
    public async Task SnapshotAsync(string file, string page, string? lang, string? pal, string? mode)
    {
        if (lang is not null) Loc.Set(lang);
        if (pal is not null || mode is not null) ThemeManager.Apply(pal ?? ThemeManager.Palette, mode ?? ThemeManager.Mode);
        await Task.Delay(1200);
        var rnd = new Random(7);
        List<RunRecord> Demo() => Enumerable.Range(0, 12).Select(i => new RunRecord(DateTime.Now.AddDays(-12 + i).AddHours(-3), new[] { "todo", "quick", "full" }[i % 3],
            (long)(rnd.Next(40, 1800) * 1048576L), rnd.Next(30, 140), 20, i % 5 == 0 ? 1 : 0, 0, false)).ToList();
        switch (page)
        {
            case "home": _history = Demo(); DrawHomeChart(); break;
            case "custom": NavCustom.IsChecked = true; break;
            case "devices":
            {
                NavCustom.IsChecked = true;
                var (gh, er) = await Devices.ScanAsync();
                var sb = new System.Text.StringBuilder();
                foreach (var g in gh.GroupBy(d => d.Group)) { sb.AppendLine($"{g.Key} ({g.Count()})"); foreach (var d in g) sb.AppendLine("   •  " + d.Name); }
                if (er.Count > 0) { sb.AppendLine(); sb.AppendLine(T("dev.errorsHeader", er.Count)); foreach (var d in er) sb.AppendLine($"   •  {d.Name} ({d.Group})"); }
                _ = Confirm(T("dev.title").ToUpper(Culture), T("dev.dlg.t", gh.Count, er.Count), T("dev.dlg.x"), T("dev.dlg.yes"), T("dlg.cancel"), sb.ToString().TrimEnd());
                break;
            }
            case "video": NavVideo.IsChecked = true; break;
            case "fonts": NavFonts.IsChecked = true; await Task.Delay(4000); break;
            case "vibrance": NavVibrance.IsChecked = true; break;
            case "customres": NavCustomRes.IsChecked = true; break;
            case "hud": NavHud.IsChecked = true; break;
            case "nvdriver": NavNvDriver.IsChecked = true; await Task.Delay(4000); break;
            case "pcdrivers":
                NavPcDrivers.IsChecked = true;
                // Solo en memoria (la captura no guarda nada): un componente descartado para ver la seccion
                _settings.PcIgnore.Add("comp:microsoft.gameinput|Microsoft GameInput");
                _pcShowIgnored = true;
                await ScanPcDriversAsync();
                await Task.Delay(500); break;
            case "wtweaks": NavWinTweaks.IsChecked = true; await Task.Delay(5000); break;
            case "wapps": NavWinApps.IsChecked = true; await Task.Delay(9000); break;
            case "wstartup": NavWinStartup.IsChecked = true; await Task.Delay(2000); break;
            case "wtasks": NavWinTasks.IsChecked = true; await Task.Delay(3000); break;
            case "hud-a1": case "hud-c1": case "hud-m": _settings.HudStyle = page[4..]; NavHud.IsChecked = true; await Task.Delay(300); PageHud.ScrollToEnd(); break;
            case "videobottom": NavVideo.IsChecked = true; await Task.Delay(400); PageVideo.ScrollToEnd(); break;
            case "custombottom": NavCustom.IsChecked = true; await Task.Delay(400); CustomScroll.ScrollToEnd(); break;
            case "cs2": NavCs2.IsChecked = true; break;
            case "settings": NavSettings.IsChecked = true; break;
            case "servers": NavServers.IsChecked = true; await Task.Delay(6000); break;
            case "backup": NavBackup.IsChecked = true; break;
            case "log": NavLog.IsChecked = true; break;
            case "first": ShowFirstRun(); break;
            case "history": NavHistory.IsChecked = true; RefreshHistory(Demo()); break;
            case "update":
                UpdateTitle.Text = T("upd.title", "5.1.1"); UpdateSub.Text = T("upd.sub", Updater.Current);
                ShowUpdateNotes(Updater.WhatsNew(File.Exists("notes_demo.md") ? File.ReadAllText("notes_demo.md") : ""));
                UpdateBanner.Visibility = Visibility.Visible; break;
            case "run":
                _steps.Clear();
                foreach (var d in Runner.BuildSteps(RunPlan.For(RunMode.Todo, _settings, _sys, false), _settings, _sys)) _steps.Add(new StepVM(d));
                for (int i = 0; i < _steps.Count; i++) _steps[i].Status = i < 6 ? StepStatus.Ok : i == 6 ? StepStatus.Running : StepStatus.Pending;
                _steps[3].Status = StepStatus.Warn; _steps[1].Detail = T("run.stepFreed", Sys.FormatBytes(1288490188));
                _steps[6].HasProgress = true; _steps[6].Progress = 0.4;
                RunTitle.Text = T("plan.todo"); RunOverline.Text = T("run.overline.running"); RunState.Text = T("run.state.running");
                SetOverall(0.37); RunFreed.Text = Sys.FormatBytes(1975684956); RunTime.Text = "00:42";
                ShowPage(PageRun); break;
        }
        await Task.Delay(700);
        UpdateLayout();
        var root = (FrameworkElement)Content;
        var dpi = VisualTreeHelper.GetDpi(this);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using (var fs = File.Create(file)) enc.Save(fs);
        App.ExitApp();
    }

    // ===================================================================
    //  Dialogo y toast
    // ===================================================================
    private TaskCompletionSource<bool>? _dialogTcs;

    private Task<bool> Confirm(string overline, string title, string text, string yes, string no, string? list = null)
    {
        DialogList.Text = list ?? "";
        DialogListBox.Visibility = string.IsNullOrEmpty(list) ? Visibility.Collapsed : Visibility.Visible;
        DialogOverline.Text = overline;
        DialogTitle.Text = title;
        DialogText.Text = text;
        DialogYes.Content = yes;
        DialogNo.Content = no;
        Dialog.Visibility = Visibility.Visible;
        Dialog.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        _dialogTcs = new TaskCompletionSource<bool>();
        return _dialogTcs.Task;
    }

    private void CloseDialog(bool r)
    {
        Dialog.Visibility = Visibility.Collapsed;
        _dialogTcs?.TrySetResult(r);
    }

    private void DialogYes_Click(object s, RoutedEventArgs e) => CloseDialog(true);
    private void DialogNo_Click(object s, RoutedEventArgs e) => CloseDialog(false);

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3200))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3600))));
        Toast.BeginAnimation(OpacityProperty, a);
    }
}
