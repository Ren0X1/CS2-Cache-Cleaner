using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// "Drivers PC": drivers de todos los dispositivos (salvo NVIDIA, que tiene su propia pestana) desde fuentes
/// oficiales y firmadas (Windows Update y Catalogo de Microsoft Update, como OpenDriverUpdater) y componentes
/// de juego (Java, Visual C++, .NET, DirectX...) con winget, al estilo de Driver Booster.
/// </summary>
public static class PcDrivers
{
    public sealed record Device(string Name, string Class, string Version, DateTime Date, string Provider, string DeviceId, string[] HardwareIds);

    public enum Source { WindowsUpdate, Catalog }

    public sealed record DriverUpdate(string DeviceName, string Current, string NewVersion, DateTime NewDate, string Title, Source Source, string Id, long Size, string Class);

    public sealed record Component(string Name, string Id, string Current, string Available);

    public static string WorkDir => Path.Combine(AppPaths.Root, "pcdrivers");

    private static readonly HttpClient Http = CreateHttp();
    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        return h;
    }

    // ------------------------------------------------------------------
    //  NVIDIA queda fuera siempre (tiene su pestana)
    // ------------------------------------------------------------------
    public static bool IsNvidia(string? text) => text is not null &&
        (text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || text.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------
    //  Dispositivos con driver
    // ------------------------------------------------------------------
    private static readonly string[] SkipPrefixes = [@"ROOT\", @"SWD\", @"STORAGE\VOLUME", @"UMB\", @"HTREE\", @"COMPOSITEBUS\", @"VMS_", @"SW\"];

    public static List<Device> Devices()
    {
        var hwIds = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var q = new ManagementObjectSearcher("SELECT PNPDeviceID, HardwareID FROM Win32_PnPEntity");
            foreach (ManagementObject o in q.Get())
                using (o)
                    if (o["PNPDeviceID"] is string id && o["HardwareID"] is string[] ids) hwIds[id] = ids;
        }
        catch { }

        var list = new List<Device>();
        try
        {
            using var q = new ManagementObjectSearcher("SELECT DeviceName, DeviceClass, DriverVersion, DriverDate, DriverProviderName, Manufacturer, DeviceID, HardWareID FROM Win32_PnPSignedDriver");
            foreach (ManagementObject o in q.Get())
                using (o)
                {
                    var name = o["DeviceName"]?.ToString();
                    var id = o["DeviceID"]?.ToString() ?? "";
                    var ver = o["DriverVersion"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ver) || id.Length == 0) continue;
                    if (SkipPrefixes.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                    var provider = o["DriverProviderName"]?.ToString() ?? "";
                    var hw = o["HardWareID"]?.ToString() ?? "";
                    if (IsNvidia(provider) || IsNvidia(hw) || IsNvidia(o["Manufacturer"]?.ToString()) || IsNvidia(name)) continue;
                    DateTime date = default;
                    if (o["DriverDate"] is string d && d.Length >= 8)
                        try { date = ManagementDateTimeConverter.ToDateTime(d); } catch { }
                    var ids = hwIds.TryGetValue(id, out var a) ? a : hw.Length > 0 ? [hw] : [];
                    list.Add(new Device(name.Trim(), o["DeviceClass"]?.ToString() ?? "", ver, date, provider, id, ids));
                }
        }
        catch (Exception ex) { Logger.Detail("PCDRIVERS: " + ex.Message); }
        return list.GroupBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                   .OrderBy(d => d.Class).ThenBy(d => d.Name).ToList();
    }

    // ------------------------------------------------------------------
    //  Windows Update (lo que Windows considera aplicable a este PC)
    // ------------------------------------------------------------------
    private static dynamic NewSession() => Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")!)!;

    public static List<DriverUpdate> WindowsUpdate(IReadOnlyList<Device> devices)
    {
        var res = new List<DriverUpdate>();
        dynamic session = NewSession();
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic r = searcher.Search("IsInstalled=0 and Type='Driver'");
        foreach (dynamic u in r.Updates)
        {
            string title = u.Title ?? "", maker = "", hw = "", model = "", cls = "";
            try { maker = u.DriverManufacturer ?? ""; } catch { }
            try { hw = u.DriverHardwareID ?? ""; } catch { }
            try { model = u.DriverModel ?? ""; } catch { }
            try { cls = u.DriverClass ?? ""; } catch { }
            if (IsNvidia(title) || IsNvidia(maker) || IsNvidia(hw)) continue;
            DateTime date = default;
            try { date = (DateTime)u.DriverVerDate; } catch { }
            var m = Regex.Match(title, @"\(([\d.]+)\)\s*$");
            var dev = devices.FirstOrDefault(d => d.HardwareIds.Any(x => x.Equals(hw, StringComparison.OrdinalIgnoreCase)));
            long size = 0;
            try { size = (long)(decimal)u.MaxDownloadSize; } catch { }
            res.Add(new DriverUpdate(dev?.Name ?? (model.Length > 0 ? model : title), dev?.Version ?? "", m.Success ? m.Groups[1].Value : "", date,
                                     title, Source.WindowsUpdate, (string)u.Identity.UpdateID, size, dev?.Class ?? cls));
        }
        return res;
    }

    /// <summary>
    /// Descarga e instala por Windows Update los drivers indicados, de uno en uno (para poder mostrar el progreso).
    /// onStart(i, titulo) se llama antes de cada uno. Devuelve (instalados, reinicio necesario).
    /// </summary>
    public static (int Installed, bool Reboot) InstallWindowsUpdate(IEnumerable<string> updateIds, Action<int, string>? onStart = null)
    {
        var want = new HashSet<string>(updateIds, StringComparer.OrdinalIgnoreCase);
        dynamic session = NewSession();
        dynamic r = session.CreateUpdateSearcher().Search("IsInstalled=0 and Type='Driver'");
        var todo = new List<dynamic>();
        foreach (dynamic u in r.Updates)
            if (want.Contains((string)u.Identity.UpdateID)) todo.Add(u);
        int ok = 0; bool reboot = false;
        for (int i = 0; i < todo.Count; i++)
        {
            dynamic u = todo[i];
            string title = u.Title;
            onStart?.Invoke(i, title);
            try
            {
                try { if (!(bool)u.EulaAccepted) u.AcceptEula(); } catch { }
                dynamic coll = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;
                coll.Add(u);
                dynamic dl = session.CreateUpdateDownloader();
                dl.Updates = coll;
                dl.Download();
                dynamic inst = session.CreateUpdateInstaller();
                inst.Updates = coll;
                dynamic ir = inst.Install();
                int code = (int)ir.GetUpdateResult(0).ResultCode;   // 2 = correcto, 3 = correcto con errores
                if (code is 2 or 3) ok++;
                reboot |= (bool)ir.RebootRequired;
                Logger.Info(T("lg.pcd.wu", title, code is 2 or 3 ? "OK" : "code " + code));
            }
            catch (Exception ex) { Logger.Error(T("lg.pcd.wu", title, ex.Message)); }
        }
        return (ok, reboot);
    }

    // ------------------------------------------------------------------
    //  Catalogo de Microsoft Update (versiones mas nuevas por Hardware ID)
    // ------------------------------------------------------------------
    private static readonly string[] CatalogClasses = ["NET", "MEDIA", "BLUETOOTH", "SYSTEM", "HDC", "SCSIADAPTER", "USB", "DISPLAY", "SOFTWARECOMPONENT", "EXTENSION", "BIOMETRIC", "SENSOR", "CAMERA", "IMAGE"];

    private sealed record CatalogRow(string Id, string Title, string Products, DateTime Date, string Version, long Size);

    public static async Task<List<DriverUpdate>> CatalogAsync(IReadOnlyList<Device> devices, IProgress<double>? progress, CancellationToken ct)
    {
        var targets = devices.Where(d => CatalogClasses.Contains(d.Class, StringComparer.OrdinalIgnoreCase) && d.HardwareIds.Length > 0
                                         && !d.Provider.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)).ToList();
        var res = new List<DriverUpdate>();
        int done = 0;
        using var gate = new SemaphoreSlim(6);
        var tasks = targets.Select(async d =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var best = await BestForDeviceAsync(d, ct);
                if (best is not null)
                    lock (res) res.Add(new DriverUpdate(d.Name, d.Version, best.Version, best.Date, best.Title, Source.Catalog, best.Id, best.Size, d.Class));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Logger.Detail($"CATALOG {d.Name}: {ex.Message}"); }
            finally { gate.Release(); progress?.Report((double)Interlocked.Increment(ref done) / Math.Max(1, targets.Count)); }
        });
        await Task.WhenAll(tasks);
        return res;
    }

    private static IEnumerable<string> Queries(string[] hwIds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in hwIds)
        {
            if (id.Contains("&CC_", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var q in new[] { id, Cut(id, "&REV_"), Cut(id, "&SUBSYS_") })
                if (q.Length > 0 && seen.Add(q)) yield return q;
            if (seen.Count >= 4) yield break;
        }
        static string Cut(string s, string m) { var i = s.IndexOf(m, StringComparison.OrdinalIgnoreCase); return i > 0 ? s[..i] : ""; }
    }

    private static async Task<CatalogRow?> BestForDeviceAsync(Device d, CancellationToken ct)
    {
        if (!Version.TryParse(Norm(d.Version), out var cur)) return null;
        bool win11 = Environment.OSVersion.Version.Build >= 22000;
        foreach (var q in Queries(d.HardwareIds))
        {
            var html = await Http.GetStringAsync("https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(q), ct);
            var rows = Parse(html);
            if (rows.Count == 0) continue;
            return rows
                .Where(r => !Regex.IsMatch(r.Title + " " + r.Products, @"\b(x86|ARM64|ARM)\b", RegexOptions.IgnoreCase) || r.Title.Contains("x64", StringComparison.OrdinalIgnoreCase))
                .Where(r => win11 ? r.Products.Contains("Windows 11", StringComparison.OrdinalIgnoreCase) || r.Products.Contains("Vibranium", StringComparison.OrdinalIgnoreCase)
                                  : r.Products.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                .Where(r => Version.TryParse(Norm(r.Version), out var v) && v > cur && !IsNvidia(r.Title))
                .OrderByDescending(r => Version.Parse(Norm(r.Version))).ThenByDescending(r => r.Date)
                .FirstOrDefault();
        }
        return null;
    }

    private static string Norm(string v)
    {
        var p = v.Trim().Split('.').Where(x => x.Length > 0).Take(4).ToList();
        while (p.Count < 2) p.Add("0");
        return string.Join('.', p.Select(x => int.TryParse(x, out var n) ? n.ToString() : "0"));
    }

    private static List<CatalogRow> Parse(string html)
    {
        var list = new List<CatalogRow>();
        foreach (Match m in Regex.Matches(html, @"<tr id=""([0-9a-f-]{36})_R\d+"">(.*?)</tr>", RegexOptions.Singleline))
        {
            var cells = Regex.Matches(m.Groups[2].Value, @"<td[^>]*>(.*?)</td>", RegexOptions.Singleline).Select(c => Clean(c.Groups[1].Value)).ToList();
            if (cells.Count < 7 || !cells[3].Contains("Driver", StringComparison.OrdinalIgnoreCase)) continue;
            DateTime.TryParse(cells[4], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date);
            long size = 0;
            var sz = Regex.Match(m.Groups[2].Value, @"_originalSize"">(\d+)<");
            if (sz.Success) long.TryParse(sz.Groups[1].Value, out size);
            var ver = cells[5];
            if (!Regex.IsMatch(ver, @"^\d+(\.\d+)+$")) { var tv = Regex.Match(cells[1], @"\(([\d.]+)\)"); ver = tv.Success ? tv.Groups[1].Value : ""; }
            if (ver.Length > 0) list.Add(new CatalogRow(m.Groups[1].Value, cells[1], cells[2], date, ver, size));
        }
        return list;
        static string Clean(string s) => Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", " ")), @"\s+", " ").Trim();
    }

    /// <summary>Descarga el .cab del catalogo, lo descomprime e instala sus INF con pnputil (Windows solo lo aplica si encaja y es mejor).</summary>
    public static async Task<int> InstallCatalogAsync(DriverUpdate u, CancellationToken ct)
    {
        var payload = $"[{{\"size\":0,\"languages\":\"\",\"uidInfo\":\"{u.Id}\",\"updateID\":\"{u.Id}\"}}]";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["updateIDs"] = payload });
        using var resp = await Http.PostAsync("https://www.catalog.update.microsoft.com/DownloadDialog.aspx", form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var url = Regex.Match(body, @"https?://[^'""\s]+\.cab", RegexOptions.IgnoreCase).Value;
        if (url.Length == 0) throw new InvalidOperationException(T("lg.pcd.nourl"));
        var dir = Path.Combine(WorkDir, u.Id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var cab = Path.Combine(dir, "driver.cab");
        await using (var src = await Http.GetStreamAsync(url, ct))
        await using (var dst = File.Create(cab)) await src.CopyToAsync(dst, ct);
        var ex = Path.Combine(dir, "x");
        Directory.CreateDirectory(ex);
        await Sys.RunAsync(Path.Combine(Environment.SystemDirectory, "expand.exe"), $"\"{cab}\" -F:* \"{ex}\"", 300, ct);
        if (!Directory.EnumerateFiles(ex, "*.inf", SearchOption.AllDirectories).Any()) throw new InvalidOperationException(T("lg.pcd.noinf"));
        int code = await Sys.RunAsync(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), $"/add-driver \"{ex}\\*.inf\" /subdirs /install", 600, ct);
        Logger.Info(T("lg.pcd.cat", u.DeviceName, u.NewVersion, code));
        try { Directory.Delete(dir, true); } catch { }
        return code;
    }

    // ------------------------------------------------------------------
    //  Componentes de juego (winget)
    // ------------------------------------------------------------------
    private static readonly string[] ComponentIds =
    [
        "Oracle.JavaRuntimeEnvironment", "Oracle.JDK", "Oracle.JavaSE", "EclipseAdoptium.", "Microsoft.OpenJDK", "Azul.Zulu", "Amazon.Corretto",
        "Microsoft.VCRedist", "Microsoft.DotNet.Runtime", "Microsoft.DotNet.DesktopRuntime", "Microsoft.DirectX", "Microsoft.XNARedist",
        "Microsoft.GameInput", "OpenAL.", "KhronosGroup.VulkanRT", "Microsoft.VCLibs",
    ];

    public static bool IsComponent(string id) => !IsNvidia(id) && ComponentIds.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static string? Winget()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
        return File.Exists(p) ? p : "winget.exe";
    }

    public static async Task<List<Component>> ComponentsAsync(CancellationToken ct)
    {
        var (code, output) = await RunCaptureAsync(Winget()!, "upgrade --accept-source-agreements --disable-interactivity", ct);
        return ParseWinget(output).Where(c => IsComponent(c.Id)).ToList();
    }

    /// <summary>Lee la tabla de "winget upgrade" (en cualquier idioma) por la posicion de las columnas.</summary>
    public static List<Component> ParseWinget(string output)
    {
        var lines = output.Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).ToList();
        int dash = lines.FindIndex(l => l.Length > 20 && l.All(c => c == '-'));
        if (dash < 1) return [];
        var head = lines[dash - 1];
        // Cabecera: Nombre Id Version Disponible Origen (nombres segun idioma): columnas por posicion de cada palabra
        var cols = Regex.Matches(head, @"\S+").Select(m => m.Index).ToList();
        if (cols.Count < 4) return [];
        var res = new List<Component>();
        foreach (var l in lines.Skip(dash + 1))
        {
            if (l.Length < cols[3] || !l.Contains('.')) continue;
            string Col(int i) => l.Length <= cols[i] ? "" : (i + 1 < cols.Count && l.Length > cols[i + 1] ? l[cols[i]..cols[i + 1]] : l[cols[i]..]).Trim();
            var id = Col(1);
            if (id.Length == 0 || id.Contains(' ')) continue;
            res.Add(new Component(Col(0), id, Col(2), Col(3)));
        }
        return res;
    }

    public static async Task<bool> UpgradeComponentAsync(Component c, CancellationToken ct)
    {
        var (code, output) = await RunCaptureAsync(Winget()!, $"upgrade --id {c.Id} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", ct);
        bool ok = code == 0 || output.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase) || output.Contains("Instalado correctamente", StringComparison.OrdinalIgnoreCase);
        Logger.Info(T("lg.pcd.comp", c.Name, c.Available, ok ? "OK" : $"0x{code:X8}"));
        return ok;
    }

    private static async Task<(int Code, string Output)> RunCaptureAsync(string file, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync(ct);
        var e = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await o + await e);
    }

    public static void CleanWork() { try { if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true); } catch { } }
}
