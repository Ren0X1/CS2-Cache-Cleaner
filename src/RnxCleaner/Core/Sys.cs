using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RnxCleaner.Core;

/// <summary>Utilidades de bajo nivel: lanzar procesos ocultos y vaciar carpetas.</summary>
public static class Sys
{
    public static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    public static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>Ejecuta un comando sin ventana. Devuelve el codigo de salida (-1 si no arranca, -2 si se agota el tiempo).</summary>
    public static async Task<int> RunAsync(string file, string args, int timeoutSec = 300, CancellationToken ct = default)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                }
            };
            p.Start();
            // Hay que vaciar stdout/stderr o el proceso se bloquea si escribe mucho
            var o = p.StandardOutput.ReadToEndAsync(ct);
            var e = p.StandardError.ReadToEndAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                return -2;
            }
            await Task.WhenAll(o, e);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    public static Task<int> CmdAsync(string command, int timeoutSec = 300, CancellationToken ct = default) =>
        RunAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c " + command, timeoutSec, ct);

    /// <summary>Equivalente a "robocopy vacio destino /MIR": borra todo lo de dentro, deja la carpeta.</summary>
    public static void EmptyDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var info = new DirectoryInfo(dir);
        IEnumerable<FileSystemInfo> items;
        try { items = info.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }).ToList(); }
        catch { return; }
        foreach (var it in items)
        {
            try
            {
                if (it is DirectoryInfo d)
                {
                    // Los enlaces (junctions) se quitan sin entrar en ellos
                    if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) { d.Delete(false); continue; }
                    EmptyDirectory(d.FullName);
                    d.Attributes = FileAttributes.Normal;
                    d.Delete(false);
                }
                else
                {
                    it.Attributes = FileAttributes.Normal;
                    it.Delete();
                }
            }
            catch { /* en uso o sin permiso: se salta, como hacia robocopy */ }
        }
    }

    /// <summary>Equivalente a "del /f /q [/s] carpeta\patron".</summary>
    public static void DeleteFiles(string dir, string pattern, bool recursive = false)
    {
        if (!Directory.Exists(dir)) return;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = 0
            }).ToList();
        }
        catch { return; }
        foreach (var f in files) DeleteFile(f);
    }

    public static void DeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch { }
    }

    public static void KillProcess(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(true); p.WaitForExit(5000); } catch { }
            finally { p.Dispose(); }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public static void EmptyRecycleBin()
    {
        const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;
        try { SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound); } catch { }
    }

    public static string FormatBytes(long bytes)
    {
        double b = Math.Abs(bytes);
        string s = b >= 1L << 30 ? $"{b / (1L << 30):0.##} GB" : b >= 1 << 20 ? $"{b / (1 << 20):0.#} MB" : $"{b / 1024:0} KB";
        return bytes < 0 ? "-" + s : s;
    }
}
