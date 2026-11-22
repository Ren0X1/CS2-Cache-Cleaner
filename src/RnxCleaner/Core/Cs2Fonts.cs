using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Font Manager de CS2. La forma que funciona en CS2:
///  - la fuente va en  &lt;CS2&gt;\game\csgo\panorama\fonts
///  - y ahi mismo un fonts.conf (el juego original no lo tiene: usa el de game\core),
///    con la fuente en &lt;fontpattern&gt; y cada variante de Stratum2 redirigida a ella.
/// Volver a la original = quitar ese fonts.conf y la fuente. No se toca nada de Valve.
/// </summary>
public static partial class Cs2Fonts
{
    public sealed record FontFile(string Path, string Family);

    public const string RnxFontFile = "BebasNeue-Regular.ttf";
    private const string Marker = "<!-- RNX Cache Cleaner: custom font";

    /// <summary>game\csgo\panorama\fonts de la instalacion de CS2 (null si no hay CS2).</summary>
    public static string? GameFontsDir()
    {
        var lib = Cs2Modules.Cs2Library();
        if (lib is null) return null;
        var dir = Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive", "game", "csgo", "panorama", "fonts");
        return Directory.Exists(Path.GetDirectoryName(dir)) ? dir : null;
    }

    /// <summary>Nombre de familia real de un .ttf/.otf (el que hay que poner en fonts.conf).</summary>
    public static string? FamilyOf(string file)
    {
        try
        {
            var g = new GlyphTypeface(new Uri(file));
            var names = g.Win32FamilyNames.Count > 0 ? g.Win32FamilyNames : g.FamilyNames;
            if (names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var n)) return n;
            return names.Values.FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>FontFamily de WPF para previsualizar un archivo de fuente.</summary>
    public static FontFamily? Preview(FontFile f)
    {
        try { return new FontFamily(new Uri(Path.GetDirectoryName(f.Path)! + Path.DirectorySeparatorChar), "./#" + f.Family); }
        catch { return null; }
    }

    /// <summary>La Bebas Neue incluida (se extrae a tools).</summary>
    public static FontFile? RnxFont()
    {
        var dir = Path.Combine(AppPaths.Tools, "fonts");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, RnxFontFile);
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("fonts." + RnxFontFile);
        if (res is null) return null;
        if (!File.Exists(dest) || new FileInfo(dest).Length != res.Length)
            using (var o = File.Create(dest)) res.CopyTo(o);
        return FamilyOf(dest) is { } fam ? new FontFile(dest, fam) : null;
    }

