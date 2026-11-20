using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace RnxCleaner.Core;

/// <summary>
/// Idiomas: ingles (por defecto), espanol, ruso, aleman, frances y chino.
/// Las traducciones estan en Strings.cs. En XAML: Text="{c:L clave}"; al
/// cambiar de idioma todas las ligaduras se actualizan solas.
/// En codigo: Loc.T("clave", args...).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly string[] Langs = ["en", "es", "ru", "de", "fr", "zh"];
    private static readonly string[] Cultures = ["en-US", "es-ES", "ru-RU", "de-DE", "fr-FR", "zh-CN"];

    public static Loc I { get; } = new();
    public static string Lang { get; private set; } = "en";
    public static int Index { get; private set; }
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? Changed;

    public string this[string key] => T(key);

    public static void Set(string lang)
    {
        var i = Array.IndexOf(Langs, lang);
        if (i < 0) i = 0;
        Index = i;
        Lang = Langs[i];
        Culture = CultureInfo.GetCultureInfo(Cultures[i]);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture;
        I.PropertyChanged?.Invoke(I, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke();
    }

    /// <summary>Texto traducido; con argumentos se formatea como string.Format.</summary>
    public static string T(string key, params object?[] args)
    {
        if (!Strings.Table.TryGetValue(key, out var row)) return key;
        var s = row[Index] ?? row[0];
        return args.Length == 0 ? s : string.Format(Culture, s, args);
    }

    /// <summary>Texto en un idioma concreto (pantalla de primer arranque).</summary>
    public static string In(string lang, string key)
    {
        var i = Math.Max(0, Array.IndexOf(Langs, lang));
        return Strings.Table.TryGetValue(key, out var row) ? row[i] : key;
    }
}

/// <summary>{c:L clave} en XAML: una ligadura al texto traducido.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider sp) =>
        new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay }.ProvideValue(sp);
}
