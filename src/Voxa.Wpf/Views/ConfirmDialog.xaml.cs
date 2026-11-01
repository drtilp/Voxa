using System.Windows;
using System.Windows.Interop;
using Voxa.Wpf.Interop;

namespace Voxa.Wpf.Views;

/// <summary>Dark yes/no question for destructive actions ("Delete … from this PC?"); a MessageBox would be light.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string headline, string message, string confirmText)
    {
        InitializeComponent();
        HeadlineText.Text = headline;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        SourceInitialized += (_, _) => NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
