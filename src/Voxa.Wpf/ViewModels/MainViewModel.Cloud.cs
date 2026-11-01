using System.Windows.Input;
using Voxa.Core.Asr;
using Voxa.Remote;
using Voxa.Wpf.Audio;
using Voxa.Wpf.Services;
using Voxa.Wpf.Transcription;

namespace Voxa.Wpf.ViewModels;

/// <summary>The cloud server: speech recognition and translation on our server (Settings → Cloud server).</summary>
public sealed partial class MainViewModel
{
    private string? _cloudStatus;
    private bool _isCheckingCloud;

    public ICommand CheckCloudCommand { get; }

    public string? CloudUrl
    {
        get => _settings.CloudUrl;
        set
        {
            var url = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (url == _settings.CloudUrl) return;
            UpdateSettings(s => s with { CloudUrl = url });
            OnPropertyChanged();
            OnCloudSettingsChanged();
        }
    }

    /// <summary>Result of "Check connection": what the server runs, or why it can't be used.</summary>
    public string? CloudStatus
    {
        get => _cloudStatus;
        private set
        {
            if (SetProperty(ref _cloudStatus, value))
                OnPropertyChanged(nameof(HasCloudStatus));
        }
    }

    public bool HasCloudStatus => CloudStatus is not null;

    private void OnCloudSettingsChanged()
    {
        CloudStatus = null;
        if (SelectedTranslator == TranslatorKind.Cloud) RecreateTranslator();
        if (SelectedModel?.Location == AsrModelLocation.Remote) RestartLiveIfRunning();
    }

    // Wakes a sleeping server (scale to zero) and waits for it, so the user sees that the setup works.
    private async Task CheckCloudAsync()
    {
        var (server, problem) = CloudServer.Create(_settings);
        if (server is null)
        {
            CloudStatus = problem;
            return;
        }

        _isCheckingCloud = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            CloudStatus = "Connecting…";
            await server.WaitUntilReadyAsync(() => CloudStatus = WakingUpText);
            var info = await server.GetInfoAsync();
            CloudStatus = $"Ready: {info.Asr}, translation {info.Translation ?? "none"}, runs on {info.Backend}.";
        }
        catch (Exception ex)
        {
            CloudStatus = ex.Message;
        }
        finally
        {
            _isCheckingCloud = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private const string WakingUpText = "The cloud server is waking up, about 2 minutes…";

    /// <summary>Live run against the server; its connection state goes to the overlay status line.</summary>
    private LiveTranscriptionSession StartCloudSession(AudioInput input, CancellationTokenSource run)
    {
        var (server, problem) = CloudServer.Create(_settings);
        if (server is null) throw new InvalidOperationException(problem);

        var transcriber = new RemoteTranscriber(server);
        transcriber.StateChanged += state => _dispatcher.BeginInvoke(() =>
        {
            if (_liveCts == run) SetLiveStatus(CloudStateText(state, input));
        });
        SetLiveStatus("Connecting to the cloud server…");
        return LiveTranscriptionSession.Remote(transcriber, input);
    }

    private static string CloudStateText(RemoteTranscriberState state, AudioInput input) => state switch
    {
        RemoteTranscriberState.Connecting => "Connecting to the cloud server…",
        RemoteTranscriberState.WakingUp => WakingUpText,
        RemoteTranscriberState.Listening => ListeningText(input),
        RemoteTranscriberState.Paused => "Cloud server paused (nobody spoke), continues when sound plays.",
        RemoteTranscriberState.Reconnecting => "Connection lost, reconnecting to the cloud server…",
        _ => "",
    };

    private static string ListeningText(AudioInput input) =>
        input == AudioInput.Microphone ? "Listening to the microphone…" : "Listening…";
}
