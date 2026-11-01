namespace Voxa.Core.Translation;

/// <summary>
/// Port for machine translation (Ports &amp; Adapters), same idea as IAsrEngine.
/// Implementations live in Voxa.Translators.* projects.
/// </summary>
public interface ITranslator : IDisposable
{
    string Name { get; }

    /// <param name="targetLanguage">ISO 639-1 code, e.g. "en".</param>
    /// <param name="sourceLanguage">Hint; null = the translator detects it.</param>
    Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken ct = default);
}

/// <param name="DetectedLanguage">Source language as detected (or the hint), null when unknown.</param>
public sealed record TranslationResult(string Text, string? DetectedLanguage);

public sealed class TranslationException(string message, Exception? inner = null) : Exception(message, inner);
