using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Voxa.Core.Pipeline;
using Voxa.Core.Remote;
using Voxa.Engines.SherpaOnnx;
using Microsoft.Extensions.Options;

namespace Voxa.Api.Ingest;

/// <summary>
/// <see cref="RemoteProtocol.IngestPath"/>: the server fetches the media itself and streams the transcript back.
/// <code>
/// URL ─guard─► yt-dlp (media URL) ─guard─► ffmpeg (16 kHz) ─► TranscriptionPipeline ─► IngestSegment ─► socket
/// </code>
/// Recorded media is processed as fast as the engine allows (finals only), so captions can be ahead of the player;
/// live streams arrive in real time (with partials). Leaving the socket stops the download at once.
/// </summary>
public sealed class IngestService(
    PublicUrlGuard guard, MediaResolver resolver, FfmpegAudioReader reader, IOptions<ServerOptions> options,
    ILogger<IngestService> logger)
{
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrentIngests, options.Value.MaxConcurrentIngests);

    /// <summary>Taken before the WebSocket is accepted, so a busy server answers with a plain 503.</summary>
    public bool TryReserve() => _slots.Wait(0);

    public void Release() => _slots.Release();

    public async Task RunAsync(WebSocket socket, Uri url, double start, Models models, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var clientGone = WatchForCloseAsync(socket, stop); // the client sends nothing; a close (or drop) ends the job
        try
        {
            var media = await ResolveAsync(url, stop.Token);
            await SendAsync(socket, new IngestInfo(media.Title, media.IsLive, media.Duration, start), stop.Token);
            await TranscribeAsync(socket, media, start, models, stop.Token);
            await SendAsync(socket, new IngestEnd(), stop.Token);
            await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, RemoteProtocol.DoneCloseReason);
        }
        catch (IngestException ex)
        {
            logger.LogInformation("Ingest of {Url} failed: {Error}", url, ex.Message);
            await TrySendErrorAsync(socket, ex.Message);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // client left
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingest of {Url} failed", url);
            await TrySendErrorAsync(socket, "Transcription failed on the server.");
        }
        finally
        {
            // After our close frame the client answers with its own; cancelling the receive before that would abort
            // the socket and could drop the last messages (the error text) still on their way.
            await Task.WhenAny(clientGone, Task.Delay(CloseHandshakeTimeout, CancellationToken.None));
            await stop.CancelAsync();
            await clientGone;
        }
    }

    private async Task<ResolvedMedia> ResolveAsync(Uri url, CancellationToken ct)
    {
        if (await guard.CheckAsync(url, ct) is { } pageProblem) throw new IngestException(pageProblem);
        var media = await resolver.ResolveAsync(url, ct);
        if (await guard.CheckAsync(media.Url, ct) is { } mediaProblem) throw new IngestException(mediaProblem);
        return media;
    }

    private async Task TranscribeAsync(WebSocket socket, ResolvedMedia media, double start, Models models, CancellationToken ct)
    {
        await using var pipeline = new TranscriptionPipeline(new SileroVoiceActivityDetector(models.VadModelPath),
            models.Engine, new TranscriptionPipelineOptions { EmitPartials = media.IsLive }, models.LanguageIdentifier);

        var offset = TimeSpan.FromSeconds(media.IsLive ? 0 : start);
        var send = Task.Run(async () =>
        {
            await foreach (var segment in pipeline.ReadSegmentsAsync(ct))
            {
                if (segment.Text.Length == 0) continue; // nothing to show; ingest clients don't track partials to clear
                await SendAsync(socket, new IngestSegment(segment with { Start = segment.Start + offset, End = segment.End + offset }), ct);
            }
        }, ct);

        ExceptionDispatchInfo? readFailure = null;
        try
        {
            await foreach (var chunk in reader.ReadAsync(media, start, options.Value.MaxIngestDuration, ct))
                await pipeline.WriteAudioAsync(chunk, ct); // waits while the engine is behind: ffmpeg is throttled too
        }
        catch (Exception ex)
        {
            readFailure = ExceptionDispatchInfo.Capture(ex);
        }
        pipeline.Complete();

        // One sender at a time on a WebSocket: the error (if any) goes out only after the last segment.
        try { await send; }
        catch (OperationCanceledException) when (readFailure is not null) { }
        readFailure?.Throw();
    }

    private static async Task WatchForCloseAsync(WebSocket socket, CancellationTokenSource stop)
    {
        var buffer = new byte[256];
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        await stop.CancelAsync();
    }

    private static Task SendAsync(WebSocket socket, IngestMessage message, CancellationToken ct) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, RemoteProtocol.Json), WebSocketMessageType.Text, true, ct);

    private static async Task TrySendErrorAsync(WebSocket socket, string message)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            await SendAsync(socket, new IngestError(message), CancellationToken.None);
            await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "error");
        }
        catch (WebSocketException) { }
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await socket.CloseOutputAsync(status, reason, CancellationToken.None);
    }
}
