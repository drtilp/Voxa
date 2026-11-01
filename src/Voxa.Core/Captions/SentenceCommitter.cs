using System.Text.RegularExpressions;
using Voxa.Core.Asr;

namespace Voxa.Core.Captions;

/// <summary>A piece of speech ready to translate and show: one sentence (or clause of a very long one).</summary>
/// <param name="Sequence">Global order of units; the subtitles are shown in this order.</param>
public sealed record SubtitleUnit(long Sequence, long SegmentId, string Text);

/// <summary>
/// Cuts the growing transcript of one speech segment into sentences <b>while the person keeps talking</b>.
/// <para>
/// The VAD closes a segment only after a pause, so a speaker without pauses produces one 15 s "sentence" that
/// would be translated (and shown) only at its end. Instead, every partial is split into sentences and a
/// finished sentence is committed as soon as it is <b>stable</b>: the same text in two partials in a row
/// (partials re-transcribe the whole segment, so the last words still change). The final commits the rest.
/// </para>
/// Committed text is tracked by word count: the final may differ from the partials in punctuation or a word,
/// but rarely in the number of words before the committed boundary.
/// </summary>
public sealed partial class SentenceCommitter
{
    /// <summary>An unfinished sentence longer than this is split at a comma / semicolon / dash.</summary>
    public int MaxSentenceLength { get; init; } = 160;

    /// <summary>A clause shorter than this is not worth a subtitle of its own.</summary>
    public int MinClauseLength { get; init; } = 40;

    private long _segmentId = -1;
    private int _committedWords;
    private List<string> _previousCandidates = [];
    private long _nextSequence;

    /// <summary>Text of the current segment that hasn't been committed yet (still being spoken).</summary>
    public string PendingText { get; private set; } = "";

    /// <returns>Newly committed units, in order. Usually empty for a partial.</returns>
    public IReadOnlyList<SubtitleUnit> Update(TranscriptSegment segment)
    {
        if (segment.Id != _segmentId)
        {
            _segmentId = segment.Id;
            _committedWords = 0;
            _previousCandidates = [];
        }

        var words = SplitWords(segment.Text);
        var rest = string.Join(' ', words.Skip(_committedWords));
        var committed = new List<SubtitleUnit>();

        if (segment.IsFinal)
        {
            // The segment is over: everything left is final, split into sentences for readable subtitles.
            foreach (var sentence in SplitSentences(rest))
                committed.Add(Commit(sentence));
            _previousCandidates = [];
            PendingText = "";
            return committed;
        }

        var candidates = CompleteUnits(rest);
        int stable = 0;
        while (stable < candidates.Count && stable < _previousCandidates.Count
               && candidates[stable] == _previousCandidates[stable])
        {
            committed.Add(Commit(candidates[stable]));
            stable++;
        }

        _previousCandidates = candidates.Skip(stable).ToList();
        PendingText = string.Join(' ', words.Skip(_committedWords));
        return committed;
    }

    public void Reset()
    {
        _segmentId = -1;
        _committedWords = 0;
        _previousCandidates = [];
        PendingText = "";
    }

    private SubtitleUnit Commit(string text)
    {
        _committedWords += SplitWords(text).Length;
        return new SubtitleUnit(_nextSequence++, _segmentId, text);
    }

    /// <summary>Finished units at the start of <paramref name="text"/>; the last, unfinished sentence is left out.</summary>
    private List<string> CompleteUnits(string text)
    {
        var sentences = SplitSentences(text);
        if (sentences.Count == 0) return [];

        var complete = sentences.Take(sentences.Count - 1).ToList();
        var tail = sentences[^1];

        // A run-on sentence with no end in sight: release its clauses, keep the newest one pending.
        if (tail.Length > MaxSentenceLength)
        {
            var clauses = SplitClauses(tail);
            complete.AddRange(clauses.Take(clauses.Count - 1));
        }

        return complete;
    }

    public static List<string> SplitSentences(string text) =>
        SentenceEnd().Split(text.Trim()).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    private List<string> SplitClauses(string sentence)
    {
        var clauses = new List<string>();
        var current = "";
        foreach (var piece in ClauseEnd().Split(sentence))
        {
            current = current.Length == 0 ? piece : current + " " + piece;
            if (current.Length >= MinClauseLength)
            {
                clauses.Add(current);
                current = "";
            }
        }

        if (current.Length > 0)
        {
            if (clauses.Count > 0 && current.Length < MinClauseLength / 2)
                clauses[^1] += " " + current; // don't leave a tiny last clause
            else
                clauses.Add(current);
        }
        return clauses;
    }

    private static string[] SplitWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // End of sentence: . ! ? … followed by a space and a capital letter, digit or quote ("e.g. this" stays one sentence),
    // or CJK full stops without a space.
    [GeneratedRegex(@"(?<=[.!?…])\s+(?=[\p{Lu}\p{N}""„«“'(¿¡])|(?<=[。！？])")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"(?<=[,;:])\s+|\s+(?=[–—]\s)")]
    private static partial Regex ClauseEnd();
}
