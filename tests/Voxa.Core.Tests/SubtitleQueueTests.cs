using Voxa.Core.Captions;

namespace Voxa.Core.Tests;

public class SubtitleQueueTests
{
    private readonly ManualClock _clock = new();

    private SubtitleQueue Queue() => new(_clock) { MinReadingTime = TimeSpan.FromSeconds(2), CharactersPerSecond = 10 };

    [Fact]
    public void Lines_are_shown_in_order_even_when_translations_finish_out_of_order()
    {
        var queue = Queue();
        queue.Add(new SubtitleUnit(0, 0, "jedna"));
        queue.Add(new SubtitleUnit(1, 0, "dva"));

        Assert.False(queue.SetTranslation(1, "two")); // waits for #0
        Assert.True(queue.SetTranslation(0, "one"));

        Assert.Equal("one", queue.Previous?.Text);
        Assert.Equal("two", queue.Current?.Text);
    }

    [Fact]
    public void A_line_is_not_pushed_off_screen_before_it_could_be_read()
    {
        var queue = Queue();
        Show(queue, 0, "A long first sentence."); // 22 chars -> 1 + 2.2 = 3.2 s
        Show(queue, 1, "second");

        queue.Add(new SubtitleUnit(2, 0, "třetí"));
        queue.SetTranslation(2, "third");
        Assert.Equal("second", queue.Current?.Text); // the first line is still being read

        _clock.Advance(TimeSpan.FromSeconds(3.3));
        Assert.True(queue.Tick());
        Assert.Equal("second", queue.Previous?.Text);
        Assert.Equal("third", queue.Current?.Text);
    }

    [Fact]
    public void Too_big_backlog_skips_reading_time_to_catch_up()
    {
        var queue = Queue();
        Show(queue, 0, "first");
        Show(queue, 1, "second");
        for (int i = 2; i <= 4; i++)
        {
            queue.Add(new SubtitleUnit(i, 0, $"orig {i}"));
            queue.SetTranslation(i, $"line {i}");
        }

        // 3 ready lines > MaxBacklog 2: shown until the backlog is back at the limit.
        Assert.Equal("line 2", queue.Current?.Text);
        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public void Last_line_disappears_after_idle_time()
    {
        var queue = Queue();
        Show(queue, 0, "bye");

        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(queue.Tick());
        _clock.Advance(TimeSpan.FromSeconds(1.1));
        Assert.True(queue.Tick());
        Assert.True(queue.IsEmpty);
    }

    [Theory]
    [InlineData("ok", 2)]                     // minimum
    [InlineData("1234567890123456789", 2.9)] // 1 s + 19 chars / 10 per s
    public void Reading_time_grows_with_length(string text, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), Queue().ReadingTime(text));

    private static void Show(SubtitleQueue queue, long sequence, string translation)
    {
        queue.Add(new SubtitleUnit(sequence, 0, "orig"));
        queue.SetTranslation(sequence, translation);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
