using Voxa.Core.Asr;

namespace Voxa.Core.Pipeline;

/// <summary>
/// Streaming transcription, local (<see cref="TranscriptionPipeline"/>) or on a server (Voxa.Remote): 16 kHz mono
/// audio in, partial and final segments out. Disposing stops it without waiting for the last sentence.
/// </summary>
public interface ITranscriber : IAsyncDisposable
{
    /// <summary>Non-blocking, safe to call from an audio callback. Samples are copied.</summary>
    bool TryWriteAudio(ReadOnlySpan<float> samples16kMono);

    /// <summary>No more audio: the last sentence is finished, then the output completes.</summary>
    void Complete();

    IAsyncEnumerable<TranscriptSegment> ReadSegmentsAsync(CancellationToken ct = default);

    /// <summary>Audio lost because processing (or the connection) couldn't keep up.</summary>
    long DroppedChunks { get; }
}
