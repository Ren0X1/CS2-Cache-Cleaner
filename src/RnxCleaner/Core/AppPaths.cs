using System.IO;
using System.Reflection;

namespace RnxCleaner.Core;

/// <summary>
/// Todo lo que el programa guarda vive en %LOCALAPPDATA%\RNX Cache Cleaner:
/// ajustes, log, token de Google, herramientas extraidas y copias locales
/// de los backups. El .exe se puede mover o actualizar sin perder nada.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RNX Cache Cleaner");

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Log => Path.Combine(Root, "RNX_Cleaner.log");
    public static string Tools => Path.Combine(Root, "tools");
    public static string Backups => Path.Combine(Root, "backups");
    public static string GoogleToken => Path.Combine(Root, "google");

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "5.0";

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Tools);
        Directory.CreateDirectory(Backups);
    }

    /// <summary>
    /// Extrae una herramienta embebida (nvidiaProfileInspector.exe, CS2.nip,
    /// raton.reg) a la carpeta tools y devuelve su ruta. Solo reescribe el
    /// archivo si ha cambiado (p. ej. tras actualizar el programa).
    /// </summary>
    public static string? ExtractTool(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var res = asm.GetManifestResourceStream("tools." + name);
        if (res is null) return null;

        Directory.CreateDirectory(Tools);
        var dest = Path.Combine(Tools, name);
        using var ms = new MemoryStream();
        res.CopyTo(ms);
        var bytes = ms.ToArray();
        try
        {
            if (!File.Exists(dest) || !File.ReadAllBytes(dest).AsSpan().SequenceEqual(bytes))
                File.WriteAllBytes(dest, bytes);
        }
        catch (IOException)
        {
            // En uso (p. ej. el Inspector abierto): nos vale la copia existente
            if (!File.Exists(dest)) throw;
        }
        return dest;
    }
}
