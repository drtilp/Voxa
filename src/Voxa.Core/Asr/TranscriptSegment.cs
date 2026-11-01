namespace Voxa.Core.Asr;

/// <summary>
/// Streaming ASR output. Partial segments (IsFinal = false) keep growing until the final one closes the line.
/// </summary>
/// <param name="Start">Position in the audio stream (from the start of the session).</param>
/// <param name="Id">Sentence number: partials and the final of one sentence share it, so a client can replace by Id
/// (and attach a translation that arrives later).</param>
/// <param name="Language">Spoken language of a final (ISO 639-1), when a language identifier is configured.</param>
public sealed record TranscriptSegment(
    string Text, bool IsFinal, TimeSpan Start = default, TimeSpan End = default, long Id = 0, string? Language = null);
