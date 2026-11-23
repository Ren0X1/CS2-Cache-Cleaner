using System.IO;
using System.Management;
using System.Text.Json;
using Microsoft.Win32;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>Nivel de riesgo de un ajuste (misma escala que optimizerDuck).</summary>
public enum TweakRisk { Safe, Moderate, Risky }

public enum TweakCat { Performance, Gpu, Privacy, Ai, Experience, Services }

/// <summary>
/// Ajustes de Windows (inspirados en la seleccion y la clasificacion de riesgo de optimizerDuck,
/// implementacion propia de RNX). Cada ajuste es una lista de operaciones; al aplicarlo se guarda
/// el valor anterior de cada operacion y "Deshacer" lo deja exactamente como estaba.
/// </summary>
public static class WinTweaks
{
    // ------------------------------------------------------------------
    //  Operaciones
    // ------------------------------------------------------------------
    public abstract class Op
    {
        public abstract string Key { get; }
        /// <summary>false si no aplica en este PC (servicio o tarea inexistente...): no cuenta.</summary>
        public virtual bool Exists => true;
        public abstract bool IsApplied();
        /// <summary>Estado actual serializado (null = no existia).</summary>
        public abstract string? Capture();
        public abstract void Apply();
        public abstract void Restore(string? previous);
        /// <summary>Solo las operaciones de registro se pueden probar en la rama aislada.</summary>
        public virtual bool Testable => true;
    }

    /// <summary>Con valor, el registro se redirige a HKCU\{TestRoot} (pruebas internas, nunca toca el sistema).</summary>
    public static string? TestRoot { get; set; }

    private static (RegistryKey Hive, string Sub) Resolve(string path)
    {
        var i = path.IndexOf('\\');
        var hive = path[..i].ToUpperInvariant();
        var sub = path[(i + 1)..];
        bool lm = hive is "HKLM" or "HKEY_LOCAL_MACHINE";
        if (TestRoot is { } t) return (Registry.CurrentUser, $@"{t}\{(lm ? "HKLM" : "HKCU")}\{sub}");
        return (lm ? Registry.LocalMachine : Registry.CurrentUser, sub);
    }

    public sealed class RegOp(string path, string name, object value, RegistryValueKind kind = RegistryValueKind.DWord) : Op
    {
        public string Path { get; } = path;
        public string Name { get; } = name;
        public override string Key => $"reg:{Path}|{Name}";

        private object? Read()
        {
            var (h, s) = Resolve(Path);
            using var k = h.OpenSubKey(s);
            return k?.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        public override bool IsApplied()
        {
            var cur = Read();
            if (cur is null) return false;
            return kind switch
            {
                RegistryValueKind.DWord => cur is int i && i == Convert.ToInt32(value),
                RegistryValueKind.QWord => cur is long l && l == Convert.ToInt64(value),
                _ => cur.ToString() == value.ToString(),
            };
        }

        public override string? Capture()
        {
            var (h, s) = Resolve(Path);
            using var k = h.OpenSubKey(s);
            if (k?.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } v) return null;
            var kd = k.GetValueKind(Name);
            string data = v switch
            {
                byte[] b => Convert.ToBase64String(b),
                string[] a => string.Join("\u0001", a),
                _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "",
            };
            return $"{(int)kd}|{data}";
        }

        public override void Apply()
        {
            var (h, s) = Resolve(Path);
            using var k = h.CreateSubKey(s, true);
            k.SetValue(Name, value, kind);
        }

        public override void Restore(string? previous)
        {
            var (h, s) = Resolve(Path);
            if (previous is null)
            {
                using var k0 = h.OpenSubKey(s, true);
                k0?.DeleteValue(Name, false);
                return;
            }
            var bar = previous.IndexOf('|');
            var kd = (RegistryValueKind)int.Parse(previous[..bar]);
            var data = previous[(bar + 1)..];
            object v = kd switch
            {
                RegistryValueKind.DWord => unchecked((int)long.Parse(data, System.Globalization.CultureInfo.InvariantCulture)),
                RegistryValueKind.QWord => long.Parse(data, System.Globalization.CultureInfo.InvariantCulture),
                RegistryValueKind.Binary => Convert.FromBase64String(data),
                RegistryValueKind.MultiString => data.Split('\u0001'),
                _ => data,
            };
            using var k = h.CreateSubKey(s, true);
            k.SetValue(Name, v, kd);
        }
    }

