using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Reloj de la barra de tareas de RNX: ciclo de vida (como el HUD).</summary>
public partial class App
{
    private static ClockWindow? _clockWin;
    private static DispatcherTimer? _clockTimer;
    private static HudSensors? _clockSensors;
    private static Thread? _clockSensorThread;
    private static volatile bool _clockSensorRun;
    public static bool ClockRunning => _clockTimer is not null;
    public static event Action? ClockTick;
    public static string ClockPreviewTop = "", ClockPreviewBottom = "";

    /// <summary>Arranca o para el reloj segun los ajustes.</summary>
    public static void ApplyClock()
    {
        if (Shared.ClockEnabled && !_gpuPaused) StartClock(); else StopClock(restoreNative: true);
    }

    /// <summary>Al abrir RNX: si el reloj de Windows quedo oculto (p. ej. cierre inesperado) y el de RNX esta apagado, se repone.</summary>
    public static void RepairNativeClock()
    {
        if (!Shared.ClockEnabled && Shared.ClockHidNative)
        {
            try { TaskbarClock.SetNativeVisible(true); } catch { }
            Shared.ClockHidNative = false;
            Shared.Save();
        }
    }

    private static void StartClock()
    {
        if (_clockTimer is not null) { RenderClock(true); return; }
        if (TaskbarClock.NativeVisible())
        {
            try { TaskbarClock.SetNativeVisible(false); Shared.ClockHidNative = true; Shared.Save(); } catch (Exception ex) { Logger.Detail("CLOCK: " + ex.Message); }
        }
        TaskbarClock.SampleSystem();
        _clockWin = new ClockWindow();
        _clockWin.Show();
        int tick = 0;
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => RenderClock(++tick % 3 == 1);
        _clockTimer.Start();
        RenderClock(true);
        Logger.Info(T("lg.clock.on"));
    }

    private static void StopClock(bool restoreNative)
    {
        _clockTimer?.Stop();
        _clockTimer = null;
        _clockWin?.Close();
        _clockWin = null;
        StopClockSensors();
        if (restoreNative && Shared.ClockHidNative)
        {
            try { TaskbarClock.SetNativeVisible(true); } catch { }
            Shared.ClockHidNative = false;
            Shared.Save();
            Logger.Info(T("lg.clock.off"));
        }
        ClockTick?.Invoke();
    }

    /// <summary>Al salir de RNX el reloj de Windows vuelve siempre (al abrir RNX se vuelve a ocultar si esta activado).</summary>
    private static void StopClockForExit() => StopClock(restoreNative: true);

    // ---- Sensores de GPU/temperaturas: los del HUD si esta en marcha; si no, propios y solo si hacen falta
    private static HudSensors? ClockSensors(bool needed)
    {
        if (!needed) { StopClockSensors(); return null; }
        if (_sensors is { } hud) return hud;
        if (_clockSensorThread is null && !_gpuPaused)
        {
            _clockSensorRun = true;
            _clockSensorThread = new Thread(() =>
            {
                try
                {
                    _clockSensors = new HudSensors();
                    while (_clockSensorRun) { _clockSensors.Update(); Thread.Sleep(2000); }
                }
                catch (Exception ex) { Logger.Detail("CLOCK sensores: " + ex.Message); }
                finally { _clockSensors?.Dispose(); _clockSensors = null; }
            }) { IsBackground = true, Name = "RNX reloj sensores" };
            _clockSensorThread.Start();
        }
        return _clockSensors;
    }

    private static void StopClockSensors()
    {
        _clockSensorRun = false;
        var t = _clockSensorThread;
        _clockSensorThread = null;
        if (t is not null && t != Thread.CurrentThread) Task.Run(() => t.Join(TimeSpan.FromSeconds(5)));
    }

    private static Brush ClockBrush()
    {
        if (Shared.ClockColor.Length == 7 && Shared.ClockColor[0] == '#')
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Shared.ClockColor)); } catch { }
        // Automatico: el color de texto de la barra segun el tema de Windows
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        bool light = k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        return light ? Brushes.Black : Brushes.White;
    }

    public static (string Top, string Bottom, string Tip) ClockText(Settings s, HudSensors? sensors)
    {
        var culture = Loc.Culture;
        var now = DateTime.Now;
        string F(string t) => TaskbarClock.Format(t, s.ClockTimeFormat, s.ClockDateFormat, culture, sensors, now);
        var tip = culture.TextInfo.ToTitleCase(now.ToString("D", culture)) + "\n" + now.ToString("T", culture);
        return (F(s.ClockTop), F(s.ClockBottom), tip);
    }

    private static void RenderClock(bool relocate)
    {
        if (_clockWin is null) return;
        TaskbarClock.SampleSystem();
        var sensors = ClockSensors(TaskbarClock.NeedsSensors(Shared.ClockTop + Shared.ClockBottom));
        var (top, bottom, tip) = ClockText(Shared, sensors);
        (ClockPreviewTop, ClockPreviewBottom) = (top, bottom);
        _clockWin.SetText(top, bottom, Shared.ClockFont, Shared.ClockSize, Shared.ClockBold, ClockBrush(), tip);

        if (relocate)
        {
            // Oculto en juegos a pantalla completa en el monitor principal
            if (FullscreenOnPrimary()) { if (_clockWin.IsVisible) _clockWin.Hide(); ClockTick?.Invoke(); return; }
            if (!_clockWin.IsVisible) _clockWin.Show();
            if (TaskbarClock.Locate() is { } loc && loc.Taskbar.H < 200)
            {
                _clockWin.UpdateLayout();
                _clockWin.Place(loc.TrayLeft, loc.Taskbar.Y, loc.Taskbar.H);
            }
            _clockWin.KeepOnTop();
        }
        ClockTick?.Invoke();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECTC r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
    [StructLayout(LayoutKind.Sequential)] private struct RECTC { public int L, T, R, B; }

    private static bool FullscreenOnPrimary()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == GetDesktopWindow() || fg == GetShellWindow()) return false;
        var sb = new System.Text.StringBuilder(64);
        GetClassName(fg, sb, 64);
        if (sb.ToString() is "WorkerW" or "Progman" or "Shell_TrayWnd") return false;
        if (!GetWindowRect(fg, out var r)) return false;
        var scr = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        return r.L <= scr.Left && r.T <= scr.Top && r.R >= scr.Right && r.B >= scr.Bottom;
    }
}
