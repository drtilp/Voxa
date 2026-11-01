namespace Voxa.Core.LanguageId;

/// <summary>
/// Decides the spoken language from per-sentence guesses. A single short sentence is often misjudged
/// (Slovak heard as Czech or Spanish in 3 s), so the language is a vote of the last few sentences weighted
/// by their length, announced only with enough evidence, and switched only when another language wins the vote.
/// </summary>
public sealed class LanguageTracker
{
    private readonly Queue<(string Language, TimeSpan Duration)> _votes = new();

    /// <summary>How many recent sentences vote.</summary>
    public int Window { get; init; } = 6;

    /// <summary>Speech needed before the first announcement.</summary>
    public TimeSpan MinEvidence { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>The winner's share of the votes; below it the language stays as it was.</summary>
    public double MinShare { get; init; } = 0.6;

    public string? Current { get; private set; }

    /// <returns>The new language when the decision changed (first detection or a switch), else null.</returns>
    public string? Add(string language, TimeSpan duration)
    {
        _votes.Enqueue((language, duration));
        while (_votes.Count > Window) _votes.Dequeue();

        var total = _votes.Sum(v => v.Duration.TotalSeconds);
        if (total < MinEvidence.TotalSeconds) return null;

        var (winner, seconds) = _votes
            .GroupBy(v => v.Language)
            .Select(g => (Language: g.Key, Seconds: g.Sum(v => v.Duration.TotalSeconds)))
            .MaxBy(g => g.Seconds);

        if (winner == Current || seconds / total < MinShare) return null;
        Current = winner;
        return winner;
    }

    public void Reset()
    {
        _votes.Clear();
        Current = null;
    }
}
