using System.IO;

namespace RnxCleaner.Core;

/// <summary>Modulos de CS2: shadercache, raton.reg y perfil NVIDIA (.nip).</summary>
public static class Cs2Modules
{
    // ------------------------------------------------------------------
    //  Shadercache: se busca sola en TODAS las bibliotecas de Steam
    // ------------------------------------------------------------------

    /// <summary>Carpetas de biblioteca de Steam (la principal + las de libraryfolders.vdf).</summary>
    public static List<string> SteamLibraries()
    {
        var libs = new List<string>();
        var steam = SystemInfo.SteamPath();
        if (steam is not null)
        {
            libs.Add(steam);
            foreach (var vdf in new[] { Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Path.Combine(steam, "config", "libraryfolders.vdf") })
            {
                try
                {
                    if (!File.Exists(vdf)) continue;
                    foreach (System.Text.RegularExpressions.Match m in
                             System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch { }
            }
        }
        // Por si el registro o el vdf fallan: rutas tipicas en cada disco
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            foreach (var sub in new[] { "SteamLibrary", "Steam", @"Program Files (x86)\Steam", @"Program Files\Steam", @"Games\Steam" })
                libs.Add(Path.Combine(d.RootDirectory.FullName, sub));

        return libs.Where(Directory.Exists)
                   .Select(l => Path.GetFullPath(l).TrimEnd('\\'))
                   .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Busca steamapps\shadercache\730 en todas las bibliotecas.</summary>
    public static string? FindShaderCache() =>
        SteamLibraries().Select(l => Path.Combine(l, "steamapps", "shadercache", "730")).FirstOrDefault(Directory.Exists);

    /// <summary>Biblioteca donde esta instalado CS2 (aunque aun no haya shadercache).</summary>
    public static string? Cs2Library() =>
        SteamLibraries().FirstOrDefault(l => File.Exists(Path.Combine(l, "steamapps", "appmanifest_730.acf")));

    /// <summary>
    /// Ruta a usar: la que el usuario puso a mano si existe; si no, la que se
    /// encuentre buscando en las bibliotecas (y se guarda para la proxima vez).
    /// </summary>
    public static string? ResolveShaderPath(Settings s)
    {
        var custom = (s.ShaderPath ?? "").Trim().Trim('"');
        if (custom.Length > 0 && Directory.Exists(custom)) return custom;

        var found = FindShaderCache();
        if (found is not null && !string.Equals(found, custom, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Detail("Shadercache detectada automaticamente: " + found);
            s.ShaderPath = found;
            s.Save();
        }
        return found;
    }

    public static Task<StepStatus> DeleteShaderCacheAsync(Settings s)
    {
        var path = ResolveShaderPath(s);
        if (path is null)
        {
            var lib = Cs2Library();
            Logger.Info(lib is null
                ? "Shadercache: no se ha encontrado CS2 en ninguna biblioteca de Steam, nada que borrar"
                : "Shadercache: ya esta limpia, nada que borrar (CS2 la regenera al abrir el juego)");
            return Task.FromResult(StepStatus.Skipped);
        }
        try { Directory.Delete(path, recursive: true); }
        catch
        {
            // Algun archivo bloqueado: borramos lo que se pueda
            Sys.EmptyDirectory(path);
            try { Directory.Delete(path, false); } catch { }
        }
        if (!Directory.Exists(path))
        {
            Logger.Ok("Shadercache eliminada: " + path);
            return Task.FromResult(StepStatus.Ok);
        }
        Logger.Error("No se pudo borrar la shadercache del todo (Steam abierto?)");
        return Task.FromResult(StepStatus.Warn);
    }

    public static async Task<bool> ImportMouseAsync(CancellationToken ct = default)
    {
        var reg = AppPaths.ExtractTool("raton.reg");
        if (reg is null) { Logger.Error("raton.reg no viene dentro del programa"); return false; }
        var code = await Sys.RunAsync(Path.Combine(Environment.SystemDirectory, "reg.exe"), $"import \"{reg}\"", 60, ct);
        if (code == 0) { Logger.Ok("Config raton aplicada"); return true; }
        Logger.Error($"Importando raton.reg (codigo {code})");
        return false;
    }

    public static async Task<bool> ImportNvidiaProfileAsync(CancellationToken ct = default)
    {
        var exe = AppPaths.ExtractTool("nvidiaProfileInspector.exe");
        var nip = AppPaths.ExtractTool("CS2.nip");
        if (exe is null || nip is null)
        {
            Logger.Error("NVIDIA: faltan nvidiaProfileInspector.exe o CS2.nip dentro del programa");
            return false;
        }
        var code = await Sys.RunAsync(exe, $"-silentImport \"{nip}\"", 120, ct);
        if (code == 0) { Logger.Ok("NVIDIA: perfil CS2 importado"); return true; }
        Logger.Error($"NVIDIA: fallo importando el perfil CS2 (codigo {code})");
        return false;
    }

    /// <summary>Abre NVIDIA Profile Inspector para tocar el perfil a mano.</summary>
    public static void OpenNvidiaInspector()
    {
        var exe = AppPaths.ExtractTool("nvidiaProfileInspector.exe");
        if (exe is null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppPaths.Tools });
    }
}
