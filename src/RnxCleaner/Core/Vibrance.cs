using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>Un juego con su color y (opcional) su resolucion.</summary>
public sealed class VibranceProfile
{
    public string Name { get; set; } = "";       // nombre del proceso sin .exe (como vibranceGUI)
    public string Path { get; set; } = "";
    public int Percent { get; set; } = 80;      // NVIDIA 50..100 · AMD 0..300
    public bool ChangeResolution { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Hz { get; set; }
}

/// <summary>Acceso al "Digital Vibrance" del driver (NVIDIA: NVAPI · AMD: ADL saturacion).</summary>
public interface IVibranceDriver : IDisposable
{
    string Vendor { get; }
    int MinPercent { get; }
    int MaxPercent { get; }
    int DefaultPercent { get; }
    /// <summary>Aplica el % en todas las pantallas o solo en la principal.</summary>
    bool Set(int percent, bool primaryOnly);
    int? Get();
}

// ----------------------------------------------------------------------
//  NVIDIA (nvapi64.dll)
// ----------------------------------------------------------------------
public sealed class NvidiaVibrance : IVibranceDriver
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitD();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumD(int index, out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AssocD([MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDvcD(IntPtr handle, uint output, ref DvcInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetDvcD(IntPtr handle, uint output, int level);

    [StructLayout(LayoutKind.Sequential)]
    private struct DvcInfo { public uint Version; public int Current; public int Min; public int Max; }

    private readonly EnumD _enum;
    private readonly AssocD? _assoc;
    private readonly GetDvcD _get;
    private readonly SetDvcD _set;

    public string Vendor => "NVIDIA";
    public int MinPercent => 50;
    public int MaxPercent => 100;
    public int DefaultPercent => 50;

    private NvidiaVibrance()
    {
        T F<T>(uint id) where T : Delegate
        {
            var p = QueryInterface(id);
            if (p == IntPtr.Zero) throw new EntryPointNotFoundException($"NVAPI 0x{id:X8}");
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }
        if (F<InitD>(0x0150E828)() != 0) throw new InvalidOperationException("NvAPI_Initialize");
        _enum = F<EnumD>(0x9ABDD40D);
        _get = F<GetDvcD>(0x4085DE45);
        _set = F<SetDvcD>(0x172409B4);
        var a = QueryInterface(0x35C29134);
        _assoc = a == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<AssocD>(a);
    }

    public static NvidiaVibrance? TryCreate()
    {
        try { return new NvidiaVibrance(); } catch { return null; }
    }

    /// <summary>% de la interfaz (50..100) -> nivel del driver (0..63).</summary>
    public static int ToLevel(int percent) => (int)Math.Round((Math.Clamp(percent, 50, 100) - 50) * 63 / 50.0);
    public static int ToPercent(int level) => (int)Math.Round(50 + Math.Clamp(level, 0, 63) * 50 / 63.0);

    private List<IntPtr> Handles(bool primaryOnly)
    {
        if (primaryOnly && _assoc is not null)
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
            if (primary is not null && _assoc(primary, out var h) == 0) return [h];
        }
        var list = new List<IntPtr>();
        for (int i = 0; i < 16 && _enum(i, out var h) == 0; i++) list.Add(h);
        return list;
    }

    public bool Set(int percent, bool primaryOnly)
    {
        int level = ToLevel(percent);
        bool ok = true;
        foreach (var h in Handles(primaryOnly)) ok &= _set(h, 0, level) == 0;
        return ok;
    }

    public int? Get()
    {
        var hs = Handles(false);
        if (hs.Count == 0) return null;
        var info = new DvcInfo { Version = (uint)(Marshal.SizeOf<DvcInfo>() | (1 << 16)) };
        return _get(hs[0], 0, ref info) == 0 ? ToPercent(info.Current) : null;
    }

    public void Dispose() { }
}

// ----------------------------------------------------------------------
//  AMD (atiadlxx.dll, saturacion de color; como vibranceGUI: 0..300, normal 100)
// ----------------------------------------------------------------------
public sealed class AmdVibrance : IVibranceDriver
{
    private const string Adl = "atiadlxx.dll";
    private const int ColorSaturation = 4;        // ADL_DISPLAY_COLOR_SATURATION
    private const int AdapterInfoSize = 1572;     // sizeof(AdapterInfo)
    private const int DisplayInfoSize = 552;      // sizeof(ADLDisplayInfo)

    private delegate IntPtr AllocD(int size);
    private static readonly AllocD Alloc = size => Marshal.AllocCoTaskMem(size);

    [DllImport(Adl)] private static extern int ADL_Main_Control_Create(AllocD cb, int connected);
    [DllImport(Adl)] private static extern int ADL_Main_Control_Destroy();
    [DllImport(Adl)] private static extern int ADL_Adapter_NumberOfAdapters_Get(ref int n);
    [DllImport(Adl)] private static extern int ADL_Adapter_AdapterInfo_Get(IntPtr info, int size);
    [DllImport(Adl)] private static extern int ADL_Adapter_Active_Get(int adapter, ref int active);
    [DllImport(Adl)] private static extern int ADL_Display_DisplayInfo_Get(int adapter, ref int count, out IntPtr info, int force);
    [DllImport(Adl)] private static extern int ADL_Display_Color_Get(int adapter, int display, int type, ref int cur, ref int def, ref int min, ref int max, ref int step);
    [DllImport(Adl)] private static extern int ADL_Display_Color_Set(int adapter, int display, int type, int value);

    private readonly List<(int Adapter, int Display, string Device)> _displays = [];

    public string Vendor => "AMD";
    public int MinPercent => 0;
    public int MaxPercent => 300;
    public int DefaultPercent => 100;

    private AmdVibrance()
    {
        if (ADL_Main_Control_Create(Alloc, 1) != 0) throw new InvalidOperationException("ADL_Main_Control_Create");
        int n = 0;
        if (ADL_Adapter_NumberOfAdapters_Get(ref n) != 0 || n <= 0) return;
        var buf = Marshal.AllocCoTaskMem(AdapterInfoSize * n);
        try
        {
            for (int i = 0; i < n; i++) Marshal.WriteInt32(buf, i * AdapterInfoSize, AdapterInfoSize);
            if (ADL_Adapter_AdapterInfo_Get(buf, AdapterInfoSize * n) != 0) return;
            for (int i = 0; i < n; i++)
            {
                var b = buf + i * AdapterInfoSize;
                int adapter = Marshal.ReadInt32(b, 4);
                var device = Marshal.PtrToStringAnsi(b + 536) ?? "";
                int active = 0;
                if (ADL_Adapter_Active_Get(adapter, ref active) != 0 || active == 0) continue;
                int count = 0;
                if (ADL_Display_DisplayInfo_Get(adapter, ref count, out var di, 0) != 0 || di == IntPtr.Zero) continue;
                for (int d = 0; d < count; d++)
                {
                    var p = di + d * DisplayInfoSize;
                    int logical = Marshal.ReadInt32(p, 0), logicalAdapter = Marshal.ReadInt32(p, 8);
                    int value = Marshal.ReadInt32(p, 548);   // iDisplayInfoValue: 0x1 conectada, 0x2 en uso
                    if (logicalAdapter != -1 && (value & 0x3) == 0x3 && !_displays.Any(x => x.Adapter == adapter && x.Display == logical))
                        _displays.Add((adapter, logical, device));
                }
                Marshal.FreeCoTaskMem(di);
            }
        }
        finally { Marshal.FreeCoTaskMem(buf); }
    }

    public static AmdVibrance? TryCreate()
    {
        try { var a = new AmdVibrance(); if (a._displays.Count > 0) return a; a.Dispose(); } catch { }
        return null;
    }

    public bool Set(int percent, bool primaryOnly)
    {
        var primary = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
        bool ok = true;
        foreach (var d in _displays.Where(d => !primaryOnly || primary is null || d.Device == primary))
            ok &= ADL_Display_Color_Set(d.Adapter, d.Display, ColorSaturation, Math.Clamp(percent, 0, 300)) == 0;
        return ok;
    }

    public int? Get()
    {
        if (_displays.Count == 0) return null;
        int cur = 0, def = 0, min = 0, max = 0, step = 0;
        var d = _displays[0];
        return ADL_Display_Color_Get(d.Adapter, d.Display, ColorSaturation, ref cur, ref def, ref min, ref max, ref step) == 0 ? cur : null;
    }

    public void Dispose() { try { ADL_Main_Control_Destroy(); } catch { } }
}

// ----------------------------------------------------------------------
//  Observador: cambia color/resolucion al entrar y salir de cada juego
// ----------------------------------------------------------------------
public sealed class VibranceEngine : IDisposable
{
    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    private const uint EventForeground = 0x0003, EventMinimizeEnd = 0x0017;

    private readonly IVibranceDriver _driver;
    private readonly Settings _s;
    private readonly WinEventProc _proc;   // referencia viva para que el GC no la recoja
    private IntPtr _hook;
    private string? _active;               // perfil aplicado ahora mismo
    private bool _resChanged;

    public IVibranceDriver Driver => _driver;
    public bool Running => _hook != IntPtr.Zero;
    public string? ActiveGame => _active;
    public event Action? Changed;

    private VibranceEngine(IVibranceDriver driver, Settings s)
    {
        _driver = driver;
        _s = s;
        _proc = OnEvent;
    }

    public static IVibranceDriver? DetectDriver() => (IVibranceDriver?)NvidiaVibrance.TryCreate() ?? AmdVibrance.TryCreate();

    public static VibranceEngine? Create(Settings s) => DetectDriver() is { } d ? new VibranceEngine(d, s) : null;

    /// <summary>Hay que llamarlo desde un hilo con bucle de mensajes (el de la interfaz).</summary>
    public void Start()
    {
        if (Running) return;
        _hook = SetWinEventHook(EventForeground, EventMinimizeEnd, IntPtr.Zero, _proc, 0, 0, 0);
        Logger.Detail(T("lg.vib.started", _driver.Vendor));
        Evaluate(GetForegroundWindow());
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        Leave();
        Changed?.Invoke();
    }

    /// <summary>Vuelve a evaluar la ventana activa (p. ej. tras cambiar un perfil).</summary>
    public void Refresh() { if (Running) { _active = null; Evaluate(GetForegroundWindow()); } }

    private void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        if (hwnd != IntPtr.Zero && hwnd == GetForegroundWindow()) Evaluate(hwnd);
    }

    private void Evaluate(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId) return;   // nuestra propia ventana no cuenta
            string name;
            try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch { return; }
            var prof = _s.VibranceProfiles.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (prof is null) { Leave(); return; }
            if (_active == prof.Name) return;
            _active = prof.Name;
            _driver.Set(prof.Percent, _s.VibrancePrimaryOnly);
            if (prof.ChangeResolution && prof.Width > 0 && Display.Change(prof.Width, prof.Height, prof.Hz)) _resChanged = true;
            Logger.Detail(T("lg.vib.game", prof.Name, prof.Percent));
            Changed?.Invoke();
        }
        catch (Exception ex) { Logger.Detail("VIBRANCE: " + ex.Message); }
    }

