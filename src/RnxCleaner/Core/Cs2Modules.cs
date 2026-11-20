using System.IO;

using static RnxCleaner.Core.Loc;

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
            Logger.Detail(T("lg.sh.detected", found));
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
            Logger.Info(lib is null ? T("lg.sh.noCs2") : T("lg.sh.clean"));
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
            Logger.Ok(T("lg.sh.deleted", path));
            return Task.FromResult(StepStatus.Ok);
        }
        Logger.Error(T("lg.sh.fail"));
        return Task.FromResult(StepStatus.Warn);
    }

    /// <summary>El .reg del usuario si lo ha elegido (Ajustes) y existe; si no, el incluido.</summary>
    public static string? MouseReg(Settings s)
    {
        if (!string.IsNullOrWhiteSpace(s.RegPath) && File.Exists(s.RegPath)) return s.RegPath;
        if (!string.IsNullOrWhiteSpace(s.RegPath)) Logger.Warn(T("lg.custom.missing", s.RegPath));
        return AppPaths.ExtractTool("raton.reg");
    }


    /// <summary>Exporta la configuracion del raton actual de Windows (HKCU\Control Panel\Mouse) a un .reg.</summary>
    public static async Task<bool> ExportMouseAsync(string path, CancellationToken ct = default)
    {
        bool ok = await ExportMouseRawAsync(path, ct);
        if (ok) Logger.Ok(T("lg.mouse.exported", path));
        else Logger.Error(T("lg.mouse.exportFail", path));
        return ok;
    }

    public static async Task<bool> ExportMouseRawAsync(string path, CancellationToken ct = default)
    {
        var code = await Sys.RunAsync(Path.Combine(Environment.SystemDirectory, "reg.exe"),
                                      $@"export ""HKCU\Control Panel\Mouse"" ""{path}"" /y", 60, ct);
        return code == 0 && File.Exists(path) && new FileInfo(path).Length > 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint param, int[] vparam, uint winIni);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SystemParametersInfo")]
    private static extern bool SystemParametersInfoPtr(uint action, uint param, IntPtr vparam, uint winIni);

    /// <summary>
    /// Aplica al momento la velocidad y la aceleracion del registro (sin cerrar
    /// sesion). Las curvas de aceleracion se cargan al iniciar sesion.
    /// </summary>
    public static void ApplyMouseLive()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            if (k is null) return;
            int I(string n, int d) => int.TryParse(k.GetValue(n)?.ToString(), out var v) ? v : d;
            const uint SPI_SETMOUSE = 0x0004, SPI_SETMOUSESPEED = 0x0071, SPIF_SENDCHANGE = 0x2;
            SystemParametersInfo(SPI_SETMOUSE, 0, [I("MouseThreshold1", 6), I("MouseThreshold2", 10), I("MouseSpeed", 1)], SPIF_SENDCHANGE);
            SystemParametersInfoPtr(SPI_SETMOUSESPEED, 0, I("MouseSensitivity", 10), SPIF_SENDCHANGE);
        }
        catch { }
    }

    public static async Task<bool> ImportMouseAsync(Settings s, CancellationToken ct = default)
    {
        var reg = MouseReg(s);
        if (reg is not null && reg == s.RegPath) Logger.Detail(T("lg.mouse.custom", reg));
        if (reg is null) { Logger.Error(T("lg.mouse.missing")); return false; }
        await Undo.SaveMouseAsync(ct);
        var code = await Sys.RunAsync(Path.Combine(Environment.SystemDirectory, "reg.exe"), $"import \"{reg}\"", 60, ct);
        if (code == 0) { ApplyMouseLive(); Logger.Ok(T("lg.mouse.ok")); return true; }
        Logger.Error(T("lg.mouse.fail", code));
        return false;
    }

    public static async Task<bool> ImportNvidiaProfileAsync(Settings s, CancellationToken ct = default)
    {
        var exe = AppPaths.ExtractTool("nvidiaProfileInspector.exe");
        // Siempre el perfil RNX Performance incluido (el usuario no puede cambiarlo)
        var nip = AppPaths.ExtractTool("RNX_Performance.nip");
        if (exe is null || nip is null)
        {
            Logger.Error(T("lg.nv.missing"));
            return false;
        }
        Undo.SaveNvidia();
        var code = await Sys.RunAsync(exe, $"-silentImport \"{nip}\"", 120, ct);
        if (code == 0) { s.NvidiaApplied = DateTime.Now; s.Save(); Logger.Ok(T("lg.nv.ok")); return true; }
        Logger.Error(T("lg.nv.fail", code));
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
