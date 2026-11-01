namespace Voxa.Core.Captions;

/// <summary>A translated unit on screen since <paramref name="ShownAt"/>.</summary>
public sealed record SubtitleLine(long Sequence, string Text, string Original, DateTimeOffset ShownAt);

/// <summary>
/// Translated subtitles, two lines (<see cref="Previous"/> above <see cref="Current"/>), shown <b>in order</b> and
/// each <b>long enough to read</b>.
/// <para>
/// Translations finish out of order and in bursts (a long monologue ends with several sentences at once).
/// A line leaves the screen only when two newer lines are shown, and a new line waits until the line it would
/// push out has been readable for <see cref="ReadingTime"/>. If the backlog grows anyway (fast speaker, slow
/// translation), lines are skipped through faster so the subtitles don't fall further and further behind.
/// </para>
/// Pure logic with an injected clock (<see cref="TimeProvider"/>): the UI calls <see cref="Tick"/> on a timer.
/// </summary>
public sealed class SubtitleQueue(TimeProvider clock)
{
    private readonly SortedDictionary<long, (string Original, string? Translation)> _pending = [];

    /// <summary>Waiting translated lines above which reading time is ignored, to catch up.</summary>
    public int MaxBacklog { get; init; } = 2;

    /// <summary>Reading speed: subtitle guidelines use ~15-17 characters per second.</summary>
    public double CharactersPerSecond { get; init; } = 15;

    public TimeSpan MinReadingTime { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxReadingTime { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>No new line for this long (after it was readable): the subtitles disappear.</summary>
    public TimeSpan IdleClear { get; init; } = TimeSpan.FromSeconds(6);

    public SubtitleLine? Current { get; private set; }
    public SubtitleLine? Previous { get; private set; }

    public bool IsEmpty => Current is null && Previous is null && _pending.Count == 0;

    /// <summary>Waiting for translation or for its turn on screen.</summary>
    public int PendingCount => _pending.Count;

    public void Add(SubtitleUnit unit) => _pending[unit.Sequence] = (unit.Text, null);

    /// <summary>Translation finished (or failed: pass the original). Returns true when the screen changed.</summary>
    public bool SetTranslation(long sequence, string translation)
    {
        if (!_pending.TryGetValue(sequence, out var item)) return false;
        _pending[sequence] = item with { Translation = translation };
        return Tick();
    }

    public TimeSpan ReadingTime(string text)
    {
        var time = TimeSpan.FromSeconds(1 + text.Length / CharactersPerSecond);
        return time < MinReadingTime ? MinReadingTime : time > MaxReadingTime ? MaxReadingTime : time;
    }

    /// <summary>Moves ready lines on screen and clears stale ones. Returns true when the screen changed.</summary>
    public bool Tick()
    {
        var now = clock.GetUtcNow();
        bool changed = false;

        while (_pending.Count > 0)
        {
            var (sequence, item) = _pending.First();
            if (item.Translation is null) break; // keep the order: wait for the oldest translation

            int ready = _pending.Values.TakeWhile(p => p.Translation is not null).Count();
            bool previousRead = Previous is null || now - Previous.ShownAt >= ReadingTime(Previous.Text);
            if (!previousRead && ready <= MaxBacklog) break;

            Previous = Current;
            Current = new SubtitleLine(sequence, item.Translation, item.Original, now);
            _pending.Remove(sequence);
            changed = true;
        }

        if (Current is not null && _pending.Count == 0
            && now - Current.ShownAt >= Max(IdleClear, ReadingTime(Current.Text)))
        {
            Current = Previous = null;
            changed = true;
        }

        return changed;
    }

    public void Clear()
    {
        _pending.Clear();
        Current = Previous = null;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
