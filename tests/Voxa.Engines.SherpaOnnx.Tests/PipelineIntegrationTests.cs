using Voxa.Core.Asr;
using Voxa.Core.Audio;
using Voxa.Core.Pipeline;

namespace Voxa.Engines.SherpaOnnx.Tests;

/// <summary>Real Silero VAD + Parakeet Ultra on two reference recordings separated by silence.</summary>
public sealed class PipelineIntegrationTests(ParakeetUltraInt8Fixture fixture) : IClassFixture<ParakeetUltraInt8Fixture>
{
    [Fact]
    public async Task Vad_splits_two_recordings_into_two_accurate_finals()
    {
        var engine = fixture.RequireEngine();
        var vadModel = Path.Combine(ModelPaths.Models, "vad", "silero_vad.onnx");
        Assert.SkipUnless(File.Exists(vadModel), $"Model not downloaded: {vadModel}");

        var first = WavFile.Read(ModelPaths.WhisperTurbo(Path.Combine("test_wavs", "0.wav")));
        var second = WavFile.Read(ModelPaths.WhisperTurbo(Path.Combine("test_wavs", "1.wav")));
        Assert.Equal(TranscriptionPipeline.SampleRate, first.SampleRate);
        Assert.Equal(TranscriptionPipeline.SampleRate, second.SampleRate);
        float[] stream = [.. first.Samples, .. new float[16_000], .. second.Samples, .. new float[16_000]];

        await using var pipeline = new TranscriptionPipeline(new SileroVoiceActivityDetector(vadModel), engine);
        var ct = TestContext.Current.CancellationToken;
        var reading = ReadAllAsync(pipeline, ct);

        // Faster than real time -> the backlog rule skips most partials (unit-tested separately); finals must be exact.
        foreach (var chunk in stream.Chunk(1_600)) // 100 ms chunks like an audio callback
            await pipeline.WriteAudioAsync(chunk, ct);
        pipeline.Complete();
        var segments = await reading;

        foreach (var s in segments)
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{(s.IsFinal ? "FINAL  " : "partial")} {s.Start:mm\\:ss\\.f}-{s.End:mm\\:ss\\.f} {s.Text}");

        var finals = segments.Where(s => s.IsFinal && s.Text.Length > 0).ToList();
        Assert.True(finals.Count >= 2, $"expected at least 2 finals, got {finals.Count}");
        Assert.Contains("yellow lamps", finals[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lovely child", string.Join(' ', finals.Skip(1).Select(f => f.Text)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, pipeline.DroppedChunks);
    }

    private static async Task<List<TranscriptSegment>> ReadAllAsync(TranscriptionPipeline pipeline, CancellationToken ct)
    {
        var list = new List<TranscriptSegment>();
        await foreach (var segment in pipeline.ReadSegmentsAsync(ct))
            list.Add(segment);
        return list;
    }
}
