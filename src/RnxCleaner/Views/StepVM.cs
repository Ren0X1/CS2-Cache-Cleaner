using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using RnxCleaner.Core;

namespace RnxCleaner.Views;

/// <summary>Una fila de la pantalla de progreso.</summary>
public sealed class StepVM(RunStep step) : INotifyPropertyChanged
{
    public RunStep Step { get; } = step;
    public string Title => Step.Title;

    private StepStatus _status = StepStatus.Pending;
    public StepStatus Status
    {
        get => _status;
        set { _status = value; On(); On(nameof(Glyph)); On(nameof(Brush)); On(nameof(IsRunning)); On(nameof(TitleBrush)); }
    }

    private string _detail = "";
    public string Detail { get => _detail; set { _detail = value; On(); } }

    private double _progress;
    public double Progress { get => _progress; set { _progress = value; On(); } }

    private bool _hasProgress;
    public bool HasProgress { get => _hasProgress; set { _hasProgress = value; On(); } }

    public bool IsRunning => Status == StepStatus.Running;

    public string Glyph => Status switch
    {
        StepStatus.Pending => "[ -- ]",
        StepStatus.Running => "[ >> ]",
        StepStatus.Ok => "[ OK ]",
        StepStatus.Warn => "[WARN]",
        StepStatus.Error => "[FAIL]",
        StepStatus.Skipped => "[SKIP]",
        _ => ""
    };

    private static Brush B(string key) => (Brush)System.Windows.Application.Current.Resources[key];

    public Brush Brush => Status switch
    {
        StepStatus.Running => B("Hud"),
        StepStatus.Ok => B("Hud"),
        StepStatus.Warn => B("Amber"),
        StepStatus.Error => B("Red"),
        _ => B("Faint"),
    };

    public Brush TitleBrush => Status is StepStatus.Pending or StepStatus.Skipped ? B("Muted") : B("Text");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void On([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
