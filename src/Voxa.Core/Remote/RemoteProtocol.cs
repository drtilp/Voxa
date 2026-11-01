using System.Text.Json;

namespace Voxa.Core.Remote;

/// <summary>
/// Wire contract between the app and the remote inference server (src/Voxa.Api), shared by both sides.
/// <code>
/// GET  /health         200 once the models are loaded, 503 before (startup probe)
/// GET  /v1/info        ServerInfo
/// WS   /v1/transcribe  client → binary PCM16 LE, 16 kHz mono; text "end" = no more audio
///                      server → text JSON TranscriptSegment (partials and finals), closes after the last final;
///                      closes with reason "idle" after minutes without speech (the app should reconnect on speech)
/// POST /v1/translate   TranslateRequest → TranslationResult
/// WS   /v1/ingest?url=&lt;page or media URL&gt;&amp;start=&lt;seconds&gt;
///                      the server fetches the audio itself (yt-dlp + ffmpeg); server → text JSON IngestMessage
///                      (info, then segments with times in the media, then end or error)
/// </code>
/// With an API key configured on the server, /v1/* needs "Authorization: Bearer &lt;key&gt;". Browsers can't set
/// headers on a WebSocket: they offer the subprotocols <see cref="SubProtocol"/> and <see cref="KeySubProtocolPrefix"/>
/// + key instead; the server answers with <see cref="SubProtocol"/> only. Any origin may call the API (CORS): the key
/// protects it, not the origin.
/// </summary>
public static class RemoteProtocol
{
    public const string TranscribePath = "/v1/transcribe";
    public const string TranslatePath = "/v1/translate";
    public const string InfoPath = "/v1/info";
    public const string HealthPath = "/health";
    public const string IngestPath = "/v1/ingest";

    /// <summary>WebSocket subprotocol of this API; the server selects it when the client offers it.</summary>
    public const string SubProtocol = "voxa.v1";

    /// <summary>Browser clients offer "voxa.key.&lt;API key&gt;" as a second subprotocol (never echoed back).</summary>
    public const string KeySubProtocolPrefix = "voxa.key.";

    /// <summary>Text message from the client: no more audio, flush the last sentence and close.</summary>
    public const string EndOfAudio = "end";

    /// <summary>Close reason after the last final, once the client sent <see cref="EndOfAudio"/>.</summary>
    public const string DoneCloseReason = "done";

    /// <summary>Close reason when nobody spoke for the server's idle timeout.</summary>
    public const string IdleCloseReason = "idle";

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>PCM16 halves the upload against float32 (32 KB/s); ASR doesn't hear the difference.</summary>
    public static float[] Pcm16ToFloat(ReadOnlySpan<byte> pcm16)
    {
        var samples = new float[pcm16.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(pcm16[2 * i] | pcm16[2 * i + 1] << 8) / 32768f;
        return samples;
    }

    public static byte[] FloatToPcm16(ReadOnlySpan<float> samples)
    {
        var pcm16 = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp(samples[i] * 32768f, short.MinValue, short.MaxValue);
            pcm16[2 * i] = (byte)value;
            pcm16[2 * i + 1] = (byte)(value >> 8);
        }
        return pcm16;
    }
}

public sealed record TranslateRequest(string Text, string TargetLanguage, string? SourceLanguage = null);

/// <param name="Translation">Null when the server has no translation model.</param>
/// <param name="Backend">Where the models run, e.g. "cpu" or "cuda".</param>
public sealed record ServerInfo(string Asr, string? LanguageId, string? Translation, string Backend);
