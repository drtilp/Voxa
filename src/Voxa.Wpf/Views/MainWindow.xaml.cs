using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Voxa.Wpf.Interop;

namespace Voxa.Wpf.Views;

/// <summary>
/// The app window with two pages: the main page (target language, source, start) and <see cref="SettingsPage"/>.
/// Navigation is view state only, so it lives here and not in the ViewModel.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Duration PageFade = new(TimeSpan.FromMilliseconds(120));

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
            FitToWorkArea(this);
        };
    }

    /// <summary>Set by the app on exit; otherwise closing only hides the window to the tray.</summary>
    public bool AllowClose { get; set; }

    public bool IsSettingsOpen => SettingsPage.Visibility == Visibility.Visible;

    public void ShowSettings()
    {
        if (IsSettingsOpen) return;
        SettingsPage.ScrollToTop();
        SwitchPage(from: HomePage, to: SettingsPage);
    }

    public void ShowHome()
    {
        if (!IsSettingsOpen) return;
        SettingsPage.ClearSecrets();
        SwitchPage(from: SettingsPage, to: HomePage);
    }

    private static void SwitchPage(UIElement from, UIElement to)
    {
        from.Visibility = Visibility.Collapsed;
        to.Visibility = Visibility.Visible;
        to.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, PageFade));
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => ShowSettings();

    private void OnSettingsBack(object? sender, EventArgs e) => ShowHome();

    // Esc and the mouse "back" button leave the settings, like in a browser or Windows Settings.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsSettingsOpen)
        {
            ShowHome();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1 && IsSettingsOpen)
        {
            ShowHome();
            e.Handled = true;
        }
        base.OnPreviewMouseDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            ShowHome(); // the next open starts on the main page
        }

        base.OnClosing(e);
    }

    /// <summary>The windows have a fixed height, but never taller than the screen; the content scrolls instead.</summary>
    internal static void FitToWorkArea(Window window)
    {
        double max = SystemParameters.WorkArea.Height - 32;
        if (window.Height > max) window.Height = max;
    }
}
