using System.IO;
using System.Management;
using Microsoft.Win32;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner.Core;

public sealed class SystemInfo
{
    public string Windows { get; private init; } = "Windows";
    public string User { get; private init; } = Environment.UserName;
    public string GpuName { get; private init; } = "Desconocida";
    public string GpuBrand { get; private init; } = "Generica";
    public bool IsNvidia { get; private init; }

    public static SystemInfo Detect()
    {
        string raw = "";
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            raw = string.Join(" | ", s.Get().Cast<ManagementObject>().Select(o => o["Name"]?.ToString()).Where(n => !string.IsNullOrWhiteSpace(n)));
        }
        catch { }

        bool nv = raw.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
        string brand = nv ? "NVIDIA"
            : raw.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD Radeon"
            : raw.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD"
            : raw.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel"
            : "Generica";

        // Si hay varias GPU (iGPU + dedicada) mostramos la principal
        string name = raw.Split(" | ").FirstOrDefault(n => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                      ?? raw.Split(" | ").FirstOrDefault() ?? "Desconocida";

        return new SystemInfo { GpuName = string.IsNullOrWhiteSpace(name) ? "Desconocida" : name, GpuBrand = brand, IsNvidia = nv, Windows = WindowsName() };
    }

    private static string WindowsName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = int.TryParse(k?.GetValue("CurrentBuildNumber")?.ToString(), out var b) ? b : Environment.OSVersion.Version.Build;
            var display = k?.GetValue("DisplayVersion")?.ToString();
            var name = build >= 22000 ? "Windows 11" : "Windows 10";
            return display is null ? name : $"{name} {display}";
        }
        catch { return "Windows"; }
    }

    public static long TotalBytesC()
    {
        try { return new DriveInfo("C").TotalSize; } catch { return 0; }
    }

    public static long FreeBytesC()
    {
        try { return new DriveInfo("C").AvailableFreeSpace; } catch { return 0; }
    }

    /// <summary>Ruta de Steam: registro primero, luego las tipicas (igual que el .ps1 de backup).</summary>
    public static string? SteamPath()
    {
        (RegistryKey root, string key, string val)[] claves =
        [
            (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
        ];
        foreach (var (root, key, val) in claves)
        {
            try
            {
                using var k = root.OpenSubKey(key);
                if (k?.GetValue(val) is string p && Directory.Exists(p)) return Path.GetFullPath(p);
            }
            catch { }
        }
        foreach (var d in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                     @"C:\Steam", @"D:\Steam"
                 })
            if (Directory.Exists(d)) return d;
        return null;
    }

    public static bool IsRunning(string processName) =>
        System.Diagnostics.Process.GetProcessesByName(processName).Length > 0;
}
