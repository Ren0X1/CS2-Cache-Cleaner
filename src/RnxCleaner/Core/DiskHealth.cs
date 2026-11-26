using System.Text.Json;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>Salud de los discos (lo que Windows sabe de cada disco: salud, desgaste, temperatura, errores) y espacio libre.</summary>
public static class DiskHealth
{
    public enum Level { Ok, Warning, Critical }

    public sealed record Volume(string Letter, double SizeGb, double FreeGb)
    {
        public double FreePct => SizeGb > 0 ? 100 * FreeGb / SizeGb : 0;
    }

    public sealed record Disk(string Name, string Media, double SizeGb, string Health, string Status, int? Wear, int? Temp, int? Hours, long Errors, List<Volume> Volumes)
    {
        public List<(Level Level, string Key, string Arg)> Issues
        {
            get
            {
                var l = new List<(Level, string, string)>();
                if (!Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase)) l.Add((Health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase) ? Level.Critical : Level.Warning, "dh.i.health", Health));
                if (Wear is >= 95) l.Add((Level.Critical, "dh.i.wear", Wear + " %"));
                else if (Wear is >= 80) l.Add((Level.Warning, "dh.i.wear", Wear + " %"));
                if (Temp is >= 70) l.Add((Level.Warning, "dh.i.temp", Temp + " °C"));
                if (Errors > 0) l.Add((Level.Warning, "dh.i.errors", Errors.ToString()));
                foreach (var v in Volumes)
                    if (v.FreePct < 10 || v.FreeGb < 15) l.Add((v.FreePct < 5 ? Level.Critical : Level.Warning, "dh.i.space", $"{v.Letter}: {v.FreeGb:0.0} GB"));
                return l;
            }
        }
        public Level Worst => Issues.Count == 0 ? Level.Ok : Issues.Max(i => i.Level);
    }

    public static async Task<List<Disk>> ReadAsync(CancellationToken ct)
    {
        const string ps = """
            $res = foreach ($d in Get-PhysicalDisk) {
              $r = $d | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue
              $vols = @(Get-Partition -DiskNumber $d.DeviceId -ErrorAction SilentlyContinue | Where-Object DriveLetter | ForEach-Object {
                $v = Get-Volume -DriveLetter $_.DriveLetter -ErrorAction SilentlyContinue
                if ($v -and $v.Size -gt 0) { [PSCustomObject]@{ Letter = [string]$_.DriveLetter; SizeGb = [math]::Round($v.Size / 1GB, 1); FreeGb = [math]::Round($v.SizeRemaining / 1GB, 1) } } })
              [PSCustomObject]@{
                Name = $d.FriendlyName; Media = [string]$d.MediaType; SizeGb = [math]::Round($d.Size / 1GB, 0)
                Health = [string]$d.HealthStatus; Status = [string]$d.OperationalStatus
                Wear = $r.Wear; Temp = $r.Temperature; Hours = $r.PowerOnHours
                Errors = [long]($r.ReadErrorsUncorrected + $r.WriteErrorsUncorrected)
                Volumes = $vols
              }
            }
            @($res) | ConvertTo-Json -Depth 4 -Compress
            """;
        var json = (await NetTweaks.PowerShellAsync(ps, ct)).Trim();
        if (json.Length == 0 || json == "null") return [];
        if (json.StartsWith('{')) json = "[" + json + "]";
        var list = new List<Disk>();
        foreach (var e in JsonSerializer.Deserialize<List<JsonElement>>(json) ?? [])
        {
            int? I(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i > 0 ? i : null;
            var vols = new List<Volume>();
            if (e.TryGetProperty("Volumes", out var vs) && vs.ValueKind == JsonValueKind.Array)
                foreach (var v in vs.EnumerateArray())
                    vols.Add(new Volume(v.GetProperty("Letter").GetString() ?? "", v.GetProperty("SizeGb").GetDouble(), v.GetProperty("FreeGb").GetDouble()));
            long errs = e.TryGetProperty("Errors", out var er) && er.ValueKind == JsonValueKind.Number ? er.GetInt64() : 0;
            list.Add(new Disk(e.GetProperty("Name").GetString() ?? "?", e.GetProperty("Media").GetString() ?? "", e.GetProperty("SizeGb").GetDouble(),
                              e.GetProperty("Health").GetString() ?? "", e.GetProperty("Status").GetString() ?? "", I("Wear"), I("Temp"), I("Hours"), errs, vols));
        }
        return list;
    }

    /// <summary>Resumen de avisos para el registro.</summary>
    public static void Log(List<Disk> disks)
    {
        foreach (var d in disks)
            foreach (var (lvl, key, arg) in d.Issues)
            {
                var msg = T("lg.dh.issue", d.Name, T(key, arg));
                if (lvl == Level.Critical) Logger.Error(msg); else Logger.Warn(msg);
            }
    }
}
