using System.IO;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>Una zona de limpieza (equivale a una subrutina :LIMPIAR_* del .bat).</summary>
public sealed record CleanupItem(
    string Id,
    string TitleEs,
    string DescriptionEs,
    bool InQuick,
    bool InFull,
    bool Risky,
    Func<CancellationToken, Task> Run)
{
    /// <summary>Nombre y descripcion en el idioma actual (Strings.cs: z.id / zd.id).</summary>
    public string Title => T("z." + Id);
    public string Description => T("zd." + Id);
}

/// <summary>
/// Las 18 zonas de limpieza, en el mismo orden que la limpieza personalizada
/// del .bat. InQuick/InFull reproducen exactamente lo que hacian
/// :LIMPIEZA_RAPIDA y :LIMPIEZA_COMPLETA.
/// </summary>
public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupItem> All { get; } =
    [
        new("wtemp", "Windows Temp", @"C:\Windows\Temp", true, true, false, _ =>
        {
            Sys.EmptyDirectory(Path.Combine(Sys.WinDir, "Temp"));
            Logger.Detail(T("lg.z.wtemp"));
            return Task.CompletedTask;
        }),
        new("utemp", "Temp de usuario", "%TEMP%", true, true, false, _ =>
        {
            // ".net": DLL que el propio .exe (single-file) desempaqueta para la interfaz.
            //         Si se borran, el programa falla al cerrarse (DllNotFoundException).
            // "RNX_Update": descargas de la autoactualizacion.
            Sys.EmptyDirectory(Path.GetTempPath(), ".net", "RNX_Update");
            Logger.Detail(T("lg.z.utemp", Path.GetTempPath()));
            return Task.CompletedTask;
        }),
        new("pref", "Prefetch", @"C:\Windows\Prefetch", true, true, false, _ =>
        {
            Sys.DeleteFiles(Path.Combine(Sys.WinDir, "Prefetch"), "*");
            Logger.Detail(T("lg.z.pref"));
            return Task.CompletedTask;
        }),
        new("recent", "Archivos recientes", "Accesos .lnk (respeta los anclados del Explorador)", true, true, false, _ =>
        {
            // Solo los .lnk sueltos: AutomaticDestinations/CustomDestinations guardan
            // las Jump Lists y los anclados del Explorador y NO se tocan.
            Sys.DeleteFiles(Path.Combine(Sys.RoamingAppData, @"Microsoft\Windows\Recent"), "*.lnk");
            Logger.Detail(T("lg.z.recent"));
            return Task.CompletedTask;
        }),
        new("print", "Cola de impresión", "Trabajos atascados del spooler", false, true, false, async ct =>
        {
            await Sys.CmdAsync("net stop spooler", 60, ct);
            Sys.DeleteFiles(Path.Combine(Environment.SystemDirectory, @"spool\PRINTERS"), "*");
            await Sys.CmdAsync("net start spooler", 60, ct);
            Logger.Detail(T("lg.z.print"));
        }),
        new("dns", "Cache DNS", "ipconfig /flushdns", true, true, false, async ct =>
        {
            await Sys.RunAsync("ipconfig.exe", "/flushdns", 60, ct);
            Logger.Detail(T("lg.z.dns"));
        }),
        new("wup", "Cache Windows Update", @"SoftwareDistribution\Download", false, true, false, async ct =>
        {
            foreach (var s in new[] { "wuauserv", "bits", "cryptsvc" }) await Sys.CmdAsync("net stop " + s, 90, ct);
            Sys.EmptyDirectory(Path.Combine(Sys.WinDir, @"SoftwareDistribution\Download"));
            foreach (var s in new[] { "wuauserv", "bits", "cryptsvc" }) await Sys.CmdAsync("net start " + s, 90, ct);
            Logger.Detail(T("lg.z.wup"));
        }),
        new("thumb", "Miniaturas", "thumbcache (reinicia el Explorador)", false, true, false, async ct =>
        {
            Sys.KillProcess("explorer");
            await Task.Delay(800, ct);
            Sys.DeleteFiles(Path.Combine(Sys.LocalAppData, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", recursive: true);
            // Windows suele relanzar el Explorador solo; si no, lo arrancamos nosotros
            await Task.Delay(2500, ct);
            if (!SystemInfo.IsRunning("explorer"))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true }); } catch { }
            }
            Logger.Detail(T("lg.z.thumb"));
        }),
        new("icon", "Icon cache", "IconCache.db e iconcache_*.db", false, true, false, _ =>
        {
            Sys.DeleteFile(Path.Combine(Sys.LocalAppData, "IconCache.db"));
            Sys.DeleteFiles(Path.Combine(Sys.LocalAppData, @"Microsoft\Windows\Explorer"), "iconcache_*.db", recursive: true);
            Logger.Detail(T("lg.z.icon"));
            return Task.CompletedTask;
        }),
        new("store", "Cache Microsoft Store", "wsreset", false, true, false, async ct =>
        {
            // wsreset vacia la cache y al terminar SIEMPRE abre la Store.
            // La cerramos nosotros, salvo que ya estuviera abierta antes.
            bool storeWasOpen = SystemInfo.IsRunning("WinStore.App");
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wsreset.exe")
                    { UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized });
                if (p is not null)
                {
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(90));
                    try { await p.WaitForExitAsync(t.Token); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            if (!storeWasOpen)
            {
                // La Store tarda un poco en aparecer tras wsreset: la esperamos hasta 15 s
                for (int i = 0; i < 30; i++)
                {
                    if (SystemInfo.IsRunning("WinStore.App"))
                    {
                        await Task.Delay(1500, ct); // que termine de arrancar antes de cerrarla
                        Sys.KillProcess("WinStore.App");
                        Logger.Detail(T("lg.z.storeClosed"));
                        break;
                    }
                    await Task.Delay(500, ct);
                }
            }
            Logger.Detail(T("lg.z.store"));
        }),
        new("deliv", "Delivery Optimization", "Descargas P2P de Windows Update", false, true, false, _ =>
        {
            Sys.EmptyDirectory(Path.Combine(Sys.WinDir, @"SoftwareDistribution\DeliveryOptimization"));
            Logger.Detail(T("lg.z.deliv"));
            return Task.CompletedTask;
        }),
        new("dumps", "Memory dumps y logs CBS", "Minidump, MEMORY.DMP, WER", false, true, false, _ =>
        {
            Sys.DeleteFiles(Path.Combine(Sys.WinDir, "Minidump"), "*");
            Sys.DeleteFile(Path.Combine(Sys.WinDir, "MEMORY.DMP"));
            Sys.DeleteFiles(Path.Combine(Sys.WinDir, @"Logs\CBS"), "*.log");
            Sys.EmptyDirectory(Path.Combine(Sys.ProgramData, @"Microsoft\Windows\WER\ReportArchive"));
            Sys.EmptyDirectory(Path.Combine(Sys.ProgramData, @"Microsoft\Windows\WER\ReportQueue"));
            Logger.Detail(T("lg.z.dumps"));
            return Task.CompletedTask;
        }),
        new("font", "Cache de fuentes", "FontCache*.dat, FNTCACHE.DAT", false, true, false, _ =>
        {
            Sys.DeleteFiles(Sys.LocalAppData, "FontCache*.dat");
            Sys.DeleteFile(Path.Combine(Environment.SystemDirectory, "FNTCACHE.DAT"));
            Logger.Detail(T("lg.z.font"));
            return Task.CompletedTask;
        }),
        new("recycle", "Vaciar papelera", "Papelera de reciclaje de todas las unidades", false, true, false, _ =>
        {
            Sys.EmptyRecycleBin();
            Logger.Detail(T("lg.z.recycle"));
            return Task.CompletedTask;
        }),
        new("eventos", "Registros de eventos", "Visor de eventos y logs de Windows", true, true, true, async ct =>
        {
            // wevtutil el -> wevtutil cl de cada registro
            var names = new List<string>();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("wevtutil.exe", "el")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var output = await p.StandardOutput.ReadToEndAsync(ct);
                await p.WaitForExitAsync(ct);
                names.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            // En paralelo (de 8 en 8): hay ~1000 registros y uno a uno tarda minutos
            await Parallel.ForEachAsync(names, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
                async (n, t) => await Sys.RunAsync("wevtutil.exe", $"cl \"{n}\"", 30, t));

            Sys.DeleteFiles(Path.Combine(Environment.SystemDirectory, @"winevt\Logs"), "*.evtx", recursive: true);
            Sys.DeleteFiles(Path.Combine(Sys.WinDir, "Logs"), "*.log", recursive: true);
            Sys.DeleteFiles(Path.Combine(Sys.WinDir, "Logs"), "*.etl", recursive: true);
            Sys.DeleteFiles(Path.Combine(Sys.WinDir, "Panther"), "*.log", recursive: true);
            Sys.DeleteFile(Path.Combine(Sys.WinDir, @"Logs\DISM\dism.log"));
            Sys.DeleteFile(Path.Combine(Sys.WinDir, "WindowsUpdate.log"));
            Logger.Detail(T("lg.z.eventos", names.Count));
        }),
        new("gpu", "Cache de drivers gráficos", "DXCache/GLCache de NVIDIA, AMD e Intel", true, true, false, _ =>
        {
            string[] dirs =
            [
                Path.Combine(Sys.LocalAppData, @"NVIDIA\DXCache"),
                Path.Combine(Sys.LocalAppData, @"NVIDIA\GLCache"),
                Path.Combine(Sys.RoamingAppData, @"NVIDIA\ComputeCache"),
                Path.Combine(Sys.LocalAppData, @"NVIDIA Corporation\NV_Cache"),
                Path.Combine(Sys.LocalAppData, @"AMD\DxCache"),
                Path.Combine(Sys.LocalAppData, @"AMD\GLCache"),
                Path.Combine(Sys.LocalAppData, @"Intel\ShaderCache"),
            ];
            foreach (var d in dirs) Sys.EmptyDirectory(d);
            Logger.Detail(T("lg.z.gpu"));
            return Task.CompletedTask;
        }),
        new("discord", "Cache Discord", "Cierra Discord y borra su cache", false, false, false, async ct =>
        {
            if (SystemInfo.IsRunning("Discord"))
            {
                Sys.KillProcess("Discord");
                await Task.Delay(1000, ct);
            }
            foreach (var d in new[] { "Cache", "Code Cache", "GPUCache" })
                Sys.EmptyDirectory(Path.Combine(Sys.RoamingAppData, "discord", d));
            Logger.Detail(T("lg.z.discord"));
        }),
        new("ssd", "Optimizar SSD (TRIM)", "defrag C: /L", false, true, false, async ct =>
        {
            await Sys.RunAsync("defrag.exe", "C: /L", 900, ct);
            Logger.Detail(T("lg.z.ssd"));
        }),
    ];

    public static CleanupItem Get(string id) => All.First(i => i.Id == id);

    // Orden exacto de :LIMPIEZA_RAPIDA en el .bat
    public static IReadOnlyList<CleanupItem> Quick { get; } =
        new[] { "wtemp", "utemp", "pref", "recent", "gpu", "dns", "eventos" }.Select(Get).ToList();

    // Orden exacto de :LIMPIEZA_COMPLETA en el .bat
    public static IReadOnlyList<CleanupItem> Full { get; } =
        new[] { "wtemp", "utemp", "pref", "recent", "print", "dns", "wup", "thumb", "icon", "store", "deliv", "dumps", "gpu", "font", "recycle", "eventos", "ssd" }.Select(Get).ToList();
}
