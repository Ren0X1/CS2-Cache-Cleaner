using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Microsoft.Win32;

namespace RnxCleaner.Core;

/// <summary>
/// Reloj de la barra de tareas de RNX (idea de "Taskbar Clock Customization" de Windhawk, implementacion propia y sin
/// inyectar nada): se oculta el reloj de Windows con su ajuste oficial y RNX dibuja el suyo junto a la bandeja.
/// </summary>
public static class TaskbarClock
{
    // ------------------------------------------------------------------
    //  Reloj nativo de Windows (ajuste oficial "Mostrar hora y fecha en la bandeja del sistema")
    // ------------------------------------------------------------------
    private const string AdvKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string HideValue = "ShowSystrayDateTimeValueName";

    public static bool NativeVisible()
    {
        using var k = Registry.CurrentUser.OpenSubKey(AdvKey);
        return k?.GetValue(HideValue) is not int v || v != 0;
    }

    public static void SetNativeVisible(bool visible)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(AdvKey, true))
            k.SetValue(HideValue, visible ? 1 : 0, RegistryValueKind.DWord);
        // Explorer lo aplica al momento al recibir el aviso de cambio de la bandeja
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "TraySettings", 0x0002, 1000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    // ------------------------------------------------------------------
    //  Donde esta la bandeja (para colocar el reloj a su izquierda)
    // ------------------------------------------------------------------
    public readonly record struct Rect(double X, double Y, double W, double H);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? name);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    /// <summary>Barra de tareas principal y borde izquierdo de los iconos de la bandeja, en pixeles fisicos.</summary>
    public static (Rect Taskbar, double TrayLeft)? Locate()
    {
        var h = FindWindow("Shell_TrayWnd", null);
        if (h == IntPtr.Zero || !GetWindowRect(h, out var r)) return null;
        var bar = new Rect(r.L, r.T, r.R - r.L, r.B - r.T);
        double trayLeft = r.R - 200;
        // Toda la bandeja (iconos de programas + sistema) es la ventana TrayNotifyWnd: su borde izquierdo es el limite
        var notify = FindWindowEx(h, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && GetWindowRect(notify, out var n) && n.R > n.L) trayLeft = n.L;
        else
            try
            {
                var el = AutomationElement.FromHandle(h);
                double min = double.MaxValue;
                foreach (var id in new[] { "NotifyItemIcon", "SystemTrayIcon" })
                    foreach (AutomationElement i in el.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id)))
                    {
                        var b = i.Current.BoundingRectangle;
                        if (b.Width > 0 && b.Height > 0) min = Math.Min(min, b.X);
                    }
                if (min < double.MaxValue) trayLeft = min;
            }
            catch { }
        return (bar, trayLeft);
    }

    // ------------------------------------------------------------------
    //  Metricas del sistema (baratas; las de GPU/temperaturas llegan de los sensores del HUD)
    // ------------------------------------------------------------------
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [StructLayout(LayoutKind.Sequential)] private struct MEMSTAT { public uint Len, Load; public ulong Total, Avail, TotalPage, AvailPage, TotalVirt, AvailVirt, ExtVirt; }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);

    private static long _idle, _kernel, _user, _netRx, _netTx;
    private static DateTime _netAt;
    public static double Cpu, RamPct, RamUsedGb, RamTotalGb, Down, Up;

    public static void SampleSystem()
    {
        if (GetSystemTimes(out var i, out var k, out var u))
        {
            long di = i - _idle, dt = (k - _kernel) + (u - _user);
            if (_kernel != 0 && dt > 0) Cpu = Math.Clamp(100.0 * (dt - di) / dt, 0, 100);
            (_idle, _kernel, _user) = (i, k, u);
        }
        var m = new MEMSTAT { Len = (uint)Marshal.SizeOf<MEMSTAT>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            RamPct = m.Load;
            RamTotalGb = m.Total / 1073741824.0;
            RamUsedGb = (m.Total - m.Avail) / 1073741824.0;
        }
        try
        {
            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                {
                    var s = ni.GetIPStatistics();
                    rx += s.BytesReceived; tx += s.BytesSent;
                }
            var now = DateTime.UtcNow;
            var secs = (now - _netAt).TotalSeconds;
            if (_netAt != default && secs > 0.2) { Down = Math.Max(0, (rx - _netRx) / secs); Up = Math.Max(0, (tx - _netTx) / secs); }
            (_netRx, _netTx, _netAt) = (rx, tx, now);
        }
        catch { }
    }

    // ------------------------------------------------------------------
    //  Variables (mismo estilo que el mod de Windhawk)
    // ------------------------------------------------------------------
    public static readonly string[] Variables =
    [
        "%time%", "%date%", "%weekday%", "%weeknum%", "%dayofyear%",
        "%cpu%", "%cpu_temp%", "%gpu%", "%gpu_temp%", "%vram%", "%ram%", "%ram_used%",
        "%download%", "%upload%",
    ];

    /// <summary>true si el texto usa sensores de GPU/temperatura (hay que arrancar LibreHardwareMonitor).</summary>
    public static bool NeedsSensors(string text) => Regex.IsMatch(text, "%(cpu_temp|gpu|gpu_temp|vram)%");

    public static string Speed(double bps) => bps >= 1048576 ? $"{bps / 1048576:0.0} MB/s" : $"{bps / 1024:0} KB/s";

    public static string Format(string template, string timeFmt, string dateFmt, CultureInfo culture, HudSensors? s, DateTime now)
    {
        if (string.IsNullOrEmpty(template)) return "";
        string Safe(Func<string> f) { try { return f(); } catch { return "?"; } }
        string Na(float? v, string fmt) => v is { } x ? x.ToString(fmt, culture) : "-";
        return Regex.Replace(template, "%[a-z_]+%", m => m.Value switch
        {
            "%time%" => Safe(() => now.ToString(timeFmt, culture)),
            "%date%" => Safe(() => now.ToString(dateFmt, culture)),
            "%weekday%" => culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetDayName(now.DayOfWeek)),
            "%weeknum%" => ISOWeek.GetWeekOfYear(now).ToString(),
            "%dayofyear%" => now.DayOfYear.ToString(),
            "%cpu%" => $"{Cpu:0}%",
            "%cpu_temp%" => Na(s?.CpuTemp, "0") + "°",
            "%gpu%" => Na(s?.GpuLoad, "0") + "%",
            "%gpu_temp%" => Na(s?.GpuTemp, "0") + "°",
            "%vram%" => s?.VramUsedMb is { } u ? $"{u / 1024:0.0} GB" : "-",
            "%ram%" => $"{RamPct:0}%",
            "%ram_used%" => $"{RamUsedGb:0.0}/{RamTotalGb:0} GB",
            "%download%" => Speed(Down),
            "%upload%" => Speed(Up),
            _ => m.Value,
        });
    }

    public sealed record Preset(string Id, string Top, string Bottom, string Time, string Date);

    public static readonly Preset[] Presets =
    [
        new("classic", "%time%", "%date%", "HH:mm", "dd/MM/yyyy"),
        new("seconds", "%time%", "%weekday% %date%", "HH:mm:ss", "dd/MM"),
        new("perf", "%time%  ·  CPU %cpu%", "GPU %gpu%  ·  %gpu_temp%", "HH:mm", "dd/MM"),
        new("temps", "%time%", "CPU %cpu_temp%  ·  GPU %gpu_temp%", "HH:mm", "dd/MM"),
        new("net", "%time%", "↓ %download%  ↑ %upload%", "HH:mm", "dd/MM"),
    ];
}
