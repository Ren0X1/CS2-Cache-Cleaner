using System.IO;
using System.Text.Json;
using Microsoft.Win32;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>Apps preinstaladas (AppX): listar con su riesgo y desinstalar las elegidas.</summary>
public static class AppxApps
{
    public sealed record App(string Name, string FullName, string Version, TweakRisk? Risk);

    // Clasificacion de optimizerDuck: "seguras" se pueden quitar sin problema; "precaucion" son utiles para muchos usuarios.
    private static readonly string[] Safe =
    [
        "Microsoft.BingWeather", "Microsoft.BingNews", "Microsoft.News", "MicrosoftStart", "Microsoft.BingFinance", "Microsoft.BingSports",
        "Microsoft.BingFoodAndDrink", "Microsoft.BingHealthAndFitness", "Microsoft.BingTravel", "Microsoft.BingTranslator", "Microsoft.GetStarted",
        "Microsoft.WindowsTips", "Microsoft.WindowsFeedbackHub", "Microsoft.Messaging", "Microsoft.OneConnect", "Microsoft.People", "Microsoft.SkypeApp",
        "MicrosoftTeams", "MSTeams", "Microsoft.OneNote", "Microsoft.Todos", "Microsoft.MicrosoftOfficeHub", "Microsoft.Office.OneNote",
        "Microsoft.Office.Sway", "Microsoft.MicrosoftJournal", "Microsoft.MicrosoftPowerBIForWindows", "Microsoft.Wallet", "Microsoft.MSWallet",
        "Microsoft.Microsoft3DViewer", "Microsoft.MixedReality.Portal", "Microsoft.Print3D", "Microsoft.3DBuilder", "Microsoft.Paint3D",
        "Microsoft.ZuneVideo", "Microsoft.WindowsSoundRecorder", "Microsoft.StickyNotes", "Microsoft.MicrosoftStickyNotes",
        "Microsoft.MicrosoftSolitaireCollection", "Microsoft.WindowsAlarms", "Microsoft.549981C3F5F10", "Microsoft.Windows.DevHome",
        "Microsoft.WindowsMaps", "Microsoft.XboxApp", "Microsoft.Windows.AIHub", "Microsoft.MinecraftUWP", "Microsoft.NetworkSpeedTest",
        "Drawboard.DrawboardPDF", "SpotifyAB.SpotifyMusic", "Clipchamp.Clipchamp", "Disney.37853FC22B2CE", "Amazon.com.Amazon", "4DF9E0F8.Netflix",
        "BytedancePte.Ltd.TikTok", "king.com.CandyCrushSaga", "king.com.CandyCrushSodaSaga", "king.com.BubbleWitch3Saga", "AmazonVideo.PrimeVideo",
        "Facebook.Instagram", "FACEBOOK.FACEBOOK", "7EE7776C.LinkedInforWindows", "Flipboard", "iHeartRadio", "PandoraMediaInc", "TuneInRadio",
        "Duolingo-LearnLanguagesforFree", "PicsArt-PhotoStudio", "WinZipUniversal", "AdobeSystemsIncorporated.AdobePhotoshopExpress", "OneCalendar",
        "NYTCrossword", "MarchofEmpires", "flaregamesGmbH.RoyalRevolt", "Asphalt8Airborne", "FarmVille2CountryEscape", "HiddenCity",
        "CaesarsSlotsFreeCasino", "COOKINGFEVER", "DisneyMagicKingdoms", "PhototasticCollage", "AutodeskSketchBook",
        "PolarrPhotoEditorAcademicEdition", "Sidia.LiveWallpaper", "HULULLC.HULUPLUS", "SlingTV", "DolbyLaboratories.DolbyAccess",
    ];

