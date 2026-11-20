using System.IO;
using System.Text.Json;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>Una ejecucion terminada (para el historial).</summary>
public sealed record RunRecord(DateTime When, string Mode, long FreedBytes, double Seconds, int Ok, int Warn, int Error, bool Cancelled);

/// <summary>
/// Historial de limpiezas en %LOCALAPPDATA%\RNX Cache Cleaner\history.json.
/// Se conservan las ultimas 200 ejecuciones.
/// </summary>
public static class History
{
    private const int Max = 200;
    private static string FilePath => Path.Combine(AppPaths.Root, "history.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static List<RunRecord> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch { /* historial corrupto: se empieza de cero */ }
        return [];
    }

    public static void Add(RunRecord r)
    {
        try
        {
            var all = Load();
            all.Add(r);
            if (all.Count > Max) all.RemoveRange(0, all.Count - Max);
            AppPaths.Ensure();
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) { Logger.Warn(T("lg.histFail", ex.Message)); }
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { }
    }
}
