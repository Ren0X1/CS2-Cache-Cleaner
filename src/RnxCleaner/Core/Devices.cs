using System.IO;
using System.Text.Json;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Dispositivos antiguos (los que Windows recuerda pero ya no estan conectados)
/// y dispositivos conectados con error. Solo a mano, con lista previa.
/// Los drivers no se tocan: si se vuelve a conectar algo, se reinstala solo.
/// </summary>
public static class Devices
{
    public sealed record Dev(string InstanceId, string? FriendlyName, string? Class)
    {
        public string Name => string.IsNullOrWhiteSpace(FriendlyName) ? InstanceId : FriendlyName!;
        public string Group => string.IsNullOrWhiteSpace(Class) ? "Other" : Class!;
    }

    /// <summary>Clases que no se tocan nunca (placa, CPU, firmware, TPM...).</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
        { "System", "Computer", "Processor", "Firmware", "SecurityDevices", "LegacyDriver", "SoftwareComponent" };

    public static async Task<(List<Dev> Ghosts, List<Dev> Errors)> ScanAsync(CancellationToken ct = default)
    {
        const string ps = """
            $g = @(Get-PnpDevice | Where-Object { -not $_.Present } | Select-Object InstanceId, FriendlyName, Class)
            $e = @(Get-PnpDevice -PresentOnly | Where-Object { $_.Status -eq 'Error' } | Select-Object InstanceId, FriendlyName, Class)
            ConvertTo-Json -Compress -Depth 3 -InputObject @{ g = $g; e = $e }
            """;
        var json = (await NetTweaks.PowerShellAsync(ps, ct)).Trim();
        using var doc = JsonDocument.Parse(json.Length > 0 ? json : "{}");
        List<Dev> Read(string prop)
        {
            if (!doc.RootElement.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
            return arr.EnumerateArray()
                .Select(x => new Dev(x.GetProperty("InstanceId").GetString() ?? "",
                                     x.TryGetProperty("FriendlyName", out var n) ? n.GetString() : null,
                                     x.TryGetProperty("Class", out var c) ? c.GetString() : null))
                .Where(d => d.InstanceId.Length > 0 && !Protected.Contains(d.Group))
                .OrderBy(d => d.Group).ThenBy(d => d.Name).ToList();
        }
        return (Read("g"), Read("e"));
    }

    private static string PnpUtil => Path.Combine(Environment.SystemDirectory, "pnputil.exe");

    /// <summary>Quita los antiguos y reinicia los que fallan. Devuelve (quitados, fallidos, arreglados).</summary>
    public static async Task<(int Removed, int Failed, int Fixed)> CleanAsync(List<Dev> ghosts, List<Dev> errors, CancellationToken ct = default)
    {
        int removed = 0, failed = 0, fixd = 0;
        foreach (var d in ghosts)
        {
            ct.ThrowIfCancellationRequested();
            var code = await Sys.RunAsync(PnpUtil, $"/remove-device \"{d.InstanceId}\"", 60, ct);
            if (code == 0) { removed++; Logger.Detail(T("lg.dev.removed", d.Name, d.Group)); }
            else { failed++; Logger.Detail(T("lg.dev.removeFail", d.Name, code)); }
        }
        foreach (var d in errors)
        {
            ct.ThrowIfCancellationRequested();
            await Sys.RunAsync(PnpUtil, $"/restart-device \"{d.InstanceId}\"", 60, ct);
        }
        await Sys.RunAsync(PnpUtil, "/scan-devices", 120, ct);
        if (errors.Count > 0)
        {
            var still = (await ScanAsync(ct)).Errors.Select(e => e.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var d in errors)
                if (still.Contains(d.InstanceId)) Logger.Warn(T("lg.dev.stillError", d.Name));
                else { fixd++; Logger.Ok(T("lg.dev.fixed", d.Name)); }
        }
        Logger.Ok(T("lg.dev.done", removed, failed, fixd));
        return (removed, failed, fixd);
    }
}
