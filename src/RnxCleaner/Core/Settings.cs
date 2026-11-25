using System.IO;
using System.Text.Json;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>Ajustes persistentes (sustituyen a las variables del .bat y a shadercache.txt).</summary>
public sealed class Settings
{
    public const string DefaultShaderPath = @"C:\Program Files (x86)\Steam\steamapps\shadercache\730";

    /// <summary>La casilla: si esta marcada, se hace backup de userdata y se sube a Google Drive.</summary>
    public bool BackupToDrive { get; set; }

    /// <summary>Carpeta de Google Drive donde se guardan los ZIP.</summary>
    public string DriveFolder { get; set; } = "RNX Cache Cleaner Backups";

    public string ShaderPath { get; set; } = DefaultShaderPath;

    /// <summary>.reg de raton propio (vacio = el incluido: configuracion de raton por defecto de Windows).</summary>
    public string RegPath { get; set; } = "";

    /// <summary>Tamano de la fuente personalizada de CS2 (1 = 100 %).</summary>
    public double FontSize { get; set; } = 0.95;

    /// <summary>Color en juegos (digital vibrance), como vibranceGUI.</summary>
    public bool VibranceEnabled { get; set; }
    public int VibranceDefault { get; set; } = 50;
    public bool VibrancePrimaryOnly { get; set; }
    public List<VibranceProfile> VibranceProfiles { get; set; } = [];

    /// <summary>Arrancar con Windows en segundo plano (tarea con permisos de admin).</summary>
    public bool Autostart { get; set; }
    public bool TrayHintShown { get; set; }

    /// <summary>HUD de rendimiento (apagado por defecto).</summary>
    public bool HudEnabled { get; set; }
    public bool HudVisible { get; set; } = true;
    public int HudHotkeyMods { get; set; }
    public int HudHotkeyVk { get; set; }
    public string HudStyle { get; set; } = "a1";
    public string HudCorner { get; set; } = "tl";
    public double HudX { get; set; } = 40;
    public double HudY { get; set; } = 40;
    public bool HudLocked { get; set; } = true;
    public double HudScale { get; set; } = 1.0;
    public double HudOpacity { get; set; } = 0.58;
    /// <summary>Ajustes de Windows aplicados por el usuario desde RNX (ids de WinTweaks).</summary>
    public List<string> WinTweaks { get; set; } = [];
    /// <summary>Drivers PC descartados por el usuario ("dev:nombre|etiqueta" o "comp:id|etiqueta"): no vuelven a salir en los analisis.</summary>
    public List<string> PcIgnore { get; set; } = [];
    public bool HudCpu { get; set; } = true;
    public bool HudRam { get; set; } = true;
    public bool HudGpu { get; set; } = true;
    public bool HudFps { get; set; } = true;
    public bool HudGraph { get; set; } = true;

    /// <summary>Cuenta de Steam elegida en Video CS2.</summary>
    public string VideoAccount { get; set; } = "";

    /// <summary>Punto de restauracion de Windows antes de Completa y Todo.</summary>
    public bool RestorePoint { get; set; } = true;

    /// <summary>Limpieza programada: off, logon, daily, weekly; modo quick, full o todo.</summary>
    public string ScheduleFreq { get; set; } = "off";
    public string ScheduleMode { get; set; } = "full";

    /// <summary>Ultima vez que se importo el perfil NVIDIA desde la app.</summary>
    public DateTime? NvidiaApplied { get; set; }

    /// <summary>Huella del plan de energia importado por ultima vez (para reimportarlo si cambia).</summary>
    public string PowerPlanHash { get; set; } = "";

    /// <summary>Idioma (en, es, ru, de, fr, zh), color y apariencia (system, light, dark).</summary>
    public string Language { get; set; } = "en";
    public string Palette { get; set; } = "ambar";
    public string ThemeMode { get; set; } = "system";

    /// <summary>false hasta que el usuario pasa la pantalla de primer arranque.</summary>
    public bool FirstRunDone { get; set; }

    /// <summary>Ultimo modo elegido en la pantalla de inicio: quick, full o todo.</summary>
    public string LastMode { get; set; } = "todo";

    /// <summary>Archivos mas grandes que esto se quedan fuera del ZIP.</summary>
    public int BackupMaxMB { get; set; } = 500;

    /// <summary>Carpetas de userdata que no se guardan (capturas, clips, caches).</summary>
    public List<string> BackupExclude { get; set; } = ["760", "gamerecordings", "inventorymsgcache", "ugc", "ugcmsgcache"];

    /// <summary>Opciones marcadas en "Personalizada" (ids de CleanupCatalog).</summary>
    public List<string> CustomSelection { get; set; } = [];

