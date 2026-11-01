using System.Windows;
using System.Windows.Controls;

namespace Voxa.Wpf.Views;

/// <summary>All settings except the ones on the main page (target language, source, start). Shown inside <see cref="MainWindow"/>.</summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    /// <summary>The back button; the window switches back to the main page.</summary>
    public event EventHandler? BackRequested;

    private void OnBack(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Each visit starts at the top.</summary>
    public void ScrollToTop() => Scroll.ScrollToTop();

    /// <summary>Typed but unsaved secrets don't stay in the page when it's left.</summary>
    public void ClearSecrets()
    {
        AzureKeyBox.Clear();
        DeepLKeyBox.Clear();
    }

    // PasswordBox.Password isn't a dependency property (on purpose: the secret shouldn't live in bindings),
    // so it is handed to the ViewModel here and cleared right away.
    private void OnSaveAzureKey(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && AzureKeyBox.Password.Length > 0)
            vm.SetAzureKey(AzureKeyBox.Password);
        AzureKeyBox.Clear();
    }

    private void OnSaveDeepLKey(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && DeepLKeyBox.Password.Length > 0)
            vm.SetDeepLKey(DeepLKeyBox.Password);
        DeepLKeyBox.Clear();
    }
}
