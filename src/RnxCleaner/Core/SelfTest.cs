using System.Diagnostics;
using Microsoft.Win32;
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

    /// <summary>--selftest-network: DNS, autotuning, OEM y su deshacer. Deja todo como estaba.</summary>
    public static async Task NetworkAsync(string reportPath)
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); }
        bool ok = true;
        void Check(bool cond, string what) { L((cond ? "[OK]   " : "[FAIL] ") + what); ok &= cond; }
        Logger.Written += e => L("       log: " + e.Text);
        var undoDir = Path.Combine(AppPaths.Root, "undo");
        var undoBak = undoDir + ".selftest";
        if (Directory.Exists(undoDir)) { if (Directory.Exists(undoBak)) Directory.Delete(undoBak, true); Directory.Move(undoDir, undoBak); }
        string Oem0()
        {
            using var k = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(Oem.Key);
            return string.Join(" | ", Oem.Values.Select(v => $"{v}={k?.GetValue(v)}"));
        }
        try
        {
            Loc.Set("es");
            var adapters = await NetTweaks.AdaptersAsync();
            foreach (var a in adapters) L($"Adaptador: {a.Name} (#{a.Index}) DNS fijo='{a.Static}' actual={await NetTweaks.CurrentDnsAsync(a.Index)}");
            Check(adapters.Count > 0, "Hay adaptadores fisicos conectados");
            var dnsBefore = new Dictionary<int, string>();
            foreach (var a in adapters) dnsBefore[a.Index] = await NetTweaks.CurrentDnsAsync(a.Index);
            var tcpBefore = await NetTweaks.AutoTuningAsync();
            var oemBefore = Oem0();
            L($"Autotuning antes: {tcpBefore}");
            L($"OEM antes: {oemBefore}");

            Check(await NetTweaks.ApplyAsync(CancellationToken.None) == StepStatus.Ok, "Red aplicada");
            foreach (var a in adapters) Check((await NetTweaks.CurrentDnsAsync(a.Index)) == "1.1.1.1,8.8.8.8", $"DNS de {a.Name} = 1.1.1.1, 8.8.8.8");
            Check(await NetTweaks.AutoTuningAsync() == "normal", "Autotuning en normal");
            Check(Undo.HasDns, "DNS anterior guardado para deshacer");

            Check(await Oem.ApplyAsync(CancellationToken.None) == StepStatus.Ok, "OEM aplicado");
            var oemNow = Oem0();
            L("OEM ahora: " + oemNow);
            Check(System.Text.RegularExpressions.Regex.IsMatch(oemNow, @"Model=RNX \d{5} "), "Modelo RNX + 5 cifras: " + Oem.Model());
            Check(Oem.Model() == Oem.Model(), "El modelo es siempre el mismo en este PC");
            Check(File.Exists(Oem.LogoPath) && new FileInfo(Oem.LogoPath).Length > 1000, "Logo en " + Oem.LogoPath);
            Check(Undo.HasOem, "OEM anterior guardado para deshacer");

            Check(await Undo.RestoreAsync(), "Deshacer sin errores");
            foreach (var a in adapters) Check((await NetTweaks.CurrentDnsAsync(a.Index)) == dnsBefore[a.Index], $"DNS de {a.Name} vuelve a '{dnsBefore[a.Index]}'");
            Check(await NetTweaks.AutoTuningAsync() == tcpBefore, "Autotuning vuelve a " + tcpBefore);
            Check(Oem0() == oemBefore, "Informacion OEM vuelve a la de antes");
            Check(!Undo.Available, "No queda nada pendiente de deshacer");

            var fix = AppPaths.ExtractTool("fixStoreIssues.cmd");
            Check(fix is not null && File.ReadAllBytes(fix).AsSpan().SequenceEqual(File.ReadAllBytes(@"D:\Programas Varios\fixStoreIssues.cmd")), "fixStoreIssues.cmd incluido identico al original");
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

    /// <summary>--selftest-video: todo sobre COPIAS de cs2_video.txt; los originales no se tocan.</summary>
    public static async Task VideoAsync(string reportPath)
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); }
        bool ok = true;
        void Check(bool cond, string what) { L((cond ? "[OK]   " : "[FAIL] ") + what); ok &= cond; }
        string Hash(string f) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))[..16] + (File.GetAttributes(f).HasFlag(FileAttributes.ReadOnly) ? " RO" : " RW");
        try
        {
            Loc.Set("es");
            var accs = Cs2Video.Accounts();
            foreach (var a in accs) L($"Cuenta: {a.Name} ({a.Id}) -> {a.File}");
            Check(accs.Count > 0 && accs.All(a => a.Name != a.Id), "Cuentas con cs2_video.txt y su nombre de Steam");
            var before = accs.ToDictionary(a => a.File, a => Hash(a.File));

            var dir = Path.Combine(Path.GetTempPath(), "rnx_video_test");
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, "cs2_video.txt");
            if (File.Exists(copy)) File.SetAttributes(copy, FileAttributes.Normal);
            File.Copy(accs[0].File, copy, true);
            File.SetAttributes(copy, FileAttributes.Normal);
            var orig = File.ReadAllBytes(copy);

            var map = Cs2Video.Read(copy);
            Check(map.Count >= 30 && map["setting.defaultres"].Length > 0, $"Lee {map.Count} claves (res {map["setting.defaultres"]}x{map["setting.defaultresheight"]})");
            Cs2Video.Write(copy, map.Where(k => k.Key.StartsWith("setting.")).ToDictionary(k => k.Key, k => k.Value));
            Check(File.ReadAllBytes(copy).AsSpan().SequenceEqual(orig), "Guardar sin cambios deja el archivo identico (CRLF incluido)");

            Cs2Video.BackupOnce(copy);
            Check(File.Exists(copy + ".rnxbak"), "Copia .rnxbak la primera vez");
            Cs2Video.Write(copy, Cs2Video.Preset("fps"));
            var m2 = Cs2Video.Read(copy);
            Check(Cs2Video.Preset("fps").All(kv => m2[kv.Key] == kv.Value), "Preset Maximos FPS aplicado");
            Check(m2["setting.defaultres"] == map["setting.defaultres"] && m2["VendorID"] == map["VendorID"] && m2.Count == map.Count, "El resto de claves se conserva");
            Check(File.ReadAllText(copy).Contains("\r\n") && !File.ReadAllText(copy).Replace("\r\n", "").Contains('\n'), "Sigue con saltos CRLF");

            Cs2Video.Write(copy, new Dictionary<string, string> { ["setting.nueva_clave_prueba"] = "7" });
            Check(Cs2Video.Read(copy)["setting.nueva_clave_prueba"] == "7" && File.ReadAllText(copy).TrimEnd().EndsWith("}"), "Una clave nueva entra antes de la llave final");

            Cs2Video.SetLocked(copy, true);
            Check(Cs2Video.IsLocked(copy), "Bloquear = solo lectura");
            bool threw = false;
            try { File.WriteAllText(copy, "x"); } catch (UnauthorizedAccessException) { threw = true; }
            Check(threw, "Bloqueado, nadie puede escribirlo (como CS2)");
            Cs2Video.SetLocked(copy, false);
            Check(!Cs2Video.IsLocked(copy), "Desbloquear quita el solo lectura");

            var (res, hz) = Cs2Video.DisplayModes();
            L($"Resoluciones del monitor: {res.Count} ({string.Join(", ", res.Take(6).Select(r => $"{r.W}x{r.H}"))}...) Hz: {string.Join(", ", hz)}");
            Check(res.Count > 3 && hz.Count > 0, "Resoluciones y Hz del monitor");

            // Config completa de RNX sobre una COPIA de una carpeta 730 (cuenta ficticia "selftest")
            var fake730 = Path.Combine(dir, "730");
            foreach (var f in Directory.GetFiles(Cs2Video.Folder730(accs[^1]), "*", SearchOption.AllDirectories))
            {
                var d = Path.Combine(fake730, Path.GetRelativePath(Cs2Video.Folder730(accs[^1]), f));
                Directory.CreateDirectory(Path.GetDirectoryName(d)!);
                File.Copy(f, d, true);
                File.SetAttributes(d, FileAttributes.Normal);
            }
            File.SetAttributes(Path.Combine(fake730, "local", "cfg", "cs2_video.txt"), FileAttributes.ReadOnly);
            var fakeAcc = new Cs2Video.Account("selftest", "selftest", Path.Combine(fake730, "local", "cfg", "cs2_video.txt"));
            if (File.Exists(Cs2Video.ConfigBackup(fakeAcc))) File.Delete(Cs2Video.ConfigBackup(fakeAcc));
            var keysBefore = File.ReadAllBytes(Path.Combine(fake730, "local", "cfg", "cs2_user_keys_0_slot0.vcfg"));
            int napplied = Cs2Video.ApplyRnxConfig(fakeAcc, fake730);
            Check(napplied == 9, $"Config de RNX aplicada ({napplied} archivos)");
            Check(!Cs2Video.IsLocked(fakeAcc.File), "cs2_video.txt queda SIN solo lectura");
            var conv = File.ReadAllText(Path.Combine(fake730, "local", "cfg", "cs2_user_convars_0_slot0.vcfg"));
            var keys = File.ReadAllText(Path.Combine(fake730, "local", "cfg", "cs2_user_keys_0_slot0.vcfg"));
            Check(!conv.Contains("\"name\"") && !keys.Contains("\"F6\"") && keys.Contains("+jump"), "Sin nombre ni bind F6, con el resto de binds");
            Check(File.Exists(Path.Combine(fake730, "remotecache.vdf")) && Cs2Video.HasConfigBackup(fakeAcc), "No toca remotecache.vdf y guarda la config anterior");
            int nrest = Cs2Video.RestoreConfig(fakeAcc, fake730);
            Check(File.ReadAllBytes(Path.Combine(fake730, "local", "cfg", "cs2_user_keys_0_slot0.vcfg")).AsSpan().SequenceEqual(keysBefore) && !Cs2Video.HasConfigBackup(fakeAcc), $"Restaurar devuelve la config anterior exacta ({nrest} archivos)");
            foreach (var f in Directory.GetFiles(fake730, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(fake730, true);

            foreach (var f in new[] { copy, copy + ".rnxbak" }) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
            Directory.Delete(dir);
            Check(accs.All(a => Hash(a.File) == before[a.File]), "Tus cs2_video.txt originales siguen identicos y bloqueados como estaban");
        }
        catch (Exception ex) { L("[FAIL] Excepcion: " + ex); ok = false; }
        L(ok ? "RESULTADO: TODO OK" : "RESULTADO: HAY FALLOS");
        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>--selftest-fonts: Font Manager sobre una COPIA de game\csgo\panorama\fonts.</summary>
    public static async Task FontsAsync(string reportPath)
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); }
        bool ok = true;
        void Check(bool cond, string what) { L((cond ? "[OK]   " : "[FAIL] ") + what); ok &= cond; }
        var undoDir = Path.Combine(AppPaths.Root, "undo");
        var undoBak = undoDir + ".selftest";
        if (Directory.Exists(undoDir)) { if (Directory.Exists(undoBak)) Directory.Delete(undoBak, true); Directory.Move(undoDir, undoBak); }
        try
        {
            Loc.Set("es");
            var real = Cs2Fonts.GameFontsDir();
            L("Carpeta real: " + real);
            Check(real is not null && real.EndsWith(Path.Combine("game", "csgo", "panorama", "fonts")), "Encuentra game/csgo/panorama/fonts de CS2");
            var realConf = Path.Combine(real!, "fonts.conf");
            var before = File.ReadAllBytes(realConf);
            var st0 = Cs2Fonts.Read(real!);
            L($"Estado real: custom={st0.Custom} familia={st0.Family} tamano={st0.Size} archivos={string.Join(",", st0.FontFiles)}");
            Check(st0.Custom && st0.Family == "Bebas Neue" && Math.Abs(st0.Size - 0.95) < 0.001, "Detecta tu Bebas Neue al 95 %");

            var dir = Path.Combine(Path.GetTempPath(), "rnx_fonts_test");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.Copy(realConf, Path.Combine(dir, "fonts.conf"));
            File.Copy(Path.Combine(real!, "BebasNeue-Regular.ttf"), Path.Combine(dir, "BebasNeue-Regular.ttf"));
            File.WriteAllText(Path.Combine(dir, "notosans-regular.ttf"), "valve");

            var rnx = Cs2Fonts.RnxFont();
            Check(rnx is not null && rnx.Family == "Bebas Neue", "Fuente de RNX incluida: " + rnx?.Family);
            Cs2Fonts.Apply(dir, rnx!, 0.95);
            var gen = File.ReadAllText(Path.Combine(dir, "fonts.conf")).Replace("\r\n", "\n");
            var mine = System.Text.Encoding.UTF8.GetString(before).Replace("\r\n", "\n");
            var genNoMarker = string.Join("\n", gen.Split('\n').Where(l => !l.Contains("RNX Cache Cleaner: custom font")));
            Check(genNoMarker == mine, "Con Bebas al 95 % genera exactamente tu fonts.conf (mas una linea de marca)");

            var installed = Cs2Fonts.Installed();
            L($"Fuentes instaladas: {installed.Count} (p. ej. {string.Join(", ", installed.Take(5).Select(f => f.Family))})");
            var arial = installed.FirstOrDefault(f => f.Family == "Arial");
            Check(installed.Count > 20 && arial is not null, "Lista las fuentes de Windows (Arial: " + arial?.Path + ")");
            Cs2Fonts.Apply(dir, arial!, 1.0);
            var st = Cs2Fonts.Read(dir);
            Check(st.Custom && st.ByRnx && st.Family == "Arial" && Math.Abs(st.Size - 1.0) < 0.001, "Cambiar a Arial al 100 %");
            Check(!File.Exists(Path.Combine(dir, "BebasNeue-Regular.ttf")) && File.Exists(Path.Combine(dir, Path.GetFileName(arial!.Path))), "Quita la fuente anterior y copia la nueva");
            Cs2Fonts.RestoreOriginal(dir);
            Check(!File.Exists(Path.Combine(dir, "fonts.conf")) && !File.Exists(Path.Combine(dir, Path.GetFileName(arial.Path))) && !Cs2Fonts.Read(dir).Custom, "Volver a la original quita fonts.conf y la fuente");
            Check(File.Exists(Path.Combine(dir, "notosans-regular.ttf")), "Nunca borra las fuentes de Valve");
            Directory.Delete(dir, true);
            Check(File.ReadAllBytes(realConf).AsSpan().SequenceEqual(before) && File.Exists(Path.Combine(real!, "BebasNeue-Regular.ttf")), "Tu CS2 real sigue intacto");
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

    /// <summary>--selftest-res: lee el registro real; escribe solo en HKCU\Software\RNX_selftest.</summary>
    public static async Task ResAsync(string reportPath)
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); }
        bool ok = true;
        void Check(bool cond, string what) { L((cond ? "[OK]   " : "[FAIL] ") + what); ok &= cond; }
        const string TestKey = @"Software\RNX_selftest";
        try
        {
            Loc.Set("es");
            var gpus = CustomRes.Gpus();
            foreach (var g in gpus) L($"GPU: {g.Name} ({g.Vendor}) -> {g.DriverKey}");
            Check(gpus.Count > 0 && gpus[0].Vendor == CustomRes.Vendor.Nvidia, "Detecta la grafica NVIDIA y su clave de driver");
            var real = CustomRes.Read(gpus[0]);
            L("Tu lista real: " + string.Join(" ", real));
            Check(real.Contains(new CustomRes.Res(1352, 1080)), "Lee tu 1352x1080 creada con SRE");
            Check(CustomRes.HasBackup(gpus[0]), "Detecta la copia del driver (NV_Modes.bak)");
            var (modes, _) = Cs2Video.DisplayModes();
            L("Windows ofrece 1352x1080: " + modes.Contains((1352, 1080)));

            // --- NVIDIA sobre una copia en HKCU
            using (var src = CustomRes.Open(gpus[0], false)!)
            {
                Registry.CurrentUser.DeleteSubKeyTree(TestKey, false);
                using var k = Registry.CurrentUser.CreateSubKey(TestKey);
                var origBak = (string[])src.GetValue("NV_Modes.bak")!;
                k.SetValue("NV_Modes", origBak, RegistryValueKind.MultiString);   // estado "de fabrica" del driver
                var fab = CustomRes.ReadNvidia(k);
                L("Lista de fabrica: " + string.Join(" ", fab));
                Check(fab.Count > 10 && !fab.Contains(new CustomRes.Res(1352, 1080)), "Lee la lista de fabrica del driver");
                var withNew = fab.Append(new CustomRes.Res(1352, 1080)).Append(new CustomRes.Res(1176, 664)).ToList();
                CustomRes.WriteNvidia(k, withNew);
                var nv = ((string[])k.GetValue("NV_Modes")!)[0];
                L("NV_Modes escrito: " + nv);
                Check(nv.StartsWith("{*}SHV ") && nv.EndsWith("=1") && nv.Contains(" 1352x1080 ") && nv.Contains(" 1176x664"), "Formato NV_Modes como SRE");
                Check(((string[])k.GetValue("NV_Modes.bak")!).SequenceEqual(origBak), "Guarda la lista original en NV_Modes.bak");
                var mine = ((string[])src.GetValue("NV_Modes")!)[0];
                CustomRes.WriteNvidia(k, CustomRes.ReadNvidia(k).Where(r => r != new CustomRes.Res(1176, 664)));
                Check(((string[])k.GetValue("NV_Modes")!)[0] == mine, "Con tu misma lista genera exactamente tu NV_Modes de SRE");
                Check(CustomRes.RestoreNvidia(k) && ((string[])k.GetValue("NV_Modes")!).SequenceEqual(origBak) && k.GetValue("NV_Modes.bak") is null, "Restaurar deja el driver como venia");
            }

            // --- AMD simulado
            using (var k = Registry.CurrentUser.CreateSubKey(TestKey + @"\amd"))
            {
                var def = CustomRes.ReadAmd(k);
                Check(def.Count == 32, "AMD: sin valores = 32 resoluciones de fabrica");
                CustomRes.WriteAmd(k, def.Where(r => r != new CustomRes.Res(1280, 720)).Append(new CustomRes.Res(1352, 1080)));
                var a = CustomRes.ReadAmd(k);
                Check(a.Contains(new CustomRes.Res(1352, 1080)) && !a.Contains(new CustomRes.Res(1280, 720)) && a.Count == 32, "AMD: anade 1352x1080 y restringe 1280x720");
                var bcd = (byte[])k.GetValue("DALNonStandardModesBCD1")!;
                Check(bcd.Length == 8 && bcd[0] == 0x13 && bcd[1] == 0x52 && bcd[2] == 0x10 && bcd[3] == 0x80, "AMD: BCD 13 52 10 80");
                Check(CustomRes.RestoreAmd(k) && CustomRes.ReadAmd(k).Count == 32 && k.GetValue("DALNonStandardModesBCD1") is null, "AMD: restaurar vuelve a fabrica");
            }
        }
        catch (Exception ex) { L("[FAIL] Excepcion: " + ex); ok = false; }
        finally { Registry.CurrentUser.DeleteSubKeyTree(TestKey, false); }
        L("Clave de pruebas borrada: " + (Registry.CurrentUser.OpenSubKey(TestKey) is null));
        L(ok ? "RESULTADO: TODO OK" : "RESULTADO: HAY FALLOS");
        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>--selftest-vibrance: lee el color y "aplica" el mismo nivel (no cambia nada visible).</summary>
    public static async Task VibranceAsync(string reportPath)
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); }
        bool ok = true;
        void Check(bool cond, string what) { L((cond ? "[OK]   " : "[FAIL] ") + what); ok &= cond; }
        try
        {
            Loc.Set("es");
            var d = VibranceEngine.DetectDriver();
            Check(d is not null && d.Vendor == "NVIDIA", "Driver de color: " + d?.Vendor);
            var cur = d!.Get();
            L($"Color actual: {cur} % (rango {d.MinPercent}-{d.MaxPercent})");
            Check(cur is >= 50 and <= 100, "Lee el color actual");
            Check(d.Set(cur!.Value, primaryOnly: false), "Aplicar el mismo nivel en todas las pantallas");
            Check(d.Set(cur.Value, primaryOnly: true), "Aplicar el mismo nivel solo en la principal");
            Check(d.Get() == cur, "El color sigue igual que antes");
            Check(NvidiaVibrance.ToPercent(44) == 85 && NvidiaVibrance.ToLevel(85) == 44 && NvidiaVibrance.ToLevel(100) == 63 && NvidiaVibrance.ToLevel(50) == 0, "Conversion % <-> nivel como vibranceGUI (44 = 85 %)");

            var (list, winPct, primary) = VibranceEngine.ImportVibranceGui(true);
            foreach (var p in list) L($"  vibranceGUI: {p.Name} {p.Percent} % res={p.ChangeResolution} {p.Width}x{p.Height}@{p.Hz} -> {p.Path}");
            Check(list.Count == 5 && list.First(p => p.Name == "cs2").Percent == 85 && list.First(p => p.Name == "RustClient").Percent == 100, "Importa tus 5 juegos de vibranceGUI (CS2 85 %, Rust 100 %)");
            Check(winPct == 50 && primary == false, "Importa Windows 50 % y todas las pantallas");

            var s1 = new Settings { Language = "de", Palette = "oceano", VibranceEnabled = true, VibranceProfiles = list, ShaderPath = @"X:
oiaja" };
            var json = s1.ExportPortable();
            var s2 = new Settings();
            var shaderBefore = s2.ShaderPath;
            s2.ImportPortable(json);
            Check(s2.Language == "de" && s2.Palette == "oceano" && s2.VibranceEnabled && s2.VibranceProfiles.Count == 5 && s2.VibranceProfiles[1].Percent == list[1].Percent, "Exportar/importar la config portable");
            Check(s2.ShaderPath == shaderBefore && !json.Contains("ShaderPath") && !json.Contains("PowerPlanHash"), "Lo que depende del PC no viaja");

            var eng = VibranceEngine.Create(s1)!;
            eng.Start();
            Check(eng.Running, "Observador arrancado (SetWinEventHook)");
            eng.Stop();
            Check(!eng.Running && d.Get() == cur, "Observador parado y el color igual");
            eng.Dispose();
            d.Dispose();
        }
        catch (Exception ex) { L("[FAIL] Excepcion: " + ex); ok = false; }
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