    public enum StartType { Automatic, AutomaticDelayed, Manual, Disabled }

    /// <summary>Tipo de inicio de un servicio (se cambia en su clave del registro, como hace services.msc).</summary>
    public sealed class SvcOp(string name, StartType start) : Op
    {
        public string Name { get; } = name;
        public StartType Start { get; } = start;
        public override string Key => "svc:" + Name;
        private string Path => @"HKLM\SYSTEM\CurrentControlSet\Services\" + Name;

        public override bool Exists
        {
            get { var (h, s) = Resolve(Path); using var k = h.OpenSubKey(s); return k?.GetValue("Start") is int; }
        }

        private (int Start, int Delayed)? Read()
        {
            var (h, s) = Resolve(Path);
            using var k = h.OpenSubKey(s);
            if (k?.GetValue("Start") is not int st) return null;
            return (st, k.GetValue("DelayedAutostart") is int d ? d : -1);   // -1 = no existia
        }

        private (int Start, int Delayed) Target => Start switch
        {
            StartType.Automatic => (2, 0),
            StartType.AutomaticDelayed => (2, 1),
            StartType.Manual => (3, 0),
            _ => (4, 0),
        };

        public override bool IsApplied()
        {
            if (Read() is not { } cur) return true;
            var t = Target;
            return cur.Start == t.Start && (t.Start != 2 || Math.Max(cur.Delayed, 0) == t.Delayed);
        }

        public override string? Capture() => Read() is { } c ? $"{c.Start}|{c.Delayed}" : null;

        public override void Apply() => Write(Target.Start, Target.Delayed);

        public override void Restore(string? previous)
        {
            if (previous is null) return;
            var p = previous.Split('|');
            Write(int.Parse(p[0]), int.Parse(p[1]));
        }

        private void Write(int start, int delayed)
        {
            var (h, s) = Resolve(Path);
            // En la rama de pruebas la clave se crea; en el sistema real nunca se crea un servicio que no existe
            using var k = TestRoot is null ? h.OpenSubKey(s, true) : h.CreateSubKey(s, true);
            if (k is null) return;
            k.SetValue("Start", start, RegistryValueKind.DWord);
            if (delayed < 0) k.DeleteValue("DelayedAutostart", false);
            else if (start == 2 || k.GetValue("DelayedAutostart") is not null) k.SetValue("DelayedAutostart", delayed, RegistryValueKind.DWord);
        }
    }

    /// <summary>Tarea programada de Windows (se desactiva, nunca se borra).</summary>
    public sealed class TaskOp(string path) : Op
    {
        public string TaskPath { get; } = path;
        public override string Key => "task:" + TaskPath;
        public override bool Testable => false;

        private static dynamic Service()
        {
            var t = Type.GetTypeFromProgID("Schedule.Service")!;
            dynamic s = Activator.CreateInstance(t)!;
            s.Connect();
            return s;
        }

        private bool? Enabled()
        {
            try
            {
                var i = TaskPath.LastIndexOf('\\');
                dynamic task = Service().GetFolder(i <= 0 ? @"\" : TaskPath[..i]).GetTask(TaskPath[(i + 1)..]);
                return (bool)task.Enabled;
            }
            catch { return null; }
        }

        public override bool Exists => Enabled() is not null;
        public override bool IsApplied() => Enabled() is not true;
        public override string? Capture() => Enabled() is { } e ? (e ? "1" : "0") : null;
        public override void Apply() => Set(false);
        public override void Restore(string? previous) { if (previous == "1") Set(true); }

        private void Set(bool enabled)
        {
            var i = TaskPath.LastIndexOf('\\');
            dynamic task = Service().GetFolder(i <= 0 ? @"\" : TaskPath[..i]).GetTask(TaskPath[(i + 1)..]);
            task.Enabled = enabled;
        }
    }

    /// <summary>Hibernacion e inicio rapido (powercfg /h).</summary>
    public sealed class HibernateOp : Op
    {
        public override string Key => "hibernate";
        public override bool Testable => false;
        private static bool On()
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return k?.GetValue("HibernateEnabled") is not int i || i != 0;
        }
        public override bool IsApplied() => !On();
        public override string? Capture() => On() ? "1" : "0";
        public override void Apply() => Powercfg("/h off");
        public override void Restore(string? previous) { if (previous == "1") Powercfg("/h on"); }
        private static void Powercfg(string a) => Sys.RunAsync("powercfg.exe", a, 60).GetAwaiter().GetResult();
    }

