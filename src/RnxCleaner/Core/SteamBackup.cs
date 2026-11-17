using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace RnxCleaner.Core;

public sealed record BackupZip(string SteamId, string Path, string Hash, long Size);

/// <summary>
/// Port de RNX_Backup_Userdata.ps1: un ZIP por cuenta de Steam con toda su
/// carpeta userdata (estructura de Steam intacta dentro del ZIP), saltandose
/// capturas, clips, caches y archivos gigantes. Solo se rehace el ZIP de una
/// cuenta si su contenido ha cambiado.
/// </summary>
public static class SteamBackup
{
    public static async Task<List<BackupZip>> RunAsync(Settings s, IProgress<double>? progress, CancellationToken ct)
    {
        var result = new List<BackupZip>();
        var steam = SystemInfo.SteamPath();
        if (steam is null) { Logger.Warn("BACKUP: no se encontro la instalacion de Steam, omitido"); return result; }
        var userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata)) { Logger.Warn("BACKUP: no existe " + userdata + ", omitido"); return result; }

        Directory.CreateDirectory(AppPaths.Backups);
        Logger.Detail("BACKUP: origen  " + userdata);
        Logger.Detail("BACKUP: destino " + AppPaths.Backups);

        long maxBytes = (long)s.BackupMaxMB * 1024 * 1024;
        var exclude = new HashSet<string>(s.BackupExclude, StringComparer.OrdinalIgnoreCase);
        var indexPath = Path.Combine(AppPaths.Backups, "_indice.txt");
        var previous = ReadIndex(indexPath);
        var lines = new List<string>();

        var accounts = Directory.GetDirectories(userdata)
            .Where(d => Path.GetFileName(d).All(char.IsDigit)).ToList();
        int n = 0;

        foreach (var acc in accounts)
        {
            ct.ThrowIfCancellationRequested();
            var sid = Path.GetFileName(acc);
            var baseLen = acc.Length + 1;

            var files = new DirectoryInfo(acc)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 })
                .Where(f => !exclude.Contains(f.FullName[baseLen..].Split('\\')[0]))
                .ToList();

            foreach (var big in files.Where(f => f.Length > maxBytes))
                Logger.Warn($"BACKUP: omitido por tamano ({big.Length / 1048576.0:0.0} MB > {s.BackupMaxMB} MB): {big.FullName}");
            files = files.Where(f => f.Length <= maxBytes).OrderBy(f => f.FullName, StringComparer.Ordinal).ToList();

            if (files.Count == 0) { Logger.Warn($"BACKUP: SteamID {sid} no tiene nada que guardar"); n++; continue; }

            var hash = ContentHash(files, acc.Length);
            var zip = Path.Combine(AppPaths.Backups, sid + ".zip");
            var sum = files.Sum(f => f.Length);

            if (previous.TryGetValue(sid, out var old) && old == hash && File.Exists(zip))
            {
                Logger.Detail($"BACKUP: SteamID {sid} sin cambios, ZIP intacto");
                lines.Add($"{sid}|{hash}|{files.Count}|{sum}");
                result.Add(new BackupZip(sid, zip, hash, new FileInfo(zip).Length));
                n++; progress?.Report((double)n / accounts.Count);
                continue;
            }

            var state = File.Exists(zip) ? "actualizado" : "creado";
            var tmp = zip + ".tmp";
            int locked = 0;
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                await Task.Run(() =>
                {
                    using var z = ZipFile.Open(tmp, ZipArchiveMode.Create);
                    for (int i = 0; i < files.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var f = files[i];
                        try { z.CreateEntryFromFile(f.FullName, f.FullName[baseLen..].Replace('\\', '/'), CompressionLevel.Optimal); }
                        catch (IOException) { locked++; }
                        catch (UnauthorizedAccessException) { locked++; }
                        if (i % 25 == 0) progress?.Report((n + (double)i / files.Count) / accounts.Count);
                    }
                }, ct);
                File.Move(tmp, zip, overwrite: true);
            }
            catch (OperationCanceledException) { try { File.Delete(tmp); } catch { } throw; }
            catch (Exception ex)
            {
                Logger.Error($"BACKUP: comprimiendo {sid} -> {ex.Message}");
                try { File.Delete(tmp); } catch { }
                n++; continue;
            }

            if (locked > 0) Logger.Warn($"BACKUP: {sid} -> {locked} archivo(s) en uso omitidos (Steam abierto?)");
            var size = new FileInfo(zip).Length;
            lines.Add($"{sid}|{hash}|{files.Count}|{sum}");
            result.Add(new BackupZip(sid, zip, hash, size));
            Logger.Ok($"BACKUP: {sid}.zip {state} - {files.Count} archivos, {Sys.FormatBytes(size)}");
            n++; progress?.Report((double)n / accounts.Count);
        }

        if (lines.Count > 0) File.WriteAllLines(indexPath, lines.Order(), Encoding.UTF8);
        if (accounts.Count == 0) Logger.Warn("BACKUP: no se encontro ninguna cuenta en userdata");
        else Logger.Ok($"BACKUP: OK - {accounts.Count} cuenta(s), {Sys.FormatBytes(result.Sum(r => r.Size))}");
        return result;
    }

    private static Dictionary<string, string> ReadIndex(string path)
    {
        var d = new Dictionary<string, string>();
        try
        {
            if (File.Exists(path))
                foreach (var l in File.ReadAllLines(path))
                {
                    var p = l.Split('|');
                    if (p.Length >= 2) d[p[0]] = p[1];
                }
        }
        catch { }
        return d;
    }

    // Mismo criterio que el .ps1: ruta relativa + tamano + fecha de cada archivo
    private static string ContentHash(List<FileInfo> files, int baseLen)
    {
        var sb = new StringBuilder();
        foreach (var f in files)
            sb.Append(f.FullName[baseLen..]).Append('|').Append(f.Length).Append('|').Append(f.LastWriteTimeUtc.Ticks).AppendLine();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }
}
