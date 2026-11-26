using System.IO;
using System.Text.Json;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Limpieza de reglas del Firewall de Windows creadas por programas: huerfanas (el programa ya no existe) y duplicadas
/// (misma regla exacta varias veces). Nunca toca reglas de Windows ni las de RNX. Antes de borrar se exporta el firewall
/// completo (netsh advfirewall export) para poder restaurarlo tal cual.
/// </summary>
public static class FirewallCleaner
{
    public enum Kind { Orphan, Duplicate }

    public sealed record Rule(string Id, string Name, string Program, string Direction, string Protocol, Kind Kind);

    public static string BackupDir => Path.Combine(AppPaths.Root, "backups", "firewall");

    /// <summary>Busca reglas huerfanas y duplicadas (lectura en bloque: rapido aunque haya cientos de reglas).</summary>
    public static async Task<List<Rule>> ScanAsync(CancellationToken ct)
    {
        const string ps = """
            $apps = @{}; Get-NetFirewallApplicationFilter -All | ForEach-Object { $apps[$_.InstanceID] = $_.Program }
            $ports = @{}; Get-NetFirewallPortFilter -All | ForEach-Object { $ports[$_.InstanceID] = $_ }
            $seen = @{}
            $out = foreach ($r in Get-NetFirewallRule) {
              if ($r.Group -and $r.Group.StartsWith('@')) { continue }
              if ($r.DisplayName.StartsWith('@{') -or $r.DisplayName -like 'RNX*') { continue }
              $p = $apps[$r.Name]; $pf = $ports[$r.Name]
              $proto = if ($pf) { [string]$pf.Protocol } else { '' }
              $kind = $null
              if ($p -and $p -ne 'Any' -and $p -ne 'System') {
                $exp = [Environment]::ExpandEnvironmentVariables($p)
                if ($exp.Length -gt 3 -and $exp[1] -eq ':' -and -not (Test-Path -LiteralPath $exp)) { $kind = 'Orphan' }
              }
              $key = ($r.DisplayName, $r.Direction, $r.Action, $r.Profile, $p, $proto, ($pf.LocalPort -join ','), ($pf.RemotePort -join ','), $r.Enabled) -join '|'
              if (-not $kind) { if ($seen.ContainsKey($key)) { $kind = 'Duplicate' } else { $seen[$key] = 1 } }
              if ($kind) { [PSCustomObject]@{ Id = $r.Name; Name = $r.DisplayName; Program = [string]$p; Direction = [string]$r.Direction; Protocol = $proto; Kind = $kind } }
            }
            @($out) | ConvertTo-Json -Compress
            """;
        var json = (await NetTweaks.PowerShellAsync(ps, ct)).Trim();
        if (json.Length == 0 || json == "null") return [];
        if (json.StartsWith('{')) json = "[" + json + "]";
        var list = JsonSerializer.Deserialize<List<JsonElement>>(json) ?? [];
        return list.Select(e => new Rule(
                e.GetProperty("Id").GetString() ?? "", e.GetProperty("Name").GetString() ?? "", e.GetProperty("Program").GetString() ?? "",
                e.GetProperty("Direction").GetString() ?? "", e.GetProperty("Protocol").GetString() ?? "",
                e.GetProperty("Kind").GetString() == "Duplicate" ? Kind.Duplicate : Kind.Orphan))
            .Where(r => r.Id.Length > 0)
            .OrderBy(r => r.Kind).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Exporta el firewall completo. Devuelve la ruta del archivo .wfw.</summary>
    public static async Task<string> BackupAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(BackupDir);
        var file = Path.Combine(BackupDir, $"firewall_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.wfw");
        int code = await Sys.RunAsync("netsh.exe", $"advfirewall export \"{file}\"", 120, ct);
        if (code != 0 || !File.Exists(file)) throw new IOException(T("lg.fw.backupFail", code));
        Logger.Info(T("lg.fw.backup", file));
        return file;
    }

    public static string? LastBackup() =>
        Directory.Exists(BackupDir) ? Directory.GetFiles(BackupDir, "*.wfw").OrderByDescending(f => f).FirstOrDefault() : null;

    /// <summary>Copia previa y borrado de las reglas indicadas (por su identificador interno).</summary>
    public static async Task<(int Removed, int Failed, string Backup)> RemoveAsync(IReadOnlyList<Rule> rules, CancellationToken ct)
    {
        var backup = await BackupAsync(ct);
        var tmp = Path.Combine(Path.GetTempPath(), $"rnx_fw_{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(tmp, rules.Select(r => r.Id), ct);
        var res = (await NetTweaks.PowerShellAsync(
            $"$ok=0;$f=0; foreach ($n in Get-Content -LiteralPath '{tmp.Replace("'", "''")}') {{ if ($n) {{ try {{ Remove-NetFirewallRule -Name $n -ErrorAction Stop; $ok++ }} catch {{ $f++ }} }} }}; \"$ok|$f\"", ct)).Trim();
        try { File.Delete(tmp); } catch { }
        var parts = res.Split('|');
        int ok = parts.Length == 2 && int.TryParse(parts[0], out var a) ? a : 0, fail = parts.Length == 2 && int.TryParse(parts[1], out var b) ? b : rules.Count;
        Logger.Info(T("lg.fw.removed", ok, fail));
        return (ok, fail, backup);
    }

    /// <summary>Deja el firewall exactamente como en la copia (sustituye todas las reglas actuales).</summary>
    public static async Task RestoreAsync(string file, CancellationToken ct)
    {
        int code = await Sys.RunAsync("netsh.exe", $"advfirewall import \"{file}\"", 180, ct);
        if (code != 0) throw new IOException(T("lg.fw.restoreFail", code));
        Logger.Info(T("lg.fw.restored", Path.GetFileName(file)));
    }
}
