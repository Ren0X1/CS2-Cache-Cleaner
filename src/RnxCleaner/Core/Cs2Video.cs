using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RnxCleaner.Core;

/// <summary>
/// cs2_video.txt (Steam\userdata\&lt;cuenta&gt;\730\local\cfg): opciones graficas de CS2.
/// Se edita conservando el resto del archivo tal cual; el bloqueo es el
/// atributo de solo lectura (asi CS2 no lo sobrescribe al cerrar).
/// </summary>
public static partial class Cs2Video
{
    public sealed record Account(string Id, string Name, string File);

    /// <summary>Cuentas de Steam de este PC que tienen cs2_video.txt.</summary>
    public static List<Account> Accounts()
    {
        var steam = SystemInfo.SteamPath();
        if (steam is null) return [];
        var names = PersonaNames(steam);
        var list = new List<Account>();
        var userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata)) return list;
        foreach (var dir in Directory.GetDirectories(userdata))
        {
            var id = Path.GetFileName(dir);
            var file = Path.Combine(dir, "730", "local", "cfg", "cs2_video.txt");
            if (!File.Exists(file) || !ulong.TryParse(id, out var acc) || acc == 0) continue;
            list.Add(new Account(id, names.GetValueOrDefault(id) ?? id, file));
        }
        return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Nombre visible de cada cuenta (config\loginusers.vdf, SteamID64 -> id de 32 bits).</summary>
    private static Dictionary<string, string> PersonaNames(string steam)
    {
        var map = new Dictionary<string, string>();
        try
        {
            var vdf = Path.Combine(steam, "config", "loginusers.vdf");
            if (!File.Exists(vdf)) return map;
            var text = File.ReadAllText(vdf);
            foreach (Match m in UserBlock().Matches(text))
            {
                if (!ulong.TryParse(m.Groups["id"].Value, out var id64)) continue;
                var name = PersonaRx().Match(m.Groups["body"].Value);
                if (name.Success) map[(id64 - 76561197960265728UL).ToString()] = name.Groups[1].Value;
            }
        }
        catch { }
        return map;
    }

    [GeneratedRegex("\"(?<id>7656\\d{13})\"\\s*\\{(?<body>[^}]*)\\}")]
    private static partial Regex UserBlock();
    [GeneratedRegex("\"PersonaName\"\\s*\"([^\"]*)\"")]
    private static partial Regex PersonaRx();

    [GeneratedRegex("^(\\s*)\"([^\"]+)\"(\\s+)\"([^\"]*)\"(.*)$")]
    private static partial Regex KvLine();

    // ------------------------------------------------------------------
    //  Lectura / escritura
    // ------------------------------------------------------------------
    public static Dictionary<string, string> Read(string file)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(file))
        {
            var m = KvLine().Match(line);
            if (m.Success) map[m.Groups[2].Value] = m.Groups[4].Value;
        }
        return map;
    }

    /// <summary>Cambia solo las claves indicadas y conserva todo lo demas (orden, formato, claves desconocidas).</summary>
    public static void Write(string file, IReadOnlyDictionary<string, string> changes)
    {
        var raw = File.ReadAllText(file);
        var nl = raw.Contains("\r\n") ? "\r\n" : "\n";   // CS2 lo escribe con CRLF: se respeta
        var lines = File.ReadAllLines(file).ToList();
        var pending = new Dictionary<string, string>(changes, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Count; i++)
        {
            var m = KvLine().Match(lines[i]);
            if (!m.Success || !pending.Remove(m.Groups[2].Value, out var v)) continue;
            lines[i] = $"{m.Groups[1].Value}\"{m.Groups[2].Value}\"{m.Groups[3].Value}\"{v}\"{m.Groups[5].Value}";
        }
        if (pending.Count > 0)
        {
            int close = lines.FindLastIndex(l => l.Trim() == "}");
            if (close < 0) close = lines.Count;
            lines.InsertRange(close, pending.Select(p => $"\t\"{p.Key}\"\t\t\"{p.Value}\""));
        }
        var attrs = File.GetAttributes(file);
        bool ro = attrs.HasFlag(FileAttributes.ReadOnly);
        if (ro) File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        try { File.WriteAllText(file, string.Join(nl, lines) + nl, new UTF8Encoding(false)); }
        finally { if (ro) File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly); }
    }

    public static bool IsLocked(string file) => File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly);

    public static void SetLocked(string file, bool locked)
    {
        var a = File.GetAttributes(file);
        File.SetAttributes(file, locked ? a | FileAttributes.ReadOnly : a & ~FileAttributes.ReadOnly);
    }

    /// <summary>Copia de seguridad la primera vez que la app toca el archivo (cs2_video.txt.rnxbak).</summary>
    public static void BackupOnce(string file)
    {
        var bak = file + ".rnxbak";
        if (!File.Exists(bak)) File.Copy(file, bak);
    }

    // ------------------------------------------------------------------
    //  Presets (los valores son los que usa CS2)
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Config completa de RNX (carpeta 730 de Ren0X1, sin lo que es de su cuenta)
    // ------------------------------------------------------------------
    private const string CfgPrefix = "cs2cfg\\";

    /// <summary>Carpeta ...\userdata\&lt;cuenta&gt;\730 de una cuenta.</summary>
    public static string Folder730(Account a) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(a.File)!, "..", ".."));

    public static string ConfigBackup(Account a) => Path.Combine(AppPaths.Root, "undo", $"cs2config_{a.Id}.zip");
    public static bool HasConfigBackup(Account a) => File.Exists(ConfigBackup(a));

    private static List<(string Rel, string Res)> RnxFiles() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith(CfgPrefix))
            .Select(n => (n[CfgPrefix.Length..].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar), n))
            .ToList();

    /// <summary>
    /// Copia la config de RNX en la cuenta. Antes guarda (una vez) los archivos
    /// que se van a sobrescribir, para poder restaurarlos. cs2_video.txt queda sin solo lectura.
    /// </summary>
    public static int ApplyRnxConfig(Account a, string? root730 = null)
    {
        var root = root730 ?? Folder730(a);
        var files = RnxFiles();
        if (files.Count == 0) throw new FileNotFoundException("cs2config");
        var bak = ConfigBackup(a);
        if (!File.Exists(bak))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(bak)!);
            using var zip = ZipFile.Open(bak, ZipArchiveMode.Create);
            foreach (var (rel, _) in files)
            {
                var src = Path.Combine(root, rel);
                if (File.Exists(src)) zip.CreateEntryFromFile(src, rel.Replace('\\', '/'));
            }
        }
        var asm = Assembly.GetExecutingAssembly();
        foreach (var (rel, res) in files)
        {
            var dest = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal);
            using var s = asm.GetManifestResourceStream(res)!;
            using var o = File.Create(dest);
            s.CopyTo(o);
        }
        return files.Count;
    }

    /// <summary>Vuelve a poner la config que tenia la cuenta antes de aplicar la de RNX.</summary>
    public static int RestoreConfig(Account a, string? root730 = null)
    {
        var root = root730 ?? Folder730(a);
        var bak = ConfigBackup(a);
        int n = 0;
        using (var zip = ZipFile.OpenRead(bak))
            foreach (var e in zip.Entries)
            {
                var dest = Path.Combine(root, e.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal);
                e.ExtractToFile(dest, overwrite: true);
                n++;
            }
        File.Delete(bak);
        return n;
    }

    public static readonly string[] Presets = ["fps", "balanced", "quality"];

    public static Dictionary<string, string> Preset(string name) => name switch
    {
        // Maximo FPS: lo que usa la mayoria de profesionales
        "fps" => new()
        {
            ["setting.videocfg_shadow_quality"] = "0", ["setting.videocfg_dynamic_shadows"] = "1",
            ["setting.videocfg_texture_detail"] = "0", ["setting.r_texturefilteringquality"] = "0",
            ["setting.shaderquality"] = "0", ["setting.videocfg_particle_detail"] = "0",
            ["setting.videocfg_ao_detail"] = "0", ["setting.videocfg_hdr_detail"] = "-1",
            ["setting.videocfg_fsr_detail"] = "0", ["setting.msaa_samples"] = "2", ["setting.r_csgo_cmaa_enable"] = "0",
            ["setting.mat_vsync"] = "0", ["setting.r_low_latency"] = "1",
        },
        "balanced" => new()
        {
            ["setting.videocfg_shadow_quality"] = "1", ["setting.videocfg_dynamic_shadows"] = "1",
            ["setting.videocfg_texture_detail"] = "1", ["setting.r_texturefilteringquality"] = "3",
            ["setting.shaderquality"] = "0", ["setting.videocfg_particle_detail"] = "1",
            ["setting.videocfg_ao_detail"] = "0", ["setting.videocfg_hdr_detail"] = "-1",
            ["setting.videocfg_fsr_detail"] = "0", ["setting.msaa_samples"] = "4", ["setting.r_csgo_cmaa_enable"] = "0",
            ["setting.mat_vsync"] = "0", ["setting.r_low_latency"] = "1",
        },
        _ => new()
        {
            ["setting.videocfg_shadow_quality"] = "3", ["setting.videocfg_dynamic_shadows"] = "1",
            ["setting.videocfg_texture_detail"] = "2", ["setting.r_texturefilteringquality"] = "5",
            ["setting.shaderquality"] = "1", ["setting.videocfg_particle_detail"] = "3",
            ["setting.videocfg_ao_detail"] = "2", ["setting.videocfg_hdr_detail"] = "3",
            ["setting.videocfg_fsr_detail"] = "0", ["setting.msaa_samples"] = "8", ["setting.r_csgo_cmaa_enable"] = "0",
            ["setting.mat_vsync"] = "0", ["setting.r_low_latency"] = "1",
        },
    };

    // ------------------------------------------------------------------
    //  Modos del monitor principal (resoluciones y Hz)
    // ------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);

    public static (List<(int W, int H)> Res, List<int> Hz) DisplayModes()
    {
        var res = new HashSet<(int, int)>();
        var hz = new HashSet<int>();
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
        {
            if (dm.dmBitsPerPel < 32 || dm.dmPelsWidth < 800) continue;
            res.Add((dm.dmPelsWidth, dm.dmPelsHeight));
            if (dm.dmDisplayFrequency > 1) hz.Add(dm.dmDisplayFrequency);
        }
        // Resoluciones 4:3 tipicas para jugar estirado aunque el monitor no las anuncie
        foreach (var r in new[] { (1280, 960), (1440, 1080), (1024, 768) }) res.Add(r);
        return (res.OrderByDescending(r => r.Item1 * r.Item2).ThenByDescending(r => r.Item1).ToList(),
                hz.OrderByDescending(h => h).ToList());
    }
}
