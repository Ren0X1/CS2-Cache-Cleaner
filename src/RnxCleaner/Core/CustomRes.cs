using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Resoluciones escaladas personalizadas (las que la grafica estira por GPU, p. ej. 1352x1080).
/// Misma idea que Scaled Resolution Editor de ToastyX (https://monitortests.com), escrita de cero:
/// se amplia la lista de resoluciones que el driver ofrece, en la clave del driver de la grafica.
///  - NVIDIA: valor NV_Modes  "{*}SHV 1920x1080 1352x1080 ...=1"   (copia original en NV_Modes.bak)
///  - AMD:    DALNonStandardModesBCD(1..n) en BCD de 8 bytes       (copia en ...BCD.bak)
/// Windows las ve despues de reiniciar el PC (o el driver).
/// </summary>
public static partial class CustomRes
{
    public enum Vendor { None, Nvidia, Amd }

    public sealed record Gpu(string Name, string DriverKey, Vendor Vendor);
    public readonly record struct Res(int W, int H) : IComparable<Res>
    {
        public int CompareTo(Res o) => W != o.W ? o.W.CompareTo(W) : o.H.CompareTo(H);   // de mayor a menor
        public override string ToString() => $"{W}x{H}";
    }

    public const int Min = 100, Max = 15999;
    public static bool Valid(int w, int h) => w is >= Min and <= Max && h is >= Min and <= Max;

    private const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\";

