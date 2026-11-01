using Voxa.Core.Asr;
using Voxa.Core.LanguageId;
using Voxa.Core.Pipeline;

namespace Voxa.Core.Tests;

public class TranscriptionPipelineTests
{
    private static readonly TranscriptionPipelineOptions NoPartials = new() { PartialInterval = TimeSpan.FromHours(1) };

    [Fact]
    public async Task Speech_followed_by_silence_gives_one_final_with_timing()
    {
        var engine = new RecordingEngine();
        await using var pipeline = new TranscriptionPipeline(new ThresholdVad(), engine, NoPartials);

        await WriteAsync(pipeline, Signal.Silence(0.5), Signal.Speech(1.0), Signal.Silence(0.5));
        var segments = await CompleteAndReadAllAsync(pipeline);

        var final = Assert.Single(segments);
        Assert.True(final.IsFinal);
        Assert.Equal($"{Signal.Samples(1.0)} samples", final.Text);
        Assert.Equal(TimeSpan.FromSeconds(0.5), final.Start);
        Assert.Equal(TimeSpan.FromSeconds(1.5), final.End);
    }

    [Fact]
    public async Task Language_is_identified_for_finals_long_enough()
    {
        var identifier = new FixedLanguage("cs");
        await using var pipeline = new TranscriptionPipeline(new ThresholdVad(), new RecordingEngine(), NoPartials, identifier);

        await WriteAsync(pipeline, Signal.Speech(2.0), Signal.Silence(0.5), Signal.Speech(0.5), Signal.Silence(0.5));
        var segments = await CompleteAndReadAllAsync(pipeline);

        Assert.Equal("cs", segments[0].Language);
        Assert.Null(segments[1].Language);           // 0.5 s: too short for a reliable guess
        Assert.Equal([Signal.Samples(2.0)], identifier.Calls);
    }

    private sealed class FixedLanguage(string language) : ILanguageIdentifier
    {
        public List<int> Calls { get; } = [];

