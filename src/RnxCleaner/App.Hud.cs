using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>
/// HUD de rendimiento: sensores (LibreHardwareMonitor) en un hilo aparte, FPS del juego
/// en primer plano (ETW, sin inyectar nada), ventana superpuesta y tecla global.
/// </summary>
public partial class App
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private const int HotkeyId = 0x524E;   // "RN"
    private const uint MOD_NOREPEAT = 0x4000;

    private static HwndSource? _hotkeyWnd;
    private static HudWindow? _hudWin;
    private static HudSensors? _sensors;
    private static FpsMonitor? _fps;
    private static DispatcherTimer? _hudTimer;
    private static volatile bool _sensorRun;
    private static Thread? _sensorThread;
    private static int _gamePid;

    public static HudData? HudLast { get; private set; }
    public static bool HudRunning => _hudTimer is not null;
    public static bool HudCpuSensorsOk => _sensors?.CpuTemp is not null;
    public static event Action? HudTick;

    /// <summary>Arranca o para el HUD segun los ajustes y (re)registra la tecla.</summary>
    public static void ApplyHud()
    {
        if (_gpuPaused) return;
        if (Shared.HudEnabled) StartHud(); else StopHud();
        RegisterHudHotkey();
    }

    private static void StartHud()
    {
        if (_hudTimer is null)
        {
            try { _fps = new FpsMonitor(); } catch (Exception ex) { Logger.Detail("HUD FPS: " + ex.Message); }
            _sensorRun = true;
            _sensorThread = new Thread(() =>
            {
                try
                {
                    _sensors = new HudSensors();
                    while (_sensorRun) { _sensors.Update(); Thread.Sleep(1000); }
                }
                catch (Exception ex) { Logger.Detail("HUD sensores: " + ex.Message); }
                finally { _sensors?.Dispose(); _sensors = null; }
            }) { IsBackground = true, Name = "RNX HUD sensores" };
            _sensorThread.Start();

            _hudWin = new HudWindow();
            _hudWin.Moved += (x, y) => { Shared.HudCorner = "free"; Shared.HudX = x; Shared.HudY = y; Shared.Save(); HudTick?.Invoke(); };
            _hudTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            int ticks = 0;
            _hudTimer.Tick += (_, _) =>
            {
                TrackForeground();
                HudLast = Snapshot();
                HudHistory.Feed(HudLast, DateTime.Now);
                if (_hudWin is { } w)
                {
                    w.View.Render(HudLast, Shared);
                    w.SetCorner(Shared.HudCorner, Shared.HudX, Shared.HudY);
                    w.SetLocked(Shared.HudLocked);
                    if (Shared.HudVisible && !w.IsVisible) w.Show();
                    if (!Shared.HudVisible && w.IsVisible) w.Hide();
                    if (++ticks % 4 == 0 && w.IsVisible) w.KeepOnTop();
                }
                HudTick?.Invoke();
            };
            _hudTimer.Start();
            Logger.Info(T("lg.hud.on"));
        }
    }

    private static void StopHud()
    {
        if (_hudTimer is null) return;
        HudHistory.Flush();
        _hudTimer.Stop();
        _hudTimer = null;
        _sensorRun = false;
        try { _fps?.Dispose(); } catch { }
        _fps = null;
        _hudWin?.Close();
        _hudWin = null;
        HudLast = null;
        HudTick?.Invoke();
        Logger.Info(T("lg.hud.off"));
    }

    /// <summary>Los FPS se miden del programa de la ventana activa (el juego), no de RNX ni del escritorio.</summary>
    private static void TrackForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return;
        var name = FpsMonitor.ProcessName((int)pid);
        if (name is "explorer" or "ShellExperienceHost" or "SearchHost" or "StartMenuExperienceHost" or "") return;
        if (_gamePid != (int)pid) { _gamePid = (int)pid; _fps?.Track(_gamePid); }
    }

    private static HudData Snapshot()
    {
        var s = _sensors;
        var f = _fps?.Read() ?? (0, 0, 0, 0, 0, []);
        return new HudData(
            s?.CpuName ?? "CPU", s?.CpuLoad, s?.CpuClock, s?.CpuTemp,
            s?.RamName ?? "RAM", s?.RamUsedMb, s?.RamTotalMb ?? 0,
            s?.GpuName ?? "GPU", s?.GpuLoad, s?.GpuClock, s?.GpuTemp, s?.VramUsedMb, s?.VramTotalMb,
            FpsMonitor.ProcessName(_gamePid), f.Fps, f.Avg, f.Low1, f.Low01, f.FrameMs, f.Graph);
    }

    /// <summary>
    /// Antes de instalar un driver grafico: para el HUD y Digital Vibrance y espera a que el hilo
    /// de sensores salga, para que nada lea la GPU (NVML/NVAPI) mientras se sustituye el driver.
    /// </summary>
    public static async Task PauseGpuAsync()
    {
        _gpuPaused = true;
        if (Vibrance is { } v) { try { v.Dispose(); } catch { } Vibrance = null; }
        var t = _sensorThread;
        StopHud();
        if (t is not null) await Task.Run(() => t.Join(TimeSpan.FromSeconds(10)));
        _sensorThread = null;
        // El reloj sigue, pero sin leer la GPU mientras se cambia el driver
        var ct = _clockSensorThread;
        StopClockSensors();
        if (ct is not null) await Task.Run(() => ct.Join(TimeSpan.FromSeconds(10)));
    }

    /// <summary>Tras la instalacion: espera a que el driver nuevo este listo y lo vuelve a arrancar todo.</summary>
    public static async Task ResumeGpuAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        _gpuPaused = false;
        StartVibrance();
        ApplyHud();
    }

    private static bool _gpuPaused;

    /// <summary>--selftest-gpupause: arranca HUD y Vibrance, los pausa (como al instalar un driver) y los reanuda.</summary>
    internal static async Task GpuPauseTestAsync(string report)
    {
        var sb = new System.Text.StringBuilder();
        bool ok = true;
        void C(bool c, string w) { sb.AppendLine((c ? "OK   " : "FALLO ") + w); ok &= c; }
        await Current.Dispatcher.InvokeAsync(async () =>
        {
            bool hudWas = Shared.HudEnabled, visWas = Shared.HudVisible;
            Shared.HudEnabled = true; Shared.HudVisible = false;   // en memoria, no se guarda
            StartVibrance(); ApplyHud();
            await Task.Delay(3000);
            C(HudRunning && _sensorThread is { IsAlive: true } && _sensors is not null, "HUD y sensores en marcha");
            C(Vibrance is not null, "Vibrance en marcha");
            var t = _sensorThread;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await PauseGpuAsync();
            C(t is { IsAlive: false }, $"hilo de sensores terminado ({sw.ElapsedMilliseconds} ms)");
            C(!HudRunning && Vibrance is null && _sensors is null, "nada lee la GPU durante la pausa");
            ApplyHud(); StartVibrance();
            C(!HudRunning && Vibrance is null, "no se reactiva por error durante la pausa");
            await ResumeGpuAsync();
            await Task.Delay(2500);
            C(HudRunning && _sensors is not null && Vibrance is not null, "reanudado: HUD, sensores y Vibrance otra vez en marcha");
            var t2 = _sensorThread;
            Shared.HudEnabled = hudWas; Shared.HudVisible = visWas;
            Vibrance?.Dispose(); Vibrance = null; StopHud();
            if (t2 is not null) await Task.Run(() => t2.Join(5000));
        }).Task.Unwrap();
        sb.AppendLine(ok ? "RESULTADO: TODO OK" : "RESULTADO: HAY FALLOS");
        await System.IO.File.WriteAllTextAsync(report, sb.ToString());
    }

    /// <summary>Vuelve a abrir los sensores (p. ej. tras instalar PawnIO).</summary>
    public static void RestartHud()
    {
        if (!Shared.HudEnabled) return;
        StopHud();
        StartHud();
    }

    private static void StopHudForExit() { StopHud(); if (_hotkeyWnd is not null) { UnregisterHotKey(_hotkeyWnd.Handle, HotkeyId); _hotkeyWnd.Dispose(); } }

    // ------------------------------------------------------------------
    //  Tecla global para mostrar / ocultar
    // ------------------------------------------------------------------
    /// <summary>Registra la tecla del HUD. false si otra aplicacion ya la usa.</summary>
    public static bool RegisterHudHotkey()
    {
        if (_hotkeyWnd is null)
        {
            var p = new HwndSourceParameters("RNX HUD hotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };   // HWND_MESSAGE
            _hotkeyWnd = new HwndSource(p);
            _hotkeyWnd.AddHook(HotkeyHook);
        }
        UnregisterHotKey(_hotkeyWnd.Handle, HotkeyId);
        if (!Shared.HudEnabled || Shared.HudHotkeyVk == 0) return true;
        bool ok = RegisterHotKey(_hotkeyWnd.Handle, HotkeyId, (uint)Shared.HudHotkeyMods | MOD_NOREPEAT, (uint)Shared.HudHotkeyVk);
        if (!ok) Logger.Warn(T("lg.hud.keyBusy", HotkeyText(Shared.HudHotkeyMods, Shared.HudHotkeyVk)));
        return ok;
    }

    private static IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Shared.HudVisible = !Shared.HudVisible;
            Shared.Save();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public static string HotkeyText(int mods, int vk)
    {
        if (vk == 0) return "";
        var parts = new List<string>();
        if ((mods & 2) != 0) parts.Add("Ctrl");
        if ((mods & 4) != 0) parts.Add("Shift");
        if ((mods & 1) != 0) parts.Add("Alt");
        if ((mods & 8) != 0) parts.Add("Win");
        var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk).ToString();
        if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) key = key[1..];
        parts.Add(key);
        return string.Join(" + ", parts);
    }
}
