using System.Windows;
using System.Windows.Threading;
using RnxCleaner.Core;

namespace RnxCleaner;

public partial class App : Application
{
    internal static bool SnapMode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.Ensure();

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Logger.Error("Error inesperado: " + a.ExceptionObject);

        // Igual que el .bat: RNX_Cache_Cleaner.exe /todo  (/rapida, /completa, /nobackup, /shader, /raton, /nvidia, /backup)
        RunMode? auto = null;
        foreach (var a in e.Args)
            if (RunPlan.Parse(a) is { } m) { auto = m; break; }

        // Autoprueba del plan de energia: --selftest-power <informe.txt>
        int sp = Array.FindIndex(e.Args, a => a.Equals("--selftest-power", StringComparison.OrdinalIgnoreCase));
        if (sp >= 0 && sp + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.PowerAsync(e.Args[sp + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int sh = Array.FindIndex(e.Args, a => a.Equals("--selftest-hud", StringComparison.OrdinalIgnoreCase));
        if (sh >= 0 && sh + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.HudAsync(e.Args[sh + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        // Captura de la pantalla de activacion: --snap-activation <png>
        int sna = Array.FindIndex(e.Args, a => a.Equals("--snap-activation", StringComparison.OrdinalIgnoreCase));
        if (sna >= 0 && sna + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Loc.Set(Shared.Language);
            ThemeManager.Apply(Shared.Palette, Shared.ThemeMode);
            Window aw = sna + 2 < e.Args.Length && e.Args[sna + 2] == "picker"
                ? new ColorPickerWindow(System.Windows.Media.Color.FromRgb(0xB1, 0x74, 0xF7), _ => { })
                : new ActivationWindow();
            aw.ShowActivated = false; aw.WindowStartupLocation = WindowStartupLocation.Manual; aw.Left = -20000; aw.Top = -20000;
            aw.Show();
            Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(800);
                var root = (FrameworkElement)aw.Content;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen()) { dc.DrawRectangle(aw.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight)); dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight)); }
                bmp.Render(dv);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var f = System.IO.File.Create(e.Args[sna + 1])) enc.Save(f);
                Shutdown();
            });
            return;
        }

        int sds = Array.FindIndex(e.Args, a => a.Equals("--selftest-drivesettings", StringComparison.OrdinalIgnoreCase));
        if (sds >= 0 && sds + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var rep = e.Args[sds + 1];
            _ = Task.Run(async () =>
            {
                string r;
                try { r = await DriveSync.TestSettingsAsync(Shared, CancellationToken.None); } catch (Exception ex) { r = "FALLO " + ex.Message; }
                await System.IO.File.WriteAllTextAsync(rep, r);
            }).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int sac = Array.FindIndex(e.Args, a => a.Equals("--selftest-activation", StringComparison.OrdinalIgnoreCase));
        if (sac >= 0 && sac + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.ActivationAsync(e.Args[sac + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int swt = Array.FindIndex(e.Args, a => a.Equals("--selftest-wintweaks", StringComparison.OrdinalIgnoreCase));
        if (swt >= 0 && swt + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.WinTweaksAsync(e.Args[swt + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int sgp = Array.FindIndex(e.Args, a => a.Equals("--selftest-gpupause", StringComparison.OrdinalIgnoreCase));
        if (sgp >= 0 && sgp + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = GpuPauseTestAsync(e.Args[sgp + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int snd = Array.FindIndex(e.Args, a => a.Equals("--selftest-nvdriver", StringComparison.OrdinalIgnoreCase));
        if (snd >= 0 && snd + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.NvDriverAsync(e.Args[snd + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int svb = Array.FindIndex(e.Args, a => a.Equals("--selftest-vibrance", StringComparison.OrdinalIgnoreCase));
        if (svb >= 0 && svb + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.VibranceAsync(e.Args[svb + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int sr = Array.FindIndex(e.Args, a => a.Equals("--selftest-res", StringComparison.OrdinalIgnoreCase));
        if (sr >= 0 && sr + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.ResAsync(e.Args[sr + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int sfo = Array.FindIndex(e.Args, a => a.Equals("--selftest-fonts", StringComparison.OrdinalIgnoreCase));
        if (sfo >= 0 && sfo + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.FontsAsync(e.Args[sfo + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int sv = Array.FindIndex(e.Args, a => a.Equals("--selftest-video", StringComparison.OrdinalIgnoreCase));
        if (sv >= 0 && sv + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.VideoAsync(e.Args[sv + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int sn = Array.FindIndex(e.Args, a => a.Equals("--selftest-network", StringComparison.OrdinalIgnoreCase));
        if (sn >= 0 && sn + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.NetworkAsync(e.Args[sn + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        int sf = Array.FindIndex(e.Args, a => a.Equals("--selftest-safety", StringComparison.OrdinalIgnoreCase));
        if (sf >= 0 && sf + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.SafetyAsync(e.Args[sf + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        // Autoprueba del bloqueo de servidores: --selftest-servers <informe.txt>
        int st = Array.FindIndex(e.Args, a => a.Equals("--selftest-servers", StringComparison.OrdinalIgnoreCase));
        if (st >= 0 && st + 1 < e.Args.Length)
        {
            SnapMode = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = SelfTest.ServersAsync(e.Args[st + 1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }

        // Herramienta interna de desarrollo: --snap <png> [pagina]
        // Renderiza la ventana fuera de pantalla a un PNG y sale (no ejecuta nada).
        int snap = Array.FindIndex(e.Args, a => a.Equals("--snap", StringComparison.OrdinalIgnoreCase));
        if (snap >= 0 && snap + 1 < e.Args.Length)
        {
            SnapMode = true;
            var sw = new MainWindow(null) { SnapMode = true, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            MainWindow = sw;
            sw.Show();
            string? Arg(int k) => snap + k < e.Args.Length ? e.Args[snap + k] : null;
            _ = sw.SnapshotAsync(e.Args[snap + 1], Arg(2) ?? "home", Arg(3), Arg(4), Arg(5));
            return;
        }

        // Limpieza programada (Programador de tareas): minimizada, sin preguntas y con aviso al final
        bool scheduled = auto is not null && e.Args.Any(a => a.Equals(Scheduler.Flag, StringComparison.OrdinalIgnoreCase));

        // Solo una RNX con ventana: si ya hay una (p. ej. en la bandeja), se muestra esa
        bool trayStart = auto is null && e.Args.Any(a => a.Equals(TrayFlag, StringComparison.OrdinalIgnoreCase));
        if (auto is null && !ClaimSingleInstance()) { Shutdown(); return; }

        // Activacion de primer uso: sin activar no se ejecuta nada (ni ventana, ni modos /todo..., ni la bandeja)
        if (!Activation.IsActivated())
        {
            if (auto is not null || trayStart) { Logger.Info("ACTIVATION: this PC is not activated, start blocked"); Shutdown(); return; }
            Loc.Set(Shared.Language);
            ThemeManager.Apply(Shared.Palette, Shared.ThemeMode);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (new ActivationWindow().ShowDialog() != true) { Shutdown(); return; }
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        Oem.RepairLogo();

        var w = new MainWindow(auto, scheduled);
        if (scheduled) { w.ShowActivated = false; w.WindowState = WindowState.Minimized; }
        MainWindow = w;
        if (auto is null) StartBackground();
        // Inicio con Windows (/bandeja): solo el icono y el observador de color
        if (trayStart) { if (!KeepInTray) Shutdown(); return; }
        w.Show();
    }

    private static void OnUnhandled(object s, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Error inesperado: " + e.Exception);
        if (SnapMode) { e.Handled = true; Current.Shutdown(1); return; }
        MessageBox.Show(e.Exception.Message, "RNX Cache Cleaner", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
