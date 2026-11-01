namespace Voxa.Core.Asr;

/// <summary>
/// Port for a speech recognition model (Ports &amp; Adapters). Implementations live in Voxa.Engines.* projects.
/// </summary>
/// <remarks>
/// All current models (Whisper, Parakeet) are offline: they transcribe a finished chunk of audio.
/// Streaming is built on top by the pipeline (VAD cuts speech into segments, each one goes here).
/// </remarks>
public interface IAsrEngine : IDisposable
{
    string Name { get; }

    /// <param name="samples">Mono PCM, floats in [-1, 1].</param>
    /// <param name="sampleRate">Engines resample to what the model needs (16 kHz).</param>
    Task<AsrResult> TranscribeAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken ct = default);
}

/// <param name="Language">Detected language code (e.g. "uk"), null when the model doesn't report it.</param>
public sealed record AsrResult(string Text, string? Language, TimeSpan AudioDuration, TimeSpan ProcessingTime)
{
    /// <summary>Real-Time Factor: processing time / audio length. Below 1 = faster than real time.</summary>
    public double RealTimeFactor => AudioDuration > TimeSpan.Zero ? ProcessingTime / AudioDuration : 0;
}
