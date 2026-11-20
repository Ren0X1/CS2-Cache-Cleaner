using System.IO;
using System.Management;
using System.Text.RegularExpressions;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Punto de restauracion de Windows antes de Completa / Todo.
/// Windows solo permite uno cada 24 h (si ya hay uno reciente, se usa ese).
/// </summary>
public static class RestorePoint
{
    private sealed record Point(uint Seq, DateTime When);

    public static Task<StepStatus> CreateAsync(string planName, CancellationToken ct) => Task.Run(() => Create(planName), ct);

    private static StepStatus Create(string planName)
    {
        try
        {
            var before = Latest();
            uint rc;
            using (var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null))
            using (var inp = cls.GetMethodParameters("CreateRestorePoint"))
            {
                inp["Description"] = $"RNX Cache Cleaner - {planName}";
                inp["RestorePointType"] = 12;  // MODIFY_SETTINGS
                inp["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
                using var outp = cls.InvokeMethod("CreateRestorePoint", inp, null);
                rc = Convert.ToUInt32(outp["ReturnValue"]);
            }
            var after = Latest();

            if (rc == 0 && after is not null && (before is null || after.Seq != before.Seq))
            {
                Logger.Ok(T("lg.rp.ok"));
                return StepStatus.Ok;
            }
            if (after is not null && after.When > DateTime.Now.AddHours(-24))
            {
                // Windows no crea otro si ya hay uno de las ultimas 24 h
                Logger.Info(T("lg.rp.recent", after.When.ToString("g", Culture)));
                return StepStatus.Ok;
            }
            Logger.Warn(T("lg.rp.disabled", rc));
            return StepStatus.Warn;
        }
        catch (Exception ex)
        {
            Logger.Warn(T("lg.rp.fail", ex.Message));
            return StepStatus.Warn;
        }
    }

    private static Point? Latest()
    {
        try
        {
            using var q = new ManagementObjectSearcher(@"\\.\root\default", "SELECT SequenceNumber, CreationTime FROM SystemRestore");
            Point? best = null;
            foreach (ManagementObject o in q.Get())
                using (o)
                {
                    var p = new Point(Convert.ToUInt32(o["SequenceNumber"]),
                                      ManagementDateTimeConverter.ToDateTime(o["CreationTime"]?.ToString() ?? ""));
                    if (best is null || p.Seq > best.Seq) best = p;
                }
            return best;
        }
        catch { return null; }
    }
}

/// <summary>
/// Deshacer: antes de que RNX toque el raton o el plan de energia se guarda
/// lo que habia (solo la primera vez, hasta que se deshaga).
/// </summary>
public static class Undo
{
    private static string Dir => Path.Combine(AppPaths.Root, "undo");
    private static string MouseFile => Path.Combine(Dir, "mouse_before.reg");
    private static string PowerFile => Path.Combine(Dir, "power_before.txt");

    public static bool HasMouse => File.Exists(MouseFile);
    public static bool HasPower => File.Exists(PowerFile);
    public static bool Available => HasMouse || HasPower;

    /// <summary>GUID y nombre del plan guardado.</summary>
    public static (string Guid, string Name)? SavedPower()
    {
        try
        {
            if (!HasPower) return null;
            var l = File.ReadAllLines(PowerFile);
            return (l[0].Trim(), l.Length > 1 ? l[1].Trim() : l[0].Trim());
        }
        catch { return null; }
    }

    public static async Task SaveMouseAsync(CancellationToken ct)
    {
        if (HasMouse) return;
        Directory.CreateDirectory(Dir);
        if (await Cs2Modules.ExportMouseRawAsync(MouseFile, ct)) Logger.Detail(T("lg.undo.savedMouse"));
    }

