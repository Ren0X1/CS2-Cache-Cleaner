using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Path = System.IO.Path;
using System.Windows.Threading;
using RnxCleaner.Core;
using RnxCleaner.Views;

namespace RnxCleaner;

public partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private readonly SystemInfo _sys = SystemInfo.Detect();
    private readonly RunMode? _autoMode;
    public bool SnapMode { get; set; }
    private UpdateInfo? _update;

    private readonly ObservableCollection<StepVM> _steps = [];
    private readonly List<CheckBox> _customBoxes = [];
    private CancellationTokenSource? _runCts;
    private bool _running;
    private bool _keepOpen;

    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(RunMode? autoMode)
    {
        _autoMode = autoMode;
        InitializeComponent();

        StepsList.ItemsSource = _steps;
        VersionText.Text = $"v{Updater.Current}";
        Title = $"RNX Cache Cleaner Pro v{AppPaths.Version}";

        FillSystemInfo();
        BuildCustomList();
        LoadSettingsIntoUi();
        StartHud();

        StateChanged += (_, _) =>
        {
            // Con WindowChrome, al maximizar Windows recorta ~7px por cada lado
            ((Border)Content).Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        };

        Loaded += async (_, _) =>
        {
            await RefreshDriveAsync();
            if (_autoMode is null && !SnapMode) _ = CheckUpdatesAsync(silent: true);
            if (_autoMode is { } m)
                await RunAsync(RunPlan.For(m, _settings, _sys, interactive: false), auto: true);
        };
    }

    // ===================================================================
    //  Arranque de la interfaz
    // ===================================================================
    private void FillSystemInfo()
    {
        SysWin.Text = _sys.Windows;
        SysUser.Text = _sys.User;
        SysGpu.Text = _sys.GpuName.Replace("NVIDIA GeForce ", "");
        SysGpu.ToolTip = _sys.GpuName;
        UpdateDisk();

        if (!_sys.IsNvidia)
        {
            NvidiaBtn.IsEnabled = false;
            NvidiaSub.Text = "No se ha detectado una GPU NVIDIA en este equipo: este módulo solo funciona con tarjetas NVIDIA.";
            CustomNvidia.IsEnabled = false;
        }
    }

    private void UpdateDisk() => SysDisk.Text = "C: " + Sys.FormatBytes(SystemInfo.FreeBytesC()) + " libres";

    private void BuildCustomList()
    {
        int i = 1;
        foreach (var item in CleanupCatalog.All)
        {
            var title = new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Text") };
            var desc = new TextBlock { Text = item.Description, Style = (Style)FindResource("Sub"), FontSize = 11 };
            var panel = new StackPanel();
            panel.Children.Add(title);
            panel.Children.Add(desc);
            if (item.Risky)
                panel.Children.Add(new TextBlock { Text = "Precaución", Foreground = (Brush)FindResource("Amber"), FontSize = 10, Margin = new Thickness(0, 3, 0, 0) });

            var cb = new CheckBox { Style = (Style)FindResource("CheckCard"), Content = panel, Tag = item.Id };
            cb.Click += CustomItem_Click;
            _customBoxes.Add(cb);
        }
        CustomList.ItemsSource = _customBoxes;
    }

    private void LoadSettingsIntoUi()
    {
        HomeBackupToggle.IsChecked = BackupToggle.IsChecked = _settings.BackupToDrive;
        ShaderPathBox.Text = _settings.ShaderPath;
        DriveFolderBox.Text = _settings.DriveFolder;
        MaxMbBox.Text = _settings.BackupMaxMB.ToString();
        ExcludeBox.Text = string.Join(",", _settings.BackupExclude);

        var sel = _settings.CustomSelection.Count == 0
            ? CleanupCatalog.Quick.Select(q => q.Id).ToHashSet()
            : _settings.CustomSelection.ToHashSet();
        foreach (var cb in _customBoxes) cb.IsChecked = sel.Contains((string)cb.Tag);
        CustomShader.IsChecked = sel.Contains("mod:shader");
        CustomMouse.IsChecked = sel.Contains("mod:mouse");
        CustomNvidia.IsChecked = _sys.IsNvidia && sel.Contains("mod:nvidia");
        UpdateCustomCount();
    }

    // ===================================================================
    //  Elementos "vivos" del HUD: cinta de rumbo, reloj Zulu, reticula
    // ===================================================================
    private void StartHud()
    {
        _hudTimer.Tick += (_, _) =>
        {
            ZuluClock.Text = DateTime.Now.ToString("HH:mm");
        };
        _hudTimer.Start();

    }

    // ===================================================================
    //  Barra de titulo y navegacion
    // ===================================================================
    private void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private async void Close_Click(object s, RoutedEventArgs e)
    {
        if (_running && !await Confirm("AVISO", "Hay una limpieza en marcha", "Si cierras ahora se cancela lo que quede pendiente.", "Cerrar igualmente", "Seguir"))
            return;
        _runCts?.Cancel();
        Close();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && sender != NavHome) return;
        if (_running) return;
        ShowPage(sender switch
        {
            RadioButton r when r == NavCustom => PageCustom,
            RadioButton r when r == NavCs2 => PageCs2,
            RadioButton r when r == NavBackup => PageBackup,
            RadioButton r when r == NavLog => PageLog,
            RadioButton r when r == NavHistory => PageHistory,
            _ => PageHome
        });
        if (sender == NavLog) LogBox.Text = Logger.ReadAll();
        if (sender == NavHistory) RefreshHistory();
    }

    private void ShowPage(FrameworkElement page)
    {
        if (PageHome is null) return;
        foreach (var p in new FrameworkElement[] { PageHome, PageCustom, PageCs2, PageBackup, PageLog, PageHistory, PageRun })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    // ===================================================================
    //  Modos
    // ===================================================================
    private async void Quick_Click(object s, RoutedEventArgs e) => await RunAsync(RunPlan.For(RunMode.Quick, _settings, _sys, true));
    private async void Full_Click(object s, RoutedEventArgs e) => await RunAsync(RunPlan.For(RunMode.Full, _settings, _sys, true));
    private async void Todo_Click(object s, RoutedEventArgs e) => await RunAsync(RunPlan.For(RunMode.Todo, _settings, _sys, true));

    private async void RunCustom_Click(object s, RoutedEventArgs e)
    {
        SaveCustomSelection();
        var ids = _customBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToHashSet();
        var plan = new RunPlan
        {
            Name = "Limpieza personalizada",
            Cleanup = CleanupCatalog.All.Where(i => ids.Contains(i.Id)).ToList(),
            Shader = CustomShader.IsChecked == true,
            Mouse = CustomMouse.IsChecked == true,
            Nvidia = CustomNvidia.IsChecked == true && _sys.IsNvidia,
            Backup = _settings.BackupToDrive,
        };
        if (plan.Cleanup.Count == 0 && !plan.Shader && !plan.Mouse && !plan.Nvidia)
        {
            ShowToast("Selecciona al menos una opción");
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

    private void OpenInspector_Click(object s, RoutedEventArgs e)
    {
        try { Cs2Modules.OpenNvidiaInspector(); }
        catch (Exception ex) { ShowToast("No se pudo abrir el Inspector: " + ex.Message); }
    }

    private async void BackupNow_Click(object s, RoutedEventArgs e)
    {
        SaveBackupSettings();
        await RunAsync(RunPlan.For(RunMode.BackupOnly, _settings, _sys, true));
    }

    // ===================================================================
    //  Ejecucion de una mision
    // ===================================================================
    private async Task RunAsync(RunPlan plan, bool auto = false)
    {
        if (_running) return;

        // Mismo aviso que el .bat: con Steam abierto la shadercache puede no borrarse
        if (plan.Shader && plan.Interactive && SystemInfo.IsRunning("steam"))
        {
            if (!await Confirm("AVISO", "Steam está abierto",
                    "Conviene cerrar Steam antes de borrar la shadercache o algunos archivos no se podrán eliminar.",
                    "Continuar igualmente", "Saltar shadercache"))
                plan.Shader = false;
        }

        // Backup activado pero sin sesion de Google: mejor resolverlo antes de empezar
        if (plan.Backup && plan.Interactive && DriveSync.IsConfigured && !await DriveSync.HasTokenAsync())
        {
            if (await Confirm("GOOGLE DRIVE", "Google Drive sin conectar",
                    "El backup está activado pero aún no has iniciado sesión con Google. Se abrirá el navegador para autorizarlo.",
                    "Conectar ahora", "Seguir sin subir"))
                await ConnectDriveAsync();
        }

        var stepsDef = Runner.BuildSteps(plan, _settings, _sys);
        if (stepsDef.Count == 0) { ShowToast("No hay nada que ejecutar"); return; }

        _running = true;
        _keepOpen = false;
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        Logger.StartSession(plan.Name);
        Logger.Info($"=== INICIO {plan.Name.ToUpperInvariant()} ===");
        if (!plan.Backup && plan.Name.Contains("sin backup")) Logger.Info("MODO /nobackup: backup y subida a Drive omitidos");

        _steps.Clear();
        foreach (var st in stepsDef) _steps.Add(new StepVM(st));
        LiveLog.Items.Clear();
        Logger.Written += OnLog;

        RunTitle.Text = plan.Name;
        RunOverline.Text = "EN CURSO";
        RunOverline.Foreground = (Brush)FindResource("Faint");
        RunBar.Value = 0;
        RunPercent.Text = "0%";
        RunFreed.Text = "---";
        RunState.Text = "En curso";
        RunState.Foreground = (Brush)FindResource("Muted");
        CancelBtn.Visibility = Visibility.Visible;
        DoneBtn.Visibility = Visibility.Collapsed;
        KeepOpenBtn.Visibility = Visibility.Collapsed;
        FooterLeft.Text = "Ejecutando: " + plan.Name;
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
            if (ct.IsCancellationRequested)
            {
                vm.Status = StepStatus.Skipped;
                continue;
            }

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
                Logger.Warn("Abortado por el usuario");
            }
            catch (Exception ex)
            {
                vm.Status = StepStatus.Error;
                Logger.Error($"{vm.Title}: {ex.Message}");
            }

            // El backup escribe ZIPs en C:, eso no cuenta como "liberado"
            long delta = SystemInfo.FreeBytesC() - before;
            if (!vm.Title.Contains("ackup") && delta > 0)
            {
                freed += delta;
                if (delta > 1024 * 1024) vm.Detail = "+" + Sys.FormatBytes(delta) + " liberados";
            }
            switch (vm.Status)
            {
                case StepStatus.Ok: ok++; break;
                case StepStatus.Warn: warn++; break;
                case StepStatus.Error: err++; break;
            }
            RunFreed.Text = Sys.FormatBytes(freed);
            UpdateDisk();
        }

        sw.Stop();
        clock.Stop();
        RunTime.Text = sw.Elapsed.ToString(@"mm\:ss");
        Logger.Written -= OnLog;
        SetOverall(1);
        cancelled |= ct.IsCancellationRequested;

        History.Add(new RunRecord(DateTime.Now, plan.Name, freed, sw.Elapsed.TotalSeconds, ok, warn, err, cancelled));
        Logger.Write(err > 0 ? LogLevel.Warn : LogLevel.Ok,
            $"=== FIN {plan.Name.ToUpperInvariant()} - liberado {Sys.FormatBytes(freed)} en {sw.Elapsed:mm\\:ss} ({ok} ok, {warn} avisos, {err} errores) ===");

        if (cancelled) { RunState.Text = "Cancelada"; RunState.Foreground = (Brush)FindResource("Amber"); RunOverline.Text = "CANCELADA"; }
        else if (err > 0) { RunState.Text = $"{err} con errores"; RunState.Foreground = (Brush)FindResource("Red"); RunOverline.Text = "TERMINADA CON ERRORES"; }
        else if (warn > 0) { RunState.Text = $"Completada · {warn} aviso(s)"; RunState.Foreground = (Brush)FindResource("Amber"); RunOverline.Text = "COMPLETADA"; }
        else { RunState.Text = "Completada"; RunState.Foreground = (Brush)FindResource("Hud"); RunOverline.Text = "COMPLETADA"; }
        RunOverline.Foreground = RunState.Foreground;

        FooterLeft.Text = "Listo";
        CancelBtn.Visibility = Visibility.Collapsed;
        DoneBtn.Visibility = Visibility.Visible;
        Nav.IsEnabled = true;
        _running = false;
        _runCts.Dispose();
        _runCts = null;

        // Lanzado con /todo, /rapida...: como el .bat, se cierra solo al terminar
        if (auto) { await AutoCloseAsync(); return; }

        // Tras una limpieza desde la ventana, ofrecer reiniciar
        if (plan.Cleanup.Count > 0 && !cancelled)
            await OfferRestartAsync();
    }

    private async Task OfferRestartAsync()
    {
        if (!await Confirm("REINICIO RECOMENDADO", "¿Reiniciar el PC ahora?",
                "Es recomendable reiniciar para que Windows termine de liberar los archivos que estaban en uso y " +
                "regenere limpias las cachés (miniaturas, iconos, fuentes y drivers gráficos). Guarda lo que tengas abierto antes.",
                "Reiniciar ahora", "Más tarde"))
            return;
        Logger.Info("Reinicio solicitado por el usuario");
        await Sys.RunAsync("shutdown.exe", "/r /t 10 /c \"RNX Cache Cleaner: reiniciando para terminar la limpieza\"", 30);
        Application.Current.Shutdown();
    }

    private async Task AutoCloseAsync()
    {
        KeepOpenBtn.Visibility = Visibility.Visible;
        for (int s = 6; s > 0; s--)
        {
            if (_keepOpen) { KeepOpenBtn.Visibility = Visibility.Collapsed; return; }
            FooterRight.Text = $"Se cierra en {s} s";
            await Task.Delay(1000);
        }
        if (!_keepOpen) Close();
        FooterRight.Text = "Administrador";
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
            var brush = (Brush)FindResource(e.Level switch
            {
                LogLevel.Error => "Red",
                LogLevel.Warn => "Amber",
                LogLevel.Ok => "Hud",
                LogLevel.Detail => "Faint",
                _ => "Muted"
            });
            LiveLog.Items.Add(new TextBlock
            {
                Text = $"{e.Time:HH:mm:ss}  {e.Text}",
                Foreground = brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1),
            });
            while (LiveLog.Items.Count > 400) LiveLog.Items.RemoveAt(0);
            LiveScroll.ScrollToEnd();
        });
    }

    private void Cancel_Click(object s, RoutedEventArgs e)
    {
        _runCts?.Cancel();
        CancelBtn.IsEnabled = false;
        RunState.Text = "Cancelando...";
        Dispatcher.BeginInvoke(() => CancelBtn.IsEnabled = true, DispatcherPriority.Background);
    }

    private void KeepOpen_Click(object s, RoutedEventArgs e)
    {
        _keepOpen = true;
        KeepOpenBtn.Visibility = Visibility.Collapsed;
        FooterRight.Text = "Administrador";
    }

    private void Done_Click(object s, RoutedEventArgs e)
    {
        NavHome.IsChecked = true;
        ShowPage(PageHome);
    }

    // ===================================================================
    //  Personalizada
    // ===================================================================
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
        if (sel.Count == 0) sel.Add("none"); // distinguir "nada marcado" de "primera vez"
        _settings.CustomSelection = sel;
        _settings.Save();
        UpdateCustomCount();
    }

    private void UpdateCustomCount()
    {
        int n = _customBoxes.Count(c => c.IsChecked == true)
                + (CustomShader.IsChecked == true ? 1 : 0) + (CustomMouse.IsChecked == true ? 1 : 0) + (CustomNvidia.IsChecked == true ? 1 : 0);
        CustomCount.Text = $"{n} seleccionadas";
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
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta shadercache de CS2 (steamapps\\shadercache\\730)" };
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
            ShowToast("Shadercache de CS2 encontrada");
            return;
        }
        var lib = Cs2Modules.Cs2Library();
        if (lib is not null)
        {
            ShaderPathBox.Text = Path.Combine(lib, "steamapps", "shadercache", "730");
            SaveShaderPath();
            ShowToast("CS2 encontrado; la shadercache ya está limpia (se regenera al abrir el juego)");
        }
        else ShowToast("No se ha encontrado CS2 en ninguna biblioteca de Steam");
    }

    // ===================================================================
    //  Backup / Google Drive
    // ===================================================================
    private async void BackupToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = ((CheckBox)sender).IsChecked == true;
        _settings.BackupToDrive = on;
        HomeBackupToggle.IsChecked = BackupToggle.IsChecked = on;
        _settings.Save();

        if (on && !DriveSync.IsConfigured)
        {
            ShowToast("Esta compilación no trae Google Drive configurado: el backup se guardará solo en local");
            return;
        }
        if (on && !await DriveSync.HasTokenAsync())
            await ConnectDriveAsync();
        await RefreshDriveAsync();
    }

    private async void DriveConnect_Click(object s, RoutedEventArgs e) => await ConnectDriveAsync();

    private async Task ConnectDriveAsync()
    {
        if (!DriveSync.IsConfigured) { ShowToast("Esta compilación no trae Google Drive configurado"); return; }
        DriveConnectBtn.IsEnabled = false;
        DriveAccount.Text = "Esperando al navegador...";
        ShowToast("Abriendo el navegador: inicia sesión con Google y acepta");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await DriveSync.AuthorizeAsync(true, cts.Token);
            ShowToast("Google Drive conectado");
        }
        catch (Exception ex)
        {
            Logger.Warn("DRIVE: no se completo el inicio de sesion: " + ex.Message);
            ShowToast("No se completó el inicio de sesión");
        }
        finally { DriveConnectBtn.IsEnabled = true; }
        await RefreshDriveAsync();
    }

    private async void DriveDisconnect_Click(object s, RoutedEventArgs e)
    {
        if (!await Confirm("GOOGLE DRIVE", "Desconectar Google Drive",
                "Se borra el acceso guardado en este PC. Los backups que ya están en tu Drive no se tocan.", "Desconectar", "Cancelar"))
            return;
        await DriveSync.SignOutAsync();
        await RefreshDriveAsync();
        ShowToast("Google Drive desconectado");
    }

    private async Task RefreshDriveAsync()
    {
        var hud = (Brush)FindResource("Hud");
        var faint = (Brush)FindResource("Faint");
        var amber = (Brush)FindResource("Amber");

        if (!DriveSync.IsConfigured)
        {
            DriveAccount.Text = "No disponible";
            DriveAccountSub.Text = "Esta compilación no trae el cliente de Google (google_client.json). El backup se guarda solo en local.";
            DriveDot.Fill = amber;
            DriveConnectBtn.Visibility = Visibility.Collapsed;
            DriveDisconnectBtn.Visibility = Visibility.Collapsed;
            SysLink.Text = "No disponible"; SysLink.Foreground = amber;
            HomeBackupStatus.Text = _settings.BackupToDrive ? "Activado · solo copia local (Drive no disponible en esta compilación)" : "Guarda la config de todos tus juegos (userdata) antes de limpiar.";
            return;
        }

        if (!await DriveSync.HasTokenAsync())
        {
            DriveAccount.Text = "Sin conectar";
            DriveAccountSub.Text = "Se abrirá el navegador para autorizar el acceso (solo a los archivos que crea este programa).";
            DriveDot.Fill = faint;
            DriveConnectBtn.Visibility = Visibility.Visible;
            DriveDisconnectBtn.Visibility = Visibility.Collapsed;
            SysLink.Text = "Sin conectar"; SysLink.Foreground = faint;
            HomeBackupStatus.Text = _settings.BackupToDrive
                ? "Activado · falta iniciar sesión con Google (pestaña Backup)"
                : "Antes de limpiar guarda la config de todos tus juegos (userdata) y la sube a tu Drive.";
            return;
        }

        DriveConnectBtn.Visibility = Visibility.Collapsed;
        DriveDisconnectBtn.Visibility = Visibility.Visible;
        DriveDot.Fill = hud;
        DriveDot.Effect = (System.Windows.Media.Effects.Effect)FindResource("Glow");
        SysLink.Text = "Conectado"; SysLink.Foreground = hud;
        string? email = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            email = await DriveSync.GetAccountEmailAsync(cts.Token);
        }
        catch { }
        DriveAccount.Text = email is null ? "Conectado" : "Conectado como " + email;
        DriveAccountSub.Text = $"Los ZIP se guardan en la carpeta \"{_settings.DriveFolder}\" de tu Drive.";
        HomeBackupStatus.Text = _settings.BackupToDrive
            ? $"Activado · se sube a \"{_settings.DriveFolder}\"" + (email is null ? "" : $" ({email})")
            : "Desactivado. Actívalo para guardar la config de tus juegos antes de limpiar.";
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
    //  Accesos directos en el escritorio
    // ===================================================================
    private void ShortcutTodo_Click(object s, RoutedEventArgs e) => CreateShortcut("RNX - TODO", "/todo");
    private void ShortcutNoBackup_Click(object s, RoutedEventArgs e) => CreateShortcut("RNX - Limpiar sin backup", "/nobackup");

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
            ShowToast($"Acceso directo \"{name}\" creado en el escritorio");
        }
        catch (Exception ex) { ShowToast("No se pudo crear el acceso directo: " + ex.Message); }
    }

    // ===================================================================
    //  Historial
    // ===================================================================
    private List<RunRecord> _history = [];

    private sealed record HistRow(string Date, string Mode, string Freed, string Time, string Result, Brush ResultBrush);

    private void RefreshHistory(List<RunRecord>? demo = null)
    {
        _history = demo ?? History.Load();
        var real = _history.Where(h => !h.Cancelled).ToList();
        long total = real.Sum(h => Math.Max(0, h.FreedBytes));
        HistCount.Text = real.Count.ToString();
        HistTotal.Text = Sys.FormatBytes(total);
        HistAvg.Text = real.Count == 0 ? "0 MB" : Sys.FormatBytes(total / real.Count);
        HistLast.Text = _history.Count == 0 ? "--" : _history[^1].When.ToString("dd/MM HH:mm");

        HistTable.ItemsSource = _history.AsEnumerable().Reverse().Select(h => new HistRow(
            h.When.ToString("dd/MM/yyyy HH:mm"), h.Mode, Sys.FormatBytes(Math.Max(0, h.FreedBytes)),
            TimeSpan.FromSeconds(h.Seconds).ToString(@"mm\:ss"),
            h.Cancelled ? "Cancelada" : h.Error > 0 ? $"{h.Error} error(es)" : h.Warn > 0 ? $"OK · {h.Warn} aviso(s)" : "OK",
            (Brush)FindResource(h.Cancelled || h.Warn > 0 ? "Amber" : h.Error > 0 ? "Red" : "Muted"))).ToList();
        DrawHistoryChart();
    }

    private void HistChart_SizeChanged(object s, SizeChangedEventArgs e) => DrawHistoryChart();

    /// <summary>Barras (una sola serie) con el espacio liberado en las ultimas 30 ejecuciones.</summary>
    private void DrawHistoryChart()
    {
        var c = HistChart;
        c.Children.Clear();
        var data = _history.TakeLast(30).ToList();
        HistEmpty.Visibility = data.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (data.Count == 0 || c.ActualWidth < 50 || c.ActualHeight < 40) return;

        const double axisW = 64, axisH = 18, top = 12;
        double w = c.ActualWidth - axisW, h = c.ActualHeight - axisH - top;
        double maxMb = Math.Max(1, data.Max(d => Math.Max(0, d.FreedBytes)) / 1048576.0);
        double niceMax = NiceCeiling(maxMb);

        var mono = (FontFamily)FindResource("Mono");
        var muted = (Brush)FindResource("Faint");
        var grid = (Brush)FindResource("Line");
        var bar = (Brush)FindResource("Hud");

        // Rejilla recesiva: 0, 1/2, max
        foreach (var f in new[] { 0.0, 0.5, 1.0 })
        {
            double y = top + h - f * h;
            c.Children.Add(new System.Windows.Shapes.Line { X1 = axisW, X2 = axisW + w, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = FormatMb(niceMax * f, niceMax >= 1000), FontFamily = mono, FontSize = 10, Foreground = muted, Width = axisW - 10, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(lbl, 0); Canvas.SetTop(lbl, y - 7);
            c.Children.Add(lbl);
        }

        double slot = w / 30;              // ancho fijo por ejecucion: la barra no engorda con pocos datos
        double bw = Math.Max(4, slot - 2); // 2px de separacion entre barras
        for (int i = 0; i < data.Count; i++)
        {
            var d = data[i];
            double mb = Math.Max(0, d.FreedBytes) / 1048576.0;
            double bh = Math.Max(d.FreedBytes > 0 ? 2 : 0, mb / niceMax * h);
            double x = axisW + i * slot + 1;

            var rect = new Border { Width = bw, Height = bh, Background = bar, CornerRadius = new CornerRadius(Math.Min(4, bw / 2), Math.Min(4, bw / 2), 0, 0), Opacity = 0.85 };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, top + h - bh);
            c.Children.Add(rect);

            // Zona de hover de toda la columna (mayor que la barra)
            var hit = new Border
            {
                Width = slot, Height = h, Background = Brushes.Transparent,
                ToolTip = $"{d.When:dd/MM/yyyy HH:mm}\n{d.Mode}\nLiberado: {Sys.FormatBytes(Math.Max(0, d.FreedBytes))}\nDuración: {TimeSpan.FromSeconds(d.Seconds):mm\\:ss}"
            };
            hit.MouseEnter += (_, _) => rect.Opacity = 1;
            hit.MouseLeave += (_, _) => rect.Opacity = 0.85;
            Canvas.SetLeft(hit, axisW + i * slot); Canvas.SetTop(hit, top);
            c.Children.Add(hit);
        }

        // Fechas: primera y ultima ejecucion
        void DateLabel(int i, bool right)
        {
            var t = new TextBlock { Text = data[i].When.ToString("dd/MM"), FontFamily = mono, FontSize = 10, Foreground = muted };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = axisW + i * slot + slot / 2 - (right ? t.DesiredSize.Width : 0);
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

    private static string FormatMb(double mb, bool gb) => gb ? $"{mb / 1024:0.#} GB" : $"{mb:0} MB";

    private async void HistoryClear_Click(object s, RoutedEventArgs e)
    {
        if (!await Confirm("HISTORIAL", "Borrar el historial", "Se borran las estadísticas de limpiezas guardadas en este PC. No afecta a nada más.", "Borrar", "Cancelar"))
            return;
        History.Clear();
        RefreshHistory();
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
                if (!silent) ShowToast($"Ya tienes la última versión (v{Updater.Current})");
                return;
            }
            UpdateTitle.Text = $"Versión {_update.Version} disponible";
            UpdateSub.Text = $"Tienes la {Updater.Current}. Se descarga, se instala y el programa se vuelve a abrir. Tu configuración se mantiene.";
            UpdateBar.Visibility = Visibility.Collapsed;
            UpdateNowBtn.IsEnabled = UpdateLaterBtn.IsEnabled = true;
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBanner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            Logger.Detail($"UPDATE: disponible {_update.Tag} (actual {Updater.Current})");
        }
        catch (Exception ex)
        {
            if (!silent) ShowToast("No se pudo comprobar si hay actualizaciones");
            Logger.Detail("UPDATE: no se pudo comprobar: " + ex.Message);
        }
        finally { CheckUpdatesBtn.IsEnabled = true; }
    }

    private async void CheckUpdates_Click(object s, RoutedEventArgs e) => await CheckUpdatesAsync(silent: false);

    private void UpdateLater_Click(object s, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;

    private void UpdateNotes_Click(object s, RoutedEventArgs e)
    {
        if (_update is not null) OpenUnelevated(_update.PageUrl);
    }

    private async void UpdateNow_Click(object s, RoutedEventArgs e)
    {
        if (_update is null) return;
        if (_running) { ShowToast("Espera a que termine la limpieza en curso"); return; }

        UpdateNowBtn.IsEnabled = UpdateLaterBtn.IsEnabled = false;
        UpdateBar.Visibility = Visibility.Visible;
        UpdateBar.Value = 0;
        UpdateSub.Text = "Descargando...";
        var progress = new Progress<double>(p =>
        {
            UpdateBar.Value = p;
            UpdateSub.Text = $"Descargando... {(int)(p * 100)}%";
        });
        try
        {
            await Updater.ApplyAsync(_update, progress, CancellationToken.None);
            UpdateSub.Text = "Instalando. El programa se cerrará y se volverá a abrir.";
            await Task.Delay(800);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error("UPDATE: " + ex.Message);
            UpdateSub.Text = "No se pudo actualizar: " + ex.Message;
            UpdateBar.Visibility = Visibility.Collapsed;
            UpdateNowBtn.IsEnabled = UpdateLaterBtn.IsEnabled = true;
        }
    }

    // ===================================================================
    //  Captura interna (--snap): para revisar el diseno sin tocar la pantalla
    // ===================================================================
    public async Task SnapshotAsync(string file, string page)
    {
        await Task.Delay(1500);
        switch (page)
        {
            case "custom": NavCustom.IsChecked = true; break;
            case "cs2": NavCs2.IsChecked = true; break;
            case "backup": NavBackup.IsChecked = true; break;
            case "log": NavLog.IsChecked = true; break;
            case "history":
                NavHistory.IsChecked = true;
                var rnd = new Random(7);
                var modes = new[] { "TODO", "Limpieza rápida", "Limpieza completa" };
                RefreshHistory(Enumerable.Range(0, 18).Select(i => new RunRecord(DateTime.Now.AddDays(-18 + i), modes[i % 3],
                    (long)(rnd.Next(40, 1600) * 1048576L), rnd.Next(30, 140), 20, i % 5 == 0 ? 1 : 0, 0, false)).ToList());
                break;
            case "update":
                UpdateTitle.Text = "Versión 5.1.0 disponible";
                UpdateSub.Text = $"Tienes la {Updater.Current}. Se descarga, se instala y el programa se vuelve a abrir. Tu configuración se mantiene.";
                UpdateBanner.Visibility = Visibility.Visible;
                break;
            case "run":
                _steps.Clear();
                var demo = Runner.BuildSteps(RunPlan.For(RunMode.Todo, _settings, _sys, false), _settings, _sys);
                foreach (var d in demo) _steps.Add(new StepVM(d));
                for (int i = 0; i < _steps.Count; i++)
                    _steps[i].Status = i < 6 ? StepStatus.Ok : i == 6 ? StepStatus.Running : StepStatus.Pending;
                _steps[3].Status = StepStatus.Warn; _steps[1].Detail = "+1.2 GB liberados";
                _steps[6].HasProgress = true; _steps[6].Progress = 0.4;
                RunTitle.Text = "Todo"; SetOverall(0.37); RunFreed.Text = "1.84 GB"; RunTime.Text = "00:42";
                foreach (var t in new[] { "Limpiada: Windows Temp", "Cache DNS vaciada", "AVISO Steam abierto" })
                    LiveLog.Items.Add(new TextBlock { Text = "22:41:0" + LiveLog.Items.Count + "  " + t, Foreground = (Brush)FindResource(t.StartsWith("AVISO") ? "Amber" : "Muted") });
                ShowPage(PageRun);
                break;
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
        Application.Current.Shutdown();
    }

    // ===================================================================
    //  Dialogo y toast
    // ===================================================================
    private TaskCompletionSource<bool>? _dialogTcs;

    private Task<bool> Confirm(string overline, string title, string text, string yes, string no)
    {
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
