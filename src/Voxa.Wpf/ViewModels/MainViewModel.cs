using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Voxa.Core.Asr;
using Voxa.Core.Captions;
using Voxa.Core.Translation;
using Voxa.Engines.SherpaOnnx;
using Voxa.Remote;
using Voxa.Translators.Llama;
using Voxa.Wpf.Asr;
using Voxa.Wpf.Audio;
using Voxa.Wpf.Services;
using Voxa.Wpf.Transcription;

namespace Voxa.Wpf.ViewModels;

/// <summary>Shared by the overlay, the settings window and the tray icon.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const string RecordingOverlayText = "Is Recording Now";
    private static readonly TimeSpan CaptionHoldAfterEnd = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CaptionIdleClear = TimeSpan.FromSeconds(6);

    private readonly IAudioRecorder _recorder;
    private readonly IFileDialogService _fileDialogs;
    private ModelCatalog _catalog;
    private readonly SettingsStore _settingsStore;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly DispatcherTimer _captionIdleTimer;
    private readonly DispatcherTimer _subtitleTimer;
    private readonly Stopwatch _elapsed = new();
    private readonly CaptionBoard _captions = new();

    // Translation: finished sentences (also mid-monologue) -> translated -> shown in order, each long enough to read.
    private readonly SentenceCommitter _committer = new();
    private readonly SubtitleQueue _subtitles = new(TimeProvider.System);

    private AppSettings _settings;
    private bool _isRecording;
    private bool _isOverlayVisible = true;
    private string _overlayText = "";
    private string _overlayPreviousText = "";
    private string _overlayOriginalText = "";
    private bool _showRecordingIndicator;
    private string _elapsedText = "00:00:00";
    private string? _lastRecordingPath;
    private string? _errorMessage;

    // Bumped on every caption reset, so a translation that finishes late can't land on the next session's line.
    private int _captionGeneration;
    private CancellationTokenSource? _captionTestCts;

    // Live transcription
    private CancellationTokenSource? _liveCts;
    private bool _restartLiveAfterStop;
    private string _liveStatus = "";
    private IAsrEngine? _engine;
    private AsrModelOption? _engineModel;

    // Translation
    private ITranslator? _translator;
    private string? _translatorProblem;
    private string? _translatorInfo;
    private int _translatorVersion;

    /// <summary>Developer tools of the Dev channel; the settings page binds its visibility to these.</summary>
    public AppFeatures Features => AppFeatures.Current;

    public MainViewModel(IAudioRecorder recorder, IFileDialogService fileDialogs, SettingsStore settingsStore)
    {
        _recorder = recorder;
        _fileDialogs = fileDialogs;
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        _catalog = ModelCatalog.For(_settings.ModelsFolder);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _elapsedTimer.Tick += OnElapsedTick;
        _captionIdleTimer = new DispatcherTimer { Interval = CaptionIdleClear };
        _captionIdleTimer.Tick += OnCaptionIdle;
        // Lines wait for their turn (reading time), so the queue needs ticks even when no new speech arrives.
        _subtitleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _subtitleTimer.Tick += OnSubtitleTick;
        _recorder.StateChanged += OnRecorderStateChanged;

        ReloadModels();
        CreateDownloadItems();

        ToggleRecordingCommand = new RelayCommand(ToggleRecording);
        OpenRecordingsFolderCommand = new RelayCommand(OpenRecordingsFolder);
        AddOnnxModelCommand = new RelayCommand(AddOnnxModel);
        AddTranslationModelCommand = new RelayCommand(AddTranslationModel);
        BrowseModelsFolderCommand = new RelayCommand(BrowseModelsFolder);
        ResetModelsFolderCommand = new RelayCommand(() => SetModelsFolder(null), () => !IsDefaultModelsFolder);
        OpenModelsFolderCommand = new RelayCommand(OpenModelsFolder);
        OpenSetupWizardCommand = new RelayCommand(() => SetupWizardRequested?.Invoke(this, EventArgs.Empty));
        CheckForUpdatesCommand = new RelayCommand(() => _ = CheckForUpdatesAsync(manual: true),
            () => !_isCheckingForUpdates && !IsUpdating);
        InstallUpdateCommand = new RelayCommand(InstallUpdate, () => HasUpdate && !IsUpdating);
        SkipUpdateCommand = new RelayCommand(SkipUpdate, () => HasUpdate && !IsUpdating);
        OpenReleaseNotesCommand = new RelayCommand(() => OpenUrl(AvailableUpdate?.PageUrl ?? AppInfo.ReleasesPage));
        ToggleCaptionTestCommand = new RelayCommand(ToggleCaptionTest);
        ToggleLiveCommand = new RelayCommand(ToggleLive, () => IsLive || SelectedModel is not null);
        CheckCloudCommand = new RelayCommand(() => _ = CheckCloudAsync(), () => !_isCheckingCloud);

        RecreateTranslator();
    }

    public ICommand ToggleRecordingCommand { get; }
    public ICommand OpenRecordingsFolderCommand { get; }
    public ICommand ToggleCaptionTestCommand { get; }
    public ICommand ToggleLiveCommand { get; }

    private void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        _settings = change(_settings);
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (IOException ex)
        {
            ErrorMessage = $"Settings not saved: {ex.Message}";
        }
    }

    // ---------- Audio input (models: MainViewModel.Models.cs) ----------

    public IReadOnlyList<AudioInput> AudioInputs { get; } = Enum.GetValues<AudioInput>();

    public AudioInput SelectedAudioInput
    {
        get => _settings.AudioInput;
        set
        {
            if (value == _settings.AudioInput) return;
            UpdateSettings(s => s with { AudioInput = value });
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMicrophoneInput));
            RestartLiveIfRunning();
        }
    }

    /// <summary>Two-way bindable flag for the settings toggle (System audio ⇄ Microphone).</summary>
    public bool IsMicrophoneInput
    {
        get => SelectedAudioInput == AudioInput.Microphone;
        set => SelectedAudioInput = value ? AudioInput.Microphone : AudioInput.SystemAudio;
    }

    // Switch on the fly: stop, RunLiveAsync then starts again with the new model / input.
    private void RestartLiveIfRunning()
    {
        if (!IsLive) return;
        _restartLiveAfterStop = true;
        _liveCts?.Cancel();
    }

    // ---------- Translation ----------

    public IReadOnlyList<Language> TargetLanguages => Language.Targets;

    public Language SelectedTargetLanguage
    {
        get => Language.FromCode(_settings.TargetLanguage);
        set
        {
            if (value is null || value.Code == _settings.TargetLanguage) return;
            UpdateSettings(s => s with { TargetLanguage = value.Code });
            OnPropertyChanged();
            TranslatorProblem = TargetLanguageProblem();
        }
    }

    /// <summary>Chosen in Settings → Translation (<see cref="SelectedTranslationOption"/>).</summary>
    public TranslatorKind SelectedTranslator => _settings.Translator;

    public bool IsTranslationOff => SelectedTranslator == TranslatorKind.Off;

    public bool IsAzureTranslator => SelectedTranslator == TranslatorKind.Azure;

    public bool IsDeepLTranslator => SelectedTranslator == TranslatorKind.DeepL;

    public bool HasDeepLKey => !string.IsNullOrEmpty(_settings.DeepLKeyProtected);

    /// <summary>Neutral status of the translator (e.g. which local model and backend), shown in Settings.</summary>
    public string? TranslatorInfo
    {
        get => _translatorInfo;
        private set
        {
            if (SetProperty(ref _translatorInfo, value))
                OnPropertyChanged(nameof(HasTranslatorInfo));
        }
    }

    public bool HasTranslatorInfo => TranslatorInfo is not null;

    public bool ShowOriginal
    {
        get => _settings.ShowOriginal;
        set
        {
            if (value == _settings.ShowOriginal) return;
            UpdateSettings(s => s with { ShowOriginal = value });
            OnPropertyChanged();
            UpdateOverlay();
        }
    }

    public string? AzureRegion
    {
        get => _settings.AzureRegion;
        set
        {
            var region = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (region == _settings.AzureRegion) return;
            UpdateSettings(s => s with { AzureRegion = region });
            OnPropertyChanged();
            RecreateTranslator();
        }
    }

    public bool HasAzureKey => !string.IsNullOrEmpty(_settings.AzureKeyProtected);

    /// <summary>Problem with the current translator setup (missing key, ...), shown in Settings.</summary>
    public string? TranslatorProblem
    {
        get => _translatorProblem;
        private set
        {
            if (SetProperty(ref _translatorProblem, value))
                OnPropertyChanged(nameof(HasTranslatorProblem));
        }
    }

    public bool HasTranslatorProblem => TranslatorProblem is not null;

    /// <summary>Called from the settings window (PasswordBox can't be bound, by design).</summary>
    public void SetAzureKey(string key)
    {
        UpdateSettings(s => s with
        {
            AzureKeyProtected = string.IsNullOrWhiteSpace(key) ? null : SettingsStore.Protect(key.Trim()),
        });
        OnPropertyChanged(nameof(HasAzureKey));
        RecreateTranslator();
    }

    /// <summary>Called from the settings window, same reason as <see cref="SetAzureKey"/>.</summary>
    public void SetDeepLKey(string key)
    {
        UpdateSettings(s => s with
        {
            DeepLKeyProtected = string.IsNullOrWhiteSpace(key) ? null : SettingsStore.Protect(key.Trim()),
        });
        OnPropertyChanged(nameof(HasDeepLKey));
        RecreateTranslator();
    }

    private void OnTranslatorChanged()
    {
        OnPropertyChanged(nameof(SelectedTranslator));
        OnPropertyChanged(nameof(SelectedTranslationOption));
        OnPropertyChanged(nameof(IsTranslationOff));
        OnPropertyChanged(nameof(IsAzureTranslator));
        OnPropertyChanged(nameof(IsDeepLTranslator));
    }

    // async void: top-level "event" (settings changed). A version number protects against overlapping loads:
    // switching Local -> Azure -> Local quickly must not end with two models in VRAM or the wrong one active.
    private async void RecreateTranslator()
    {
        int version = ++_translatorVersion;
        var old = _translator;
        _translator = null;
        old?.Dispose();
        ResetSubtitles(); // lines waiting for the old translator would block the queue
        UpdateOverlay();

        TranslatorInfo = SelectedTranslator == TranslatorKind.Local ? "Loading the local translation model…" : null;
        TranslatorProblem = null;
        try
        {
            var (translator, problem) = await TranslatorFactory.CreateAsync(_settings, _selectedTranslationModel?.Path);
            if (version != _translatorVersion)
            {
                translator?.Dispose(); // a newer setting won meanwhile
                return;
            }

            _translator = translator;
            TranslatorProblem = problem ?? TargetLanguageProblem();
            TranslatorInfo = translator switch
            {
                LlamaTranslator local => $"{local.Name} on {LlamaTranslator.LoadedBackend ?? "?"}",
                RemoteTranslator => "Runs on the cloud server (the first sentence wakes it up, ~2 minutes).",
                _ => null,
            };
        }
        catch (Exception ex)
        {
            if (version != _translatorVersion) return;
            TranslatorInfo = null;
            TranslatorProblem = $"Translator failed to load: {ex.Message}";
        }
        UpdateOverlay();
    }

    // The cloud server runs the same Hy-MT2 model as the local translator.
    private string? TargetLanguageProblem() =>
        _translator is LlamaTranslator or RemoteTranslator && !LlamaTranslator.SupportsTarget(_settings.TargetLanguage)
            ? $"The translation model can't translate into {SelectedTargetLanguage.Name}. Pick another language or translator."
            : null;

    private bool IsTranslating => _translator is not null;

    // Fire and forget from the UI thread: the overlay must not wait for the network. The result is attached
    // to the sentence by Id whenever it arrives (or dropped if that sentence is gone).
    private async void TranslateInBackground(SubtitleUnit unit)
    {
        if (_translator is not { } translator) return;
        int generation = _captionGeneration;
        string text;
        try
        {
            var result = await translator.TranslateAsync(unit.Text, _settings.TargetLanguage);
            text = result.Text;
        }
        catch (Exception ex)
        {
            text = unit.Text; // show at least the original rather than nothing (and don't block the queue)
            TranslatorProblem = $"Translation failed: {ex.Message}";
        }

        if (generation != _captionGeneration) return;
        if (_subtitles.SetTranslation(unit.Sequence, text))
            UpdateOverlay();
        _subtitleTimer.Start();
    }

    // ---------- Recording ----------

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetProperty(ref _isRecording, value))
                OnPropertyChanged(nameof(RecordButtonText));
        }
    }

    public string RecordButtonText => IsRecording ? "Stop" : "Record";

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value);
    }

    public string? LastRecordingPath
    {
        get => _lastRecordingPath;
        private set
        {
            if (SetProperty(ref _lastRecordingPath, value))
                OnPropertyChanged(nameof(HasLastRecording));
        }
    }

    public bool HasLastRecording => LastRecordingPath is not null;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => ErrorMessage is not null;

    private void ToggleRecording()
    {
        ErrorMessage = null;
        try
        {
            if (_recorder.IsRecording) _recorder.Stop();
            else _recorder.Start();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void OnRecorderStateChanged(object? sender, RecordingStateChangedEventArgs e)
    {
        // The recorder may raise the event from the audio thread; bindings must be updated on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnRecorderStateChanged(sender, e));
            return;
        }

        IsRecording = e.IsRecording;

        if (e.IsRecording)
        {
            _elapsed.Restart();
            ElapsedText = "00:00:00";
            _elapsedTimer.Start();
        }
        else
        {
            _elapsed.Stop();
            _elapsedTimer.Stop();
            LastRecordingPath = e.FilePath;
        }

        if (e.Error is not null)
            ErrorMessage = $"Recording stopped: {e.Error.Message}";

        UpdateOverlay();
    }

    private void OnElapsedTick(object? sender, EventArgs e) =>
        ElapsedText = _elapsed.Elapsed.ToString(@"hh\:mm\:ss");

    private void OpenRecordingsFolder()
    {
        Directory.CreateDirectory(_recorder.RecordingsFolder);
        var args = LastRecordingPath is not null && File.Exists(LastRecordingPath)
            ? $"/select,\"{LastRecordingPath}\""
            : $"\"{_recorder.RecordingsFolder}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    // ---------- Overlay ----------

    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set => SetProperty(ref _isOverlayVisible, value);
    }

    /// <summary>Main line: translation (or the transcript when translation is off).</summary>
    public string OverlayText
    {
        get => _overlayText;
        private set
        {
            if (SetProperty(ref _overlayText, value))
                OnPropertyChanged(nameof(HasOverlayText));
        }
    }

    public bool HasOverlayText => OverlayText.Length > 0;

    public string OverlayPreviousText
    {
        get => _overlayPreviousText;
        private set
        {
            if (SetProperty(ref _overlayPreviousText, value))
                OnPropertyChanged(nameof(HasOverlayPreviousText));
        }
    }

    public bool HasOverlayPreviousText => OverlayPreviousText.Length > 0;

    /// <summary>Small grey original under the translation (only with "Show original").</summary>
    public string OverlayOriginalText
    {
        get => _overlayOriginalText;
        private set
        {
            if (SetProperty(ref _overlayOriginalText, value))
                OnPropertyChanged(nameof(HasOverlayOriginalText));
        }
    }

    public bool HasOverlayOriginalText => OverlayOriginalText.Length > 0;

    public bool HasOverlayContent => HasOverlayText || HasOverlayPreviousText || HasOverlayOriginalText || HasOverlayNoticeText;

    public bool ShowRecordingIndicator
    {
        get => _showRecordingIndicator;
        private set => SetProperty(ref _showRecordingIndicator, value);
    }

    // Priority: captions > live status ("Loading…", "Listening…") > recording status.
    private void UpdateOverlay()
    {
        var current = _captions.Current;
        var previous = _captions.Previous;

        if (_captions.IsEmpty && (!IsTranslating || _subtitles.IsEmpty))
        {
            OverlayText = _liveStatus.Length > 0 ? _liveStatus : IsRecording ? RecordingOverlayText : "";
            OverlayPreviousText = OverlayOriginalText = "";
        }
        else if (!IsTranslating)
        {
            OverlayText = current?.Text ?? "";
            OverlayPreviousText = previous?.Text ?? "";
            OverlayOriginalText = "";
        }
        else
        {
            // Translated sentences from the queue; the grey original (on request) is what is being said right now,
            // or the original of the shown line once the speaker stopped.
            OverlayText = _subtitles.Current?.Text ?? "";
            OverlayPreviousText = _subtitles.Previous?.Text ?? "";
            OverlayOriginalText = !ShowOriginal ? ""
                : _committer.PendingText.Length > 0 ? _committer.PendingText
                : _subtitles.Current?.Original ?? "";
        }

        ShowRecordingIndicator = IsRecording && OverlayText == RecordingOverlayText;
        OnPropertyChanged(nameof(HasOverlayContent));
    }

    private void ApplySegment(TranscriptSegment segment)
    {
        _captions.Apply(segment);
        TrackLanguage(segment);

        if (IsTranslating)
        {
            foreach (var unit in _committer.Update(segment))
            {
                _subtitles.Add(unit);
                TranslateInBackground(unit);
            }
            _subtitleTimer.Start();
        }

        UpdateOverlay();

        _captionIdleTimer.Stop();
        _captionIdleTimer.Start();
    }

    private void ResetSubtitles()
    {
        _subtitleTimer.Stop();
        _subtitles.Clear();
        _committer.Reset();
        _captionGeneration++; // translations still running land nowhere
    }

    private void OnSubtitleTick(object? sender, EventArgs e)
    {
        if (_subtitles.Tick()) UpdateOverlay();
        if (_subtitles.IsEmpty) _subtitleTimer.Stop();
    }

    private void ClearCaptions()
    {
        ResetSubtitles();
        _captionIdleTimer.Stop();
        _captions.Clear();
        UpdateOverlay();
    }

    // Nobody spoke for a while: don't keep the last sentence on screen forever. Translated lines are cleared by
    // the subtitle queue itself, once they had their reading time.
    private void OnCaptionIdle(object? sender, EventArgs e)
    {
        _captionIdleTimer.Stop();
        _captions.Clear();
        _committer.Reset();
        UpdateOverlay();
    }

    // ---------- Live transcription ----------

    public bool IsLive => _liveCts is not null;

    public string LiveButtonText => IsLive ? "Stop live transcription" : "Start live transcription";

    private void SetLiveStatus(string status)
    {
        _liveStatus = status;
        UpdateOverlay();
    }

    private void ToggleLive()
    {
        if (_liveCts is not null)
        {
            _liveCts.Cancel();
            return;
        }
        _ = RunLiveAsync();
    }

    private async Task RunLiveAsync()
    {
        if (SelectedModel is not { } model) return;

        using var cts = new CancellationTokenSource();
        _liveCts = cts;
        OnLiveChanged();
        _captionTestCts?.Cancel();
        ErrorMessage = null;
        IsOverlayVisible = true;

        try
        {
            ResetDetectedLanguage();
            var input = SelectedAudioInput;
            await using var session = model.Location == AsrModelLocation.Remote
                ? StartCloudSession(input, cts)
                : await StartLocalSessionAsync(model, input, cts.Token);

            // Runs on the UI thread: every await returns here, so ApplySegment can touch bindings directly.
            await foreach (var segment in session.ReadSegmentsAsync(cts.Token))
            {
                if (_liveStatus.Length > 0) _liveStatus = "";
                ApplySegment(segment);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // stopped by the user or by a model / input switch
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Live transcription failed: {ex.Message}";
        }
        finally
        {
            _liveCts = null;
            _liveStatus = "";
            ClearCaptions();
            OnLiveChanged();
        }

        if (_restartLiveAfterStop)
        {
            _restartLiveAfterStop = false;
            _ = RunLiveAsync();
        }
    }

    private async Task<LiveTranscriptionSession> StartLocalSessionAsync(AsrModelOption model, AudioInput input, CancellationToken ct)
    {
        SetLiveStatus($"Loading {model.Name}…");
        var engine = await GetEngineAsync(model, ct);

        var vadPath = _vadModel?.Path
                      ?? throw new InvalidOperationException("No voice activity detection (VAD) model. Download Silero VAD in Settings.");
        var languageIdentifier = await GetLanguageIdentifierAsync();
        var session = LiveTranscriptionSession.Local(engine, vadPath, input, languageIdentifier);
        SetLiveStatus(ListeningText(input));
        return session;
    }

    // The engine is expensive to load (GBs) -> keep it until another model is selected.
    private async Task<IAsrEngine> GetEngineAsync(AsrModelOption model, CancellationToken ct)
    {
        if (_engine is not null && _engineModel == model) return _engine;

        _engine?.Dispose();
        _engine = null;

        var descriptor = ModelCatalog.Describe(model);
        var runtime = new SherpaRuntimeOptions(NumThreads: Math.Max(1, Environment.ProcessorCount / 2));
        var engine = await Task.Run(() => descriptor.CreateEngine(runtime), ct);
        (_engine, _engineModel) = (engine, model);
        return engine;
    }

    private void OnLiveChanged()
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(LiveButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- ASR overlay test ----------

    public bool IsCaptionTestRunning => _captionTestCts is not null;

    // async void is acceptable only as a top-level event/command handler; all exceptions are handled inside.
    private async void ToggleCaptionTest()
    {
        if (_captionTestCts is not null)
        {
            _captionTestCts.Cancel();
            return;
        }

        using var cts = new CancellationTokenSource();
        _captionTestCts = cts;
        OnPropertyChanged(nameof(IsCaptionTestRunning));
        IsOverlayVisible = true;

        try
        {
            // Continuations return to the UI thread (captured SynchronizationContext), so no Dispatcher needed.
            await foreach (var segment in new LyricsTestTranscriber().RunAsync(cts.Token))
                ApplySegment(segment);

            await Task.Delay(CaptionHoldAfterEnd, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // stopped by the user
        }
        catch (Exception ex)
        {
            ErrorMessage = $"ASR overlay test failed: {ex.Message}";
        }
        finally
        {
            _captionTestCts = null;
            ClearCaptions();
            OnPropertyChanged(nameof(IsCaptionTestRunning));
        }
    }

    public void Dispose()
    {
        _captionTestCts?.Cancel();
        _liveCts?.Cancel();
        foreach (var item in ModelDownloadItems) item.Cancellation?.Cancel(); // .part files stay for the next start
        _recorder.StateChanged -= OnRecorderStateChanged;
        _elapsedTimer.Stop();
        _elapsedTimer.Tick -= OnElapsedTick;
        _captionIdleTimer.Stop();
        _captionIdleTimer.Tick -= OnCaptionIdle;
        _subtitleTimer.Stop();
        _subtitleTimer.Tick -= OnSubtitleTick;
        StopWatchingModelsFolder();
        DisposeLanguageIdentifier();
        _modelsChangedTimer?.Stop();
        _engine?.Dispose();
        _translator?.Dispose();
    }
}
