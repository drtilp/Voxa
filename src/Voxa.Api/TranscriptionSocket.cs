using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Voxa.Core.Pipeline;
using Voxa.Core.Remote;
using Voxa.Engines.SherpaOnnx;

namespace Voxa.Api;

/// <summary>
/// One WebSocket = one <see cref="TranscriptionPipeline"/> (see <see cref="RemoteProtocol"/> for the messages).
/// <code>
/// socket ─receive loop─► WriteAudioAsync ─► pipeline ─► ReadSegmentsAsync ─send loop─► socket
/// </code>
/// The receive loop waits when the pipeline queue is full, so a client sending faster than the engine
/// decodes is slowed down by TCP instead of losing audio.
/// With no speech for <paramref name="idleTimeout"/> the server stops listening and closes with "idle".
/// </summary>
public static class TranscriptionSocket
{
    public static async Task RunAsync(WebSocket socket, Models models, TimeSpan idleTimeout, ILogger logger,
        CancellationToken ct)
    {
        await using var pipeline = new TranscriptionPipeline(new SileroVoiceActivityDetector(models.VadModelPath),
            models.Engine, languageIdentifier: models.LanguageIdentifier);

        var lastSpeech = new LastSpeech();
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watchdog = WatchIdleAsync(lastSpeech, idleTimeout, abort);

        var send = SendSegmentsAsync(socket, pipeline, lastSpeech, ct);
        bool timedOut = false;
        try
        {
            timedOut = await ReceiveAudioAsync(socket, pipeline, () => lastSpeech.Elapsed >= idleTimeout, abort.Token);
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // The client sent nothing at all for a minute past the timeout: the receive was cancelled, which aborts
            // the socket (no clean close possible).
        }
        finally
        {
            pipeline.Complete(); // also when the client vanished: lets the send loop finish
            await abort.CancelAsync(); // stops the watchdog
        }
        await watchdog;
        if (timedOut) logger.LogInformation("Closed a connection without speech for {Timeout}", idleTimeout);

        try
        {
            await send;
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, timedOut ? RemoteProtocol.IdleCloseReason : RemoteProtocol.DoneCloseReason, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Transcription failed");
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.InternalServerError, Truncate(ex.Message), CancellationToken.None);
        }
    }

    /// <returns>True when it stopped because of <paramref name="isIdle"/> (checked after every message).</returns>
    private static async Task<bool> ReceiveAudioAsync(WebSocket socket, TranscriptionPipeline pipeline, Func<bool> isIdle,
        CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var message = new ArrayBufferWriter<byte>();
        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, ct);
            }
            catch (WebSocketException)
            {
                return false; // connection dropped without a close handshake
            }

            if (result.MessageType == WebSocketMessageType.Close) return false;
            message.Write(buffer.AsSpan(0, result.Count));
            if (!result.EndOfMessage) continue;

            if (result.MessageType == WebSocketMessageType.Text)
            {
                if (Encoding.UTF8.GetString(message.WrittenSpan).Trim() == RemoteProtocol.EndOfAudio) return false;
            }
            else if (message.WrittenCount > 0)
            {
                await pipeline.WriteAudioAsync(RemoteProtocol.Pcm16ToFloat(message.WrittenSpan), ct);
            }
            message.ResetWrittenCount();
            if (isIdle()) return true;
        }
        return false;
    }

    // Hard stop for a client that sends nothing (not even silence), so the receive loop never gets to check isIdle.
    private static async Task WatchIdleAsync(LastSpeech lastSpeech, TimeSpan timeout, CancellationTokenSource abort)
    {
        var hardTimeout = timeout + TimeSpan.FromMinutes(1);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(abort.Token))
            {
                if (lastSpeech.Elapsed < hardTimeout) continue;
                await abort.CancelAsync();
                return;
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task SendSegmentsAsync(WebSocket socket, TranscriptionPipeline pipeline, LastSpeech lastSpeech,
        CancellationToken ct)
    {
        await foreach (var segment in pipeline.ReadSegmentsAsync(ct))
        {
            if (segment.Text.Length > 0) lastSpeech.Touch();
            if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) continue; // drain
            var json = JsonSerializer.SerializeToUtf8Bytes(segment, RemoteProtocol.Json);
            await socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
    }

    private sealed class LastSpeech
    {
        private long _timestamp = Stopwatch.GetTimestamp();

        public TimeSpan Elapsed => Stopwatch.GetElapsedTime(Interlocked.Read(ref _timestamp));

        public void Touch() => Interlocked.Exchange(ref _timestamp, Stopwatch.GetTimestamp());
    }

    // A close reason must fit into 123 bytes.
    private static string Truncate(string text) => text.Length <= 100 ? text : text[..100];
}
