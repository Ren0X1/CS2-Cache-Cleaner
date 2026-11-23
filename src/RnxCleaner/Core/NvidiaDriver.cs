using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Driver de NVIDIA "pelado", al estilo NVCleanstall / TinyNvidiaUpdateChecker:
/// busca el ultimo Game Ready en el servicio oficial de NVIDIA, descarga el instalador
/// oficial, lo descomprime (7-Zip incluido), deja solo los componentes elegidos,
/// quita telemetria y anuncios del instalador y abre el instalador de NVIDIA ya filtrado.
/// </summary>
public static class NvidiaDriver
{
    public sealed record Gpu(string Name, string InstalledVersion, bool Notebook);
    public sealed record Release(string Version, DateTime Date, string Url, string Size, string DetailsUrl, bool Studio);
    public sealed record Component(string Folder, bool Required);

    private const string Api = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup";
    private const string IdsBase = "https://github.com/ZenitH-AT/nvidia-data/raw/main/";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    public static string WorkDir => Path.Combine(AppPaths.Root, "nvidia");

    private static readonly HttpClient Http = CreateHttp();
    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return h;
    }

    // ------------------------------------------------------------------
    //  Grafica y driver instalado
    // ------------------------------------------------------------------
    public static Gpu? Detect()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Name, DriverVersion, PNPDeviceID FROM Win32_VideoController");
            foreach (ManagementObject o in q.Get())
                using (o)
                {
                    var pnp = o["PNPDeviceID"]?.ToString() ?? "";
                    if (!pnp.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)) continue;
                    var name = (o["Name"]?.ToString() ?? "NVIDIA").Trim();
                    return new Gpu(name, ToNvidiaVersion(o["DriverVersion"]?.ToString() ?? ""), name.Contains("Laptop", StringComparison.OrdinalIgnoreCase));
                }
        }
        catch { }
        return null;
    }

    /// <summary>32.0.16.2101 -> 621.01 (formato de NVIDIA).</summary>
    public static string ToNvidiaVersion(string windowsVersion)
    {
        var digits = windowsVersion.Replace(".", "");
        if (digits.Length < 5) return windowsVersion;
        var last = digits[^5..];
        return last[..3] + "." + last[3..];
    }

    // ------------------------------------------------------------------
    //  Ultimas versiones (servicio oficial de NVIDIA)
    // ------------------------------------------------------------------
    public static async Task<List<Release>> LookupAsync(Gpu gpu, bool studio, CancellationToken ct)
    {
        var gpuData = JsonDocument.Parse(await CachedAsync("gpu-data.json", ct)).RootElement;
        var osData = JsonDocument.Parse(await CachedAsync("os-data.json", ct)).RootElement;

        var key = gpu.Name.Replace("NVIDIA ", "", StringComparison.OrdinalIgnoreCase).Trim();
        string? pfid = null;
        foreach (var kind in gpu.Notebook ? new[] { "notebook", "desktop" } : new[] { "desktop", "notebook" })
            if (gpuData.TryGetProperty(kind, out var list))
            {
                foreach (var p in list.EnumerateObject())
                    if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(key.Replace(" Laptop GPU", ""), StringComparison.OrdinalIgnoreCase))
                    { pfid = p.Value.ToString(); break; }
                if (pfid is not null) break;
            }
        if (pfid is null) throw new InvalidOperationException(T("lg.nvd.unknownGpu", gpu.Name));

        string osName = Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10 64-bit";
        var osId = osData.EnumerateArray().FirstOrDefault(o => o.GetProperty("name").GetString() == osName).GetProperty("id").GetString();

        var url = $"{Api}&pfid={pfid}&osID={osId}&dch=1&numberOfResults=20&languageCode=1078";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        var res = new List<Release>();
        if (!doc.RootElement.TryGetProperty("IDS", out var ids)) return res;
        foreach (var it in ids.EnumerateArray())
        {
            var d = it.GetProperty("downloadInfo");
            string S(string n) => d.TryGetProperty(n, out var v) ? Uri.UnescapeDataString(v.ToString()) : "";
            bool isStudio = S("IsCRD") == "1";
            if (isStudio != studio || S("IsBeta") == "1" || S("IsFeaturePreview") == "1") continue;
            DateTime.TryParse(S("ReleaseDateTime"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date);
            // La pagina de notas en en-gb no carga: siempre en-us
            var details = System.Text.RegularExpressions.Regex.Replace(S("DetailsURL"), @"nvidia\.com/[a-z]{2}-[a-z]{2}/", "nvidia.com/en-us/");
            res.Add(new Release(S("Version"), date, S("DownloadURL"), S("DownloadURLFileSize"), details, isStudio));
        }
        return res;
    }

    public sealed record UpdateInfo(Gpu Gpu, Release Latest, int Behind);

    /// <summary>Comprueba si hay un Game Ready mas nuevo que el instalado (null si esta al dia o no hay NVIDIA).</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        var gpu = Detect();
        if (gpu is null) return null;
        var list = await LookupAsync(gpu, false, ct);
        int behind = Behind(list, gpu.InstalledVersion);
        return behind > 0 ? new UpdateInfo(gpu, list[0], behind) : null;
    }

    /// <summary>Cuantas versiones publicadas son mas nuevas que la instalada.</summary>
    public static int Behind(IEnumerable<Release> list, string installed) =>
        Version.TryParse(installed, out var iv) ? list.Count(r => Version.TryParse(r.Version, out var v) && v > iv) : 0;

    /// <summary>Tablas de ids (pfid y osID) de ZenitH-AT/nvidia-data, con copia local de un dia.</summary>
    private static async Task<string> CachedAsync(string file, CancellationToken ct)
    {
        Directory.CreateDirectory(WorkDir);
        var local = Path.Combine(WorkDir, file);
        if (File.Exists(local) && File.GetLastWriteTime(local) > DateTime.Now.AddDays(-1)) return await File.ReadAllTextAsync(local, ct);
        try
        {
            var text = await Http.GetStringAsync(IdsBase + file, ct);
            await File.WriteAllTextAsync(local, text, ct);
            return text;
        }
        catch when (File.Exists(local)) { return await File.ReadAllTextAsync(local, ct); }
    }

    // ------------------------------------------------------------------
    //  Descarga, descompresion y paquete limpio
    // ------------------------------------------------------------------
    /// <summary>Ruta donde queda el instalador de esa URL.</summary>
    public static string LocalFile(string url) => Path.Combine(WorkDir, Path.GetFileName(new Uri(url).LocalPath));

    /// <summary>true si el instalador ya esta descargado completo en esta sesion.</summary>
    public static bool IsDownloaded(string url) => File.Exists(LocalFile(url)) && new FileInfo(LocalFile(url)).Length > 100_000_000;

    public static async Task<string> DownloadAsync(string url, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(WorkDir);
        var file = LocalFile(url);
        if (IsDownloaded(url)) return file;
        var part = file + ".part";
        try { return await DownloadToAsync(url, file, part, progress, ct); }
        catch { try { File.Delete(part); } catch { } throw; }
    }

    private static async Task<string> DownloadToAsync(string url, string file, string part, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0, done = 0;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(part))
        {
            var buf = new byte[1 << 20];
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        if (new FileInfo(part).Length < 10_000_000) throw new IOException(T("lg.nvd.tooSmall"));
        File.Move(part, file, true);
        return file;
    }

    /// <summary>Descomprime el instalador oficial con el 7-Zip incluido. Devuelve la carpeta.</summary>
    public static async Task<string> ExtractAsync(string installer, CancellationToken ct)
    {
        var seven = AppPaths.ExtractTool("7zr.exe") ?? throw new FileNotFoundException("7zr.exe");
        var dir = Path.Combine(WorkDir, Path.GetFileNameWithoutExtension(installer));
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var code = await Sys.RunAsync(seven, $"x \"{installer}\" -o\"{dir}\" -y", 900, ct);
        if (!File.Exists(Path.Combine(dir, "setup.exe")))
            throw new IOException(T("lg.nvd.extractFail", code));
        return dir;
    }

    /// <summary>Componentes del paquete: subcarpetas con un .nvi.</summary>
    public static List<Component> Components(string dir) =>
        Directory.GetDirectories(dir)
                 .Where(d => Directory.EnumerateFiles(d, "*.nvi").Any())
                 .Select(d => Path.GetFileName(d))
                 .Where(n => !n.Equals("NVI2", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(n => n.Equals("Display.Driver", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(n => n)
                 .Select(n => new Component(n, n.Equals("Display.Driver", StringComparison.OrdinalIgnoreCase)))
                 .ToList();

    /// <summary>Perfiles: Recomendado (driver, PhysX, HD Audio) y Minimo (solo driver).</summary>
    public static bool InPreset(string folder, string preset) => preset switch
    {
        "min" => folder.Equals("Display.Driver", StringComparison.OrdinalIgnoreCase),
        _ => folder.Equals("Display.Driver", StringComparison.OrdinalIgnoreCase) || folder.Equals("PhysX", StringComparison.OrdinalIgnoreCase)
             || folder.Equals("HDAudio", StringComparison.OrdinalIgnoreCase) || folder.Equals("NVPCF", StringComparison.OrdinalIgnoreCase)
             || folder.Equals("PPC", StringComparison.OrdinalIgnoreCase) || folder.Equals("NvCpl", StringComparison.OrdinalIgnoreCase)
             || folder.Equals("MSVCRT", StringComparison.OrdinalIgnoreCase),
    };

    private static readonly string[] KeepAlways = ["NVI2", "EULA.txt", "license.txt", "ListDevices.txt", "setup.cfg", "setup.exe"];

    /// <summary>
    /// Deja en el paquete solo lo elegido (mas lo imprescindible del instalador),
    /// quita EULA/consentimiento/privacidad de setup.cfg y la telemetria y anuncios del instalador.
    /// </summary>
    public static void Prepare(string dir, IEnumerable<string> keep)
    {
        var set = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase) { "Display.Driver" };
        foreach (var d in Directory.GetDirectories(dir))
        {
            var n = Path.GetFileName(d);
            if (!set.Contains(n) && !KeepAlways.Contains(n, StringComparer.OrdinalIgnoreCase)) Directory.Delete(d, true);
        }
        foreach (var f in Directory.GetFiles(dir))
            if (!KeepAlways.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)) File.Delete(f);

        var cfg = Path.Combine(dir, "setup.cfg");
        var x = XDocument.Load(cfg);
        foreach (var e in x.Descendants("file").Where(e => (string?)e.Attribute("name") is "${{EulaHtmlFile}}" or "${{FunctionalConsentFile}}" or "${{PrivacyPolicyFile}}").ToList())
            e.Remove();
        x.Save(cfg);

        var pres = Path.Combine(dir, "NVI2", "presentations.cfg");
        if (File.Exists(pres))
        {
            var p = XDocument.Load(pres);
            foreach (var e in p.Descendants().Where(e => (string?)e.Attribute("name") is "ProgressPresentationUrl" or "ProgressPresentationSelectedPackageUrl"))
                e.SetAttributeValue("value", "");
            p.Save(pres);
        }
        Logger.Ok(T("lg.nvd.prepared", string.Join(", ", set.OrderBy(s => s))));
    }

    /// <summary>
    /// Instala directamente, sin pasos: -passive muestra solo la barra de progreso de NVIDIA,
    /// -noeula y -nofinish saltan la licencia y la pantalla final, -noreboot no reinicia solo
    /// (RNX pregunta al terminar) y -clean restablece la configuracion de NVIDIA.
    /// </summary>
    public static string InstallArgs(bool clean) => "-passive -noeula -nofinish -noreboot" + (clean ? " -clean" : "");

    public static async Task<int> InstallAsync(string dir, bool clean, CancellationToken ct)
    {
        var args = InstallArgs(clean);
        Logger.Detail("NVIDIA setup.exe " + args);
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(dir, "setup.exe"), args) { UseShellExecute = true, WorkingDirectory = dir })!;
        await p.WaitForExitAsync(ct);
        Logger.Info(T("lg.nvd.installed", p.ExitCode));
        return p.ExitCode;
    }

    /// <summary>Libera espacio: borra instaladores, descargas a medias y carpetas descomprimidas.</summary>
    public static void CleanWork()
    {
        try { if (Directory.Exists(WorkDir)) foreach (var e in Directory.EnumerateFileSystemEntries(WorkDir).Where(e => !e.EndsWith(".json"))) { if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e); } }
        catch { }
    }
}
