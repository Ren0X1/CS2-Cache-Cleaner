using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Pantalla de activacion de primer uso: pide el codigo de 6 digitos que da el administrador.</summary>
public sealed class ActivationWindow : Window
{
    private readonly TextBox _code;
    private readonly TextBlock _msg;
    private readonly Button _go;

    public ActivationWindow()
    {
        Title = "RNX Cache Cleaner";
        Width = 520; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/rnx.ico")); } catch { }
        SetResourceReference(BackgroundProperty, "Sheet");
        SetResourceReference(ForegroundProperty, "Ink");
        FontFamily = (FontFamily)FindResource("UiFont");

        var root = new StackPanel { Margin = new Thickness(36, 32, 36, 30) };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 22) };
        var logo = new Path { Data = (Geometry)FindResource("LogoGeometry"), Stretch = Stretch.Uniform, Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0) };
        logo.SetResourceReference(Shape.FillProperty, "Accent");
        head.Children.Add(logo);
        head.Children.Add(new TextBlock { Text = "RNX Cache Cleaner", FontWeight = FontWeights.Bold, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(head);

        root.Children.Add(new TextBlock { Text = T("act.title"), Style = (Style)FindResource("H1"), FontSize = 24 });
        root.Children.Add(new TextBlock { Text = T("act.sub"), Style = (Style)FindResource("Sub"), FontSize = 14, Margin = new Thickness(0, 6, 0, 22), TextWrapping = TextWrapping.Wrap });

        _code = new TextBox
        {
            MaxLength = 7, FontSize = 30, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(1),
            FontFamily = (FontFamily)FindResource("Mono"),
        };
        _code.SetResourceReference(BackgroundProperty, "Scope");
        _code.SetResourceReference(ForegroundProperty, "Ink");
        _code.SetResourceReference(BorderBrushProperty, "Edge");
        _code.SetResourceReference(TextBox.CaretBrushProperty, "Accent");
        _code.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        _code.TextChanged += (_, _) => { _go!.IsEnabled = Digits.Length == 6 && Activation.LockSeconds == 0; _msg!.Text = ""; };
        _code.KeyDown += (_, e) => { if (e.Key == Key.Enter && _go!.IsEnabled) Activate(); };
        DataObject.AddPastingHandler(_code, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText) is string t) { e.CancelCommand(); _code.Text = new string(t.Where(char.IsDigit).Take(6).ToArray()); _code.CaretIndex = _code.Text.Length; }
        });
        root.Children.Add(_code);

        _msg = new TextBlock { FontSize = 13, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, MinHeight = 18 };
        root.Children.Add(_msg);

        _go = new Button { Content = T("act.btn"), Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 14, 0, 0), IsEnabled = false, Height = 42 };
        _go.Click += (_, _) => TryActivate();
        root.Children.Add(_go);

        // Contacto para pedir el codigo
        var sep = new Border { Height = 1, Margin = new Thickness(0, 24, 0, 16) };
        sep.SetResourceReference(Border.BackgroundProperty, "Line2");
        root.Children.Add(sep);
        root.Children.Add(new TextBlock { Text = T("act.contact"), Style = (Style)FindResource("Overline"), Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(new TextBlock { Text = T("act.contact.x"), Style = (Style)FindResource("Sub"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        root.Children.Add(ContactRow("", "Discord", Activation.Discord));
        if (Activation.Email.Length > 0) root.Children.Add(ContactRow("", T("act.mail"), Activation.Email));

        if (!Activation.Available) { _code.IsEnabled = false; ShowMsg(T("act.unavailable"), "Error"); }

        Content = root;
        Loaded += (_, _) => _code.Focus();
    }

    private string Digits => new(_code.Text.Where(char.IsDigit).ToArray());

    private FrameworkElement ContactRow(string icon, string label, string value)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        var ic = new TextBlock { Text = icon, Style = (Style)FindResource("Icon"), FontSize = 14, Width = 22, VerticalAlignment = VerticalAlignment.Center };
        ic.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        sp.Children.Add(ic);
        sp.Children.Add(new TextBlock { Text = label + ":", Style = (Style)FindResource("Sub"), FontSize = 13.5, Width = 80, VerticalAlignment = VerticalAlignment.Center });
        var val = new TextBlock { Text = value, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        val.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        sp.Children.Add(val);
        return sp;
    }

    private void ShowMsg(string text, string brush)
    {
        _msg.Text = text;
        _msg.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    private async void TryActivate()
    {
        _go.IsEnabled = false;
        var r = Activation.TryActivate(Digits);
        switch (r)
        {
            case Activation.Result.Ok:
                ShowMsg(T("act.ok"), "Accent");
                await Task.Delay(700);
                DialogResult = true;
                return;
            case Activation.Result.Locked:
                ShowMsg(T("act.locked", Activation.LockSeconds), "Error");
                break;
            case Activation.Result.Unavailable:
                ShowMsg(T("act.unavailable"), "Error");
                return;
            default:
                ShowMsg(Activation.LockSeconds > 0 ? T("act.locked", Activation.LockSeconds) : T("act.wrong"), "Error");
                break;
        }
        _code.SelectAll();
        // Breve pausa entre intentos
        await Task.Delay(1500);
        _go.IsEnabled = Digits.Length == 6 && Activation.LockSeconds == 0;
        if (Activation.LockSeconds > 0) _ = UnlockLaterAsync();
    }

    private async Task UnlockLaterAsync()
    {
        while (Activation.LockSeconds > 0)
        {
            ShowMsg(T("act.locked", Activation.LockSeconds), "Error");
            await Task.Delay(1000);
        }
        _msg.Text = "";
        _go.IsEnabled = Digits.Length == 6;
    }
}