    /// <summary>"Permitir que el equipo apague este dispositivo" en los concentradores raiz USB.</summary>
    public sealed class UsbHubPowerOp : Op
    {
        public override string Key => "usbhubs";
        public override bool Testable => false;

        private static List<ManagementObject> Hubs()
        {
            var list = new List<ManagementObject>();
            try
            {
                using var q = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSPower_DeviceEnable");
                foreach (ManagementObject o in q.Get())
                    if (o["InstanceName"] is string n && n.Contains(@"USB\ROOT", StringComparison.OrdinalIgnoreCase)) list.Add(o);
                    else o.Dispose();
            }
            catch { }
            return list;
        }

        public override bool Exists { get { var h = Hubs(); h.ForEach(o => o.Dispose()); return h.Count > 0; } }

        public override bool IsApplied()
        {
            var h = Hubs();
            try { return h.All(o => o["Enable"] is false); } finally { h.ForEach(o => o.Dispose()); }
        }

        public override string? Capture()
        {
            var h = Hubs();
            try { return JsonSerializer.Serialize(h.ToDictionary(o => (string)o["InstanceName"], o => o["Enable"] is true)); }
            finally { h.ForEach(o => o.Dispose()); }
        }

        public override void Apply() => Set(_ => false);

        public override void Restore(string? previous)
        {
            if (previous is null) return;
            var map = JsonSerializer.Deserialize<Dictionary<string, bool>>(previous) ?? [];
            Set(n => map.TryGetValue(n, out var e) ? e : null);
        }

        private static void Set(Func<string, bool?> want)
        {
            var h = Hubs();
            try
            {
                foreach (var o in h)
                    if (want((string)o["InstanceName"]) is { } e) { o["Enable"] = e; o.Put(); }
            }
            finally { h.ForEach(o => o.Dispose()); }
        }
    }

    // ------------------------------------------------------------------
    //  Graficas (clave del driver de cada adaptador de pantalla)
    // ------------------------------------------------------------------
    public enum GpuVendor { Nvidia, Amd, Intel }

    private static readonly Lazy<List<(GpuVendor Vendor, string Key)>> _gpus = new(ReadGpuKeys);
    public static List<(GpuVendor Vendor, string Key)> GpuKeys() => _gpus.Value;

