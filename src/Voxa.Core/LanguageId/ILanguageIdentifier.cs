namespace Voxa.Core.LanguageId;

/// <summary>Port: which language is spoken in a piece of audio (spoken language identification).</summary>
public interface ILanguageIdentifier : IDisposable
{
    /// <returns>ISO 639-1 code ("cs", "ru"...), or null when the model can't tell.</returns>
    Task<string?> IdentifyAsync(ReadOnlyMemory<float> samples16kMono, CancellationToken ct = default);
}
