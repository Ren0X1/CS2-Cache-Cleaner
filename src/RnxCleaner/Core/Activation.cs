using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace RnxCleaner.Core;

/// <summary>
/// Activacion de primer uso con un codigo TOTP (Google Authenticator, RFC 6238: HMAC-SHA1, 30 s, 6 digitos).
/// Cada codigo sirve como mucho 1 minuto (el actual y el anterior). Al activar, se guarda en HKLM una firma
/// ligada al identificador unico de este PC (MachineGuid): copiada a otro PC o escrita a mano no sirve.
/// El secreto no esta en el repositorio: el workflow lo mete al compilar (Assets/activation.secret).
/// </summary>
public static class Activation
{
    private const string Key = @"SOFTWARE\RNX Cache Cleaner";
    private const string Value = "Activation";
    public const string Discord = "ren0x1";
    /// <summary>Correo de contacto que se muestra en la pantalla de activacion (vacio = no se muestra).</summary>
    public const string Email = "alejandromz.olv@gmail.com";

    private static byte[]? _secret;
    private static int _fails;
    private static DateTime _lockedUntil;

    /// <summary>Secreto TOTP embebido (Base32). null si esta compilacion no lo trae.</summary>
    internal static byte[]? SecretForTest() => Secret();

    private static byte[]? Secret()
    {
        if (_secret is not null) return _secret;
        using var s = typeof(Activation).Assembly.GetManifestResourceStream("activation.secret");
        if (s is null) return null;
        using var r = new StreamReader(s);
        var b32 = r.ReadToEnd().Trim().Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        return _secret = Base32Decode(b32);
    }

    public static bool Available => Secret() is { Length: >= 10 };

    private static string MachineId()
    {
        using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return k?.GetValue("MachineGuid")?.ToString() ?? Environment.MachineName;
    }

    /// <summary>Firma de este PC: HMAC-SHA256(clave derivada del secreto, MachineGuid).</summary>
    private static string Token(byte[] secret)
    {
        var key = SHA256.HashData([.. secret, .. "RNX-activation-v1"u8.ToArray()]);
        return Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(MachineId().ToLowerInvariant())));
    }

    public static bool IsActivated()
    {
        if (Secret() is not { } sec) return false;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(Key);
            return k?.GetValue(Value) is string stored &&
                   CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored), Encoding.ASCII.GetBytes(Token(sec)));
        }
        catch { return false; }
    }

    public enum Result { Ok, Wrong, Locked, Unavailable }

    /// <summary>Segundos que quedan de bloqueo tras varios intentos fallidos.</summary>
    public static int LockSeconds => Math.Max(0, (int)Math.Ceiling((_lockedUntil - DateTime.UtcNow).TotalSeconds));

    /// <summary>Comprueba el codigo y, si es valido, deja este PC activado.</summary>
    public static Result TryActivate(string code)
    {
        if (Secret() is not { } sec) return Result.Unavailable;
        if (LockSeconds > 0) return Result.Locked;
        code = new string(code.Where(char.IsDigit).ToArray());
        if (code.Length != 6 || !Verify(sec, code, DateTimeOffset.UtcNow))
        {
            // 5 fallos seguidos: 60 s sin poder probar (frena que se prueben codigos a ciegas)
            if (++_fails >= 5) { _fails = 0; _lockedUntil = DateTime.UtcNow.AddSeconds(60); }
            Logger.Detail("ACTIVATION: invalid code");
            return Result.Wrong;
        }
        using (var k = Registry.LocalMachine.CreateSubKey(Key, true))
            k.SetValue(Value, Token(sec), RegistryValueKind.String);
        _fails = 0;
        Logger.Info("ACTIVATION: " + Environment.MachineName + " activated");
        return Result.Ok;
    }

    /// <summary>Valido el codigo del intervalo actual y el del anterior (como mucho ~60 s).</summary>
    public static bool Verify(byte[] secret, string code, DateTimeOffset now)
    {
        long step = now.ToUnixTimeSeconds() / 30;
        var want = Encoding.ASCII.GetBytes(code);
        foreach (var s in new[] { step, step - 1 })
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Totp(secret, s)), want)) return true;
        return false;
    }

    public static string Totp(byte[] secret, long step)
    {
        var msg = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        var h = HMACSHA1.HashData(secret, msg);
        int o = h[^1] & 0x0F;
        int bin = ((h[o] & 0x7F) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (bin % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string s)
    {
        const string A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            int v = A.IndexOf(c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return [.. bytes];
    }

    /// <summary>Solo para las pruebas internas: quita la activacion de este PC.</summary>
    internal static void Remove()
    {
        using var k = Registry.LocalMachine.OpenSubKey(Key, true);
        k?.DeleteValue(Value, false);
    }

    internal static string? StoredRaw()
    {
        using var k = Registry.LocalMachine.OpenSubKey(Key);
        return k?.GetValue(Value) as string;
    }

    internal static void SetRaw(string? v)
    {
        if (v is null) { Remove(); return; }
        using var k = Registry.LocalMachine.CreateSubKey(Key, true);
        k.SetValue(Value, v, RegistryValueKind.String);
    }
}
