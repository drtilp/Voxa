using Voxa.Core.Asr;
using Voxa.Core.Captions;

namespace Voxa.Core.Tests;

public class CaptionBoardTests
{
    private readonly CaptionBoard _board = new();

    [Fact]
    public void Partials_of_one_sentence_replace_each_other()
    {
        _board.Apply(Partial(0, "Hello"));
        _board.Apply(Partial(0, "Hello world"));

        Assert.Equal(new CaptionLine(0, "Hello world", IsFinal: false), _board.Current);
        Assert.Null(_board.Previous);
    }

    [Fact]
    public void Finished_sentence_moves_up_when_the_next_starts()
    {
        _board.Apply(Final(0, "First."));
        _board.Apply(Partial(1, "Second"));

        Assert.Equal("First.", _board.Previous?.Text);
        Assert.Equal("Second", _board.Current?.Text);
    }

    [Fact]
    public void Empty_final_drops_the_stale_partial()
    {
        _board.Apply(Final(0, "Kept."));
        _board.Apply(Partial(1, "noise"));
        _board.Apply(Final(1, ""));

        Assert.Null(_board.Current);
        Assert.Equal("Kept.", _board.Previous?.Text);
    }

    [Fact]
    public void Translation_attaches_by_id_even_after_the_line_moved_up()
    {
        _board.Apply(Final(0, "Привіт."));
        _board.Apply(Partial(1, "Як"));

        Assert.True(_board.ApplyTranslation(0, "Hello."));

        Assert.Equal("Hello.", _board.Previous?.Translation);
        Assert.Null(_board.Current?.Translation);
    }

    [Fact]
    public void Late_translation_of_a_line_no_longer_shown_is_ignored()
    {
        _board.Apply(Final(0, "One."));
        _board.Apply(Final(1, "Two."));
        _board.Apply(Final(2, "Three."));

        Assert.False(_board.ApplyTranslation(0, "Jedna."));
        Assert.Null(_board.Previous?.Translation);
    }

    [Fact]
    public void Final_keeps_translation_slot_empty_until_it_arrives()
    {
        _board.Apply(Partial(0, "Dobrý"));
        _board.Apply(Final(0, "Dobrý den."));

        Assert.True(_board.Current?.IsFinal);
        Assert.Null(_board.Current?.Translation);
    }

    [Fact]
    public void Clear_empties_both_lines()
    {
        _board.Apply(Final(0, "One."));
        _board.Apply(Partial(1, "Two"));
        _board.Clear();

        Assert.True(_board.IsEmpty);
    }

    private static TranscriptSegment Partial(long id, string text) => new(text, IsFinal: false, Id: id);

    private static TranscriptSegment Final(long id, string text) => new(text, IsFinal: true, Id: id);
}
