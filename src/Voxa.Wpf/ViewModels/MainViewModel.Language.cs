using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Voxa.Core.Asr;
using Voxa.Core.LanguageId;
using Voxa.Engines.SherpaOnnx;

namespace Voxa.Wpf.ViewModels;

/// <summary>Spoken language: identified per sentence (Whisper), decided by a vote, announced in the overlay.</summary>
public sealed partial class MainViewModel
{
    private static readonly TimeSpan LanguageNoticeDuration = TimeSpan.FromSeconds(4);

    private readonly LanguageTracker _languageTracker = new();
    private ILanguageIdentifier? _languageIdentifier;
    private string? _languageIdentifierPath;
    private string? _languageIdModelPath;
    private string? _detectedLanguage;
    private string _overlayNoticeText = "";
    private DispatcherTimer? _languageNoticeTimer;

    /// <summary>"Czech", "Russian"... while live, once enough speech was heard.</summary>
    public string? DetectedLanguageName => _detectedLanguage is { } code ? LanguageName(code) : null;

    public bool HasDetectedLanguage => _detectedLanguage is not null;

    public string LanguageIdModelText => _languageIdModelPath is { } path
        ? Path.GetFileName(path)
        : "Off — download Whisper small under Models folder.";

    public bool HasLanguageIdModel => _languageIdModelPath is not null;

    /// <summary>Small line above the subtitles: "Czech detected" for a few seconds after a detection or a switch.</summary>
    public string OverlayNoticeText
    {
        get => _overlayNoticeText;
        private set
        {
            if (SetProperty(ref _overlayNoticeText, value))
                OnPropertyChanged(nameof(HasOverlayNoticeText));
        }
    }

    public bool HasOverlayNoticeText => OverlayNoticeText.Length > 0;

    private void RefreshLanguageIdModel()
    {
        _languageIdModelPath = _catalog.DiscoverLanguageIdModels().FirstOrDefault();
        OnPropertyChanged(nameof(LanguageIdModelText));
        OnPropertyChanged(nameof(HasLanguageIdModel));
    }

    // Loaded once (~1 s) and kept, like the ASR engine. No model = no language detection, transcription still works.
    private async Task<ILanguageIdentifier?> GetLanguageIdentifierAsync()
    {
        if (_languageIdModelPath is not { } path) return null;
        if (_languageIdentifier is not null && _languageIdentifierPath == path) return _languageIdentifier;

        _languageIdentifier?.Dispose();
        _languageIdentifier = null;
        try
        {
            _languageIdentifier = await Task.Run(() => WhisperLanguageIdentifier.TryCreate(path));
            _languageIdentifierPath = path;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Language detection unavailable: {ex.Message}";
        }
        return _languageIdentifier;
    }

    private void TrackLanguage(TranscriptSegment segment)
    {
        if (!segment.IsFinal || segment.Language is not { } language) return;
        if (_languageTracker.Add(language, segment.End - segment.Start) is not { } decided) return;

        _detectedLanguage = decided;
        OnPropertyChanged(nameof(DetectedLanguageName));
        OnPropertyChanged(nameof(HasDetectedLanguage));
        ShowLanguageNotice($"{LanguageName(decided)} detected");
    }

    private void ShowLanguageNotice(string text)
    {
        if (_languageNoticeTimer is null)
        {
            _languageNoticeTimer = new DispatcherTimer { Interval = LanguageNoticeDuration };
            _languageNoticeTimer.Tick += OnLanguageNoticeElapsed;
        }
        OverlayNoticeText = text;
        UpdateOverlay();
        _languageNoticeTimer.Stop();
        _languageNoticeTimer.Start();
    }

    private void OnLanguageNoticeElapsed(object? sender, EventArgs e)
    {
        _languageNoticeTimer?.Stop();
        OverlayNoticeText = "";
        UpdateOverlay();
    }

    private void ResetDetectedLanguage()
    {
        _languageTracker.Reset();
        _detectedLanguage = null;
        OnPropertyChanged(nameof(DetectedLanguageName));
        OnPropertyChanged(nameof(HasDetectedLanguage));
        _languageNoticeTimer?.Stop();
        OverlayNoticeText = "";
    }

    private void DisposeLanguageIdentifier()
    {
        _languageNoticeTimer?.Stop();
        _languageIdentifier?.Dispose();
    }

    /// <summary>ISO 639-1 → English name ("cs" → "Czech"); Whisper also returns codes Windows doesn't know.</summary>
    private static string LanguageName(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            return culture.ThreeLetterWindowsLanguageName == "ZZZ" ? code.ToUpperInvariant() : culture.EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return code.ToUpperInvariant();
        }
    }
}
