namespace RnxCleaner.Core;

public enum RunMode { Quick, Full, Todo, NoBackup, Custom, Shader, Mouse, Nvidia, BackupOnly }

public enum StepStatus { Pending, Running, Ok, Warn, Error, Skipped }

/// <summary>Que incluye una ejecucion. Se construye a partir del modo (o de la seleccion personalizada).</summary>
public sealed class RunPlan
{
    public required string Name { get; init; }
    public List<CleanupItem> Cleanup { get; init; } = [];
    public bool Backup { get; set; }
    public bool Shader { get; set; }
    public bool Mouse { get; set; }
    public bool Nvidia { get; set; }

    /// <summary>Si es false (modo argumentos) nunca se abre el navegador para iniciar sesion.</summary>
    public bool Interactive { get; init; } = true;

    public static string Argument(RunMode m) => m switch
    {
        RunMode.Quick => "/rapida", RunMode.Full => "/completa", RunMode.Todo => "/todo",
        RunMode.NoBackup => "/nobackup", RunMode.Shader => "/shader", RunMode.Mouse => "/raton",
        RunMode.Nvidia => "/nvidia", RunMode.BackupOnly => "/backup", _ => ""
    };

    public static RunMode? Parse(string arg) => arg.Trim().ToLowerInvariant().TrimStart('/', '-') switch
    {
        "rapida" => RunMode.Quick, "completa" => RunMode.Full, "todo" => RunMode.Todo,
        "nobackup" => RunMode.NoBackup, "shader" => RunMode.Shader, "raton" => RunMode.Mouse,
        "nvidia" => RunMode.Nvidia, "backup" => RunMode.BackupOnly, _ => null
    };

    /// <summary>Mismo comportamiento que cada argumento del .bat.</summary>
    public static RunPlan For(RunMode mode, Settings s, SystemInfo sys, bool interactive)
    {
        bool backup = s.BackupToDrive;
        return mode switch
        {
            RunMode.Quick => new() { Name = "Limpieza rápida", Cleanup = [.. CleanupCatalog.Quick], Backup = backup, Interactive = interactive },
            // La completa del .bat aplicaba el perfil NVIDIA si habia GPU NVIDIA
            RunMode.Full => new() { Name = "Limpieza completa", Cleanup = [.. CleanupCatalog.Full], Nvidia = sys.IsNvidia, Backup = backup, Interactive = interactive },
            RunMode.Todo => new() { Name = "TODO", Cleanup = [.. CleanupCatalog.Full], Nvidia = sys.IsNvidia, Shader = true, Mouse = true, Backup = backup, Interactive = interactive },
            RunMode.NoBackup => new() { Name = "TODO sin backup", Cleanup = [.. CleanupCatalog.Full], Nvidia = sys.IsNvidia, Shader = true, Mouse = true, Backup = false, Interactive = interactive },
            RunMode.Shader => new() { Name = "Shadercache", Shader = true, Backup = backup, Interactive = interactive },
            RunMode.Mouse => new() { Name = "Configuración del ratón", Mouse = true, Backup = backup, Interactive = interactive },
            RunMode.Nvidia => new() { Name = "Perfil NVIDIA CS2", Nvidia = true, Backup = backup, Interactive = interactive },
            RunMode.BackupOnly => new() { Name = "Backup Steam", Backup = true, Interactive = interactive },
            _ => new() { Name = "Limpieza personalizada", Interactive = interactive },
        };
    }
}

/// <summary>Un paso visible en la pantalla de progreso.</summary>
public sealed class RunStep(string title, string icon, Func<IProgress<double>, CancellationToken, Task<StepStatus>> work)
{
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    public Func<IProgress<double>, CancellationToken, Task<StepStatus>> Work { get; } = work;
}

public sealed record RunSummary(string Name, long FreedBytes, TimeSpan Elapsed, int Ok, int Warn, int Error, bool Cancelled);

public static class Runner
{
    public static List<RunStep> BuildSteps(RunPlan plan, Settings s, SystemInfo sys)
    {
        var steps = new List<RunStep>();
        List<BackupZip> zips = [];

        // 1) Backup ANTES de limpiar (como el .bat), la subida al final
        if (plan.Backup)
        {
            steps.Add(new("Backup config Steam (userdata)", "", async (p, ct) =>
            {
                zips = await SteamBackup.RunAsync(s, p, ct);
                return zips.Count > 0 ? StepStatus.Ok : StepStatus.Warn;
            }));
        }

        foreach (var item in plan.Cleanup)
        {
            steps.Add(new(item.Title, "", async (_, ct) =>
            {
                await item.Run(ct);
                return StepStatus.Ok;
            }));
        }

        if (plan.Nvidia)
        {
            steps.Add(new("Perfil NVIDIA CS2", "", async (_, ct) =>
            {
                if (!sys.IsNvidia) { Logger.Warn("NVIDIA: no hay GPU NVIDIA en este equipo, omitido"); return StepStatus.Skipped; }
                return await Cs2Modules.ImportNvidiaProfileAsync(ct) ? StepStatus.Ok : StepStatus.Error;
            }));
        }

        if (plan.Shader)
        {
            steps.Add(new("Shadercache CS2", "", async (_, _) =>
            {
                if (SystemInfo.IsRunning("steam")) Logger.Warn("Steam esta abierto: puede que no se borre todo");
                return await Cs2Modules.DeleteShaderCacheAsync(s);
            }));
        }

        if (plan.Mouse)
        {
            steps.Add(new("Configuración del ratón (raton.reg)", "", async (_, ct) =>
                await Cs2Modules.ImportMouseAsync(ct) ? StepStatus.Ok : StepStatus.Error));
        }

        if (plan.Backup)
        {
            steps.Add(new("Subir backup a Google Drive", "", async (p, ct) =>
                await DriveSync.UploadAsync(s, zips, plan.Interactive, p, ct) ? StepStatus.Ok : StepStatus.Warn));
        }

        return steps;
    }
}
