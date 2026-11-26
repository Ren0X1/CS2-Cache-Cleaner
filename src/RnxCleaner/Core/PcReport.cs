using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Informe del PC para soporte (para pasarlo por Discord): hardware, Windows, drivers, discos, red, estado de RNX y
/// los ultimos avisos/errores del registro. Anonimo: sin usuario, nombre del equipo, IPs ni MACs.
/// </summary>
public static class PcReport
{
    public static async Task<string> BuildAsync(Settings s, CancellationToken ct)
    {
        const string ps = """
            $o = New-Object System.Text.StringBuilder
            function L($t) { [void]$o.AppendLine($t) }
            $os = Get-CimInstance Win32_OperatingSystem
            $cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
            L ("## " + $t.win)
            L (("  {0} {1} (build {2}.{3}) · " + $t.installed + " {4:yyyy-MM-dd} · " + $t.uptime + " {5:yyyy-MM-dd HH:mm}") -f $os.Caption, $cv.DisplayVersion, $cv.CurrentBuild, $cv.UBR, $os.InstallDate, $os.LastBootUpTime)
            L ""
            L ("## " + $t.hw)
            $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
            L (("  CPU: {0} ({1} " + $t.cores + " / {2} " + $t.threads + ", {3} MHz)") -f $cpu.Name.Trim(), $cpu.NumberOfCores, $cpu.NumberOfLogicalProcessors, $cpu.MaxClockSpeed)
            $bb = Get-CimInstance Win32_BaseBoard; $bios = Get-CimInstance Win32_BIOS
            L (("  " + $t.board + ": {0} {1} · BIOS {2} ({3:yyyy-MM-dd})") -f $bb.Manufacturer, $bb.Product, $bios.SMBIOSBIOSVersion, $bios.ReleaseDate)
            $mem = Get-CimInstance Win32_PhysicalMemory
            L (("  RAM: {0:0} GB · {1} " + $t.modules + " · {2} MHz · {3}") -f (($mem | Measure-Object Capacity -Sum).Sum / 1GB), @($mem).Count, ($mem | Select-Object -First 1).ConfiguredClockSpeed, (($mem | Select-Object -Expand Manufacturer -Unique) -join ', '))
            foreach ($g in Get-CimInstance Win32_VideoController) { L ("  GPU: {0} · driver {1} ({2:yyyy-MM-dd}) · {3}x{4} {5} Hz" -f $g.Name, $g.DriverVersion, $g.DriverDate, $g.CurrentHorizontalResolution, $g.CurrentVerticalResolution, $g.CurrentRefreshRate) }
            L ""
            L ("## " + $t.disks)
            foreach ($d in Get-PhysicalDisk) { L (("  {0} · {1} · {2:0} GB · " + $t.health + ": {3} · " + $t.state + ": {4}") -f $d.FriendlyName, $d.MediaType, ($d.Size / 1GB), $d.HealthStatus, $d.OperationalStatus) }
            foreach ($v in Get-Volume | Where-Object { $_.DriveLetter -and $_.Size -gt 0 }) { L (("  {0}: {1} · {2:0.0} GB " + $t.freeof + " {3:0.0} GB ({4:0}%)") -f $v.DriveLetter, $v.FileSystem, ($v.SizeRemaining / 1GB), ($v.Size / 1GB), (100 * $v.SizeRemaining / $v.Size)) }
            L ""
            L ("## " + $t.net)
            foreach ($a in Get-NetAdapter | Where-Object Status -eq 'Up') { L ("  {0} · {1} · {2}" -f $a.InterfaceDescription, $a.LinkSpeed, $a.DriverVersion) }
            $dns = (Get-DnsClientServerAddress -AddressFamily IPv4 | Where-Object { $_.ServerAddresses } | Select-Object -First 1).ServerAddresses -join ', '
            L ("  DNS: {0}" -f $dns)
            L ""
            L ("## " + $t.power)
            L ("  " + ((powercfg /getactivescheme) -replace '^.*?:\s*', ''))
            L ""
            L ("## " + $t.audio)
            foreach ($snd in Get-CimInstance Win32_SoundDevice) { L ("  " + $snd.Name) }
            L ""
            L ("## " + $t.monitors)
            foreach ($m in Get-CimInstance -Namespace root\wmi WmiMonitorID -ErrorAction SilentlyContinue) { L ("  " + (($m.UserFriendlyName | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ }) -join '')) }
            L ""
            L ("## " + $t.av)
            foreach ($av in Get-CimInstance -Namespace root\SecurityCenter2 AntiVirusProduct -ErrorAction SilentlyContinue) { L ("  " + $av.displayName) }
            $o.ToString()
            """;
        // Textos del script en el idioma de la app
        string E(string k) => T(k).Replace("'", "''");
        var labels = "$t = @{ win='" + E("rep.h.win") + "'; hw='" + E("rep.h.hw") + "'; disks='" + E("rep.h.disks") + "'; net='" + E("rep.h.net") +
                     "'; power='" + E("rep.h.power") + "'; audio='" + E("rep.h.audio") + "'; monitors='" + E("rep.h.monitors") + "'; av='" + E("rep.h.av") +
                     "'; installed='" + E("rep.l.installed") + "'; uptime='" + E("rep.l.uptime") + "'; cores='" + E("rep.l.cores") + "'; threads='" + E("rep.l.threads") +
                     "'; board='" + E("rep.l.board") + "'; modules='" + E("rep.l.modules") + "'; health='" + E("rep.l.health") + "'; state='" + E("rep.l.state") +
                     "'; freeof='" + E("rep.l.freeof") + "' }; ";
        var sys = await NetTweaks.PowerShellAsync(labels + ps, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"RNX Cache Cleaner {Updater.Current} · {T("rep.header")} · {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine(new string('=', 70));
        sb.AppendLine();
        sb.AppendLine(sys.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("## RNX");
        sb.AppendLine($"  {T("rep.activated")}: {(Activation.IsActivated() ? "✔" : "✘")}");
        sb.AppendLine($"  {T("rep.lang")}: {s.Language} · {T("rep.theme")}: {s.Palette} / {s.ThemeMode}");
        sb.AppendLine($"  HUD: {(s.HudEnabled ? "on" : "off")} · Digital Vibrance: {s.VibranceProfiles.Count} · {T("rep.clock")}: {(s.ClockEnabled ? "on" : "off")}");
        sb.AppendLine($"  {T("rep.tweaks")}: {(s.WinTweaks.Count == 0 ? "-" : string.Join(", ", s.WinTweaks))}");
        sb.AppendLine($"  {T("rep.ignored")}: {s.PcIgnore.Count} · {T("rep.backup")}: {(s.BackupToDrive ? "Drive" : "-")} · {T("rep.rp")}: {(s.RestorePoint ? "on" : "off")}");
        sb.AppendLine();
        sb.AppendLine("## " + T("rep.log"));
        var log = Logger.ReadAll().Replace("\r", "").Split('\n');
        var issues = log.Where(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || l.Contains("FALLO", StringComparison.OrdinalIgnoreCase)
                                     || l.Contains("[WARN", StringComparison.OrdinalIgnoreCase) || l.Contains("error", StringComparison.OrdinalIgnoreCase)).TakeLast(25).ToList();
        foreach (var l in issues.Count > 0 ? issues : ["  -"]) sb.AppendLine("  " + l.Trim());
        sb.AppendLine();
        sb.AppendLine("## " + T("rep.lastlog"));
        foreach (var l in log.Where(l => l.Trim().Length > 0).TakeLast(30)) sb.AppendLine("  " + l.Trim());
        return Anonymize(sb.ToString());
    }

    /// <summary>Quita datos personales: carpeta de usuario, nombre del equipo, IPs, MACs y correos.</summary>
    public static string Anonymize(string text)
    {
        var user = Environment.UserName;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        text = Regex.Replace(text, Regex.Escape(profile), "%USERPROFILE%", RegexOptions.IgnoreCase);
        if (user.Length > 2) text = Regex.Replace(text, @"\b" + Regex.Escape(user) + @"\b", "<user>", RegexOptions.IgnoreCase);
        var pc = Environment.MachineName;
        if (pc.Length > 2) text = Regex.Replace(text, @"\b" + Regex.Escape(pc) + @"\b", "<pc>", RegexOptions.IgnoreCase);
        // Solo direcciones de red reales (privadas, o escritas como IP/destino): los numeros de version se respetan
        text = Regex.Replace(text, @"(?<![\d.])(192\.168|172\.(1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}(?![\d.])", "x.x.x.x");
        text = Regex.Replace(text, @"(?<=(IP|ip|→|->)\s*:?\s*)\d{1,3}(\.\d{1,3}){3}(?![\d.])", "x.x.x.x");
        text = Regex.Replace(text, @"\b([0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b", "xx:xx:xx:xx:xx:xx", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[\w.+-]+@[\w-]+\.[\w.]+", "<email>");
        return text;
    }

    public static async Task<string> SaveAsync(Settings s, CancellationToken ct)
    {
        var text = await BuildAsync(s, ct);
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"RNX_informe_PC_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt");
        await File.WriteAllTextAsync(file, text, new UTF8Encoding(true), ct);
        Logger.Info(T("lg.rep.saved", Path.GetFileName(file)));
        return file;
    }
}
