using Voxa.Core.LanguageId;

namespace Voxa.Core.Tests;

public class LanguageTrackerTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Language_is_announced_once_there_is_enough_speech()
    {
        var tracker = new LanguageTracker();

        Assert.Null(tracker.Add("cs", S(2.5)));          // 2.5 s < 4 s of evidence
        Assert.Equal("cs", tracker.Add("cs", S(2)));
        Assert.Null(tracker.Add("cs", S(3)));            // unchanged: no new announcement
    }

    [Fact]
    public void One_misjudged_sentence_does_not_switch_the_language()
    {
        var tracker = new LanguageTracker();
        tracker.Add("sk", S(5));
        tracker.Add("sk", S(4));

        Assert.Null(tracker.Add("cs", S(3)));            // short Slovak sentence heard as Czech
        Assert.Equal("sk", tracker.Current);
    }

    [Fact]
    public void Real_switch_wins_the_vote_after_a_few_sentences()
    {
        var tracker = new LanguageTracker { Window = 4 };
        tracker.Add("cs", S(4));
        tracker.Add("cs", S(4));

        Assert.Null(tracker.Add("ru", S(4)));
        Assert.Null(tracker.Add("ru", S(4)));             // 50:50 is not enough
        Assert.Equal("ru", tracker.Add("ru", S(4)));     // the window now holds cs, ru, ru, ru
    }

    [Fact]
    public void Unclear_mix_announces_nothing()
    {
        var tracker = new LanguageTracker();

        tracker.Add("cs", S(3));
        Assert.Null(tracker.Add("sk", S(3)));
        Assert.Null(tracker.Current);
    }
}
