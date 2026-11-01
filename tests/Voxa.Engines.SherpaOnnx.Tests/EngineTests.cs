using System.Text.RegularExpressions;
using Voxa.Core.Asr;
using Voxa.Core.Audio;

namespace Voxa.Engines.SherpaOnnx.Tests;

/// <summary>Same checks for every engine; subclasses only pick the model (fixture).</summary>
public abstract partial class EngineTestsBase(EngineFixture fixture)
{
    // Reference sample shipped with the sherpa-onnx Whisper models (LibriSpeech, 16 kHz).
    private const string EnglishReference =
        "after early nightfall the yellow lamps would light up here and there the squalid quarter of the brothels";

    [Fact]
    public async Task Transcribes_english_reference_sample()
    {
        var engine = fixture.RequireEngine();
        var audio = WavFile.Read(ModelPaths.WhisperTurbo(Path.Combine("test_wavs", "0.wav")));

        var result = await engine.TranscribeAsync(audio.Samples, audio.SampleRate, TestContext.Current.CancellationToken);

        Log(engine, "0.wav", result);
        Assert.Equal(EnglishReference, Normalize(result.Text));
    }

    [Fact]
    public async Task Silence_gives_empty_or_short_text()
    {
        var engine = fixture.RequireEngine();

        var result = await engine.TranscribeAsync(new float[16_000 * 2], 16_000, TestContext.Current.CancellationToken);

        Log(engine, "2 s silence", result);
        // Whisper is known to hallucinate on silence ("Thank you."); this documents how much.
        Assert.True(result.Text.Length < 40, $"Hallucinated: '{result.Text}'");
    }

    [Fact]
    public async Task Cancelled_token_throws_before_decoding()
    {
        var engine = fixture.RequireEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.TranscribeAsync(new float[16_000], 16_000, cts.Token));
    }

    /// <summary>Manual: transcribes the recordings made with the WPF "Record" button (Music\Voxa).</summary>
    [Fact(Explicit = true)]
    public async Task Transcribes_my_recordings()
    {
        var engine = fixture.RequireEngine();
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Voxa");
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.wav") : [];
        Assert.SkipWhen(files.Length == 0, $"No recordings in {folder}");

        foreach (var file in files.Order())
        {
            var audio = WavFile.Read(file);
            var result = await engine.TranscribeAsync(audio.Samples, audio.SampleRate, TestContext.Current.CancellationToken);
            Log(engine, Path.GetFileName(file), result);
        }
    }

    private static void Log(IAsrEngine engine, string input, AsrResult result) =>
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"[{engine.Name}] {input}: {result.AudioDuration.TotalSeconds:F1} s audio in " +
            $"{result.ProcessingTime.TotalSeconds:F2} s (RTF {result.RealTimeFactor:F2})\n  {result.Text}");

    // Lowercase, letters/digits/spaces only: punctuation and casing differ between models.
    private static string Normalize(string text) =>
        Whitespace().Replace(NonWord().Replace(text.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}' ]")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

public sealed class WhisperTurboInt8Tests(WhisperTurboInt8Fixture fixture)
    : EngineTestsBase(fixture), IClassFixture<WhisperTurboInt8Fixture>;

public sealed class ParakeetUltraInt8Tests(ParakeetUltraInt8Fixture fixture)
    : EngineTestsBase(fixture), IClassFixture<ParakeetUltraInt8Fixture>;
