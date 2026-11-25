using System.Threading;
using System.Windows;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>
/// Segundo plano: icono en la bandeja + observador de color (vibrance).
/// Con el color activado, cerrar la ventana la oculta en la bandeja.
/// </summary>
public partial class App
{
    /// <summary>Ajustes compartidos por la ventana y el observador.</summary>
    public static Settings Shared { get; } = Settings.Load();

    public static VibranceEngine? Vibrance { get; private set; }
    public static bool Exiting { get; private set; }
    /// <summary>Como vibranceGUI: cerrar la ventana siempre la deja en la bandeja.</summary>
    public static bool KeepInTray => true;

    public const string TrayFlag = "/bandeja";
    private const string MutexName = @"Local\RNX_Cache_Cleaner_UI";
    private const string ShowEvent = @"Local\RNX_Cache_Cleaner_Show";

    private static System.Windows.Forms.NotifyIcon? _tray;
    private static Mutex? _single;

    /// <summary>false si ya hay otra RNX abierta (a la que se le pide que se muestre).</summary>
    internal static bool ClaimSingleInstance()
    {
        // Permisos explicitos para el usuario actual: la RNX que arranca con Windows (tarea programada) y la que se
        // abre con el acceso directo tienen tokens distintos; con los permisos por defecto no se veian entre si.
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        bool created;
        try
        {
            var ms = new System.Security.AccessControl.MutexSecurity();
            ms.AddAccessRule(new System.Security.AccessControl.MutexAccessRule(me, System.Security.AccessControl.MutexRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            _single = System.Threading.MutexAcl.Create(true, MutexName, out created, ms);
        }
        catch (UnauthorizedAccessException) { created = false; }   // existe pero creada con otros permisos: hay otra RNX
        if (!created)
        {
            AllowSetForegroundWindow(-1);   // la RNX ya abierta puede ponerse delante
            try { EventWaitHandle.OpenExisting(ShowEvent).Set(); } catch { }
            return false;
        }
        var es = new System.Security.AccessControl.EventWaitHandleSecurity();
        es.AddAccessRule(new System.Security.AccessControl.EventWaitHandleAccessRule(me, System.Security.AccessControl.EventWaitHandleRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        var ev = System.Threading.EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, ShowEvent, out _, es);
        new Thread(() =>
        {
            while (ev.WaitOne()) Current?.Dispatcher.BeginInvoke(ShowMain);
        }) { IsBackground = true }.Start();
        return true;
    }

    public static void StartBackground()
    {
        // Digital Vibrance siempre activo mientras RNX este abierta
        StartVibrance();
        ApplyHud();
        if (!SnapMode)
        {
            _ = Task.Run(NvidiaDriver.CleanWork);
            WatchNvidiaDriver();
        }
    }

    // ---- Aviso de driver nuevo de NVIDIA (solo dentro de la app): al abrir y cada 12 h mientras RNX siga abierta
    public static NvidiaDriver.UpdateInfo? NvUpdate { get; private set; }
    public static event Action? NvUpdateChanged;

    private static async void WatchNvidiaDriver()
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        while (!Exiting)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                var info = await Task.Run(() => NvidiaDriver.CheckAsync(cts.Token));
                SetNvUpdate(info);
                if (info is not null) Logger.Detail(T("lg.nvd.available", info.Latest.Version, info.Gpu.InstalledVersion));
            }
            catch (Exception ex) { Logger.Detail("NVIDIA check: " + ex.Message); }
            await Task.Delay(TimeSpan.FromHours(12));
        }
    }

    /// <summary>Tambien lo llama la pestana del driver al consultar o instalar.</summary>
    public static void SetNvUpdate(NvidiaDriver.UpdateInfo? info)
    {
        NvUpdate = info;
        NvUpdateChanged?.Invoke();
    }

    public static void StartVibrance()
    {
        if (_gpuPaused) return;
        Vibrance ??= VibranceEngine.Create(Shared);
        if (Vibrance is not null)
        {
            Vibrance.Changed -= UpdateTray;
            Vibrance.Changed += UpdateTray;
            Vibrance.Start();
        }
        UpdateTray();
    }

    public static void StopVibrance()
    {
        Vibrance?.Stop();
        UpdateTray();
    }

    private static void UpdateTray()
    {
        if (!KeepInTray)
        {
            if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            return;
        }
        if (_tray is null)
        {
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
                Visible = true,
            };
            _tray.DoubleClick += (_, _) => ShowMain();
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(T("tray.open"), null, (_, _) => ShowMain());
            menu.Items.Add(T("tray.exit"), null, (_, _) => ExitApp());
            _tray.ContextMenuStrip = menu;
        }
        var game = Vibrance?.ActiveGame;
        var text = "RNX Cache Cleaner" + (game is not null ? " · " + game : Vibrance?.Running == true ? " · " + T("tray.watching") : "");
        _tray.Text = text.Length > 63 ? text[..63] : text;
        if (_tray.ContextMenuStrip is { } m && m.Items.Count == 2) { m.Items[0].Text = T("tray.open"); m.Items[1].Text = T("tray.exit"); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    public static void ShowMain()
    {
        if (Current?.MainWindow is not { } w) return;
        w.Show();
        bool wasMin = w.WindowState == WindowState.Minimized;
        if (wasMin) w.WindowState = WindowState.Normal;
        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (h != IntPtr.Zero) { ShowWindow(h, wasMin ? 9 /* SW_RESTORE */ : 5 /* SW_SHOW: respeta maximizada */); SetForegroundWindow(h); }
        w.Activate();
        w.Topmost = true; w.Topmost = false;
    }

    public static void HideToTray(Window w)
    {
        w.Hide();
        UpdateTray();
        if (!Shared.TrayHintShown && _tray is not null)
        {
            _tray.ShowBalloonTip(5000, "RNX Cache Cleaner", T("tray.hint"), System.Windows.Forms.ToolTipIcon.Info);
            Shared.TrayHintShown = true;
            Shared.Save();
        }
    }

    public static void ExitApp()
    {
        Exiting = true;
        Current?.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // El color y la resolucion vuelven a los de Windows al salir (o al actualizar)
        try { Vibrance?.Dispose(); } catch { }
        try { NvidiaDriver.CleanWork(); } catch { }
        try { Shared.HudEnabled = Shared.HudEnabled; StopHudForExit(); } catch { }
        try { if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        base.OnExit(e);
    }
}
