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
    private static string NvidiaDir => Path.Combine(Dir, "nvidia");

    /// <summary>Base de datos de perfiles del driver NVIDIA (la que modifica Profile Inspector).</summary>
    private static string DrsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA Corporation", "Drs");
    private static readonly string[] DrsFiles = ["nvdrsdb0.bin", "nvdrsdb1.bin", "nvdrssel.bin"];

    public static bool HasMouse => File.Exists(MouseFile);
    public static bool HasPower => File.Exists(PowerFile);
    public static bool HasNvidia => File.Exists(Path.Combine(NvidiaDir, "nvdrsdb0.bin"));
    public static bool Available => HasMouse || HasPower || HasNvidia || HasDns || HasTcp || HasOem;

    /// <summary>Copia los perfiles NVIDIA actuales antes de importar el RNX Performance.</summary>
    public static void SaveNvidia()
    {
        if (HasNvidia) return;
        try
        {
            if (!File.Exists(Path.Combine(DrsDir, "nvdrsdb0.bin"))) return;
            Directory.CreateDirectory(NvidiaDir);
            foreach (var f in DrsFiles)
            {
                var src = Path.Combine(DrsDir, f);
                if (File.Exists(src)) CopyShared(src, Path.Combine(NvidiaDir, f));
            }
            Logger.Detail(T("lg.undo.savedNvidia"));
        }
        catch (Exception ex)
        {
            try { Directory.Delete(NvidiaDir, true); } catch { }
            Logger.Detail(T("lg.undo.fail", ex.Message));
        }
    }

    /// <summary>Copia aunque el driver tenga el archivo abierto (FileShare.ReadWrite).</summary>
    private static void CopyShared(string src, string dst)
    {
        using var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var o = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);
        i.CopyTo(o);
    }

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
        if (HasNvidia)
        {
            try
            {
                foreach (var f in DrsFiles)
                {
                    var src = Path.Combine(NvidiaDir, f);
                    if (File.Exists(src)) File.Copy(src, Path.Combine(DrsDir, f), overwrite: true);
                }
                Directory.Delete(NvidiaDir, true);
                Logger.Ok(T("lg.undo.nvidia"));
            }
            catch (Exception ex) { ok = false; Logger.Error(T("lg.undo.fail", "NVIDIA: " + ex.Message)); }
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
        if (HasDns)
        {
            try
            {
                var saved = System.Text.Json.JsonSerializer.Deserialize<List<NetTweaks.Adapter>>(await File.ReadAllTextAsync(DnsFile, ct)) ?? [];
                foreach (var a in saved)
                {
                    var name = a.Name.Replace("'", "''");
                    var servers = a.Static.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
                    await NetTweaks.PowerShellAsync(servers.Length == 0
                        ? $"Set-DnsClientServerAddress -InterfaceAlias '{name}' -ResetServerAddresses"
                        : $"Set-DnsClientServerAddress -InterfaceAlias '{name}' -ServerAddresses ({string.Join(",", servers.Select(x => $"'{x}'"))})", ct);
                    Logger.Ok(T("lg.undo.dns", a.Name, servers.Length == 0 ? "DHCP" : string.Join(", ", servers)));
                }
                await Sys.RunAsync("ipconfig.exe", "/flushdns", 30, ct);
                File.Delete(DnsFile);
            }
            catch (Exception ex) { ok = false; Logger.Error(T("lg.undo.fail", "DNS: " + ex.Message)); }
        }
        if (HasTcp)
        {
            var level = File.ReadAllText(TcpFile).Trim();
            await NetTweaks.SetAutoTuningAsync(level, ct);
            if (await NetTweaks.AutoTuningAsync(ct) == level) { File.Delete(TcpFile); Logger.Ok(T("lg.undo.tcp", level)); }
            else { ok = false; Logger.Error(T("lg.undo.fail", "autotuninglevel=" + level)); }
        }
        if (HasOem)
        {
            try
            {
                using (var k = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(Oem.Key, true))
                    if (k is not null) foreach (var v in Oem.Values) k.DeleteValue(v, false);
                if (new FileInfo(OemFile).Length > 0)
                {
                    var code = await Sys.RunAsync(Path.Combine(Environment.SystemDirectory, "reg.exe"), $"import \"{OemFile}\"", 60, ct);
                    if (code != 0) throw new IOException("reg import " + code);
                }
                File.Delete(OemFile);
                Logger.Ok(T("lg.undo.oem"));
            }
            catch (Exception ex) { ok = false; Logger.Error(T("lg.undo.fail", "OEM: " + ex.Message)); }
        }
        return ok;
    }

    // ---- Red y OEM
    private static string DnsFile => Path.Combine(Dir, "dns_before.json");
    private static string TcpFile => Path.Combine(Dir, "autotuning_before.txt");
    private static string OemFile => Path.Combine(Dir, "oem_before.reg");
    public static bool HasDns => File.Exists(DnsFile);
    public static bool HasTcp => File.Exists(TcpFile);
    public static bool HasOem => File.Exists(OemFile);

    public static async Task SaveDnsAsync(List<NetTweaks.Adapter> adapters, CancellationToken ct)
    {
        if (HasDns) return;
        Directory.CreateDirectory(Dir);
        await File.WriteAllTextAsync(DnsFile, System.Text.Json.JsonSerializer.Serialize(adapters), ct);
        Logger.Detail(T("lg.undo.savedDns"));
    }

    public static void SaveAutoTuning(string level)
    {
        if (HasTcp) return;
        Directory.CreateDirectory(Dir);
        File.WriteAllText(TcpFile, level);
    }

    /// <summary>Exporta la clave OEMInformation; si no existia se deja un archivo vacio (= borrar al deshacer).</summary>
    public static async Task SaveOemAsync(CancellationToken ct)
    {
        if (HasOem) return;
        Directory.CreateDirectory(Dir);
        var code = await Sys.RunAsync(Path.Combine(Environment.SystemDirectory, "reg.exe"),
                                      $@"export ""HKLM\{Oem.Key}"" ""{OemFile}"" /y /reg:64", 60, ct);
        if (code != 0 || !File.Exists(OemFile)) await File.WriteAllTextAsync(OemFile, "", ct);
        Logger.Detail(T("lg.undo.savedOem"));
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
