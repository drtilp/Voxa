using System.Windows;
using System.Windows.Interop;
using Voxa.Wpf.Interop;

namespace Voxa.Wpf.Views;

/// <summary>First-run model wizard (variant A). Logic is in <see cref="ViewModels.SetupWizardViewModel"/>.</summary>
public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
            MainWindow.FitToWorkArea(this);
        };
    }
}
