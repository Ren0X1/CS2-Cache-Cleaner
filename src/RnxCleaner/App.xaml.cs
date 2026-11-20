using System.Windows;
using System.Windows.Threading;
using RnxCleaner.Core;

namespace RnxCleaner;

public partial class App : Application
{
    private static bool SnapMode;

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

        var w = new MainWindow(auto, scheduled);
        if (scheduled) { w.ShowActivated = false; w.WindowState = WindowState.Minimized; }
        MainWindow = w;
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
