using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string Notes,
                                string? MsiUrl, long MsiSize, string? MsiSha256,
                                string? ExeUrl, long ExeSize, string? ExeSha256);

/// <summary>
/// Autoactualizacion desde las releases de GitHub.
///
///  - Instalado con el MSI: descarga el .msi nuevo y lo lanza con
///    "msiexec /passive". El MSI sustituye la version anterior y vuelve
///    a abrir el programa.
///  - Version portable (el .exe suelto): descarga el .exe nuevo y lo
///    cambia por el actual cuando el programa se cierra.
///
/// La configuracion (%LOCALAPPDATA%\RNX Cache Cleaner) no forma parte
/// de ninguno de los dos, asi que se conserva siempre.
/// </summary>
public static partial class Updater
{
    private const string Repo = "Ren0X1/CS2-Cache-Cleaner";

    public static Version Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"RNX-Cache-Cleaner/{Current}");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    [GeneratedRegex(@"(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex VersionRx();

    /// <summary>Devuelve la release si es mas nueva que la que esta corriendo; null si no hay nada nuevo.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var m = VersionRx().Match(tag);
        if (!m.Success) return null;
        var latest = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                                 m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        if (latest <= Current) return null;

        string? msiUrl = null, exeUrl = null, msiSha = null, exeSha = null;
        long msiSize = 0, exeSize = 0;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString();
            var size = a.GetProperty("size").GetInt64();
            // GitHub publica el sha256 de cada asset en "digest" ("sha256:...")
            string? sha = a.TryGetProperty("digest", out var d) && d.GetString() is { } ds && ds.StartsWith("sha256:")
                ? ds["sha256:".Length..] : null;
            if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) { msiUrl = url; msiSize = size; msiSha = sha; }
            else if (name.Equals("RNX_Cache_Cleaner.exe", StringComparison.OrdinalIgnoreCase)) { exeUrl = url; exeSize = size; exeSha = sha; }
        }
        return new UpdateInfo(latest, tag, root.GetProperty("html_url").GetString() ?? "",
            root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            msiUrl, msiSize, msiSha, exeUrl, exeSize, exeSha);
    }

    /// <summary>
    /// Novedades para el aviso: el workflow las mete en la release entre
    /// &lt;!--notes--&gt; y &lt;!--/notes--&gt; (sacadas del changelog del README).
    /// </summary>
    public static string WhatsNew(string body, int maxLines = 9)
    {
        var m = Regex.Match(body ?? "", @"<!--notes-->(.*?)<!--/notes-->", RegexOptions.Singleline);
        if (!m.Success) return "";
        var lines = m.Groups[1].Value.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => Regex.Replace(l, @"\*\*|`", ""))
            .Select(l => l.StartsWith("- ") ? "•  " + l[2..] : l)
            .ToList();
        if (lines.Count > maxLines) lines = [.. lines.Take(maxLines - 1), "…"];
        return string.Join("\n", lines);
    }

    /// <summary>Instalado con el MSI si el .exe esta en la carpeta que registro el instalador.</summary>
    public static bool IsInstalled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\RNX\CacheCleaner");
            if (k?.GetValue("InstallDir") is not string dir) return false;
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
            return string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(exeDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Descarga la actualizacion y deja lanzado el proceso que la aplica.
    /// Al volver, quien llama tiene que cerrar el programa.
    /// </summary>
    public static async Task ApplyAsync(UpdateInfo u, IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "RNX_Update");
        Directory.CreateDirectory(dir);

        if (IsInstalled() && u.MsiUrl is not null)
        {
            var msi = Path.Combine(dir, $"RNX_Cache_Cleaner_{u.Version}.msi");
            await DownloadAsync(u.MsiUrl, msi, u.MsiSize, u.MsiSha256, progress, ct);
            Logger.Info(T("lg.upd.msi", u.Tag));
            // Pequena espera para que este proceso se cierre antes de que msiexec sustituya el .exe
            StartHidden($"/c ping -n 3 127.0.0.1 >nul & msiexec /i \"{msi}\" /passive /norestart");
            return;
        }

        if (u.ExeUrl is null) throw new InvalidOperationException("La release no trae el .exe ni el .msi");
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("No se pudo localizar el .exe actual");
        var fresh = Path.Combine(dir, "RNX_Cache_Cleaner.new.exe");
        await DownloadAsync(u.ExeUrl, fresh, u.ExeSize, u.ExeSha256, progress, ct);

        // Script que espera a que nos cerremos, cambia el .exe y lo vuelve a abrir
        var script = Path.Combine(dir, "update.cmd");
        File.WriteAllText(script,
            "@echo off\r\n" +
            "for /l %%i in (1,1,30) do (\r\n" +
            $"  move /y \"{fresh}\" \"{current}\" >nul 2>&1 && goto ok\r\n" +
            "  ping -n 2 127.0.0.1 >nul\r\n" +
            ")\r\n" +
            "exit /b 1\r\n" +
            ":ok\r\n" +
            $"start \"\" \"{current}\"\r\n" +
            "del \"%~f0\"\r\n", Encoding.ASCII);
        Logger.Info(T("lg.upd.exe", u.Tag));
        StartHidden($"/c \"{script}\"");
    }

    private static void StartHidden(string cmdArgs) =>
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmdArgs)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

    private static async Task DownloadAsync(string url, string dest, long expectedSize, string? sha256,
                                            IProgress<double>? progress, CancellationToken ct)
    {
        var tmp = dest + ".part";
        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? expectedSize;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var fs = File.Create(tmp);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        var len = new FileInfo(tmp).Length;
        if (expectedSize > 0 && len != expectedSize)
            throw new IOException($"Descarga incompleta ({len} de {expectedSize} bytes)");
        if (sha256 is not null)
        {
            await using var fs = File.OpenRead(tmp);
            var got = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!got.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("La descarga no coincide con el hash publicado en GitHub");
        }
        File.Move(tmp, dest, overwrite: true);
    }
}
