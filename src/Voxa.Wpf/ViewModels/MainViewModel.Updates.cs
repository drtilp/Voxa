using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using Voxa.Core.Updates;
using Voxa.Wpf.Services;

namespace Voxa.Wpf.ViewModels;

/// <summary>
/// Updates: check GitHub Releases, ask in a dialog, download the installer, run it and exit; the installer starts
/// Voxa again.
/// </summary>
public sealed partial class MainViewModel
{
    private static readonly TimeSpan StartupCheckDelay = TimeSpan.FromSeconds(5);

    // Own client: the download takes longer than the 10 s translator timeout.
    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    private GitHubReleaseChecker? _releases;

    private AppRelease? _availableUpdate;
    private string? _updateStatus;
    private bool _isUpdating;
    private bool _isCheckingForUpdates;
    private double _updateProgress;

    /// <summary>The installer has started; the app must exit so it can replace the files.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>A newer version was found: the app shows the update dialog.</summary>
    public event EventHandler? UpdateAvailable;

    public ICommand CheckForUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand SkipUpdateCommand { get; }
    public ICommand OpenReleaseNotesCommand { get; }

    public string CurrentVersion => AppInfo.DisplayVersion;

    public AppRelease? AvailableUpdate
    {
        get => _availableUpdate;
        private set
        {
            if (!SetProperty(ref _availableUpdate, value)) return;
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(UpdateHeadline));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasUpdate => AvailableUpdate is not null;

    public string UpdateHeadline => AvailableUpdate is { } r ? $"Voxa {VersionText(r)} is available" : "";

    /// <summary>Progress or result of the last check / download, shown in Settings and the update dialog.</summary>
    public string? UpdateStatus
    {
        get => _updateStatus;
        private set
        {
            if (SetProperty(ref _updateStatus, value))
                OnPropertyChanged(nameof(HasUpdateStatus));
        }
    }

    public bool HasUpdateStatus => UpdateStatus is not null;

    /// <summary>0..1 while the installer downloads.</summary>
    public double UpdateProgress
    {
        get => _updateProgress;
        private set => SetProperty(ref _updateProgress, value);
    }

    public bool IsUpdating
    {
        get => _isUpdating;
        private set
        {
            if (SetProperty(ref _isUpdating, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private GitHubReleaseChecker Releases => _releases ??= new GitHubReleaseChecker(
        UpdateHttp, AppInfo.GitHubOwner, AppInfo.GitHubRepo, AppInfo.InstallerAssetName);

    public bool CheckForUpdatesOnStart
    {
        get => _settings.CheckForUpdates;
        set
        {
            if (value == _settings.CheckForUpdates) return;
            UpdateSettings(s => s with { CheckForUpdates = value });
            OnPropertyChanged();
        }
    }

    /// <summary>Called once by the app after start; quiet: no message when offline or up to date.</summary>
    public async Task CheckForUpdatesOnStartAsync()
    {
        if (!CheckForUpdatesOnStart) return;
        await Task.Delay(StartupCheckDelay); // don't compete with loading the models
        await CheckForUpdatesAsync(manual: false);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_isCheckingForUpdates) return;
        _isCheckingForUpdates = true;
        CommandManager.InvalidateRequerySuggested();
        if (manual) UpdateStatus = "Checking for updates…";
        try
        {
            var release = await Releases.FindNewerAsync(AppInfo.Version);
            bool skipped = !manual && release is not null && VersionText(release) == _settings.SkippedUpdateVersion;
            AvailableUpdate = skipped ? null : release;
            if (manual)
                UpdateStatus = release is null ? $"Voxa {CurrentVersion} is the latest version." : null;
            if (AvailableUpdate is not null)
                UpdateAvailable?.Invoke(this, EventArgs.Empty);
        }
        catch (UpdateException ex)
        {
            if (manual) UpdateStatus = ex.Message; // at start a missing network is not worth a message
        }
        finally
        {
            _isCheckingForUpdates = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // async void: command handler; every exception is handled inside.
    private async void InstallUpdate()
    {
        if (AvailableUpdate is not { } release || IsUpdating) return;
        IsUpdating = true;
        try
        {
            var setup = Path.Combine(Path.GetTempPath(), $"Voxa-Setup-{VersionText(release)}.exe");
            UpdateStatus = "Downloading the update…";
            await Releases.DownloadAsync(release, setup, new Progress<double>(p =>
            {
                UpdateProgress = p;
                UpdateStatus = $"Downloading the update… {p:P0}";
            }));

            UpdateStatus = "Starting the installer… Windows asks for permission.";
            // "runas": the UAC prompt comes now, while Voxa still runs, so "No" just cancels the update.
            // /SILENT: progress window only; the installer keeps the folder and the models. /fromapp=1: it waits
            // until Voxa has exited and then starts it again (installer\Voxa.iss, StartedByUpdater).
            Process.Start(new ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART /fromapp=1")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: "No" in the UAC prompt
        {
            UpdateStatus = "Update cancelled.";
        }
        catch (Exception ex) when (ex is UpdateException or Win32Exception or IOException)
        {
            UpdateStatus = ex.Message;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private void SkipUpdate()
    {
        if (AvailableUpdate is not { } release) return;
        UpdateSettings(s => s with { SkippedUpdateVersion = VersionText(release) });
        AvailableUpdate = null;
        UpdateStatus = null;
    }

    private static string VersionText(AppRelease release) => release.Version.ToString(3);

    private static void OpenUrl(Uri url) =>
        Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
}
