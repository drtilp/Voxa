using Voxa.Core.Asr;

namespace Voxa.Core.Captions;

/// <param name="Translation">Arrives later than the final text (translation is a separate async step).</param>
public sealed record CaptionLine(long Id, string Text, bool IsFinal, string? Translation = null);

/// <summary>
/// Two-line subtitle model: the current (growing) sentence and the previous finished one.
/// Pure logic without UI, so it is unit-tested; the overlay only renders <see cref="Current"/> and <see cref="Previous"/>.
/// </summary>
public sealed class CaptionBoard
{
    public CaptionLine? Current { get; private set; }
    public CaptionLine? Previous { get; private set; }

    public bool IsEmpty => Current is null && Previous is null;

    public void Apply(TranscriptSegment segment)
    {
        if (Current is not null && Current.Id == segment.Id)
        {
            // Same sentence: partial grows or becomes final.
            if (segment.IsFinal && segment.Text.Length == 0)
            {
                Current = null; // the VAD heard noise, not speech: drop the stale partial
                return;
            }
            Current = Current with { Text = segment.Text, IsFinal = segment.IsFinal };
            return;
        }

        if (segment.Text.Length == 0) return;

        // New sentence: a finished current line moves up; an unfinished one is dropped.
        if (Current is { IsFinal: true })
            Previous = Current;
        Current = new CaptionLine(segment.Id, segment.Text, segment.IsFinal);
    }

    /// <returns>False when the sentence is no longer on screen.</returns>
    public bool ApplyTranslation(long id, string translation)
    {
        if (Current?.Id == id)
        {
            Current = Current with { Translation = translation };
            return true;
        }
        if (Previous?.Id == id)
        {
            Previous = Previous with { Translation = translation };
            return true;
        }
        return false;
    }

    public void Clear() => Current = Previous = null;
}
