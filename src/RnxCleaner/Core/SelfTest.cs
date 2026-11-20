using System.Diagnostics;
using System.IO;
using System.Text;

namespace RnxCleaner.Core;

/// <summary>
/// Autoprueba del bloqueo de servidores (RNX_Cache_Cleaner.exe --selftest-servers informe.txt).
/// Bloquea Madrid y Paris, comprueba las reglas por la API del firewall y por netsh,
/// y deja el firewall como estaba (solo quita lo que la prueba ha puesto).
/// </summary>
public static class SelfTest
{
    public static async Task ServersAsync(string reportPath)
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); }
        bool ok = true;
        void Check(bool cond, string what) { L((cond ? "[OK]   " : "[FAIL] ") + what); ok &= cond; }

        try
        {
            Loc.Set("es");
            var (pops, cache) = await ServerBlocker.LoadAsync(CancellationToken.None);
            L($"Ubicaciones cargadas: {pops.Count} (desde {(cache ? "copia local" : "API de Steam")}), IPs totales: {pops.Sum(p => p.Ips.Count)}");
            Check(pops.Count > 20, "La API de Valve devuelve ubicaciones");

            var targets = new[] { "mad", "par" }.Select(c => pops.First(p => p.Code == c)).ToList();
            foreach (var t in targets) L($"  {t.Code} {t.Name} [{t.Region}]: {string.Join(", ", t.Ips)}");

            var before = ServerBlocker.BlockedRules();
            L($"Estado inicial: {before.Count} bloqueadas ({string.Join(", ", before.Keys)})");
            var preexisting = targets.Where(t => before.ContainsKey(t.Code)).Select(t => t.Code).ToHashSet();

            foreach (var t in targets) ServerBlocker.Block(t);
            var after = ServerBlocker.BlockedRules();
            foreach (var t in targets)
            {
                Check(after.ContainsKey(t.Code), $"Estado {t.Code}: aparece BLOQUEADO tras bloquear");
                var ruleIps = after.GetValueOrDefault(t.Code, "").Split(',').Select(x => x.Split('/')[0].Trim()).ToHashSet();
                Check(ruleIps.SetEquals(t.Ips), $"Regla {t.Code}: contiene exactamente sus {t.Ips.Count} IPs");
                var netsh = Netsh($"advfirewall firewall show rule name={ServerBlocker.RulePrefix}{t.Code} verbose");
                Check(netsh.Contains(t.Ips[0]) && (netsh.Contains("Block") || netsh.Contains("Bloquear")) && (netsh.Contains("Out") || netsh.Contains("Salida")),
                      $"netsh ve la regla {t.Code} (salida, bloquear, con sus IPs)");
            }

            // Sincronizacion: si la regla tiene IPs viejas, se rehace
            var fake = targets[0] with { Ips = targets[0].Ips.Take(1).ToList() };
            ServerBlocker.Block(fake);
            int synced = ServerBlocker.SyncChangedIps(pops);
            var resync = ServerBlocker.BlockedRules();
            Check(synced >= 1 && resync[fake.Code].Split(',').Length == targets[0].Ips.Count, "Si Valve cambia las IPs, la regla se actualiza sola");

            foreach (var t in targets.Where(t => !preexisting.Contains(t.Code))) ServerBlocker.Unblock(t.Code, t.Name);
            var end = ServerBlocker.BlockedRules();
            foreach (var t in targets.Where(t => !preexisting.Contains(t.Code)))
            {
                Check(!end.ContainsKey(t.Code), $"Estado {t.Code}: aparece PERMITIDO tras desbloquear");
                Check(!Netsh($"advfirewall firewall show rule name={ServerBlocker.RulePrefix}{t.Code}").Contains(t.Ips[0]), $"netsh ya no ve la regla {t.Code}");
            }
            L($"Estado final: {end.Count} bloqueadas ({string.Join(", ", end.Keys)})");
        }
        catch (Exception ex) { L("[FAIL] Excepcion: " + ex); ok = false; }

        L(ok ? "RESULTADO: TODO OK" : "RESULTADO: HAY FALLOS");
        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Importa el .pow incluido en un GUID de prueba (comprueba nombre y que no se duplica),
    /// lo borra, y luego aplica el plan real (que en este PC ya existe: solo se activa).
    /// Deja activo el mismo plan que habia al empezar.
    /// </summary>
    public static async Task PowerAsync(string reportPath)
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); }
        bool ok = true;
        void Check(bool cond, string what) { L((cond ? "[OK]   " : "[FAIL] ") + what); ok &= cond; }
        string Active(string o) => System.Text.RegularExpressions.Regex.Match(o, "[0-9a-f]{8}-[0-9a-f-]{27}").Value;
        try
        {
            Loc.Set("es");
            var s = Settings.Load();
            var startActive = Active(await PowerPlan.PowerCfg("/getactivescheme"));
            L("Plan activo al empezar: " + startActive);

            // Diagnostico: import directo con y sin GUID
            var pow = AppPaths.ExtractTool("RNX_Performance.pow")!;
            var o1 = await PowerPlan.PowerCfg($"/import \"{pow}\"");
            var g1 = System.Text.RegularExpressions.Regex.Match(o1, "[0-9a-f]{8}-[0-9a-f-]{27}").Value;
            L("Import SIN guid -> " + o1.Trim());
            if (g1.Length > 0) await PowerPlan.PowerCfg($"/delete {g1}");
            var o2 = await PowerPlan.PowerCfg($"/import \"{pow}\" 1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f");
            L("Import CON guid -> " + o2.Trim());
            await PowerPlan.PowerCfg("/delete 1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f");

            const string test = "0b5e1c3a-7d2f-4c8e-9a61-5f3b2d4e6a70";
            await PowerPlan.PowerCfg($"/delete {test}");
            Check(await PowerPlan.ApplyAsync(s, default, test, activate: false), "Importa el .pow incluido en un GUID de prueba");
            var list = await PowerPlan.PowerCfg("/list");
            Check(list.Contains(test) && list.Contains(PowerPlan.Name), "El plan importado aparece en powercfg con el nombre RNX Performance");
            await PowerPlan.ApplyAsync(s, default, test, activate: false);
            int copies = System.Text.RegularExpressions.Regex.Matches(await PowerPlan.PowerCfg("/list"), test).Count;
            Check(copies == 1, "Importar otra vez NO lo duplica (mismo GUID)");
            await PowerPlan.PowerCfg($"/delete {test}");
            Check(!(await PowerPlan.PowerCfg("/list")).Contains(test), "Plan de prueba borrado");

            Check(await PowerPlan.ApplyAsync(s), "Plan real RNX Performance aplicado y activo (lo que hace /todo)");
            int real = System.Text.RegularExpressions.Regex.Matches(await PowerPlan.PowerCfg("/list"), PowerPlan.Guid).Count;
            Check(real == 1, "Solo hay un RNX Performance en el sistema");

            if (startActive.Length > 0) await PowerPlan.PowerCfg($"/setactive {startActive}");
            L("Plan activo al terminar: " + Active(await PowerPlan.PowerCfg("/getactivescheme")));
        }
        catch (Exception ex) { L("[FAIL] Excepcion: " + ex); ok = false; }
        L(ok ? "RESULTADO: TODO OK" : "RESULTADO: HAY FALLOS");
        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>--selftest-safety: punto de restauracion, deshacer, estado, tarea programada y novedades.</summary>
    public static async Task SafetyAsync(string reportPath)
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); }
        bool ok = true;
        void Check(bool cond, string what) { L((cond ? "[OK]   " : "[FAIL] ") + what); ok &= cond; }
        Logger.Written += e => L("       log: " + e.Text);
        // Los datos de deshacer reales del usuario se apartan y se devuelven al final
        var undoDir = Path.Combine(AppPaths.Root, "undo");
        var undoBak = undoDir + ".selftest";
        if (Directory.Exists(undoDir)) { if (Directory.Exists(undoBak)) Directory.Delete(undoBak, true); Directory.Move(undoDir, undoBak); }
        try
        {
            Loc.Set("es");
            var s = Settings.Load();

            // 1) Punto de restauracion
            L("--- Punto de restauracion");
            var rp = await RestorePoint.CreateAsync("Todo", CancellationToken.None);
            Check(rp == StepStatus.Ok, "Punto de restauracion creado o ya habia uno de las ultimas 24 h");

            // 2) Deshacer: raton + plan de energia
            L("--- Deshacer");
            var start = await PowerPlan.ActiveAsync();
            L("Plan activo al empezar: " + start);
            Check(!Undo.Available, "Sin datos de deshacer antes de empezar");
            await PowerPlan.PowerCfg($"/setactive {PowerPlan.Balanced}");
            await Cs2Modules.ImportMouseAsync(s);
            Check(Undo.HasMouse, "Al aplicar el raton se guarda el raton anterior");
            Check(await PowerPlan.ApplyAsync(s), "Plan RNX aplicado");
            Check(Undo.SavedPower()?.Guid == PowerPlan.Balanced, "Al aplicar el plan se guarda el anterior (Equilibrado): " + Undo.SavedPower());
            var st = await Cs2Status.ReadAsync(s);
            L($"Estado: plan={st.PowerName} rnx={st.PowerIsRnx} raton={st.MouseMatches} shader={st.ShaderPath} {st.ShaderBytes} bytes");
            Check(st.PowerIsRnx, "Estado: plan RNX activo");
            Check(st.MouseMatches == true, "Estado: raton coincide con el .reg aplicado");
            Check(await Undo.RestoreAsync(), "Deshacer sin errores");
            Check((await PowerPlan.ActiveAsync())?.Guid == PowerPlan.Balanced, "Tras deshacer vuelve Equilibrado");
            Check(!Undo.Available, "Tras deshacer no queda nada pendiente");
            if (start is { } st0) await PowerPlan.PowerCfg($"/setactive {st0.Guid}");
            L("Plan activo al terminar: " + await PowerPlan.ActiveAsync());

            // 3) Tarea programada
            L("--- Tarea programada");
            bool had = Scheduler.Query().Exists;
            Check(!had, "No habia tarea antes");
            Scheduler.Apply("weekly", "full");
            var q = Scheduler.Query();
            Check(q.Exists && q.Enabled, "Tarea semanal creada y activa");
            Check(q.Next is { } n && n.DayOfWeek == DayOfWeek.Sunday && n.Hour == 12, "Proxima ejecucion domingo 12:00: " + q.Next);
            Scheduler.Apply("logon", "todo");
            Check(Scheduler.Query().Exists, "Cambiar a 'al iniciar sesion' sustituye la tarea");
            Scheduler.Apply("off", "todo");
            Check(!Scheduler.Query().Exists, "Desactivar borra la tarea");

            // 3b) Deshacer perfil NVIDIA
            L("--- Deshacer NVIDIA");
            var drs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA Corporation", "Drs", "nvdrsdb0.bin");
            string H() { using var f = new FileStream(drs, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f))[..16]; }
            var h0 = H();
            Check(await Cs2Modules.ImportNvidiaProfileAsync(s), "Perfil RNX importado");
            Check(Undo.HasNvidia, "Se guardaron los perfiles NVIDIA anteriores");
            L($"hash antes={h0} tras importar={H()}");
            Check(await Undo.RestoreAsync(), "Deshacer NVIDIA sin errores");
            Check(H() == h0 && !Undo.HasNvidia, "Los perfiles NVIDIA vuelven a ser identicos a los de antes");

            // 3c) Presets de servidores (deja el firewall como estaba)
            L("--- Presets de servidores");
            var (pops, _) = await ServerBlocker.LoadAsync(CancellationToken.None);
            var before = ServerBlocker.BlockedRules().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var eu = pops.Where(p => p.Region == "eu").Select(p => p.Code).ToList();
            ServerBlocker.KeepOnly(pops, eu);
            var mid = ServerBlocker.BlockedRules();
            Check(eu.All(c => !mid.ContainsKey(c)) && pops.Where(p => p.Region != "eu").All(p => mid.ContainsKey(p.Code)), $"Solo Europa: {eu.Count} abiertas, {mid.Count} bloqueadas");
            ServerBlocker.KeepOnly(pops, pops.Select(p => p.Code).Where(c => !before.Contains(c)));
            var end = ServerBlocker.BlockedRules().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Check(end.SetEquals(before), $"Firewall restaurado como estaba ({end.Count} bloqueadas)");

            // 4) Novedades
            L("--- Novedades");
            var notes = Updater.WhatsNew("## x\n<!--notes-->\r\n- **Uno** con `code`\r\n- Dos\r\n<!--/notes-->\nresto");
            L(notes);
            Check(notes == "•  Uno con code\n•  Dos", "Se extraen y limpian las novedades");
            Check(Updater.WhatsNew("sin marcas") == "", "Sin marcas no se muestra nada");

            // 5) Aviso de Windows
            await Notifier.ShowAsync("RNX Cache Cleaner", "Prueba del aviso de limpieza programada", 4000);
            L("[INFO] Aviso mostrado (comprobar a ojo)");
        }
        catch (Exception ex) { L("[FAIL] Excepcion: " + ex); ok = false; }
        finally
        {
            try { if (Directory.Exists(undoDir)) Directory.Delete(undoDir, true); } catch { }
            if (Directory.Exists(undoBak)) Directory.Move(undoBak, undoDir);
        }
        L(ok ? "RESULTADO: TODO OK" : "RESULTADO: HAY FALLOS");
        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8);
    }

    private static string Netsh(string args)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args)
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }
}
