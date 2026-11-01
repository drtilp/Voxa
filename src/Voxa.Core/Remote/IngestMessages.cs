using System.Text.Json.Serialization;
using Voxa.Core.Asr;

namespace Voxa.Core.Remote;

/// <summary>Server → client messages of <see cref="RemoteProtocol.IngestPath"/>, told apart by "type".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(IngestInfo), "info")]
[JsonDerivedType(typeof(IngestSegment), "segment")]
[JsonDerivedType(typeof(IngestEnd), "end")]
[JsonDerivedType(typeof(IngestError), "error")]
public abstract record IngestMessage;

/// <summary>First message: what is being transcribed.</summary>
/// <param name="IsLive">Live stream: segments come in real time, show them as they arrive.</param>
/// <param name="Duration">Seconds; null for live streams or when unknown.</param>
/// <param name="Start">Where in the media the transcription starts (seconds).</param>
public sealed record IngestInfo(string? Title, bool IsLive, double? Duration, double Start) : IngestMessage;

/// <summary>A transcript segment; <see cref="TranscriptSegment.Start"/> / End are positions in the media.</summary>
public sealed record IngestSegment(TranscriptSegment Segment) : IngestMessage;

/// <summary>The whole media (or the allowed maximum) is transcribed.</summary>
public sealed record IngestEnd : IngestMessage;

public sealed record IngestError(string Message) : IngestMessage;
