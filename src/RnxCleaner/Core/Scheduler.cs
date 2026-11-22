using System.Security;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Limpieza programada con el Programador de tareas de Windows.
/// La tarea lanza "RNX_Cache_Cleaner.exe /&lt;modo&gt; /programada" con los
/// permisos mas altos (sin UAC), minimizada y sin preguntas.
/// </summary>
public static class Scheduler
{
    // En la raiz y con nombre propio: "RNX Cache Cleaner" a secas puede existir ya (tareas de la epoca del .bat)
    public const string TaskName = "RNX Cache Cleaner - Scheduled cleanup";
    public const string Flag = "/programada";

    public static readonly string[] Frequencies = ["off", "logon", "daily", "weekly"];
    public static readonly string[] Modes = ["quick", "full", "todo"];

    public sealed record Info(bool Exists, bool Enabled, DateTime? Next, DateTime? Last);

    private static dynamic Service()
    {
        var t = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler not available");
        dynamic svc = Activator.CreateInstance(t)!;
        svc.Connect();
        return svc;
    }

    public static Info Query()
    {
        try
        {
            dynamic task = Service().GetFolder(@"\").GetTask(TaskName);
            DateTime next = task.NextRunTime, last = task.LastRunTime;
            return new Info(true, (bool)task.Enabled, next.Year > 1900 ? next : (DateTime?)null, last.Year > 1900 ? last : (DateTime?)null);
        }
        catch { return new Info(false, false, null, null); }
    }

    /// <summary>Crea o sustituye la tarea; con "off" la borra.</summary>
    public static void Apply(string freq, string mode)
    {
        dynamic svc = Service();
        if (freq == "off")
        {
            try { svc.GetFolder(@"\").DeleteTask(TaskName, 0); Logger.Info(T("lg.sch.removed")); } catch { }
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("exe path");
        var arg = RunPlan.Argument(mode switch { "quick" => RunMode.Quick, "full" => RunMode.Full, _ => RunMode.Todo });
        svc.GetFolder(@"\").RegisterTask(TaskName, Xml(freq, exe, $"{arg} {Flag}"), 6 /* CREATE_OR_UPDATE */, null, null, 3 /* INTERACTIVE_TOKEN */);
        Logger.Info(T("lg.sch.set", T("sch." + freq), T("plan." + mode)));
    }

    // ------------------------------------------------------------------
    //  Inicio con Windows en segundo plano (bandeja + color en juegos)
    // ------------------------------------------------------------------
    public const string AutostartTask = "RNX Cache Cleaner - Background";

    public static bool AutostartExists()
    {
        try { Service().GetFolder(@"\").GetTask(AutostartTask); return true; } catch { return false; }
    }

    public static void SetAutostart(bool on)
    {
        dynamic root = Service().GetFolder(@"\");
        if (!on)
        {
            try { root.DeleteTask(AutostartTask, 0); } catch { }
            Logger.Info(T("lg.autostart.off"));
            return;
        }
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("exe path");
        root.RegisterTask(AutostartTask, Xml("logon", exe, App.TrayFlag, "PT10S"), 6, null, null, 3);
        Logger.Info(T("lg.autostart.on"));
    }

    private static string Xml(string freq, string exe, string args, string logonDelay = "PT3M")
    {
        string user = SecurityElement.Escape($@"{Environment.UserDomainName}\{Environment.UserName}");
        string start = DateTime.Today.ToString("yyyy-MM-dd") + "T12:00:00";
        string trigger = freq switch
        {
            "logon" => $"<LogonTrigger><UserId>{user}</UserId><Delay>{logonDelay}</Delay></LogonTrigger>",
            "daily" => $"<CalendarTrigger><StartBoundary>{start}</StartBoundary><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>",
            _ => $"<CalendarTrigger><StartBoundary>{start}</StartBoundary><ScheduleByWeek><WeeksInterval>1</WeeksInterval><DaysOfWeek><Sunday/></DaysOfWeek></ScheduleByWeek></CalendarTrigger>",
        };
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Author>RNX Cache Cleaner</Author><Description>RNX Cache Cleaner - scheduled cleanup</Description></RegistrationInfo>
              <Triggers>{trigger}</Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>{(args.Contains(App.TrayFlag) ? "PT0S" : "PT2H")}</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>{SecurityElement.Escape(args)}</Arguments></Exec></Actions>
            </Task>
            """;
    }
}

/// <summary>Aviso de Windows al terminar una limpieza programada.</summary>
public static class Notifier
{
    public static async Task ShowAsync(string title, string text, int keepMs = 8000)
    {
        try
        {
            using var icon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
                Text = "RNX Cache Cleaner",
                Visible = true,
            };
            icon.ShowBalloonTip(keepMs, title, text, System.Windows.Forms.ToolTipIcon.Info);
            await Task.Delay(keepMs);
            icon.Visible = false;
        }
        catch (Exception ex) { Logger.Detail("Notify: " + ex.Message); }
    }
}
