using System.Net.NetworkInformation;
using System.Net.Sockets;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Open NAT para Call of Duty antiguos (Black Ops II / T6 y otros que usan el puerto 3074): abre el 3074 TCP+UDP
/// en el Firewall de Windows y en el router por UPnP. Integra el "RNX Port Forwarder" (T6-Open-Nat-Script) de Ren0X1.
/// </summary>
public static class OpenNat
{
    public const int Port = 3074;
    private static string Rule(string proto) => $"RNX - Puerto {Port} {proto}";
    private static string MapDesc(string proto) => $"RNX Port {Port} {proto}";

    public sealed record State(bool FirewallTcp, bool FirewallUdp, bool? UpnpAvailable, string? RouterTcpIp, string? RouterUdpIp, string? LocalIp)
    {
        public bool Firewall => FirewallTcp && FirewallUdp;
        public bool Router => LocalIp is not null && RouterTcpIp == LocalIp && RouterUdpIp == LocalIp;
        public bool Open => Firewall && Router;
        public bool Any => FirewallTcp || FirewallUdp || RouterTcpIp is not null || RouterUdpIp is not null;
    }

    /// <summary>IP de la red local del adaptador que sale a Internet (el que tiene puerta de enlace).</summary>
    public static string? LocalIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var p = ni.GetIPProperties();
                if (!p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any))) continue;
                var ip = p.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."));
                if (ip is not null) return ip.Address.ToString();
            }
        }
        catch { }
        return null;
    }

    private static dynamic? Mappings()
    {
        try
        {
            dynamic nat = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.NATUPnP")!)!;
            return nat.StaticPortMappingCollection;   // null si el router no tiene UPnP o esta desactivado
        }
        catch { return null; }
    }

    private static string? MappedIp(dynamic? maps, string proto)
    {
        if (maps is null) return null;
        try { dynamic m = maps.Item(Port, proto); return m is null ? null : (string)m.InternalClient; } catch { return null; }
    }

    public static async Task<State> ReadAsync(CancellationToken ct)
    {
        var fw = (await NetTweaks.PowerShellAsync(
            $"@('{Rule("TCP")}','{Rule("UDP")}') | % {{ [bool](Get-NetFirewallRule -DisplayName $_ -ErrorAction SilentlyContinue) }}", ct))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool tcp = fw.Length > 0 && fw[0] == "True", udp = fw.Length > 1 && fw[1] == "True";
        return await Task.Run(() =>
        {
            var maps = Mappings();
            return new State(tcp, udp, maps is not null, MappedIp(maps, "TCP"), MappedIp(maps, "UDP"), LocalIp());
        }, ct);
    }

    /// <summary>Abre el puerto en el firewall y en el router. Devuelve el estado final.</summary>
    public static async Task<State> OpenAsync(CancellationToken ct)
    {
        foreach (var proto in new[] { "TCP", "UDP" })
            // Una sola regla por protocolo: si ya hay varias (ejecuciones antiguas del script), se dejan en una
            await NetTweaks.PowerShellAsync(
                $"$r = @(Get-NetFirewallRule -DisplayName '{Rule(proto)}' -ErrorAction SilentlyContinue); " +
                "if ($r.Count -gt 1) { $r | Select-Object -Skip 1 | Remove-NetFirewallRule }; " +
                $"if ($r.Count -eq 0) {{ New-NetFirewallRule -DisplayName '{Rule(proto)}' -Direction Inbound -Protocol {proto} -LocalPort {Port} -Action Allow -Profile Any -Enabled True | Out-Null }}", ct);

        var ip = LocalIp();
        await Task.Run(() =>
        {
            var maps = Mappings();
            if (maps is null || ip is null) return;
            foreach (var proto in new[] { "TCP", "UDP" })
            {
                try
                {
                    var cur = MappedIp(maps, proto);
                    if (cur == ip) continue;
                    // Si apuntaba a otra IP (el PC cambio de IP), se rehace
                    if (cur is not null) try { maps.Remove(Port, proto); } catch { }
                    maps.Add(Port, proto, Port, ip, true, MapDesc(proto));
                }
                catch (Exception ex) { Logger.Detail($"UPNP {proto}: {ex.Message}"); }
            }
        }, ct);

        var st = await ReadAsync(ct);
        Logger.Info(T("lg.nat.open", Port, st.Firewall ? "OK" : "-", st.UpnpAvailable == true ? (st.Router ? "OK" : "-") : T("nat.noupnp.short"), st.LocalIp ?? "?"));
        return st;
    }

    /// <summary>Quita las reglas y los mapeos creados por RNX.</summary>
    public static async Task<State> CloseAsync(CancellationToken ct)
    {
        foreach (var proto in new[] { "TCP", "UDP" })
            await NetTweaks.PowerShellAsync($"Remove-NetFirewallRule -DisplayName '{Rule(proto)}' -ErrorAction SilentlyContinue", ct);
        await Task.Run(() =>
        {
            var maps = Mappings();
            if (maps is null) return;
            foreach (var proto in new[] { "TCP", "UDP" })
                try
                {
                    dynamic m = maps.Item(Port, proto);
                    string desc = m?.Description ?? "";
                    // Solo los mapeos de RNX (o del script original): nunca los de otro programa
                    if (m is not null && desc.StartsWith("RNX", StringComparison.OrdinalIgnoreCase)) maps.Remove(Port, proto);
                }
                catch { }
        }, ct);
        var st = await ReadAsync(ct);
        Logger.Info(T("lg.nat.close", Port));
        return st;
    }
}