        public Task<string?> IdentifyAsync(ReadOnlyMemory<float> samples16kMono, CancellationToken ct = default)
        {
            Calls.Add(samples16kMono.Length);
            return Task.FromResult<string?>(language);
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task Two_sentences_give_two_finals_in_order()
    {
        var engine = new RecordingEngine();
        await using var pipeline = new TranscriptionPipeline(new ThresholdVad(), engine, NoPartials);

        await WriteAsync(pipeline, Signal.Speech(1.0), Signal.Silence(0.5), Signal.Speech(2.0), Signal.Silence(0.5));
        var segments = await CompleteAndReadAllAsync(pipeline);

        Assert.Equal([Signal.Samples(1.0), Signal.Samples(2.0)], engine.Calls);
        Assert.All(segments, s => Assert.True(s.IsFinal));
        Assert.True(segments[0].End <= segments[1].Start);
        Assert.Equal([0L, 1L], segments.Select(s => s.Id));
    }

    [Fact]
    public async Task Complete_flushes_speech_that_never_ended()
    {
        var engine = new RecordingEngine();
        await using var pipeline = new TranscriptionPipeline(new ThresholdVad(), engine, NoPartials);

        await WriteAsync(pipeline, Signal.Speech(0.8));
        var segments = await CompleteAndReadAllAsync(pipeline);

        var final = Assert.Single(segments);
        Assert.True(final.IsFinal);
        Assert.Equal($"{Signal.Samples(0.8)} samples", final.Text);
    }

    [Fact]
    public async Task Silence_only_never_calls_the_engine()
    {
        var engine = new RecordingEngine();
        await using var pipeline = new TranscriptionPipeline(new ThresholdVad(), engine, NoPartials);

        await WriteAsync(pipeline, Signal.Silence(3.0));
        var segments = await CompleteAndReadAllAsync(pipeline);

        Assert.Empty(segments);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public async Task Long_speech_produces_growing_partials_before_the_final()
    {
        var engine = new RecordingEngine();
        var options = new TranscriptionPipelineOptions
        {
            PartialInterval = TimeSpan.FromSeconds(1), PreRoll = TimeSpan.Zero,
            MaxBacklogForPartials = int.MaxValue, // test writes faster than real time; backlog skipping has its own test
        };
        await using var pipeline = new TranscriptionPipeline(new ThresholdVad(), engine, options);

        // 100 ms chunks like an audio callback
        foreach (var chunk in Signal.Speech(3.5).Chunk(Signal.Samples(0.1)))
            await pipeline.WriteAudioAsync(chunk, TestContext.Current.CancellationToken);
        await WriteAsync(pipeline, Signal.Silence(0.5));
        var segments = await CompleteAndReadAllAsync(pipeline);

        var partials = segments.Where(s => !s.IsFinal).ToList();
        Assert.Equal(3, partials.Count); // after 1 s, 2 s, 3 s
        Assert.True(partials.Select(p => p.End).SequenceEqual(partials.Select(p => p.End).Order()), "partials must grow");
        Assert.True(segments[^1].IsFinal, "final comes last");
        Assert.All(segments, s => Assert.Equal(0, s.Id)); // one sentence -> one id for partials and final
    }

    [Fact]
    public async Task Partials_are_skipped_while_audio_is_queued_but_the_final_is_not()
    {
        var engine = new RecordingEngine();
        var options = new TranscriptionPipelineOptions
        {
            PartialInterval = TimeSpan.FromSeconds(1), PreRoll = TimeSpan.Zero, MaxBacklogForPartials = 0,
        };
        var pipeline = new TranscriptionPipeline(new ThresholdVad(), engine, options);

        // Everything is queued before the worker can read it -> backlog > 0 at every partial check.
        foreach (var chunk in Signal.Speech(3.5).Chunk(Signal.Samples(0.1)))
            Assert.True(pipeline.TryWriteAudio(chunk));
        pipeline.TryWriteAudio(Signal.Silence(0.5));
        var segments = await CompleteAndReadAllAsync(pipeline);
        await pipeline.DisposeAsync();

        Assert.True(segments.Count(s => !s.IsFinal) < 3, "some partials should be skipped");
        Assert.True(Assert.Single(segments, s => s.IsFinal).IsFinal);
    }

    [Fact]
    public async Task Engine_failure_faults_the_output_stream()
    {
        var engine = new RecordingEngine { ThrowOnCall = new InvalidOperationException("model crashed") };
        await using var pipeline = new TranscriptionPipeline(new ThresholdVad(), engine, NoPartials);

        await WriteAsync(pipeline, Signal.Speech(0.5), Signal.Silence(0.5));
        pipeline.Complete();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in pipeline.ReadSegmentsAsync(TestContext.Current.CancellationToken)) { }
        });
        Assert.Equal("model crashed", ex.Message);
    }

    [Fact]
    public async Task TryWriteAudio_drops_instead_of_blocking_when_full()
    {
        var engine = new BlockingEngine();
        var options = NoPartials with { InputCapacity = 2 };
        await using var pipeline = new TranscriptionPipeline(new ThresholdVad(minSilenceSamples: 1), engine, options);

        // First chunk makes the worker block inside the engine, the rest pile up in the queue.
        pipeline.TryWriteAudio([0.5f, 0f]);
        await engine.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var accepted = Enumerable.Range(0, 5).Count(_ => pipeline.TryWriteAudio([0f]));

        Assert.Equal(2, accepted);
        Assert.Equal(3, pipeline.DroppedChunks);
        engine.Release.SetResult();
    }

    [Fact]
    public async Task Dispose_disposes_the_vad()
    {
        var vad = new ThresholdVad();
        await new TranscriptionPipeline(vad, new RecordingEngine()).DisposeAsync();
        Assert.True(vad.Disposed);
    }

    private static async Task WriteAsync(TranscriptionPipeline pipeline, params float[][] parts)
    {
        foreach (var part in parts)
            await pipeline.WriteAudioAsync(part, TestContext.Current.CancellationToken);
    }

    private static async Task<List<TranscriptSegment>> CompleteAndReadAllAsync(TranscriptionPipeline pipeline)
    {
        pipeline.Complete();
        var segments = new List<TranscriptSegment>();
        await foreach (var segment in pipeline.ReadSegmentsAsync(TestContext.Current.CancellationToken))
            segments.Add(segment);
        return segments;
    }

    private sealed class BlockingEngine : IAsrEngine
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name => "blocking";

        public async Task<AsrResult> TranscribeAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new AsrResult("", null, TimeSpan.Zero, TimeSpan.Zero);
        }

        public void Dispose() { }
    }
}