    /// <summary>Graficas NVIDIA/AMD presentes y la clave de su driver.</summary>
    public static List<Gpu> Gpus()
    {
        var list = new List<Gpu>();
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController");
            foreach (ManagementObject o in q.Get())
                using (o)
                {
                    var pnp = o["PNPDeviceID"]?.ToString();
                    if (string.IsNullOrEmpty(pnp)) continue;
                    using var en = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + pnp);
                    if (en?.GetValue("Driver") is not string drv) continue;
                    using var k = Registry.LocalMachine.OpenSubKey(ClassKey + drv);
                    var provider = k?.GetValue("ProviderName")?.ToString() ?? "";
                    var vendor = provider == "NVIDIA" ? Vendor.Nvidia
                               : provider is "Advanced Micro Devices, Inc." or "ATI Technologies Inc." ? Vendor.Amd : Vendor.None;
                    if (vendor != Vendor.None)
                        list.Add(new Gpu(k?.GetValue("DriverDesc")?.ToString() ?? o["Name"]?.ToString() ?? "GPU", ClassKey + drv, vendor));
                }
        }
        catch (Exception ex) { Logger.Detail("GPU: " + ex.Message); }
        return list;
    }

    public static RegistryKey? Open(Gpu g, bool write) => Registry.LocalMachine.OpenSubKey(g.DriverKey, write);

    // ------------------------------------------------------------------
    //  NVIDIA
    // ------------------------------------------------------------------
    private static readonly Res[] NvidiaDefault = [new(1600, 1200), new(1280, 1024), new(1024, 768), new(800, 600), new(640, 480)];

    [GeneratedRegex(@"(?<!\S)(\d{3,5})x(\d{3,5})")]
    private static partial Regex ResToken();

    private static string MultiSz(RegistryKey k, string name) =>
        k.GetValue(name) switch { string[] a => string.Join(" ", a), string s => s, _ => "" };

    public static List<Res> ReadNvidia(RegistryKey k)
    {
        var v = MultiSz(k, "NV_Modes");
        if (!v.Contains('=')) return [.. NvidiaDefault];
        // Toda la cadena: el driver pone algunos modos tras el "=" (p. ej. "...=1; 720x576x32,64=8032;")
        return Parse(v);
    }

    private static List<Res> Parse(string s) =>
        ResToken().Matches(s).Select(m => new Res(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)))
                  .Where(r => Valid(r.W, r.H)).Distinct().OrderBy(r => r).ToList();

    public static void WriteNvidia(RegistryKey k, IEnumerable<Res> list)
    {
        if (k.GetValue("NV_Modes.bak") is null)
            k.SetValue("NV_Modes.bak", k.GetValue("NV_Modes") is string[] orig ? orig : [""], RegistryValueKind.MultiString);
        var l = list.Distinct().OrderBy(r => r).ToList();
        var value = "{*}SHV " + (l.Count == 0 ? "99999x99999" : string.Join(" ", l)) + "=1";
        k.SetValue("NV_Modes", new[] { value }, RegistryValueKind.MultiString);
    }

    public static bool RestoreNvidia(RegistryKey k)
    {
        if (k.GetValue("NV_Modes.bak") is not string[] bak) return false;
        if (bak.All(string.IsNullOrEmpty)) k.DeleteValue("NV_Modes", false);
        else k.SetValue("NV_Modes", bak, RegistryValueKind.MultiString);
        k.DeleteValue("NV_Modes.bak", false);
        return true;
    }

    // ------------------------------------------------------------------
    //  AMD (BCD: 4 bytes ancho/alto en decimal empaquetado + 4 a cero)
    // ------------------------------------------------------------------
    private static readonly Res[] AmdDefault =
    [
        new(5120, 2880), new(5120, 2160), new(3840, 2400), new(3840, 2160), new(3840, 1620), new(3440, 1440), new(3200, 1800),
        new(3072, 1728), new(3024, 1296), new(2560, 1600), new(2560, 1440), new(2560, 1080), new(2310, 990), new(2100, 990),
        new(2048, 1536), new(1920, 1200), new(1920, 1080), new(1920, 820), new(1760, 990), new(1680, 1050), new(1680, 720),
        new(1600, 1200), new(1600, 1000), new(1600, 900), new(1366, 768), new(1280, 1024), new(1280, 800), new(1280, 720),
        new(1128, 634), new(1024, 768), new(800, 600), new(640, 480),
    ];
    private const string AmdModes = "DALNonStandardModesBCD", AmdRestricted = "DALRestrictedModesBCD";

    public static byte[] ToBcd(Res r) =>
    [
        (byte)((r.W / 1000 << 4) | (r.W / 100 % 10)), (byte)((r.W / 10 % 10 << 4) | (r.W % 10)),
        (byte)((r.H / 1000 << 4) | (r.H / 100 % 10)), (byte)((r.H / 10 % 10 << 4) | (r.H % 10)), 0, 0, 0, 0,
    ];

    public static Res FromBcd(byte[] b, int o) =>
        new((b[o] >> 4) * 1000 + (b[o] & 0xF) * 100 + (b[o + 1] >> 4) * 10 + (b[o + 1] & 0xF),
            (b[o + 2] >> 4) * 1000 + (b[o + 2] & 0xF) * 100 + (b[o + 3] >> 4) * 10 + (b[o + 3] & 0xF));

    private static byte[] ReadAmdBlob(RegistryKey k, string name)
    {
        var all = new List<byte>();
        void Add(object? v) { if (v is byte[] b) all.AddRange(b.Take(b.Length / 8 * 8)); }
        Add(k.GetValue(name));
        for (int i = 1; i <= 20 || k.GetValue(name + i) is not null; i++) Add(k.GetValue(name + i));
        return [.. all];
    }

    private static List<Res> Decode(byte[] blob)
    {
        var l = new List<Res>();
        for (int i = 0; i + 8 <= blob.Length; i += 8) l.Add(FromBcd(blob, i));
        return l;
    }

    /// <summary>Lista que ofrece el driver AMD: las de fabrica, menos las restringidas, mas las anadidas.</summary>
    public static List<Res> ReadAmd(RegistryKey k)
    {
        var restricted = Decode(ReadAmdBlob(k, AmdRestricted)).ToHashSet();
        return AmdDefault.Where(r => !restricted.Contains(r)).Concat(Decode(ReadAmdBlob(k, AmdModes)))
                         .Where(r => Valid(r.W, r.H)).Distinct().OrderBy(r => r).ToList();
    }

    private static void WriteAmdBlob(RegistryKey k, string name, byte[] data)
    {
        int size, start;
        if (data.Length <= 1280) { size = 64; start = 0; k.DeleteValue(name, false); }
        else { size = 128; start = size; k.SetValue(name, data[..size], RegistryValueKind.Binary); }
        int index = 1;
        for (int off = start; off < data.Length; off += size, index++)
            k.SetValue(name + index, data[off..Math.Min(off + size, data.Length)], RegistryValueKind.Binary);
        for (; index <= 20 || k.GetValue(name + index) is not null; index++) k.DeleteValue(name + index, false);
    }

    public static void WriteAmd(RegistryKey k, IEnumerable<Res> list)
    {
        var l = list.Distinct().ToHashSet();
        if (k.GetValue(AmdModes + ".bak") is null)
        {
            k.SetValue(AmdModes + ".bak", ReadAmdBlob(k, AmdModes), RegistryValueKind.Binary);
            k.SetValue(AmdRestricted + ".bak", ReadAmdBlob(k, AmdRestricted), RegistryValueKind.Binary);
        }
        var added = l.Where(r => !AmdDefault.Contains(r)).OrderBy(r => r).SelectMany(ToBcd).ToArray();
        var restricted = AmdDefault.Where(r => !l.Contains(r)).SelectMany(ToBcd).ToArray();
        WriteAmdBlob(k, AmdModes, added);
        WriteAmdBlob(k, AmdRestricted, restricted);
    }

    public static bool RestoreAmd(RegistryKey k)
    {
        if (k.GetValue(AmdModes + ".bak") is not byte[] bak) return false;
        WriteAmdBlob(k, AmdModes, bak);
        WriteAmdBlob(k, AmdRestricted, k.GetValue(AmdRestricted + ".bak") as byte[] ?? []);
        k.DeleteValue(AmdModes + ".bak", false);
        k.DeleteValue(AmdRestricted + ".bak", false);
        return true;
    }

    // ------------------------------------------------------------------
    //  Comun
    // ------------------------------------------------------------------
    public static List<Res> Read(Gpu g)
    {
        using var k = Open(g, false);
        if (k is null) return [];
        return g.Vendor == Vendor.Nvidia ? ReadNvidia(k) : ReadAmd(k);
    }

    public static bool HasBackup(Gpu g)
    {
        using var k = Open(g, false);
        return k?.GetValue(g.Vendor == Vendor.Nvidia ? "NV_Modes.bak" : AmdModes + ".bak") is not null;
    }

    public static void Write(Gpu g, IEnumerable<Res> list)
    {
        using var k = Open(g, true) ?? throw new InvalidOperationException(g.DriverKey);
        if (g.Vendor == Vendor.Nvidia) WriteNvidia(k, list); else WriteAmd(k, list);
    }

    public static bool RestoreDefault(Gpu g)
    {
        using var k = Open(g, true) ?? throw new InvalidOperationException(g.DriverKey);
        return g.Vendor == Vendor.Nvidia ? RestoreNvidia(k) : RestoreAmd(k);
    }

    public static void Add(Gpu g, Res r)
    {
        var l = Read(g);
        if (l.Contains(r)) return;
        l.Add(r);
        Write(g, l);
        Logger.Ok(T("lg.res.added", r, g.Name));
    }

    public static void Remove(Gpu g, Res r)
    {
        var l = Read(g);
        if (!l.Remove(r)) return;
        Write(g, l);
        Logger.Ok(T("lg.res.removed", r, g.Name));
    }
}