    public static async Task SavePowerAsync(CancellationToken ct)
    {
        if (HasPower) return;
        var active = await PowerPlan.ActiveAsync(ct);
        if (active is null || active.Value.Guid.Equals(PowerPlan.Guid, StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(Dir);
        await File.WriteAllLinesAsync(PowerFile, [active.Value.Guid, active.Value.Name], ct);
        Logger.Detail(T("lg.undo.savedPower", active.Value.Name));
    }

    /// <summary>Vuelve a poner el raton y el plan de antes. Devuelve false si algo falla.</summary>
    public static async Task<bool> RestoreAsync(CancellationToken ct = default)
    {
        bool ok = true;
        if (HasMouse)
        {
            var code = await Sys.RunAsync(Path.Combine(Environment.SystemDirectory, "reg.exe"), $"import \"{MouseFile}\"", 60, ct);
            if (code == 0)
            {
                Cs2Modules.ApplyMouseLive();
                File.Delete(MouseFile);
                Logger.Ok(T("lg.undo.mouse"));
            }
            else { ok = false; Logger.Error(T("lg.undo.fail", "reg import " + code)); }
        }
        if (SavedPower() is { } p)
        {
            // Si el plan de antes ya no existe se vuelve a Equilibrado
            var list = await PowerPlan.PowerCfg("/list", ct);
            var guid = list.Contains(p.Guid, StringComparison.OrdinalIgnoreCase) ? p.Guid : PowerPlan.Balanced;
            await PowerPlan.PowerCfg($"/setactive {guid}", ct);
            var now = await PowerPlan.ActiveAsync(ct);
            if (now is not null && now.Value.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(PowerFile);
                Logger.Ok(T("lg.undo.power", now.Value.Name));
            }
            else { ok = false; Logger.Error(T("lg.undo.fail", "powercfg /setactive " + guid)); }
        }
        return ok;
    }
}

/// <summary>Estado actual de los modulos de CS2 (para la tarjeta de Modulos CS2).</summary>
public sealed record Cs2State(string? PowerName, bool PowerIsRnx, bool? MouseMatches, string MouseSource,
                              string? ShaderPath, long ShaderBytes);

public static partial class Cs2Status
{
    public static async Task<Cs2State> ReadAsync(Settings s)
    {
        var power = await PowerPlan.ActiveAsync();
        return await Task.Run(() =>
        {
            // Raton: se compara el registro con los valores del .reg que usa la app
            bool custom = !string.IsNullOrWhiteSpace(s.RegPath) && File.Exists(s.RegPath);
            var reg = custom ? s.RegPath : AppPaths.ExtractTool("raton.reg");
            bool? matches = reg is null ? null : MouseMatches(reg);

            var shader = (s.ShaderPath ?? "").Trim().Trim('"');
            if (shader.Length == 0 || !Directory.Exists(shader)) shader = Cs2Modules.FindShaderCache() ?? "";
            long size = 0;
            if (shader.Length > 0)
                try { size = new DirectoryInfo(shader).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { }

            return new Cs2State(power?.Name, power?.Guid.Equals(PowerPlan.Guid, StringComparison.OrdinalIgnoreCase) == true,
                                matches, custom ? Path.GetFileName(s.RegPath) : "", shader.Length > 0 ? shader : null, size);
        });
    }

    [GeneratedRegex("^\"(?<n>[^\"]+)\"=(?<v>.*)$")]
    private static partial Regex RegLine();

    /// <summary>true si todos los valores del .reg coinciden con HKCU\Control Panel\Mouse.</summary>
    public static bool? MouseMatches(string regFile)
    {
        try
        {
            var text = File.ReadAllText(regFile); // detecta UTF-16 por el BOM
            text = Regex.Replace(text, @"\\\r?\n\s*", "");
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            if (key is null) return null;
            int compared = 0;
            foreach (var raw in text.Split('\n'))
            {
                var m = RegLine().Match(raw.Trim());
                if (!m.Success) continue;
                var name = m.Groups["n"].Value.Replace("\\\\", "\\");
                var v = m.Groups["v"].Value.Trim();
                var cur = key.GetValue(name);
                string want, have;
                if (v.StartsWith('"')) { want = v.Trim('"').Replace("\\\\", "\\"); have = cur?.ToString() ?? "\0"; }
                else if (v.StartsWith("dword:")) { want = Convert.ToUInt32(v[6..], 16).ToString(); have = cur is int i ? unchecked((uint)i).ToString() : "\0"; }
                else if (v.StartsWith("hex:")) { want = v[4..].Replace(",", "").Replace(" ", "").ToLowerInvariant(); have = cur is byte[] b ? Convert.ToHexString(b).ToLowerInvariant() : "\0"; }
                else continue;
                if (want != have) return false;
                compared++;
            }
            return compared > 0 ? true : null;
        }
        catch { return null; }
    }
}
