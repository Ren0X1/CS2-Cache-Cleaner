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
