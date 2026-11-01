using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Voxa.Core.Asr;
using Voxa.Core.Pipeline;
using Voxa.Core.Remote;

namespace Voxa.Remote;

public enum RemoteTranscriberState
{
    /// <summary>Checking the server and opening the WebSocket.</summary>
    Connecting,

    /// <summary>The server was asleep (scaled to zero) and is starting; takes 1-2 minutes.</summary>
    WakingUp,

    /// <summary>Connected, audio is streaming.</summary>
    Listening,

    /// <summary>The server closed the connection after minutes without speech; reconnects when sound comes.</summary>
    Paused,

    /// <summary>The connection dropped; trying again with a growing delay.</summary>
    Reconnecting,
}

public sealed record RemoteTranscriberOptions
{
    /// <summary>Audio that waited for the connection (waking server, reconnect) and is still sent; older is dropped.</summary>
    public TimeSpan MaxBacklog { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Audio kept while <see cref="RemoteTranscriberState.Paused"/>, sent before the sound that resumed it.</summary>
    public TimeSpan PreRoll { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>RMS of a chunk that resumes a paused connection (0.005 ≈ -46 dBFS: quiet speech, not digital silence).</summary>
    public float ResumeLevel { get; init; } = 0.005f;

    public TimeSpan ReadyTimeout { get; init; } = RemoteServer.DefaultReadyTimeout;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Failed attempts in a row before giving up (delays 1, 2, 4 … 30 s: about 2 minutes).</summary>
    public int MaxFailures { get; init; } = 8;

    /// <summary>Queued chunks while nothing reads them (waking server); the oldest are dropped beyond this.</summary>
    public int InputCapacity { get; init; } = 30_000;
}

/// <summary>
/// Remote counterpart of <see cref="TranscriptionPipeline"/>: audio in, transcript segments out, with
/// VAD, recognition and language identification on the server (see <see cref="RemoteProtocol"/>).
/// <code>
/// audio callback ─TryWriteAudio─► [input channel] ─► worker: WebSocket ⇄ server ─► [output channel] ─► ReadSegmentsAsync
/// </code>
/// The worker owns the connection: it wakes the server, reconnects after drops, pauses after the server's "idle"
/// close (so a forgotten app doesn't keep a paid replica running) and resumes when sound comes back. Sentence ids
/// restart on the server with every connection; they are shifted here so they stay unique for the caller.
/// </summary>
public sealed class RemoteTranscriber : ITranscriber
{
    private const int SampleRate = 16_000;
    private static readonly TimeSpan StableConnection = TimeSpan.FromMinutes(1);

    private enum ConnectionEnd { Finished, Idle, Dropped }

    private readonly RemoteServer _server;
    private readonly RemoteTranscriberOptions _options;
    private readonly Channel<float[]> _input;
    private readonly Channel<TranscriptSegment> _output = Channel.CreateUnbounded<TranscriptSegment>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    // Worker-only state
    private readonly List<float[]> _backlog = [];
    private long _nextId;

    private long _droppedChunks;
    private int _state = -1;

    public RemoteTranscriber(RemoteServer server, RemoteTranscriberOptions? options = null)
    {
        _server = server;
        _options = options ?? new RemoteTranscriberOptions();
        _input = Channel.CreateBounded<float[]>(
            new BoundedChannelOptions(_options.InputCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest },
            _ => Interlocked.Increment(ref _droppedChunks));
        _worker = Task.Run(RunAsync);
    }

    /// <summary>Raised on the worker thread when <see cref="State"/> changes.</summary>
    public event Action<RemoteTranscriberState>? StateChanged;

    public RemoteTranscriberState State => (RemoteTranscriberState)Math.Max(0, Volatile.Read(ref _state));

    /// <summary>Oldest audio dropped because the connection took too long (queue full).</summary>
    public long DroppedChunks => Interlocked.Read(ref _droppedChunks);

    /// <summary>Completes when the server finished the last sentence; faults when the server can't be used.</summary>
    public Task Completion => _worker;

    /// <summary>Non-blocking, safe to call from an audio callback. 16 kHz mono; samples are copied.</summary>
    public bool TryWriteAudio(ReadOnlySpan<float> samples16kMono) => _input.Writer.TryWrite(samples16kMono.ToArray());

    /// <summary>No more audio: the server finishes the last sentence, then the output completes.</summary>
    public void Complete() => _input.Writer.TryComplete();

    public IAsyncEnumerable<TranscriptSegment> ReadSegmentsAsync(CancellationToken ct = default) =>
        _output.Reader.ReadAllAsync(ct);

    private async Task RunAsync()
    {
        var ct = _cts.Token;
        try
        {
            int failures = 0;
            bool paused = false;
            while (true)
            {
                if (paused)
                {
                    SetState(RemoteTranscriberState.Paused);
                    if (!await WaitForSoundAsync(ct)) break; // stopped while paused
                    paused = false;
                }
                else if (_input.Reader.Completion.IsCompleted)
                {
                    break; // stopped while (re)connecting, nothing left to send
                }

                SetState(failures == 0 ? RemoteTranscriberState.Connecting : RemoteTranscriberState.Reconnecting);
                var started = Stopwatch.StartNew();
                ConnectionEnd end;
                try
                {
                    await _server.WaitUntilReadyAsync(() => SetState(RemoteTranscriberState.WakingUp), _options.ReadyTimeout, ct);
                    end = await RunConnectionAsync(ct);
                }
                catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
                {
                    if (++failures >= _options.MaxFailures) throw;
                    await Task.Delay(Backoff(failures), ct);
                    continue;
                }

                if (end == ConnectionEnd.Finished) break;
                if (end == ConnectionEnd.Idle)
                {
                    failures = 0;
                    paused = true;
                    continue;
                }

                // Dropped. A connection that worked for a while starts the count again; one failing right away
                // (server crashing on our audio) must not retry forever.
                if (started.Elapsed > StableConnection) failures = 0;
                if (++failures >= _options.MaxFailures)
                    throw new RemoteServerException("The connection to the cloud server keeps dropping.");
                SetState(RemoteTranscriberState.Reconnecting);
                await Task.Delay(Backoff(failures), ct);
            }

            _output.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _output.Writer.TryComplete(ex); // the consumer's await foreach rethrows it
            throw;
        }
    }

    private async Task<ConnectionEnd> RunConnectionAsync(CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {_server.Options.ApiKey}");
        socket.Options.CollectHttpResponseDetails = true;

        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectTimeout.CancelAfter(_options.ConnectTimeout);
            try
            {
                await socket.ConnectAsync(WebSocketUrl(), connectTimeout.Token);
            }
            catch (WebSocketException ex) when (socket.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.ServiceUnavailable)
            {
                throw new RemoteServerException(RemoteServer.DescribeStatus(socket.HttpStatusCode), socket.HttpStatusCode, ex);
            }
        }
        SetState(RemoteTranscriberState.Listening);

        long idOffset = _nextId;
        using var stopSending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var send = SendAudioAsync(socket, stopSending.Token);
        var receive = ReceiveSegmentsAsync(socket, idOffset, ct);

        if (await Task.WhenAny(send, receive) == send && send.IsCompletedSuccessfully)
            return await receive; // all audio sent with "end": the server finishes and closes with "done"

        await stopSending.CancelAsync();
        try { await send; }
        catch (Exception) { /* cancelled, or failed because the connection is gone */ }

        var end = await receive;
        if (socket.State == WebSocketState.CloseReceived)
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        return end;
    }

    private async Task SendAudioAsync(ClientWebSocket socket, CancellationToken ct)
    {
        // Audio that waited for the connection first (only the newest part), then live audio.
        var waiting = new List<float[]>(_backlog);
        _backlog.Clear();
        while (_input.Reader.TryRead(out var chunk)) waiting.Add(chunk);
        foreach (var chunk in Newest(waiting, _options.MaxBacklog))
            await socket.SendAsync(RemoteProtocol.FloatToPcm16(chunk), WebSocketMessageType.Binary, true, ct);

        await foreach (var chunk in _input.Reader.ReadAllAsync(ct))
            await socket.SendAsync(RemoteProtocol.FloatToPcm16(chunk), WebSocketMessageType.Binary, true, ct);

        await socket.SendAsync(Encoding.UTF8.GetBytes(RemoteProtocol.EndOfAudio), WebSocketMessageType.Text, true, ct);
    }

    private async Task<ConnectionEnd> ReceiveSegmentsAsync(ClientWebSocket socket, long idOffset, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var message = new ArrayBufferWriter<byte>();
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return socket.CloseStatusDescription switch
                    {
                        RemoteProtocol.IdleCloseReason => ConnectionEnd.Idle,
                        RemoteProtocol.DoneCloseReason when _input.Reader.Completion.IsCompleted => ConnectionEnd.Finished,
                        _ => ConnectionEnd.Dropped,
                    };
                }

                message.Write(buffer.AsSpan(0, result.Count));
                if (!result.EndOfMessage) continue;

                if (result.MessageType == WebSocketMessageType.Text
                    && JsonSerializer.Deserialize<TranscriptSegment>(message.WrittenSpan, RemoteProtocol.Json) is { } segment)
                {
                    segment = segment with { Id = idOffset + segment.Id };
                    _nextId = Math.Max(_nextId, segment.Id + 1);
                    await _output.Writer.WriteAsync(segment, ct);
                }
                message.ResetWrittenCount();
            }
        }
        catch (WebSocketException)
        {
            return ConnectionEnd.Dropped; // network gone, no close handshake
        }
    }

    /// <returns>False when the input completed (stopped) before any sound.</returns>
    private async Task<bool> WaitForSoundAsync(CancellationToken ct)
    {
        _backlog.Clear();
        long kept = 0;
        long preRoll = (long)(_options.PreRoll.TotalSeconds * SampleRate);
        await foreach (var chunk in _input.Reader.ReadAllAsync(ct))
        {
            _backlog.Add(chunk);
            kept += chunk.Length;
            while (_backlog.Count > 1 && kept - _backlog[0].Length >= preRoll)
            {
                kept -= _backlog[0].Length;
                _backlog.RemoveAt(0);
            }
            if (Rms(chunk) >= _options.ResumeLevel) return true;
        }
        return false;
    }

    private IEnumerable<float[]> Newest(List<float[]> chunks, TimeSpan limit)
    {
        long budget = (long)(limit.TotalSeconds * SampleRate);
        int first = chunks.Count;
        while (first > 0 && budget - chunks[first - 1].Length >= 0)
            budget -= chunks[--first].Length;
        if (first > 0) Interlocked.Add(ref _droppedChunks, first);
        return chunks.Skip(first);
    }

    internal static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    private Uri WebSocketUrl()
    {
        var url = new UriBuilder(_server.Url(RemoteProtocol.TranscribePath));
        url.Scheme = url.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        url.Port = url.Uri.IsDefaultPort ? -1 : url.Port;
        return url.Uri;
    }

    private static TimeSpan Backoff(int failures) =>
        TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Max(0, failures - 1))));

    // Worth another try: network trouble, timeouts, a server that is (re)starting. Not: a wrong key, a server
    // that never came up (WaitUntilReadyAsync already waited minutes).
    private static bool IsTransient(Exception ex) => ex switch
    {
        RemoteServerException remote => remote.Status == HttpStatusCode.ServiceUnavailable,
        WebSocketException or HttpRequestException or IOException => true,
        OperationCanceledException => true, // connect timeout (the caller's own cancellation is excluded)
        _ => false,
    };

    private void SetState(RemoteTranscriberState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) != (int)state)
            StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        Complete();
        await _cts.CancelAsync();
        try { await _worker; }
        catch { /* faults were already reported through the output channel */ }
        _cts.Dispose();
    }
}
