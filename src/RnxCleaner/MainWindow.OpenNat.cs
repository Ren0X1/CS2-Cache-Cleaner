using System.Windows;
using System.Windows.Controls;
using RnxCleaner.Core;

using static RnxCleaner.Core.Loc;

namespace RnxCleaner;

/// <summary>Personalizada → Open NAT para Call of Duty antiguos (puerto 3074 en firewall y router).</summary>
public partial class MainWindow
{
    private bool _natBusy;

    private async Task RefreshNatAsync()
    {
        if (_natBusy) return;
        NatState.Text = T("nat.checking");
        NatState.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        try { RenderNat(await OpenNat.ReadAsync(CancellationToken.None)); }
        catch (Exception ex) { NatState.Text = T("lg.wt.fail", ex.Message); }
    }

    private void RenderNat(OpenNat.State st)
    {
        string fw = st.Firewall ? T("nat.fw.on") : T("nat.fw.off");
        string rt = st.UpnpAvailable != true ? T("nat.rt.noupnp")
                  : st.Router ? T("nat.rt.on", st.LocalIp ?? "?")
                  : st.RouterTcpIp is not null || st.RouterUdpIp is not null ? T("nat.rt.other", st.RouterTcpIp ?? st.RouterUdpIp ?? "?")
                  : T("nat.rt.off");
        NatState.Text = fw + "  ·  " + rt;
        NatState.SetResourceReference(TextBlock.ForegroundProperty, st.Open ? "Accent" : st.Any ? "Warn" : "Muted");
        NatOpenBtn.IsEnabled = !_natBusy;
        NatCloseBtn.IsEnabled = !_natBusy && st.Any;
    }

    private async void NatOpen_Click(object s, RoutedEventArgs e)
    {
        _natBusy = true; NatOpenBtn.IsEnabled = NatCloseBtn.IsEnabled = false;
        NatState.Text = T("nat.opening");
        try
        {
            var st = await OpenNat.OpenAsync(CancellationToken.None);
            _natBusy = false;
            RenderNat(st);
            ShowToast(st.Open ? T("nat.toast.open") : st.Firewall && st.UpnpAvailable != true ? T("nat.toast.fwOnly") : T("nat.toast.partial"));
        }
        catch (Exception ex) { _natBusy = false; Logger.Error(T("lg.wt.fail", ex.Message)); NatState.Text = T("lg.wt.fail", ex.Message); }
    }

    private async void NatClose_Click(object s, RoutedEventArgs e)
    {
        _natBusy = true; NatOpenBtn.IsEnabled = NatCloseBtn.IsEnabled = false;
        try
        {
            var st = await OpenNat.CloseAsync(CancellationToken.None);
            _natBusy = false;
            RenderNat(st);
            ShowToast(T("nat.toast.closed"));
        }
        catch (Exception ex) { _natBusy = false; Logger.Error(T("lg.wt.fail", ex.Message)); NatState.Text = T("lg.wt.fail", ex.Message); }
    }
}
