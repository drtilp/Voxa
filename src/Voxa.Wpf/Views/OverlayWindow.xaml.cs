using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Voxa.Wpf.Interop;

namespace Voxa.Wpf.Views;

/// <summary>
/// Transparent, click-through, never-focused window across the bottom of the primary screen.
/// </summary>
public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        PositionAtBottom();

        SourceInitialized += (_, _) => NativeMethods.MakeOverlay(new WindowInteropHelper(this).Handle);

        // Static event: it would keep this window alive, so unsubscribe on close.
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Taskbar moved / resolution or DPI changed
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
            PositionAtBottom();
    }

    private void PositionAtBottom()
    {
        var workArea = SystemParameters.WorkArea; // primary monitor, in DIPs, without the taskbar
        Left = workArea.Left;
        Width = workArea.Width;
        Top = workArea.Bottom - Height;
    }
}
