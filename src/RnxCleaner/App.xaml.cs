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

        // Herramienta interna de desarrollo: --snap <png> [pagina]
        // Renderiza la ventana fuera de pantalla a un PNG y sale (no ejecuta nada).
        int snap = Array.FindIndex(e.Args, a => a.Equals("--snap", StringComparison.OrdinalIgnoreCase));
        if (snap >= 0 && snap + 1 < e.Args.Length)
        {
            SnapMode = true;
            var sw = new MainWindow(null) { SnapMode = true, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            MainWindow = sw;
            sw.Show();
            _ = sw.SnapshotAsync(e.Args[snap + 1], snap + 2 < e.Args.Length ? e.Args[snap + 2] : "home");
            return;
        }

        var w = new MainWindow(auto);
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
