using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>Una ubicacion de servidores de Valve (POP del Steam Datagram Relay).</summary>
public sealed record ValvePop(string Code, string Name, string Region, IReadOnlyList<string> Ips);

/// <summary>
/// Bloqueo de servidores de Valve para CS2 con el Firewall de Windows.
///
///  - Las ubicaciones y sus IPs salen en directo de la API oficial de Steam
///    (ISteamApps/GetSDRConfig, appid 730), igual que CS2 Server Picker y
///    Server Picker X. Se guarda una copia local por si no hay conexion.
///  - Bloquear = una regla de salida "RNX_SDR_codigo" que bloquea todas las
///    IPs de esa ubicacion. Desbloquear = borrar la regla.
///  - El estado (bloqueado / permitido) se lee SIEMPRE de las reglas que
///    hay en el firewall, no de un archivo propio.
/// </summary>
public static class ServerBlocker
{
    public const string RulePrefix = "RNX_SDR_";
    private const string RuleGroup = "RNX Cache Cleaner";
    private const string ApiUrl = "https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=730";
    private static string CachePath => Path.Combine(AppPaths.Root, "valve_servers.json");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // ------------------------------------------------------------------
    //  Ubicaciones
    // ------------------------------------------------------------------

    /// <summary>Descarga la lista de Valve; si falla, usa la ultima guardada.</summary>
    public static async Task<(List<ValvePop> pops, bool fromCache)> LoadAsync(CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync(ApiUrl, ct);
            var pops = Parse(json);
            if (pops.Count > 0)
            {
                try { AppPaths.Ensure(); await File.WriteAllTextAsync(CachePath, json, ct); } catch { }
                return (pops, false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { Logger.Warn(T("lg.sv.apiFail", ex.Message)); }

        if (File.Exists(CachePath))
            return (Parse(await File.ReadAllTextAsync(CachePath, ct)), true);
        return ([], true);
    }

    public static List<ValvePop> Parse(string json)
    {
        var list = new List<ValvePop>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("pops", out var pops)) return list;
        foreach (var p in pops.EnumerateObject())
        {
            if (!p.Value.TryGetProperty("relays", out var relays) || relays.GetArrayLength() == 0) continue;
            var desc = p.Value.TryGetProperty("desc", out var d) ? d.GetString() ?? p.Name : p.Name;
            // Los servidores de China (Perfect World) son otra plataforma: fuera, como en las apps de referencia
            if (desc.Contains("China", StringComparison.OrdinalIgnoreCase)) continue;
            var ips = relays.EnumerateArray()
                .Select(r => r.TryGetProperty("ipv4", out var ip) ? ip.GetString() : null)
                .Where(ip => !string.IsNullOrWhiteSpace(ip)).Select(ip => ip!).Distinct().ToList();
            if (ips.Count == 0) continue;
            list.Add(new ValvePop(p.Name, desc, RegionOf(desc), ips));
        }
        return list.OrderBy(x => RegionOrder(x.Region)).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Region a partir del pais o estado que Valve pone entre parentesis.</summary>
    public static string RegionOf(string desc)
    {
        string[] eu = ["Spain", "France", "Germany", "Netherlands", "Austria", "Poland", "Sweden", "England", "Finland", "Italy", "Luxembourg"];
        string[] na = ["Georgia", "Illinois", "Texas", "California", "Washington", "Virginia", "New York", "Canada", "Ohio", "Oregon"];
        string[] sa = ["Argentina", "Peru", "Chile", "Brazil", "Colombia"];
        string[] asia = ["India", "Hong Kong", "Singapore", "Japan", "South Korea", "Guam", "Philippines", "Taiwan"];
        string[] me = ["United Arab Emirates", "UAE", "Bahrain", "Saudi"];
        bool Has(string[] keys) => keys.Any(k => desc.Contains(k, StringComparison.OrdinalIgnoreCase));
        if (Has(me)) return "me";
        if (Has(eu)) return "eu";
        if (Has(na)) return "na";
        if (Has(sa)) return "sa";
        if (Has(asia)) return "as";
        if (desc.Contains("Australia", StringComparison.OrdinalIgnoreCase)) return "oc";
        if (desc.Contains("Africa", StringComparison.OrdinalIgnoreCase)) return "af";
        return "other";
    }

    public static readonly string[] Regions = ["eu", "na", "sa", "as", "me", "oc", "af", "other"];
    private static int RegionOrder(string r) => Array.IndexOf(Regions, r);

    // ------------------------------------------------------------------
    //  Firewall (API COM del Firewall de Windows: HNetCfg.FwPolicy2)
    // ------------------------------------------------------------------

    private static dynamic Policy() => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;

    /// <summary>Reglas RNX_SDR_ que existen ahora en el firewall: codigo -> IPs de la regla.</summary>
    public static Dictionary<string, string> BlockedRules()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (dynamic rule in Policy().Rules)
        {
            string? name = rule.Name;
            if (name is not null && name.StartsWith(RulePrefix, StringComparison.Ordinal))
                map[name[RulePrefix.Length..]] = (string?)rule.RemoteAddresses ?? "";
        }
        return map;
    }

    public static void Block(ValvePop pop)
    {
        var rules = Policy().Rules;
        RemoveAll(rules, RulePrefix + pop.Code);
        dynamic r = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
        r.Name = RulePrefix + pop.Code;
        r.Description = $"RNX Cache Cleaner: Valve {pop.Name} ({pop.Code})";
        r.Grouping = RuleGroup;
        r.Direction = 2;            // salida
        r.Action = 0;               // bloquear
        r.Protocol = 256;           // cualquier protocolo
        r.RemoteAddresses = string.Join(",", pop.Ips);
        r.Profiles = 0x7FFFFFFF;    // todos los perfiles
        r.Enabled = true;
        rules.Add(r);
        Logger.Ok(T("lg.sv.blocked", pop.Name, pop.Ips.Count));
    }

    public static void Unblock(string code, string? name = null)
    {
        RemoveAll(Policy().Rules, RulePrefix + code);
        Logger.Ok(T("lg.sv.unblocked", name ?? code));
    }

    /// <summary>Deja abiertas solo las ubicaciones de keepCodes y bloquea el resto.</summary>
    public static void KeepOnly(IEnumerable<ValvePop> pops, IEnumerable<string> keepCodes)
    {
        var keep = keepCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = BlockedRules();
        foreach (var p in pops)
        {
            bool block = !keep.Contains(p.Code);
            if (block && !now.ContainsKey(p.Code)) Block(p);
            else if (!block && now.ContainsKey(p.Code)) Unblock(p.Code, p.Name);
        }
    }

    public static int UnblockAll()
    {
        var codes = BlockedRules().Keys.ToList();
        var rules = Policy().Rules;
        foreach (var c in codes) RemoveAll(rules, RulePrefix + c);
        Logger.Ok(T("lg.sv.unblockedAll", codes.Count));
        return codes.Count;
    }

    /// <summary>Si Valve ha cambiado las IPs de una ubicacion bloqueada, se rehace su regla.</summary>
    public static int SyncChangedIps(IEnumerable<ValvePop> pops)
    {
        var current = BlockedRules();
        int n = 0;
        foreach (var p in pops)
        {
            if (!current.TryGetValue(p.Code, out var ruleIps)) continue;
            var have = ruleIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Select(x => x.Split('/')[0]).ToHashSet();
            if (!have.SetEquals(p.Ips)) { Block(p); n++; }
        }
        if (n > 0) Logger.Info(T("lg.sv.synced", n));
        return n;
    }

    private static void RemoveAll(dynamic rules, string name)
    {
        // Remove() quita una regla con ese nombre; puede haber duplicados
        for (int i = 0; i < 50; i++)
        {
            bool found = false;
            foreach (dynamic r in rules) { if ((string?)r.Name == name) { found = true; break; } }
            if (!found) return;
            rules.Remove(name);
        }
    }

    // ------------------------------------------------------------------
    //  Ping (a la primera IP que responda de la ubicacion)
    // ------------------------------------------------------------------
    public static async Task<long?> PingAsync(ValvePop pop)
    {
        foreach (var ip in pop.Ips.Take(2))
        {
            try
            {
                using var p = new Ping();
                var r = await p.SendPingAsync(ip, 900);
                if (r.Status == IPStatus.Success) return r.RoundtripTime;
            }
            catch { }
        }
        return null;
    }
}