    /// <summary>Fuentes instaladas en Windows (.ttf/.otf), de todo el equipo y del usuario.</summary>
    public static List<FontFile> Installed()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Scan(RegistryKey root, string fontsDir)
        {
            using var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (k is null) return;
            foreach (var name in k.GetValueNames())
            {
                if (k.GetValue(name) is not string v) continue;
                var path = Path.IsPathRooted(v) ? v : Path.Combine(fontsDir, v);
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if ((ext == ".ttf" || ext == ".otf") && File.Exists(path)) files.Add(path);
            }
        }
        Scan(Registry.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
        Scan(Registry.CurrentUser, Path.Combine(Sys.LocalAppData, "Microsoft", "Windows", "Fonts"));
        var list = new List<FontFile>();
        foreach (var f in files)
            if (FamilyOf(f) is { } fam) list.Add(new FontFile(f, fam));
        // Una entrada por familia: la variante "normal" si existe
        return list.GroupBy(f => f.Family, StringComparer.OrdinalIgnoreCase)
                   .Select(g => g.OrderBy(x => Regex.IsMatch(Path.GetFileNameWithoutExtension(x.Path), "(?i)bold|italic|light|black|thin|semi|medium|oblique|bd|it$|bi$|z$") ? 1 : 0)
                                 .ThenBy(x => x.Path.Length).First())
                   .OrderBy(f => f.Family, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ------------------------------------------------------------------
    //  Estado actual
    // ------------------------------------------------------------------
    public sealed record State(bool Custom, string? Family, bool ByRnx, double Size, List<string> FontFiles);

    [GeneratedRegex("<fontpattern>([^<]+\\.(?:ttf|otf))</fontpattern>", RegexOptions.IgnoreCase)]
    private static partial Regex FilePattern();
    [GeneratedRegex("<edit name=\"family\" mode=\"append\" binding=\"strong\">\\s*<string>([^<]+)</string>")]
    private static partial Regex FamilyEdit();
    [GeneratedRegex("<test name=\"family\">\\s*<string>Stratum2</string>\\s*</test>.*?<double>([0-9.]+)</double>", RegexOptions.Singleline)]
    private static partial Regex SizeOf();

    public static State Read(string fontsDir)
    {
        var conf = Path.Combine(fontsDir, "fonts.conf");
        if (!File.Exists(conf)) return new State(false, null, false, 0.95, []);
        var t = File.ReadAllText(conf);
        // La familia nueva es la que se anade a "Stratum2" (no las reglas originales de Valve)
        var custom = t.IndexOf("<!-- Custom fonts -->", StringComparison.Ordinal);
        var fam = FamilyEdit().Matches(custom >= 0 ? t[custom..] : t).Select(m => m.Groups[1].Value)
                              .FirstOrDefault(f => !f.StartsWith("Stratum2", StringComparison.OrdinalIgnoreCase));
        var size = SizeOf().Matches(custom >= 0 ? t[custom..] : t).Select(m => double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.95).FirstOrDefault(0.95);
        // Nunca se cuentan (ni se borran) las fuentes de Valve
        var files = FilePattern().Matches(t).Select(m => m.Groups[1].Value)
            .Where(f => !Regex.IsMatch(f, "^(noto|stratum)", RegexOptions.IgnoreCase) && File.Exists(Path.Combine(fontsDir, f)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new State(fam is not null, fam, t.Contains(Marker), size, files);
    }

    // ------------------------------------------------------------------
    //  Aplicar / volver a la original
    // ------------------------------------------------------------------
    private static string BackupDir => Path.Combine(AppPaths.Root, "undo", "cs2_fonts");

    /// <summary>Guarda el fonts.conf y las fuentes que hubiera antes (solo la primera vez).</summary>
    private static void BackupOnce(string fontsDir)
    {
        if (Directory.Exists(BackupDir)) return;
        var conf = Path.Combine(fontsDir, "fonts.conf");
        if (!File.Exists(conf)) return;
        Directory.CreateDirectory(BackupDir);
        File.Copy(conf, Path.Combine(BackupDir, "fonts.conf"), true);
        foreach (var f in Read(fontsDir).FontFiles)
            File.Copy(Path.Combine(fontsDir, f), Path.Combine(BackupDir, f), true);
    }

    public static void Apply(string fontsDir, FontFile font, double size)
    {
        var template = ReadTemplate();
        Directory.CreateDirectory(fontsDir);
        BackupOnce(fontsDir);

        // Fuera las fuentes que pusimos antes (si eran otras)
        var prev = Read(fontsDir);
        var file = Path.GetFileName(font.Path);
        foreach (var f in prev.FontFiles.Where(f => !f.Equals(file, StringComparison.OrdinalIgnoreCase)))
            TryDelete(Path.Combine(fontsDir, f));

        var dest = Path.Combine(fontsDir, file);
        if (!string.Equals(Path.GetFullPath(font.Path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal);
            File.Copy(font.Path, dest, true);
        }
        var conf = template.Replace("{FAMILY}", EscapeXml(font.Family))
                           .Replace("{EXT}", Path.GetExtension(file).ToLowerInvariant())
                           .Replace("{FILE}", EscapeXml(file))
                           .Replace("{SIZE}", size.ToString("0.##", CultureInfo.InvariantCulture));
        var confPath = Path.Combine(fontsDir, "fonts.conf");
        if (File.Exists(confPath)) File.SetAttributes(confPath, FileAttributes.Normal);
        File.WriteAllText(confPath, conf, new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(fontsDir, "conf.d"));
        Logger.Ok(T("lg.font.applied", font.Family, (int)Math.Round(size * 100)));
    }

    /// <summary>Quita el fonts.conf y las fuentes personalizadas: CS2 vuelve a su fuente.</summary>
    public static void RestoreOriginal(string fontsDir)
    {
        BackupOnce(fontsDir);
        var st = Read(fontsDir);
        foreach (var f in st.FontFiles) TryDelete(Path.Combine(fontsDir, f));
        TryDelete(Path.Combine(fontsDir, "fonts.conf"));
        var confd = Path.Combine(fontsDir, "conf.d");
        try { if (Directory.Exists(confd) && !Directory.EnumerateFileSystemEntries(confd).Any()) Directory.Delete(confd); } catch { }
        Logger.Ok(T("lg.font.original"));
    }

    private static string ReadTemplate()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("fonts.fonts.conf.template")
                      ?? throw new FileNotFoundException("fonts.conf.template");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static string EscapeXml(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    private static void TryDelete(string f)
    {
        try { if (File.Exists(f)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } } catch { }
    }
}