    private void Leave()
    {
        if (_active is null && !_resChanged) return;
        _driver.Set(_s.VibranceDefault, _s.VibrancePrimaryOnly);
        if (_resChanged) { Display.Restore(); _resChanged = false; }
        _active = null;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _driver.Dispose();
    }

    // ------------------------------------------------------------------
    //  Resolucion temporal (no se guarda: Windows vuelve a la suya al restaurar)
    // ------------------------------------------------------------------
    private static class Display
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string? dev, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string? dev, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string? dev, IntPtr dm, IntPtr hwnd, int flags, IntPtr param);

        private const int DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;

        public static bool Change(int w, int h, int hz)
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(null, -1, ref dm)) return false;
            if (dm.dmPelsWidth == w && dm.dmPelsHeight == h && (hz <= 0 || dm.dmDisplayFrequency == hz)) return false;
            dm.dmPelsWidth = w; dm.dmPelsHeight = h;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
            if (hz > 0) { dm.dmDisplayFrequency = hz; dm.dmFields |= DM_DISPLAYFREQUENCY; }
            return ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, 0, IntPtr.Zero) == 0;   // dinamico, sin guardar
        }

        public static void Restore() => ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
    }

    // ------------------------------------------------------------------
    //  Importar de vibranceGUI (%APPDATA%\vibranceGUI)
    // ------------------------------------------------------------------
    public static string VibranceGuiDir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vibranceGUI");
    public static bool VibranceGuiRunning => Process.GetProcessesByName("vibranceGUI").Length > 0;

    public static (List<VibranceProfile> Profiles, int? WindowsPercent, bool? PrimaryOnly) ImportVibranceGui(bool nvidia)
    {
        var list = new List<VibranceProfile>();
        var xml = System.IO.Path.Combine(VibranceGuiDir, "applicationData.xml");
        if (File.Exists(xml))
            foreach (var a in XDocument.Load(xml).Descendants("ApplicationSetting"))
            {
                int.TryParse(a.Element("IngameLevel")?.Value, out var level);
                var r = a.Element("ResolutionSettings");
                list.Add(new VibranceProfile
                {
                    Name = a.Element("Name")?.Value ?? "",
                    Path = a.Element("FileName")?.Value ?? "",
                    Percent = nvidia ? NvidiaVibrance.ToPercent(level) : level,
                    ChangeResolution = a.Element("IsResolutionChangeNeeded")?.Value == "true",
                    Width = int.TryParse(r?.Element("DmPelsWidth")?.Value, out var w) ? w : 0,
                    Height = int.TryParse(r?.Element("DmPelsHeight")?.Value, out var h) ? h : 0,
                    Hz = int.TryParse(r?.Element("DmDisplayFrequency")?.Value, out var z) ? z : 0,
                });
            }
        int? winPct = null; bool? primary = null;
        var ini = System.IO.Path.Combine(VibranceGuiDir, "vibranceGUI.ini");
        if (File.Exists(ini))
            foreach (var line in File.ReadAllLines(ini))
            {
                var kv = line.Split('=', 2);
                if (kv.Length != 2) continue;
                if (kv[0] == "inactiveValue" && int.TryParse(kv[1], out var v)) winPct = nvidia ? NvidiaVibrance.ToPercent(v) : v;
                if (kv[0] == "affectPrimaryMonitorOnly") primary = kv[1].Equals("True", StringComparison.OrdinalIgnoreCase);
            }
        return (list.Where(p => p.Name.Length > 0).ToList(), winPct, primary);
    }
}
