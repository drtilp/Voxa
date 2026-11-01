namespace Voxa.Api;

/// <summary>Section "Voxa" of the configuration; in a container set e.g. Voxa__ApiKey.</summary>
public sealed class ServerOptions
{
    public const string Section = "Voxa";

    /// <summary>
    /// asr/&lt;model folder&gt; (sherpa-onnx, Parakeet preferred), vad/*vad*.onnx, optional lid/&lt;whisper folder&gt;
    /// and translation/*.gguf - the layout the Dockerfile downloads.
    /// </summary>
    public string ModelsFolder { get; set; } = "/models";

    /// <summary>Name of the folder under asr/ to use; empty = Parakeet if present, else the first one.</summary>
    public string? AsrModel { get; set; }

    /// <summary>sherpa-onnx provider: "cpu" or "cuda" (needs the GPU build of the native libraries).</summary>
    public string Provider { get; set; } = "cpu";

    public int NumThreads { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// A connection where nobody spoke for this long is closed ("idle"). An app left running would otherwise keep
    /// the replica (and its bill) alive forever, because the client sends silence and the server pings.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Media downloads at the same time (/v1/ingest); each takes ffmpeg plus the engine's time.</summary>
    public int MaxConcurrentIngests { get; set; } = 2;

    /// <summary>Longest stretch of one media that /v1/ingest transcribes (a forgotten live stream stops here).</summary>
    public TimeSpan MaxIngestDuration { get; set; } = TimeSpan.FromHours(3);

    public string YtDlpPath { get; set; } = "yt-dlp";

    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Required on /v1/* (Authorization: Bearer, or the browser WebSocket subprotocol); empty = open (local testing only).</summary>
    public string? ApiKey { get; set; }
}
