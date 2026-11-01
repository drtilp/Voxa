namespace Voxa.Core.Vad;

/// <summary>
/// Port for Voice Activity Detection: cuts a continuous 16 kHz stream into speech segments.
/// Stateful and not thread-safe: one instance per audio stream, used from one thread.
/// </summary>
public interface IVoiceActivityDetector : IDisposable
{
    /// <summary>True while the detector is inside speech (a segment is open).</summary>
    bool IsSpeechActive { get; }

    /// <summary>Feeds 16 kHz mono samples; returns segments that finished (speech followed by enough silence).</summary>
    IReadOnlyList<SpeechSegment> Accept(ReadOnlySpan<float> samples);

    /// <summary>End of stream: closes an open segment and returns what is left.</summary>
    IReadOnlyList<SpeechSegment> Flush();
}

/// <param name="StartSample">Index of the first sample in the stream (16 kHz).</param>
public sealed record SpeechSegment(long StartSample, float[] Samples);
