using System.Windows;
using System.Windows.Interop;
using Voxa.Wpf.Interop;
using Voxa.Wpf.ViewModels;

namespace Voxa.Wpf.Views;

/// <summary>"Voxa X is available": Update now (download, install, restart) / Later / Skip this version.</summary>
public partial class UpdateDialog : Window
{
    public UpdateDialog()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    private void OnLater(object sender, RoutedEventArgs e) => Close();

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.SkipUpdateCommand.Execute(null);
        Close();
    }
}