    private static readonly string[] Caution =
    [
        "Microsoft.WindowsCalculator", "Microsoft.WindowsPhotos", "Microsoft.MSPaint", "Microsoft.Paint", "Microsoft.WindowsNotepad",
        "Microsoft.WindowsCamera", "Microsoft.ScreenSketch", "Microsoft.MicrosoftEdge", "Microsoft.MicrosoftEdge.Stable", "Microsoft.WindowsStore",
        "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.WindowsTerminal", "Microsoft.WindowsTerminalPreview",
        "Microsoft.RemoteDesktop", "Microsoft.PowerAutomateDesktop", "Microsoft.WindowsCommunicationsApps", "Microsoft.OutlookForWindows",
        "Microsoft.YourPhone", "MicrosoftWindows.CrossDevice", "Microsoft.StartExperiencesApp", "MicrosoftWindows.Client.WebExperience",
        "Microsoft.WidgetsPlatformRuntime", "Microsoft.BingSearch", "Microsoft.ZuneMusic", "MicrosoftCorporationII.MicrosoftFamily",
        "MicrosoftCorporationII.MicrosoftSupportDiagnosticTool", "MicrosoftCorporationII.QuickAssist", "Microsoft.GetHelp", "Microsoft.OneDrive",
        "Microsoft.Whiteboard", "Microsoft.M365Companions", "Microsoft.HEIFImageExtension", "Microsoft.WebMediaExtensions",
        "Microsoft.WebpImageExtension", "Microsoft.RawImageExtension", "Microsoft.VP9VideoExtensions", "Microsoft.AV1VideoExtension",
        "Microsoft.HEVCVideoExtension", "Microsoft.MPEG2VideoExtension", "Microsoft.XboxIdentityProvider", "Microsoft.XboxSpeechToTextOverlay",
        "Microsoft.XboxGameOverlay", "Microsoft.XboxGamingOverlay", "Microsoft.Xbox.TCUI", "Microsoft.GamingApp", "AD2F1837.HPAIExperienceCenter",
        "AD2F1837.HPConnectedMusic", "AD2F1837.HPConnectedPhotopoweredbySnapfish", "AD2F1837.HPDesktopSupportUtilities", "AD2F1837.HPEasyClean",
        "AD2F1837.HPFileViewer", "AD2F1837.HPJumpStarts", "AD2F1837.HPPCHardwareDiagnosticsWindows", "AD2F1837.HPPowerManager",
        "AD2F1837.HPPrinterControl", "AD2F1837.HPPrivacySettings", "AD2F1837.HPQuickDrop", "AD2F1837.HPQuickTouch", "AD2F1837.HPRegistration",
        "AD2F1837.HPSupportAssistant", "AD2F1837.HPSureShieldAI", "AD2F1837.HPSystemInformation", "AD2F1837.HPWelcome", "AD2F1837.HPWorkWell",
        "AD2F1837.myHP", "DellInc.DellSupportAssistforPCs", "DellInc.DellDigitalDelivery", "DellInc.DellMobileConnect", "E046963F.LenovoCompanion",
        "LenovoCompanyLimited.LenovoVantageService", "LGElectronics.LGMonitorApp", "EclipseManager", "ACGMediaPlayer", "ActiproSoftwareLLC",
        "CyberLinkMediaSuiteEssentials",
    ];

    public static TweakRisk? RiskOf(string name) =>
        Safe.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase)) ? TweakRisk.Safe
        : Caution.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase)) ? TweakRisk.Moderate : null;

    public static async Task<List<App>> ListAsync(CancellationToken ct)
    {
        const string ps = "Get-AppxPackage | Where-Object { -not $_.NonRemovable -and -not $_.IsFramework -and $_.SignatureKind -ne 'System' } | " +
                          "Select-Object Name, PackageFullName, @{n='Version';e={$_.Version.ToString()}} | ConvertTo-Json -Compress";
        var json = (await NetTweaks.PowerShellAsync(ps, ct)).Trim();
        if (json.Length == 0) return [];
        if (json.StartsWith('{')) json = "[" + json + "]";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(e => new App(e.GetProperty("Name").GetString() ?? "", e.GetProperty("PackageFullName").GetString() ?? "", e.GetProperty("Version").GetString() ?? "",
                                 RiskOf(e.GetProperty("Name").GetString() ?? "")))
            .GroupBy(a => a.Name).Select(g => g.First())
            .OrderBy(a => a.Risk switch { TweakRisk.Safe => 0, TweakRisk.Moderate => 2, _ => 1 }).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Desinstala para el usuario actual. Devuelve true si ya no esta instalada.</summary>
    public static async Task<bool> RemoveAsync(App a, CancellationToken ct)
    {
        var name = a.FullName.Replace("'", "''");
        var o = (await NetTweaks.PowerShellAsync($"try {{ Remove-AppxPackage -Package '{name}' -ErrorAction Stop; 'OK' }} catch {{ 'ERR ' + $_.Exception.Message }}", ct)).Trim();
        bool ok = o.StartsWith("OK");
        if (ok) Logger.Ok(T("lg.wa.removed", a.Name)); else Logger.Error(T("lg.wa.fail", a.Name, o.Length > 4 ? o[4..] : o));
        return ok;
    }
}