    /// <summary>Hash del ultimo ZIP subido a Drive por SteamID (para no resubir lo mismo).</summary>
    public Dictionary<string, string> DriveUploaded { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Lo que viaja con el backup a Drive y se restaura en otro PC. Fuera queda lo que
    /// depende del equipo: rutas, plan de energia, .reg propio, tareas programadas...
    /// </summary>
    public sealed class Portable
    {
        public int Format { get; set; } = 1;
        public string? Language { get; set; }
        public string? Palette { get; set; }
        public string? ThemeMode { get; set; }
        public string? LastMode { get; set; }
        public bool? BackupToDrive { get; set; }
        public string? DriveFolder { get; set; }
        public int? BackupMaxMB { get; set; }
        public List<string>? BackupExclude { get; set; }
        public List<string>? CustomSelection { get; set; }
        public bool? RestorePoint { get; set; }
        public double? FontSize { get; set; }
        public bool? VibranceEnabled { get; set; }
        public int? VibranceDefault { get; set; }
        public bool? VibrancePrimaryOnly { get; set; }
        public List<VibranceProfile>? VibranceProfiles { get; set; }
        public bool? HudEnabled { get; set; }
        public int? HudHotkeyMods { get; set; }
        public int? HudHotkeyVk { get; set; }
        public string? HudStyle { get; set; }
        public string? HudCorner { get; set; }
        public double? HudX { get; set; }
        public double? HudY { get; set; }
        public double? HudScale { get; set; }
        public double? HudOpacity { get; set; }
        public bool[]? HudRows { get; set; }
        public List<string>? WinTweaks { get; set; }
        public List<string>? PcIgnore { get; set; }
    }

    public string ExportPortable() => JsonSerializer.Serialize(new Portable
    {
        Language = Language, Palette = Palette, ThemeMode = ThemeMode, LastMode = LastMode,
        BackupToDrive = BackupToDrive, DriveFolder = DriveFolder, BackupMaxMB = BackupMaxMB,
        BackupExclude = BackupExclude, CustomSelection = CustomSelection, RestorePoint = RestorePoint,
        FontSize = FontSize, VibranceEnabled = VibranceEnabled, VibranceDefault = VibranceDefault,
        VibrancePrimaryOnly = VibrancePrimaryOnly, VibranceProfiles = VibranceProfiles,
        HudEnabled = HudEnabled, HudHotkeyMods = HudHotkeyMods, HudHotkeyVk = HudHotkeyVk, HudStyle = HudStyle, HudCorner = HudCorner,
        HudX = HudX, HudY = HudY, HudScale = HudScale, HudOpacity = HudOpacity, HudRows = [HudCpu, HudRam, HudGpu, HudFps, HudGraph],
        WinTweaks = WinTweaks, PcIgnore = PcIgnore,
    }, Json);

    /// <summary>Aplica lo que venga en el json (lo que no venga, se queda como esta).</summary>
    public void ImportPortable(string json)
    {
        var p = JsonSerializer.Deserialize<Portable>(json) ?? throw new InvalidDataException("rnx_settings.json");
        if (p.Language is { } l) Language = l;
        if (p.Palette is { } pa) Palette = pa;
        if (p.ThemeMode is { } tm) ThemeMode = tm;
        if (p.LastMode is { } lm) LastMode = lm;
        if (p.BackupToDrive is { } bd) BackupToDrive = bd;
        if (p.DriveFolder is { } df) DriveFolder = df;
        if (p.BackupMaxMB is { } mb) BackupMaxMB = mb;
        if (p.BackupExclude is { } be) BackupExclude = be;
        if (p.CustomSelection is { } cs) CustomSelection = cs;
        if (p.RestorePoint is { } rp) RestorePoint = rp;
        if (p.FontSize is { } fs) FontSize = fs;
        if (p.VibranceEnabled is { } ve) VibranceEnabled = ve;
        if (p.VibranceDefault is { } vd) VibranceDefault = vd;
        if (p.VibrancePrimaryOnly is { } vp) VibrancePrimaryOnly = vp;
        if (p.VibranceProfiles is { } vl) VibranceProfiles = vl;
        if (p.HudEnabled is { } he) HudEnabled = he;
        if (p.HudHotkeyMods is { } hm) HudHotkeyMods = hm;
        if (p.HudHotkeyVk is { } hk) HudHotkeyVk = hk;
        if (p.HudStyle is { } hs) HudStyle = hs;
        if (p.HudCorner is { } hc) HudCorner = hc;
        if (p.HudX is { } hx) HudX = hx;
        if (p.HudY is { } hy) HudY = hy;
        if (p.HudScale is { } hsc) HudScale = hsc;
        if (p.HudOpacity is { } ho) HudOpacity = ho;
        if (p.HudRows is { Length: 5 } hr) (HudCpu, HudRam, HudGpu, HudFps, HudGraph) = (hr[0], hr[1], hr[2], hr[3], hr[4]);
        if (p.WinTweaks is { } wt) WinTweaks = wt;
        if (p.PcIgnore is { } pi) PcIgnore = pi;
        FirstRunDone = true;
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.Settings)) ?? new Settings();
        }
        catch { /* ajustes corruptos: se empieza de cero */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            AppPaths.Ensure();
            var tmp = AppPaths.Settings + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, AppPaths.Settings, overwrite: true);
        }
        catch (Exception ex) { Logger.Warn(T("lg.settingsFail", ex.Message)); }
    }
}
