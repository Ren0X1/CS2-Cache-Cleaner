using System.Diagnostics;
using System.Management;
using LibreHardwareMonitor.Hardware;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace RnxCleaner.Core;

/// <summary>Lo que pinta el HUD en cada refresco.</summary>
public sealed record HudData(
    string CpuName, float? CpuLoad, float? CpuClock, float? CpuTemp,
    string RamName, float? RamUsedMb, float RamTotalMb,
    string GpuName, float? GpuLoad, float? GpuClock, float? GpuTemp, float? VramUsedMb, float? VramTotalMb,
    string Process, double Fps, double AvgFps, double Low1, double Low01, double FrameMs, float[] FrameTimes);

// ----------------------------------------------------------------------
//  Sensores (LibreHardwareMonitor)
// ----------------------------------------------------------------------
public sealed class HudSensors : IDisposable
{
    private readonly Computer _pc = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
    public string CpuName { get; private set; } = "CPU";
    public string GpuName { get; private set; } = "GPU";
    public string RamName { get; private set; } = "RAM";

    public float? CpuLoad, CpuClock, CpuTemp, RamUsedMb, GpuLoad, GpuClock, GpuTemp, VramUsedMb, VramTotalMb;
    public float RamTotalMb;

    public HudSensors()
    {
        _pc.Open();
        RamName = DescribeRam();
    }

