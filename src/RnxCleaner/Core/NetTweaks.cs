using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

/// <summary>
/// Red: DNS 1.1.1.1 (Cloudflare) + 8.8.8.8 (Google) en los adaptadores fisicos
/// activos y autotuning TCP en "normal". Lo anterior se guarda para Deshacer.
/// </summary>
public static class NetTweaks
{
    public static readonly string[] Dns = ["1.1.1.1", "8.8.8.8"];

    public sealed record Adapter(string Name, int Index, string Static);

    /// <summary>Adaptadores fisicos conectados y sus DNS fijos (vacio = automaticos por DHCP).</summary>
    public static async Task<List<Adapter>> AdaptersAsync(CancellationToken ct = default)
    {
        const string ps = """
            $r = Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object {
              $ns = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$($_.InterfaceGuid)" -Name NameServer -ErrorAction SilentlyContinue).NameServer
              [pscustomobject]@{ Name = $_.Name; Index = $_.ifIndex; Static = [string]$ns }
            }
            ConvertTo-Json -Compress -InputObject @($r)
            """;
        var json = (await PowerShellAsync(ps, ct)).Trim();
        if (json.Length == 0) return [];
        return JsonSerializer.Deserialize<List<Adapter>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    public static async Task<StepStatus> ApplyAsync(CancellationToken ct)
    {
        var status = StepStatus.Ok;
        var adapters = await AdaptersAsync(ct);
        if (adapters.Count == 0) { Logger.Warn(T("lg.net.none")); status = StepStatus.Warn; }
        else
        {
            await Undo.SaveDnsAsync(adapters, ct);
            var servers = string.Join(",", Dns.Select(d => $"'{d}'"));
            foreach (var a in adapters)
            {
                await PowerShellAsync($"Set-DnsClientServerAddress -InterfaceIndex {a.Index} -ServerAddresses ({servers})", ct);
                var now = await CurrentDnsAsync(a.Index, ct);
                if (now.StartsWith(string.Join(",", Dns))) Logger.Ok(T("lg.net.dns", a.Name));
                else { Logger.Error(T("lg.net.fail", $"{a.Name}: {now}")); status = StepStatus.Error; }
            }
            await Sys.RunAsync("ipconfig.exe", "/flushdns", 30, ct);
        }

        var before = await AutoTuningAsync(ct);
        if (before is not null && before != "normal") Undo.SaveAutoTuning(before);
        await Sys.RunAsync("netsh.exe", "int tcp set global autotuninglevel=normal", 30, ct);
        var after = await AutoTuningAsync(ct);
        if (after == "normal") Logger.Ok(T("lg.net.tcp"));
        else { Logger.Error(T("lg.net.fail", "autotuninglevel=" + after)); status = StepStatus.Error; }
        return status;
    }

    public static async Task<string> CurrentDnsAsync(int index, CancellationToken ct = default) =>
        (await PowerShellAsync($"(Get-DnsClientServerAddress -InterfaceIndex {index} -AddressFamily IPv4).ServerAddresses -join ','", ct)).Trim();

    /// <summary>Nivel de autotuning actual (normal, disabled, restricted...); los valores no se traducen.</summary>
    public static async Task<string?> AutoTuningAsync(CancellationToken ct = default)
    {
        var o = (await PowerShellAsync("(Get-NetTCPSetting -SettingName Internet).AutoTuningLevelLocal", ct)).Trim();
        var m = Regex.Match(o, @"^(normal|disabled|highlyrestricted|restricted|experimental)$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    public static async Task SetAutoTuningAsync(string level, CancellationToken ct = default) =>
        await Sys.RunAsync("netsh.exe", $"int tcp set global autotuninglevel={level}", 30, ct);

    public static async Task<string> PowerShellAsync(string script, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + script);
        using var p = Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync(ct);
        await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return o;
    }
}

/// <summary>Informacion OEM de Windows (Configuracion > Sistema > Informacion).</summary>
public static class Oem
{
    public const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation";
    public static readonly string[] Values = ["Manufacturer", "Model", "SupportHours", "SupportPhone", "SupportURL", "Logo"];

    /// <summary>"RNX 48213": 5 cifras sacadas del MachineGuid, siempre las mismas en ese PC.</summary>
    public static string Model()
    {
        string guid;
        using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            guid = k?.GetValue("MachineGuid")?.ToString() ?? Environment.MachineName;
        var h = SHA256.HashData(Encoding.UTF8.GetBytes("RNX:" + guid.ToLowerInvariant()));
        return "RNX " + (BitConverter.ToUInt32(h, 0) % 100000).ToString("D5");
    }

    /// <summary>C:\ProgramData\RNX\logo.bmp (fuera de la carpeta del programa: sobrevive a actualizaciones).</summary>
    public static string LogoPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RNX", "logo.bmp");

    public static bool EnsureLogo()
    {
        try
        {
            using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("oem.logo.bmp");
            if (res is null) return false;
            using var ms = new MemoryStream();
            res.CopyTo(ms);
            var bytes = ms.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(LogoPath)!);
            if (!File.Exists(LogoPath) ||!File.ReadAllBytes(LogoPath).AsSpan().SequenceEqual(bytes))
                File.WriteAllBytes(LogoPath, bytes);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Al arrancar: si la info OEM apunta a nuestro logo y falta (p. ej. tras actualizar), se repone.</summary>
    public static void RepairLogo()
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(Key);
            if (string.Equals(k?.GetValue("Logo")?.ToString(), LogoPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(LogoPath))
                EnsureLogo();
        }
        catch { }
    }

    public static async Task<StepStatus> ApplyAsync(CancellationToken ct)
    {
        try
        {
            await Undo.SaveOemAsync(ct);
            bool logo = EnsureLogo();
            var model = Model();
            using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(Key, true))
            {
                k.SetValue("Manufacturer", "RNX");
                k.SetValue("Model", model);
                k.SetValue("SupportHours", "24/7");
                k.SetValue("SupportPhone", "Discord: ren0x1");
                k.SetValue("SupportURL", "https://github.com/Ren0X1");
                if (logo) k.SetValue("Logo", LogoPath);
            }
            Logger.Ok(T("lg.oem.ok", model));
            return logo ? StepStatus.Ok : StepStatus.Warn;
        }
        catch (Exception ex)
        {
            Logger.Error(T("lg.oem.fail", ex.Message));
            return StepStatus.Error;
        }
    }
}
