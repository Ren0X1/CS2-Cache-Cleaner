using System.IO;
using System.Text.Json;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Historial de rendimiento por partida, a partir de los datos del HUD: cada sesion de un juego (1 minuto o mas con FPS)
/// se guarda con FPS medios, 1 % / 0,1 % low, temperaturas maximas y cargas medias.
/// </summary>
public static class HudHistory
{
    public sealed record Session(string Game, DateTime Start, int Seconds, double AvgFps, double Low1, double Low01,
                                 float? CpuTempMax, float? GpuTempMax, float? CpuLoadAvg, float? GpuLoadAvg);

    private static string File => Path.Combine(AppPaths.Root, "hud_history.json");
    private const int MaxSessions = 300, MinSeconds = 60, IdleSeconds = 15;

    // Sesion en curso
    private static string? _game;
    private static DateTime _start, _lastFps, _lastSample;
    private static int _samples;
    private static double _fpsSum, _low1Sum, _low01Min = double.MaxValue;
    private static float? _cpuMax, _gpuMax;
    private static double _cpuLoadSum, _gpuLoadSum;
    private static int _cpuLoadN, _gpuLoadN;

    public static event Action? Changed;

    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
        { "", "-", "explorer", "RNX_Cache_Cleaner", "dwm", "chrome", "msedge", "firefox", "brave", "opera", "Discord", "Spotify", "steamwebhelper" };

    /// <summary>Se llama en cada tic del HUD (4 veces por segundo); se muestrea una vez por segundo.</summary>
    public static void Feed(HudData d, DateTime now)
    {
        if ((now - _lastSample).TotalMilliseconds < 950) return;
        _lastSample = now;
        bool playing = d.Fps > 1 && !NotGames.Contains(d.Process);
        if (_game is not null && (!playing && (now - _lastFps).TotalSeconds > IdleSeconds || playing && !d.Process.Equals(_game, StringComparison.OrdinalIgnoreCase)))
            Flush(now);
        if (!playing) return;
        if (_game is null) { _game = d.Process; _start = now; }
        _lastFps = now;
        _samples++;
        _fpsSum += d.AvgFps > 0 ? d.AvgFps : d.Fps;
        _low1Sum += d.Low1;
        if (d.Low01 > 0) _low01Min = Math.Min(_low01Min, d.Low01);
        if (d.CpuTemp is { } ct) _cpuMax = Math.Max(_cpuMax ?? 0, ct);
        if (d.GpuTemp is { } gt) _gpuMax = Math.Max(_gpuMax ?? 0, gt);
        if (d.CpuLoad is { } cl) { _cpuLoadSum += cl; _cpuLoadN++; }
        if (d.GpuLoad is { } gl) { _gpuLoadSum += gl; _gpuLoadN++; }
    }

    /// <summary>Cierra la sesion en curso (al cambiar de juego, dejar de jugar o apagar el HUD).</summary>
    public static void Flush(DateTime? now = null)
    {
        if (_game is null) return;
        var end = now ?? DateTime.Now;
        int secs = (int)(_lastFps - _start).TotalSeconds;
        if (secs >= MinSeconds && _samples >= 30)
        {
            var s = new Session(_game, _start, secs, Math.Round(_fpsSum / _samples, 1), Math.Round(_low1Sum / _samples, 1),
                                _low01Min == double.MaxValue ? 0 : Math.Round(_low01Min, 1), _cpuMax, _gpuMax,
                                _cpuLoadN > 0 ? (float)(_cpuLoadSum / _cpuLoadN) : null, _gpuLoadN > 0 ? (float)(_gpuLoadSum / _gpuLoadN) : null);
            var all = Load();
            all.Add(s);
            if (all.Count > MaxSessions) all.RemoveRange(0, all.Count - MaxSessions);
            Save(all);
            Logger.Info(T("lg.hh.session", s.Game, s.Seconds / 60, s.AvgFps, s.Low1));
            Changed?.Invoke();
        }
        _game = null; _samples = 0; _fpsSum = _low1Sum = 0; _low01Min = double.MaxValue;
        _cpuMax = _gpuMax = null; _cpuLoadSum = _gpuLoadSum = 0; _cpuLoadN = _gpuLoadN = 0;
    }

    public static string? Current => _game;

    /// <summary>Solo para las capturas internas: partidas de ejemplo en memoria (no se guardan).</summary>
    internal static List<Session>? Demo;

    public static List<Session> Load()
    {
        if (Demo is not null) return Demo;
        try { return JsonSerializer.Deserialize<List<Session>>(System.IO.File.ReadAllText(File)) ?? []; }
        catch { return []; }
    }

    private static void Save(List<Session> all)
    {
        try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(all)); } catch { }
    }

    public static void Clear() { try { System.IO.File.Delete(File); } catch { } Changed?.Invoke(); }
}
