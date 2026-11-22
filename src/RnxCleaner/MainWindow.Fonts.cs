using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Pestana "Fuentes CS2": Font Manager que mete la fuente donde CS2 la lee de verdad.</summary>
public partial class MainWindow
{
    private List<Cs2Fonts.FontFile>? _installedFonts;
    private int _fontPage;
    private const int FontPageSize = 80;
    private const string FontSample = "Counter-Strike 2  ·  AK-47  $4700  100 HP  0123456789";

    private async void LoadFonts()
    {
        RenderFonts();
        if (_installedFonts is null)
        {
            FontEmpty.Text = T("font.loading");
            _installedFonts = await Task.Run(Cs2Fonts.Installed);
            RenderFontList();
        }
    }

    private void RenderFonts()
    {
        var dir = Cs2Fonts.GameFontsDir();
        bool has = dir is not null;
        FontNoCs2.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        FontBody.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        FontCs2Warn.Visibility = has && SystemInfo.IsRunning("cs2") ? Visibility.Visible : Visibility.Collapsed;
        if (!has) return;

        var st = Cs2Fonts.Read(dir!);
        FontState.Text = st.Custom ? T("font.state.custom", st.Family, (int)Math.Round(st.Size * 100)) : T("font.state.original");
        FontState.SetResourceReference(TextBlock.ForegroundProperty, st.Custom ? "Accent" : "Ink");
        if (st.Custom && st.Family is not null)
        {
            var file = st.FontFiles.Select(f => System.IO.Path.Combine(dir!, f)).FirstOrDefault();
            FontState.FontFamily = file is not null ? Cs2Fonts.Preview(new(file, st.Family)) ?? FontFamily : FontFamily;
        }
        else FontState.FontFamily = FontFamily;
        FontRestoreBtn.IsEnabled = st.Custom;

        // Fuente de RNX
        var rnx = Cs2Fonts.RnxFont();
        if (rnx is not null)
        {
            FontRnxPreview.FontFamily = Cs2Fonts.Preview(rnx) ?? FontFamily;
            FontRnxPreview.Text = FontSample;
        }

        // Tamano
        FontSizes.Children.Clear();
        foreach (var pct in new[] { 85, 90, 95, 100, 105, 110 })
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegItem"), Content = pct + " %", IsChecked = (int)Math.Round(_settings.FontSize * 100) == pct, GroupName = "fontsize" };
            var p = pct;
            rb.Click += (_, _) => { _settings.FontSize = p / 100.0; _settings.Save(); };
            FontSizes.Children.Add(rb);
        }
        RenderFontList();
    }

    private void RenderFontList()
    {
        FontList.Children.Clear();
        if (_installedFonts is null) { FontEmpty.Visibility = Visibility.Visible; return; }
        var q = FontSearch.Text.Trim();
        var matches = _installedFonts.Where(f => q.Length == 0 || f.Family.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        int pages = Math.Max(1, (matches.Count + FontPageSize - 1) / FontPageSize);
        _fontPage = Math.Clamp(_fontPage, 0, pages - 1);
        var shown = matches.Skip(_fontPage * FontPageSize).Take(FontPageSize).ToList();
        FontPageText.Text = T("font.page", _fontPage + 1, pages, matches.Count);
        FontPrevBtn.IsEnabled = _fontPage > 0;
        FontNextBtn.IsEnabled = _fontPage < pages - 1;
        FontPager.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        FontScroll.ScrollToTop();
        FontEmpty.Text = T("font.noresults");
        FontEmpty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var f in shown)
        {
            var row = new DockPanel { Margin = new Thickness(14, 8, 14, 8) };
            var b = new Button { Style = (Style)FindResource("GhostButton"), Content = T("font.apply"), VerticalAlignment = VerticalAlignment.Center };
            var font = f;
            b.Click += async (_, _) => await ApplyFontAsync(font);
            DockPanel.SetDock(b, Dock.Right);
            row.Children.Add(b);
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(new TextBlock { Text = f.Family, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            var prev = new TextBlock { Text = FontSample, FontSize = 18, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            if (Cs2Fonts.Preview(f) is { } ff) prev.FontFamily = ff;
            left.Children.Add(prev);
            row.Children.Add(left);
            var wrap = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
            wrap.SetResourceReference(Border.BorderBrushProperty, "Line2");
            FontList.Children.Add(wrap);
        }
    }

    private void FontSearch_TextChanged(object s, TextChangedEventArgs e) { _fontPage = 0; RenderFontList(); }
    private void FontPrev_Click(object s, RoutedEventArgs e) { _fontPage--; RenderFontList(); }
    private void FontNext_Click(object s, RoutedEventArgs e) { _fontPage++; RenderFontList(); }

    private async Task ApplyFontAsync(Cs2Fonts.FontFile font)
    {
        var dir = Cs2Fonts.GameFontsDir();
        if (dir is null) return;
        if (!await CloseCs2ForConfigAsync()) return;
        try
        {
            Cs2Fonts.Apply(dir, font, _settings.FontSize);
            ShowToast(T("font.toast.applied", font.Family));
        }
        catch (Exception ex) { Logger.Error(T("lg.font.fail", ex.Message)); ShowToast(T("lg.font.fail", ex.Message)); }
        RenderFonts();
    }

    private async void FontRnx_Click(object s, RoutedEventArgs e)
    {
        if (Cs2Fonts.RnxFont() is { } f) await ApplyFontAsync(f);
    }

    private async void FontFile_Click(object s, RoutedEventArgs e)
    {
        var path = PickFile(T("font.filter") + " (*.ttf;*.otf)|*.ttf;*.otf", null);
        if (path is null) return;
        var fam = Cs2Fonts.FamilyOf(path);
        if (fam is null) { ShowToast(T("font.toast.bad")); return; }
        await ApplyFontAsync(new Cs2Fonts.FontFile(path, fam));
    }

    private async void FontRestore_Click(object s, RoutedEventArgs e)
    {
        var dir = Cs2Fonts.GameFontsDir();
        if (dir is null) return;
        if (!await CloseCs2ForConfigAsync()) return;
        try
        {
            Cs2Fonts.RestoreOriginal(dir);
            ShowToast(T("font.toast.original"));
        }
        catch (Exception ex) { Logger.Error(T("lg.font.fail", ex.Message)); ShowToast(T("lg.font.fail", ex.Message)); }
        RenderFonts();
    }
}
