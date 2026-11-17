using System.IO;
using System.Text;

namespace RnxCleaner.Core;

public enum LogLevel { Info, Ok, Warn, Error, Detail }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Text);

/// <summary>
/// Log de la ejecucion actual (se sobrescribe en cada arranque, como el .bat)
/// y que ademas se muestra en vivo en la interfaz.
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();
    public static event Action<LogEntry>? Written;

    public static void StartSession(string mode)
    {
        AppPaths.Ensure();
        lock (Gate)
        {
            File.WriteAllText(AppPaths.Log,
                $"=== RNX Cache Cleaner v{AppPaths.Version} - {mode} - {DateTime.Now:dd/MM/yyyy HH:mm:ss} ==={Environment.NewLine}",
                Encoding.UTF8);
        }
    }

    public static void Info(string t) => Write(LogLevel.Info, t);
    public static void Ok(string t) => Write(LogLevel.Ok, t);
    public static void Warn(string t) => Write(LogLevel.Warn, t);
    public static void Error(string t) => Write(LogLevel.Error, t);
    public static void Detail(string t) => Write(LogLevel.Detail, t);

    public static void Write(LogLevel level, string text)
    {
        var e = new LogEntry(DateTime.Now, level, text);
        lock (Gate)
        {
            try
            {
                var tag = level switch { LogLevel.Error => "ERROR ", LogLevel.Warn => "AVISO ", _ => "" };
                File.AppendAllText(AppPaths.Log, $"[{e.Time:dd/MM/yyyy HH:mm:ss}] {tag}{text}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { /* el log nunca debe tumbar la limpieza */ }
        }
        Written?.Invoke(e);
    }

    public static string ReadAll()
    {
        try { return File.Exists(AppPaths.Log) ? File.ReadAllText(AppPaths.Log) : "No hay log todavia."; }
        catch (Exception ex) { return "No se pudo leer el log: " + ex.Message; }
    }
}