/// <summary>Programas que arrancan con Windows. Se activan/desactivan como en el Administrador de tareas (StartupApproved).</summary>
public static class StartupApps
{
    public sealed record Entry(string Name, string Command, string Where, RegistryKey Hive, string ApprovedKey, bool Enabled)
    {
        /// <summary>Solo en la carpeta Inicio: nombre del archivo (es el nombre del valor en StartupApproved).</summary>
        public string? FileName { get; init; }
    }

    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<Entry> List()
    {
        var list = new List<Entry>();
        void Reg(RegistryKey hive, string runKey, string approved, string where)
        {
            try
            {
                using var k = hive.OpenSubKey(runKey);
                if (k is null) return;
                foreach (var n in k.GetValueNames())
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    list.Add(new Entry(n, k.GetValue(n)?.ToString() ?? "", where, hive, approved, IsEnabled(hive, approved, n)));
                }
            }
            catch { }
        }
        void Folder(string dir, RegistryKey hive, string where)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                {
                    var n = Path.GetFileName(f);
                    list.Add(new Entry(Path.GetFileNameWithoutExtension(f), f, where, hive, "StartupFolder", IsEnabled(hive, "StartupFolder", n)) { FileName = n });
                }
            }
            catch { }
        }
        Reg(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", T("ws.st.user"));
        Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run", T("ws.st.all"));
        Reg(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", T("ws.st.all"));
        Folder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, T("ws.st.folder"));
        Folder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, T("ws.st.folder"));
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsEnabled(RegistryKey hive, string approved, string name)
    {
        try
        {
            using var k = hive.OpenSubKey(Approved + approved);
            return k?.GetValue(name) is not byte[] { Length: > 0 } b || (b[0] & 1) == 0;
        }
        catch { return true; }
    }

    public static void SetEnabled(Entry e, bool enabled)
    {
        var name = e.FileName ?? e.Name;
        using var k = e.Hive.CreateSubKey(Approved + e.ApprovedKey, true);
        var data = new byte[12];
        data[0] = (byte)(enabled ? 2 : 3);
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        k.SetValue(name, data, RegistryValueKind.Binary);
        Logger.Info(T(enabled ? "lg.ws.stOn" : "lg.ws.stOff", e.Name));
    }
}

/// <summary>Tareas programadas de terceros (fuera de \Microsoft). Las de RNX y las del usuario "RNX ..." nunca se tocan.</summary>
public static class ThirdPartyTasks
{
    public sealed record Item(string Path, string Name, string Author, bool Enabled, string NextRun);

    private static dynamic Service()
    {
        dynamic s = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        s.Connect();
        return s;
    }

    public static bool Protected(string name) => name.StartsWith("RNX", StringComparison.OrdinalIgnoreCase);

    public static List<Item> List()
    {
        var list = new List<Item>();
        void Walk(dynamic folder)
        {
            string path = folder.Path;
            if (path.StartsWith(@"\Microsoft", StringComparison.OrdinalIgnoreCase)) return;
            foreach (dynamic t in folder.GetTasks(1 /* incluir ocultas */))
            {
                try
                {
                    string name = t.Name;
                    if (Protected(name)) continue;
                    string author = "";
                    try { author = t.Definition.RegistrationInfo.Author ?? ""; } catch { }
                    // Tareas del propio Windows fuera de \Microsoft (p. ej. la del Explorador): no se listan
                    if (author.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) || author.StartsWith("$(@%SystemRoot%", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("CreateExplorerShellUnelevatedTask", StringComparison.OrdinalIgnoreCase)) continue;
                    string next = "";
                    try { DateTime n = t.NextRunTime; if (n.Year > 1900) next = n.ToString("g", Culture); } catch { }
                    list.Add(new Item((string)t.Path, name, author, (bool)t.Enabled, next));
                }
                catch { }
            }
            foreach (dynamic sub in folder.GetFolders(0)) Walk(sub);
        }
        try { Walk(Service().GetFolder(@"\")); } catch (Exception ex) { Logger.Detail("TASKS: " + ex.Message); }
        return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void SetEnabled(Item i, bool enabled)
    {
        if (Protected(i.Name)) return;
        var idx = i.Path.LastIndexOf('\\');
        dynamic task = Service().GetFolder(idx <= 0 ? @"\" : i.Path[..idx]).GetTask(i.Path[(idx + 1)..]);
        task.Enabled = enabled;
        Logger.Info(T(enabled ? "lg.ws.tkOn" : "lg.ws.tkOff", i.Name));
    }
}
