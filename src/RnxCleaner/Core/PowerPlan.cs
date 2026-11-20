using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Plan de energia "RNX Performance" (Assets/RNX_Performance.pow).
/// Se importa SIEMPRE con el mismo GUID, asi nunca se duplica:
/// si ya existe solo se activa; si el .pow incluido cambia en una
/// version nueva, se reimporta encima.
/// </summary>
public static class PowerPlan
{
    public const string Guid = "d89c4c69-0be1-4bca-a7bd-663d4511ad95";
    public const string Name = "RNX Performance";
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public static async Task<bool> ApplyAsync(Settings s, CancellationToken ct = default, string guid = Guid, bool activate = true)
    {
        var pow = AppPaths.ExtractTool("RNX_Performance.pow");
        if (pow is null) { Logger.Error(T("lg.pw.missing")); return false; }
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pow, ct)))[..16];

        bool exists = (await PowerCfg("/list", ct)).Contains(guid, StringComparison.OrdinalIgnoreCase);
        bool outdated = exists && guid == Guid && s.PowerPlanHash.Length > 0 && s.PowerPlanHash != hash;

        if (outdated)
        {
            // No se puede borrar el plan activo: se pasa un momento a Equilibrado
            await PowerCfg($"/setactive {Balanced}", ct);
            await PowerCfg($"/delete {guid}", ct);
            exists = false;
            Logger.Detail(T("lg.pw.reimport"));
        }
        if (!exists)
        {
            var o = await PowerCfg($"/import \"{pow}\" {guid}", ct);
            if ((await PowerCfg("/list", ct)).Contains(guid, StringComparison.OrdinalIgnoreCase))
                Logger.Detail(T("lg.pw.imported"));
            else
            {
                // powercfg /import es todo o nada: si este PC no admite un solo ajuste, rechaza
                // el plan entero. Respaldo: se crea el plan y se escribe ajuste a ajuste.
                Logger.Detail(T("lg.pw.importFallback", o.Trim()));
                if (!await BuildFromSettingsAsync(guid, ct)) return false;
            }
        }
        if (guid == Guid) { s.PowerPlanHash = hash; s.Save(); }
        if (!activate) return true;

        if (guid == Guid) await Undo.SavePowerAsync(ct);
        await PowerCfg($"/setactive {guid}", ct);
        bool active = (await PowerCfg("/getactivescheme", ct)).Contains(guid, StringComparison.OrdinalIgnoreCase);
        if (active) Logger.Ok(T("lg.pw.active", Name));
        else Logger.Error(T("lg.pw.activeFail"));
        return active;
    }

    // ------------------------------------------------------------------
    //  Respaldo: plan creado desde Equilibrado + los 140 ajustes del .pow
    // ------------------------------------------------------------------
    private sealed record Setting(string sub, string set, uint? ac, uint? dc);
    private sealed record PlanDef(string name, string description, List<Setting> settings);

    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref System.Guid scheme, ref System.Guid sub, ref System.Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref System.Guid scheme, ref System.Guid sub, ref System.Guid setting, uint value);

    private static async Task<bool> BuildFromSettingsAsync(string guid, CancellationToken ct)
    {
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("power.RNX_Performance.json");
        var def = res is null ? null : await JsonSerializer.DeserializeAsync<PlanDef>(res, cancellationToken: ct);
        if (def is null) { Logger.Error(T("lg.pw.missing")); return false; }

        await PowerCfg($"/duplicatescheme {Balanced} {guid}", ct);
        if (!(await PowerCfg("/list", ct)).Contains(guid, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Error(T("lg.pw.importFail", "duplicatescheme"));
            return false;
        }
        await PowerCfg($"/changename {guid} \"{def.name}\" \"{def.description}\"", ct);

        var scheme = System.Guid.Parse(guid);
        int ok = 0, skipped = 0;
        foreach (var st in def.settings)
        {
            var sub = System.Guid.Parse(st.sub);
            var set = System.Guid.Parse(st.set);
            bool good = true;
            if (st.ac is { } ac) good &= PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, ac) == 0;
            if (st.dc is { } dc) good &= PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, dc) == 0;
            if (good) ok++; else skipped++;
        }
        Logger.Detail(T("lg.pw.built", ok, skipped));
        return true;
    }

    /// <summary>Plan activo (GUID y nombre) segun powercfg /getactivescheme.</summary>
    public static async Task<(string Guid, string Name)?> ActiveAsync(CancellationToken ct = default)
    {
        var o = await PowerCfg("/getactivescheme", ct);
        var m = System.Text.RegularExpressions.Regex.Match(o, @"([0-9a-fA-F]{8}-[0-9a-fA-F-]{27})\s*\((.+?)\)");
        return m.Success ? (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim()) : null;
    }

    public static async Task<string> PowerCfg(string args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync(ct);
        var e = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return o + e;
    }
}
