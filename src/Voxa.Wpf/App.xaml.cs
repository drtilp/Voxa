using System.Windows;
using Voxa.Wpf.Audio;
using Voxa.Wpf.Services;
using Voxa.Wpf.Setup;
using Voxa.Wpf.Tray;
using Voxa.Wpf.ViewModels;
using Voxa.Wpf.Views;

namespace Voxa.Wpf;

/// <summary>
/// Composition root. The app lives in the tray: no main window, shutdown only via the tray menu.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private LoopbackAudioRecorder? _recorder;
    private MainViewModel? _viewModel;
    private OverlayWindow? _overlay;
    private MainWindow? _main;
    private SetupWizardWindow? _wizard;
    private UpdateDialog? _updateDialog;
    private readonly FileDialogService _fileDialogs = new();
    private bool _exiting;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, $@"Local\{AppInfo.DataFolderName}.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _recorder = new LoopbackAudioRecorder();
        _viewModel = new MainViewModel(_recorder, _fileDialogs, new SettingsStore());
        _viewModel.SetupWizardRequested += (_, _) => ShowSetupWizard();
        _viewModel.ExitRequested += (_, _) => ExitApp(); // the update installer is running
        _viewModel.UpdateAvailable += (_, _) => ShowUpdateDialog();

        // Variant B: models chosen in the installer -> applied once, on the first start after installing.
        if (!_viewModel.SetupCompleted && InstallManifest.TryLoad() is { } manifest)
            _viewModel.ApplySetup(manifest.ToChoice());

        _overlay = new OverlayWindow { DataContext = _viewModel };
        _overlay.Show();

        _tray = new TrayIcon(_viewModel, ShowMain, ShowSettings, ShowUpdateDialog, ExitApp);

        // Variant A (feature flag): the in-app wizard until the user finishes it.
        if (!_viewModel.SetupCompleted && _viewModel.IsFirstRunWizardEnabled)
            ShowSetupWizard();
        else
            ShowMain();

        _ = _viewModel.CheckForUpdatesOnStartAsync();
    }

    private void ShowSetupWizard()
    {
        if (_wizard is not null)
        {
            Activate(_wizard);
            return;
        }

        var wizard = new SetupWizardViewModel(_fileDialogs, _viewModel!.ModelsFolder);
        _wizard = new SetupWizardWindow { DataContext = wizard };
        wizard.Finished += (_, choice) =>
        {
            _viewModel.ApplySetup(choice);
            _wizard?.Close();
        };
        wizard.Skipped += (_, _) => _wizard?.Close();
        _wizard.Closed += (_, _) =>
        {
            _wizard = null;
            if (!_exiting) ShowMain();
        };
        Activate(_wizard);
    }

    private void ShowMain()
    {
        if (_main is null)
        {
            _main = new MainWindow { DataContext = _viewModel };
        }

        Activate(_main);
    }

    // Settings are a page of the main window, not a window of their own.
    private void ShowSettings()
    {
        ShowMain();
        _main!.ShowSettings();
    }

    // Also when Voxa only sits in the tray: the dialog is topmost, so a new version is noticed.
    private void ShowUpdateDialog()
    {
        if (_updateDialog is null)
        {
            _updateDialog = new UpdateDialog { DataContext = _viewModel };
            _updateDialog.Closed += (_, _) => _updateDialog = null;
        }
        Activate(_updateDialog);
    }

    private static void Activate(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        if (_main is not null) _main.AllowClose = true;
        _wizard?.Close();
        _updateDialog?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _viewModel?.Dispose();
        _recorder?.Dispose(); // finishes the WAV file if still recording

        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }
}