    private static List<(GpuVendor Vendor, string Key)> ReadGpuKeys()
    {
        var list = new List<(GpuVendor, string)>();
        try
        {
            using var q = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_VideoController");
            foreach (ManagementObject o in q.Get())
                using (o)
                {
                    if (o["PNPDeviceID"] is not string pnp || pnp.Length == 0) continue;
                    using var en = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + pnp);
                    if (en?.GetValue("Driver") is not string drv) continue;
                    GpuVendor? v = pnp.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
                                 : pnp.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
                                 : pnp.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel : null;
                    if (v is { } vv) list.Add((vv, @"HKLM\SYSTEM\CurrentControlSet\Control\Class\" + drv));
                }
        }
        catch { }
        return list;
    }

    private static Func<List<Op>> Gpu(GpuVendor v, params (string Name, int Value)[] values) => () =>
        GpuKeys().Where(g => g.Vendor == v).SelectMany(g => values.Select(x => (Op)new RegOp(g.Key, x.Name, x.Value))).ToList();

    private static bool HasGpu(GpuVendor v) => GpuKeys().Any(g => g.Vendor == v);

    // ------------------------------------------------------------------
    //  Catalogo
    // ------------------------------------------------------------------
    public sealed record Tweak(string Id, TweakCat Cat, TweakRisk Risk, Func<List<Op>> Ops, Func<bool>? Available = null);

    private static RegOp R(string p, string n, object v, RegistryValueKind k = RegistryValueKind.DWord) => new(p, n, v, k);
    private static RegOp S(string p, string n, string v) => new(p, n, v, RegistryValueKind.String);
    private static Func<List<Op>> L(params Op[] ops) => () => [.. ops];

    private const string Mm = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string Dc = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    private const string Cdm = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string Cc = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent";
    private const string Ai = @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI";
    private const string Ws = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search";
    private const string Adv = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Al = @"HKLM\SYSTEM\CurrentControlSet\Control\WMI\Autologger\";

    private static long RamKb()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject o in q.Get()) using (o) return Convert.ToInt64(o["TotalVisibleMemorySize"]);
        }
        catch { }
        return 0;
    }

    public static readonly List<Tweak> All =
    [
        // ---------- Rendimiento ----------
        new("bgapps", TweakCat.Performance, TweakRisk.Moderate, L(
            R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1),
            R(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0))),
        new("svchost", TweakCat.Performance, TweakRisk.Moderate, () => RamKb() is var kb and > 0
            ? [R(@"HKLM\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", unchecked((int)kb))] : []),
        new("priority", TweakCat.Performance, TweakRisk.Safe, L(
            R(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38))),
        new("mmcss", TweakCat.Performance, TweakRisk.Safe, L(
            R(Mm, "NoLazyMode", 1), R(Mm, "AlwaysOn", 1),
            R(Mm, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)), R(Mm, "SystemResponsiveness", 10),
            R(Mm + @"\Tasks\Games", "Priority", 2), S(Mm + @"\Tasks\Games", "Scheduling Category", "High"),
            S(Mm + @"\Tasks\Games", "SFIO Priority", "High"), R(Mm + @"\Tasks\Games", "GPU Priority", 8))),
        new("keyboard", TweakCat.Performance, TweakRisk.Safe, L(
            S(@"HKCU\Control Panel\Keyboard", "KeyboardDelay", "0"), S(@"HKCU\Control Panel\Keyboard", "KeyboardSpeed", "31"))),
        new("a11ykeys", TweakCat.Performance, TweakRisk.Moderate, L(
            S(@"HKCU\Control Panel\Accessibility\StickyKeys", "Flags", "26"),
            S(@"HKCU\Control Panel\Accessibility\Keyboard Response", "Flags", "2"),
            S(@"HKCU\Control Panel\Accessibility\ToggleKeys", "Flags", "34"))),
        new("throttling", TweakCat.Performance, TweakRisk.Safe, L(
            R(@"HKLM\SYSTEM\CurrentControlSet\Control\USB\AutomaticSurpriseRemoval", "AttemptRecoveryFromUsbPowerDrain", 0),
            R(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1))),
        new("usbpower", TweakCat.Performance, TweakRisk.Safe, L(new UsbHubPowerOp())),
        new("hibernate", TweakCat.Performance, TweakRisk.Moderate, L(new HibernateOp())),
        new("webdav", TweakCat.Performance, TweakRisk.Safe, L(
            R(@"HKLM\SYSTEM\CurrentControlSet\Services\WebClient\Parameters", "FileSizeLimitInBytes", unchecked((int)0xFFFFFFFF)))),

        // ---------- Grafica ----------
        new("nv.pstate", TweakCat.Gpu, TweakRisk.Safe, Gpu(GpuVendor.Nvidia, ("DisableDynamicPstate", 1)), () => HasGpu(GpuVendor.Nvidia)),
        new("nv.async", TweakCat.Gpu, TweakRisk.Moderate, Gpu(GpuVendor.Nvidia, ("DisableASyncPstates", 1)), () => HasGpu(GpuVendor.Nvidia)),
        new("amd.ulps", TweakCat.Gpu, TweakRisk.Safe, Gpu(GpuVendor.Amd, ("EnableULPS", 0)), () => HasGpu(GpuVendor.Amd)),
        new("amd.gating", TweakCat.Gpu, TweakRisk.Moderate, Gpu(GpuVendor.Amd, ("DisablePowerGating", 1), ("PP_GPUPowerDownEnabled", 0), ("DisableDynamicPstate", 1)), () => HasGpu(GpuVendor.Amd)),
        new("amd.vce", TweakCat.Gpu, TweakRisk.Moderate, Gpu(GpuVendor.Amd, ("DisableVCEPowerGating", 1), ("DisableVceClockGating", 1), ("EnableUvdClockGating", 0), ("EnableVceSwClockGating", 0)), () => HasGpu(GpuVendor.Amd)),
        new("amd.aspm", TweakCat.Gpu, TweakRisk.Safe, Gpu(GpuVendor.Amd, ("EnableAspmL0s", 0), ("EnableAspmL1", 0)), () => HasGpu(GpuVendor.Amd)),
        new("intel.flips", TweakCat.Gpu, TweakRisk.Safe, Gpu(GpuVendor.Intel, ("Display1_DisableAsyncFlips", 1)), () => HasGpu(GpuVendor.Intel)),
        new("intel.vsync", TweakCat.Gpu, TweakRisk.Safe, Gpu(GpuVendor.Intel, ("AdaptiveVsyncEnable", 0)), () => HasGpu(GpuVendor.Intel)),

        // ---------- Privacidad ----------
        new("telemetry", TweakCat.Privacy, TweakRisk.Moderate, L(
            R(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0),
            R(Dc, "AllowTelemetry", 0), R(Dc, "DoNotShowFeedbackNotifications", 1), R(Dc, "AllowCommercialDataPipeline", 0),
            R(Dc, "AllowDeviceNameInTelemetry", 0), R(Dc, "MicrosoftEdgeDataOptIn", 0),
            R(@"HKCU\SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
            R(@"HKCU\Software\Policies\Microsoft\Windows\EdgeUI", "DisableMFUTracking", 1),
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableInventory", 1),
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat", "AITEnable", 0),
            R(@"HKCU\SOFTWARE\Policies\Microsoft\Assistance\Client\1.0", "NoExplicitFeedback", 1),
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Assistance\Client\1.0", "NoActiveHelp", 1),
            R(@"HKCU\SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0),
            R(Adv, "Start_TrackProgs", 0),
            new SvcOp("DiagTrack", StartType.Disabled), new SvcOp("dmwappushservice", StartType.Disabled),
            new SvcOp("DcpSvc", StartType.Disabled), new SvcOp("diagnosticshub.standardcollector.service", StartType.Disabled),
            new TaskOp(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"),
            new TaskOp(@"\Microsoft\Windows\Application Experience\ProgramDataUpdater"),
            new TaskOp(@"\Microsoft\Windows\Application Experience\MareBackup"),
            new TaskOp(@"\Microsoft\Windows\Application Experience\StartupAppTask"),
            new TaskOp(@"\Microsoft\Windows\Application Experience\PcaPatchDbTask"),
            new TaskOp(@"\Microsoft\Windows\Autochk\Proxy"),
            new TaskOp(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"),
            new TaskOp(@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"),
            new TaskOp(@"\Microsoft\Windows\Feedback\Siuf\DmClient"),
            new TaskOp(@"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"))),
        new("wer", TweakCat.Privacy, TweakRisk.Moderate, L(
            R(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1),
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports", "PreventHandwritingErrorReports", 1),
            new SvcOp("WerSvc", StartType.Disabled), new SvcOp("PcaSvc", StartType.Disabled),
            new TaskOp(@"\Microsoft\Windows\Windows Error Reporting\QueueReporting"),
            new TaskOp(@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"),
            new TaskOp(@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticResolver"),
            new TaskOp(@"\Microsoft\Windows\Diagnosis\Scheduled"))),
        new("ads", TweakCat.Privacy, TweakRisk.Safe, L(
            R(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1),
            R(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0),
            R(Cc, "DisableTailoredExperiencesWithDiagnosticData", 1), R(Cc, "DisableWindowsConsumerFeatures", 1),
            R(Cc, "DisableSoftLanding", 1), R(Cc, "DisableThirdPartySuggestions", 1),
            R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0),
            R(@"HKCU\SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1),
            R(@"HKCU\SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1),
            R(@"HKCU\SOFTWARE\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0),
            R(@"HKCU\Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1))),
        new("cdm", TweakCat.Privacy, TweakRisk.Safe, L(
            R(Cdm, "ContentDeliveryAllowed", 0), R(Cdm, "SubscribedContent-338387Enabled", 0), R(Cdm, "SubscribedContent-338388Enabled", 0),
            R(Cdm, "SubscribedContent-338389Enabled", 0), R(Cdm, "SubscribedContent-353698Enabled", 0), R(Cdm, "SystemPaneSuggestionsEnabled", 0))),
        new("preinstalled", TweakCat.Privacy, TweakRisk.Safe, L(
            R(Cdm, "PreInstalledAppsEnabled", 0), R(Cdm, "PreInstalledAppsEverEnabled", 0),
            R(Cdm, "OemPreInstalledAppsEnabled", 0), R(Cdm, "SilentInstalledAppsEnabled", 0))),
        new("news", TweakCat.Privacy, TweakRisk.Safe, L(
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0),
            R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Feeds", "ShellFeedsTaskbarViewMode", 0))),
        new("meetnow", TweakCat.Privacy, TweakRisk.Safe, L(
            R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "HideSCAMeetNow", 1))),
        new("activity", TweakCat.Privacy, TweakRisk.Safe, L(
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivitiesOnUserConsent", 0),
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0))),
        new("location", TweakCat.Privacy, TweakRisk.Safe, L(
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1),
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableSensors", 1),
            R(@"HKLM\SYSTEM\Maps", "AutoUpdateEnabled", 0),
            R(@"HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Permissions\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}", "SensorPermissionState", 0),
            S(@"HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny"),
            S(@"HKLM\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny"),
            R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Geolocation", "Status", 0),
            R(@"HKLM\SYSTEM\CurrentControlSet\Services\lfsvc\Service\Configuration", "Status", 0),
            R(@"HKLM\Software\Microsoft\PolicyManager\default\WiFi\AllowWiFiHotSpotReporting", "Value", 0),
            R(@"HKLM\Software\Microsoft\PolicyManager\default\WiFi\AllowAutoConnectToWiFiSenseHotspots", "Value", 0),
            new TaskOp(@"\Microsoft\Windows\Maps\MapsUpdateTask"))),
        new("autologger", TweakCat.Privacy, TweakRisk.Moderate, L(
            R(Al + "AppModel", "Start", 0), R(Al + "Cellcore", "Start", 0), R(Al + "CloudExperienceHostOobe", "Start", 0),
            R(Al + "DataMarket", "Start", 0), R(Al + "DiagLog", "Start", 0), R(Al + "Diagtrack-Listener", "Start", 0),
            R(Al + "LwtNetLog", "Start", 0), R(Al + "SQMLogger", "Start", 0), R(Al + "WdiContextLog", "Start", 0), R(Al + "WiFiSession", "Start", 0))),
        new("cortana", TweakCat.Privacy, TweakRisk.Moderate, L(
            R(Ws, "AllowCortana", 0), R(Ws, "AllowCloudSearch", 0), R(Ws, "AllowCortanaAboveLock", 0),
            R(Ws, "AllowSearchToUseLocation", 0), R(Ws, "ConnectedSearchUseWeb", 0),
            R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", 0),
            R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent2", 0))),
        new("findmy", TweakCat.Privacy, TweakRisk.Safe, L(R(@"HKLM\SOFTWARE\Policies\Microsoft\FindMyDevice", "AllowFindMyDevice", 0))),
        new("delivery", TweakCat.Privacy, TweakRisk.Safe, L(R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0))),

        // ---------- IA ----------
        new("recall", TweakCat.Ai, TweakRisk.Moderate, L(
            R(@"HKLM\" + Ai, "DisableAIDataAnalysis", 1), R(@"HKLM\" + Ai, "AllowRecallEnablement", 0), R(@"HKLM\" + Ai, "TurnOffSavingSnapshots", 1),
            R(@"HKCU\" + Ai, "DisableAIDataAnalysis", 1), R(@"HKCU\" + Ai, "TurnOffSavingSnapshots", 1))),
        new("clicktodo", TweakCat.Ai, TweakRisk.Safe, L(R(@"HKLM\" + Ai, "DisableClickToDo", 1), R(@"HKCU\" + Ai, "DisableClickToDo", 1))),
        new("copilot", TweakCat.Ai, TweakRisk.Moderate, L(
            R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
            R(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
            R(Adv, "ShowCopilotButton", 0),
            S(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", "{CB3B0003-8088-4EDE-8769-8B354AB2FF8C}", ""))),

        // ---------- Experiencia ----------
        new("delays", TweakCat.Experience, TweakRisk.Safe, L(
            R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0),
            S(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "0"))),
        new("visual", TweakCat.Experience, TweakRisk.Safe, L(
            R(Adv, "TaskbarAnimations", 0), R(Adv, "ListviewShadow", 0),
            R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0),
            R(@"HKCU\Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0))),
        new("searchsugg", TweakCat.Experience, TweakRisk.Safe, L(R(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1))),
        new("recommended", TweakCat.Experience, TweakRisk.Safe, L(R(@"HKCU\SOFTWARE\Policies\Microsoft\Windows\Explorer", "HideRecommendedSection", 1))),
        new("account", TweakCat.Experience, TweakRisk.Safe, L(R(Cc, "DisableConsumerAccountStateContent", 1))),
        new("wupause", TweakCat.Experience, TweakRisk.Safe, L(R(@"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "FlightSettingsMaxPauseDays", unchecked((int)0xFFFFFFFF)))),

        // ---------- Servicios ----------
        new("services", TweakCat.Services, TweakRisk.Risky, () => ServiceList().Select(s => (Op)new SvcOp(s.Name, s.Start)).ToList()),
    ];

    /// <summary>Tipos de inicio recomendados (lista embebida, mismo criterio que optimizerDuck).</summary>
    public static List<(string Name, StartType Start)> ServiceList()
    {
        using var s = typeof(WinTweaks).Assembly.GetManifestResourceStream("tweaks.services.txt");
        if (s is null) return [];
        using var r = new StreamReader(s);
        var list = new List<(string, StartType)>();
        while (r.ReadLine() is { } line)
        {
            var p = line.Trim().Split(' ');
            if (p.Length == 2 && Enum.TryParse<StartType>(p[1].Replace("AutomaticDelayedStart", "AutomaticDelayed"), out var st)) list.Add((p[0], st));
        }
        return list;
    }

    public static IEnumerable<Tweak> Visible => All.Where(t => t.Available?.Invoke() ?? true);

    // ------------------------------------------------------------------
    //  Estado y deshacer
    // ------------------------------------------------------------------
    private static string UndoFile => System.IO.Path.Combine(AppPaths.Root, "undo", TestRoot is null ? "wintweaks.json" : "wintweaks_selftest.json");

    private static Dictionary<string, Dictionary<string, string?>> LoadUndo()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string?>>>(File.ReadAllText(UndoFile)) ?? []; }
        catch { return []; }
    }

    private static void SaveUndo(Dictionary<string, Dictionary<string, string?>> u)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(UndoFile)!);
        File.WriteAllText(UndoFile, JsonSerializer.Serialize(u));
    }

    public static bool CanUndo(Tweak t) => LoadUndo().ContainsKey(t.Id);

    private static IEnumerable<Op> Active(Tweak t) =>
        t.Ops().Where(o => (TestRoot is null || o.Testable) && SafeExists(o));

    private static bool SafeExists(Op o) { try { return o.Exists; } catch { return false; } }

    /// <summary>true si todas las operaciones que existen en este PC ya tienen el valor del ajuste.</summary>
    public static bool IsApplied(Tweak t)
    {
        var ops = Active(t).ToList();
        if (ops.Count == 0) return false;
        foreach (var o in ops) { try { if (!o.IsApplied()) return false; } catch { return false; } }
        return true;
    }

    public sealed record Result(int Done, int Failed, string? FirstError);

    /// <summary>Guarda el estado anterior de cada operacion y aplica el ajuste.</summary>
    public static Result Apply(Tweak t)
    {
        var undo = LoadUndo();
        if (!undo.TryGetValue(t.Id, out var prev)) undo[t.Id] = prev = [];
        int done = 0, failed = 0; string? err = null;
        foreach (var o in Active(t))
        {
            try
            {
                if (!prev.ContainsKey(o.Key)) prev[o.Key] = o.Capture();
                if (!o.IsApplied()) o.Apply();
                done++;
            }
            catch (Exception ex) { failed++; err ??= $"{o.Key}: {ex.Message}"; prev.Remove(o.Key); }
        }
        SaveUndo(undo);
        Logger.Info(T("lg.wt.applied", T("wt." + t.Id), done, failed));
        if (err is not null) Logger.Detail("WINDOWS: " + err);
        return new Result(done, failed, err);
    }

    /// <summary>Deja cada operacion como estaba antes de aplicar el ajuste.</summary>
    public static Result Undo(Tweak t)
    {
        var undo = LoadUndo();
        if (!undo.TryGetValue(t.Id, out var prev)) return new Result(0, 0, null);
        var ops = t.Ops().ToDictionary(o => o.Key);
        int done = 0, failed = 0; string? err = null;
        foreach (var (key, value) in prev)
        {
            if (!ops.TryGetValue(key, out var o)) continue;
            try { o.Restore(value); done++; }
            catch (Exception ex) { failed++; err ??= $"{key}: {ex.Message}"; }
        }
        undo.Remove(t.Id);
        SaveUndo(undo);
        Logger.Info(T("lg.wt.undone", T("wt." + t.Id), done, failed));
        if (err is not null) Logger.Detail("WINDOWS: " + err);
        return new Result(done, failed, err);
    }
}