    /// <summary>Lee todos los sensores (tarda unas decenas de ms: llamar desde un hilo aparte).</summary>
    public void Update()
    {
        foreach (var hw in _pc.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();
            var s = hw.Sensors.Concat(hw.SubHardware.SelectMany(x => x.Sensors)).ToList();
            float? Find(SensorType t, params string[] names) =>
                names.Select(n => s.FirstOrDefault(x => x.SensorType == t && x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))?.Value)
                     .FirstOrDefault(v => v is not null);

            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    CpuName = ShortCpu(hw.Name);
                    CpuLoad = Find(SensorType.Load, "CPU Total");
                    CpuTemp = Find(SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core Max", "Core Average", "CPU Cores");
                    var clocks = s.Where(x => x.SensorType == SensorType.Clock && x.Name.StartsWith("Core #") && x.Value > 0).Select(x => x.Value!.Value).ToList();
                    CpuClock = clocks.Count > 0 ? clocks.Max() : null;
                    break;
                case HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel:
                    // Si hay varias, la que mas carga tiene (la integrada suele estar quieta)
                    var load = Find(SensorType.Load, "GPU Core", "D3D 3D");
                    if (GpuLoad is not null && GpuName != hw.Name && (load ?? 0) < GpuLoad) break;
                    GpuName = ShortGpu(hw.Name);
                    GpuLoad = load;
                    GpuTemp = Find(SensorType.Temperature, "GPU Core", "GPU Hot Spot");
                    GpuClock = Find(SensorType.Clock, "GPU Core");
                    VramUsedMb = Find(SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
                    VramTotalMb = Find(SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
                    break;
                case HardwareType.Memory:
                    var gb = Find(SensorType.Data, "Memory Used", "Used Memory");
                    if (gb is not null) RamUsedMb = gb * 1024;
                    var free = Find(SensorType.Data, "Memory Available", "Available Memory");
                    if (gb is not null && free is not null) RamTotalMb = (gb.Value + free.Value) * 1024;
                    break;
            }
        }
    }

    private static string ShortCpu(string n) =>
        n.Replace("(R)", "").Replace("(TM)", "").Replace("Intel Core ", "").Replace("AMD ", "").Replace(" Processor", "")
         .Replace("12-Core", "").Replace("8-Core", "").Replace("6-Core", "").Replace("  ", " ").Trim();

    private static string ShortGpu(string n) => n.Replace("NVIDIA ", "").Replace("AMD ", "").Replace("GeForce ", "").Trim();

    /// <summary>"32GB DDR4" (tipo por SMBIOS).</summary>
    private static string DescribeRam()
    {
        try
        {
            long total = 0; int type = 0;
            using var q = new ManagementObjectSearcher("SELECT Capacity, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            foreach (ManagementObject o in q.Get())
                using (o)
                {
                    total += Convert.ToInt64(o["Capacity"]);
                    type = Math.Max(type, Convert.ToInt32(o["SMBIOSMemoryType"] ?? 0));
                }
            var kind = type switch { 26 => "DDR4", 34 => "DDR5", 24 => "DDR3", 30 => "LPDDR4", 35 => "LPDDR5", _ => "RAM" };
            return $"{Math.Round(total / 1073741824.0)}GB {kind}";
        }
        catch { return "RAM"; }
    }

    public void Dispose() { try { _pc.Close(); } catch { } }
}

// ----------------------------------------------------------------------
//  FPS y frametime sin tocar el juego: eventos Present de Windows (ETW),
//  como PresentMon. Nada se inyecta en el proceso (seguro con anticheats).
// ----------------------------------------------------------------------
public sealed class FpsMonitor : IDisposable
{
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");   // Present_Start = 42
    private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");   // Present_Start = 1
    private const string SessionName = "RNX_HUD_Present";

    private readonly TraceEventSession _session;
    private readonly object _lock = new();
    private readonly Dictionary<int, Queue<double>> _presents = [];   // pid -> marcas de tiempo (ms)
    private readonly List<double> _frameTimes = [];                    // del proceso actual, para 1 % / 0,1 % y media
    private int _pid;
    private double _last;

    public FpsMonitor()
    {
        _session = new TraceEventSession(SessionName) { StopOnDispose = true };
        _session.EnableProvider(Dxgi, TraceEventLevel.Informational);
        _session.EnableProvider(D3D9, TraceEventLevel.Informational);
        _session.Source.AllEvents += OnEvent;
        new Thread(() => { try { _session.Source.Process(); } catch { } }) { IsBackground = true, Name = "RNX HUD ETW" }.Start();
    }

    private void OnEvent(TraceEvent e)
    {
        int id = (int)e.ID;
        if (!((e.ProviderGuid == Dxgi && id == 42) || (e.ProviderGuid == D3D9 && id == 1))) return;
        double t = e.TimeStampRelativeMSec;
        lock (_lock)
        {
            if (!_presents.TryGetValue(e.ProcessID, out var q)) _presents[e.ProcessID] = q = new Queue<double>();
            q.Enqueue(t);
            while (q.Count > 0 && t - q.Peek() > 1000) q.Dequeue();
            if (e.ProcessID == _pid)
            {
                if (_last > 0)
                {
                    _frameTimes.Add(t - _last);
                    if (_frameTimes.Count > 3000) _frameTimes.RemoveRange(0, _frameTimes.Count - 3000);
                }
                _last = t;
            }
        }
    }

    /// <summary>Procesos que estan presentando fotogramas ahora mismo (pid -> fps).</summary>
    public List<(int Pid, int Fps)> Presenters()
    {
        lock (_lock) return _presents.Where(kv => kv.Value.Count > 0).Select(kv => (kv.Key, kv.Value.Count)).OrderByDescending(x => x.Item2).ToList();
    }

    /// <summary>Proceso del que se miden los FPS (el de la ventana activa).</summary>
    public void Track(int pid)
    {
        lock (_lock)
        {
            if (pid == _pid) return;
            _pid = pid;
            _frameTimes.Clear();
            _last = 0;
        }
    }

    public (double Fps, double Avg, double Low1, double Low01, double FrameMs, float[] Graph) Read()
    {
        lock (_lock)
        {
            double fps = _presents.TryGetValue(_pid, out var q) ? q.Count : 0;
            if (_frameTimes.Count == 0) return (fps, 0, 0, 0, 0, []);
            var sorted = _frameTimes.OrderByDescending(x => x).ToList();
            double Low(double pct)
            {
                int n = Math.Max(1, (int)Math.Ceiling(sorted.Count * pct));
                return 1000.0 / sorted.Take(n).Average();
            }
            double avg = 1000.0 / _frameTimes.Average();
            var graph = _frameTimes.Skip(Math.Max(0, _frameTimes.Count - 240)).Select(x => (float)x).ToArray();
            return (fps, avg, Low(0.01), Low(0.001), _frameTimes[^1], graph);
        }
    }

    public void Dispose()
    {
        try { _session.Dispose(); } catch { }
    }

    public static string ProcessName(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; } catch { return ""; }
    }
}

/// <summary>
/// PawnIO (driver firmado que usa LibreHardwareMonitor para temperatura y MHz de la CPU).
/// Se descarga el instalador oficial, se comprueba su firma digital y se abre para que el usuario lo complete.
/// </summary>
public static class PawnIo
{
    public const string Url = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";

    public static async Task<string> DownloadAsync(CancellationToken ct)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RNX_PawnIO");
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "PawnIO_setup.exe");
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RNX-Cache-Cleaner");
        await using (var s = await http.GetStreamAsync(Url, ct))
        await using (var f = System.IO.File.Create(file))
            await s.CopyToAsync(f, ct);
        return file;
    }

    /// <summary>Firma Authenticode valida (devuelve el firmante) o null.</summary>
    public static async Task<string?> VerifyAsync(string file, CancellationToken ct)
    {
        var o = (await NetTweaks.PowerShellAsync($"$s = Get-AuthenticodeSignature -LiteralPath '{file.Replace("'", "''")}'; $s.Status.ToString() + '|' + $s.SignerCertificate.Subject", ct)).Trim();
        var parts = o.Split('|', 2);
        return parts.Length == 2 && parts[0] == "Valid" ? parts[1] : null;
    }

    /// <summary>Abre el instalador (RNX ya es administrador) y espera a que termine. Devuelve el codigo de salida.</summary>
    public static async Task<int> RunInstallerAsync(string file, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })!;
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }
}
