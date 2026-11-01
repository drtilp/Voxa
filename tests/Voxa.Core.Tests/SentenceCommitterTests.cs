using Voxa.Core.Asr;
using Voxa.Core.Captions;

namespace Voxa.Core.Tests;

public class SentenceCommitterTests
{
    private static TranscriptSegment Partial(string text, long id = 0) => new(text, IsFinal: false, Id: id);
    private static TranscriptSegment Final(string text, long id = 0) => new(text, IsFinal: true, Id: id);

    [Fact]
    public void Finished_sentence_is_committed_once_it_is_stable_while_the_speaker_goes_on()
    {
        var committer = new SentenceCommitter();

        Assert.Empty(committer.Update(Partial("The meeting has been moved")));
        // First time the boundary appears: could still change, wait.
        Assert.Empty(committer.Update(Partial("The meeting has been moved to Friday. We will")));
        // Same first sentence in the next partial: stable -> translate it now, long before the pause.
        var units = committer.Update(Partial("The meeting has been moved to Friday. We will meet in the"));

        Assert.Equal("The meeting has been moved to Friday.", Assert.Single(units).Text);
        Assert.Equal("We will meet in the", committer.PendingText);
    }

    [Fact]
    public void Changing_sentence_is_not_committed()
    {
        var committer = new SentenceCommitter();

        committer.Update(Partial("I red the book. And"));
        var units = committer.Update(Partial("I read the book. And then"));

        Assert.Empty(units);
    }

    [Fact]
    public void Final_commits_the_rest_without_repeating_committed_sentences()
    {
        var committer = new SentenceCommitter();
        committer.Update(Partial("Good morning everyone. Today we"));
        committer.Update(Partial("Good morning everyone. Today we talk about"));

        // The final may differ slightly in the committed part (comma); the word count still lines up.
        var units = committer.Update(Final("Good morning, everyone. Today we talk about prices. Questions at the end."));

        Assert.Equal(["Today we talk about prices.", "Questions at the end."], units.Select(u => u.Text));
        Assert.Equal("", committer.PendingText);
    }

    [Fact]
    public void Short_segment_is_committed_by_its_final()
    {
        var committer = new SentenceCommitter();
        committer.Update(Partial("Hello"));

        var unit = Assert.Single(committer.Update(Final("Hello, how are you?")));

        Assert.Equal("Hello, how are you?", unit.Text);
    }

    [Fact]
    public void Run_on_sentence_is_released_by_clauses()
    {
        var committer = new SentenceCommitter { MaxSentenceLength = 80, MinClauseLength = 30 };
        const string text = "and then we went to the market in the old town, where they sold fresh bread and cheese, " +
                            "and after that we walked along the river until";

        committer.Update(Partial(text));
        var units = committer.Update(Partial(text + " the evening"));

        Assert.Equal(["and then we went to the market in the old town,", "where they sold fresh bread and cheese,"],
            units.Select(u => u.Text));
    }

    [Fact]
    public void Units_are_numbered_across_segments_and_a_new_segment_starts_clean()
    {
        var committer = new SentenceCommitter();
        var first = committer.Update(Final("One. Two.", id: 0));
        var second = committer.Update(Final("Three.", id: 1));

        Assert.Equal([0L, 1L, 2L], first.Concat(second).Select(u => u.Sequence));
        Assert.Equal(1, second[0].SegmentId);
    }

    [Theory]
    [InlineData("E.g. this is one. And two.", 2)]
    [InlineData("It costs 3.5 euros. Cheap!", 2)]
    [InlineData("Wait… What? No!", 3)]
    [InlineData("你好。今天很好。", 2)]
    public void Splits_sentences(string text, int count) =>
        Assert.Equal(count, SentenceCommitter.SplitSentences(text).Count);
}
