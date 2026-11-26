using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace RnxCleaner;

/// <summary>
/// Ventana del reloj de RNX en la barra de tareas: solo texto (sin fondo), siempre encima de la barra, sin robar el foco.
/// Un clic abre el calendario y las notificaciones de Windows, como el reloj original.
/// </summary>
public sealed class ClockWindow : Window
{
    private readonly TextBlock _top = new() { HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _bottom = new() { HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right };
    private readonly StackPanel _panel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };

    public ClockWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));   // casi transparente: recibe el clic
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.Width;
        Cursor = Cursors.Hand;
        _panel.Children.Add(_top);
        _panel.Children.Add(_bottom);
        Content = _panel;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */);
        };
        MouseLeftButtonUp += (_, _) => OpenCalendar();
    }

    public void SetText(string top, string bottom, string font, double size, bool bold, Brush color, string tooltip)
    {
        foreach (var (tb, text) in new[] { (_top, top), (_bottom, bottom) })
        {
            tb.Text = text;
            tb.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            try { tb.FontFamily = new FontFamily(font); } catch { }
            tb.FontSize = size;
            tb.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;
            tb.Foreground = color;
        }
        ToolTip = tooltip;
    }

    /// <summary>Coloca el reloj (pixeles fisicos): alto de la barra, pegado por la derecha a <paramref name="right"/>.</summary>
    public void Place(double right, double top, double height)
    {
        // Con las propiedades de WPF (en DIP): si se mueve con SetWindowPos, WPF la vuelve a su Left/Top en el siguiente layout
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Height = height / dpi;
        UpdateLayout();
        Left = right / dpi - ActualWidth - 6 / dpi;
        Top = top / dpi;
        KeepOnTop();
    }

    public void KeepOnTop()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE|NOMOVE|NOACTIVATE */);
    }

    /// <summary>Win + N: calendario y notificaciones de Windows 11.</summary>
    private static void OpenCalendar()
    {
        keybd_event(0x5B, 0, 0, 0); keybd_event(0x4E, 0, 0, 0);
        keybd_event(0x4E, 0, 2, 0); keybd_event(0x5B, 0, 2, 0);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, int extra);
}
