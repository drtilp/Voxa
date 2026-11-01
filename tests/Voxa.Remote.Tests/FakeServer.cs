using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Voxa.Core.Asr;
using Voxa.Core.Remote;
using Voxa.Core.Translation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Voxa.Remote.Tests;

public sealed record FakeServerOptions(
    int StartingHealthChecks = 0,
    int IdleAfterSilentMessages = 0,
    bool DropFirstConnection = false);

/// <summary>
/// Speaks <see cref="RemoteProtocol"/> like src/Voxa.Api, with scripted behaviour instead of models: every binary
/// message with sound becomes a final "s&lt;n&gt;" (ids from 0 per connection, like the real server).
/// </summary>
/// <remarks>
/// <see cref="FakeServerOptions.StartingHealthChecks"/>: /health answers 503 that many times (a waking server).
/// <see cref="FakeServerOptions.IdleAfterSilentMessages"/>: close with "idle" after that many silent messages.
/// <see cref="FakeServerOptions.DropFirstConnection"/>: abort the first connection after its first final.
/// </remarks>
public sealed class FakeServer : IAsyncDisposable
{
    public const string Key = "secret";

    private readonly WebApplication _app;
    private readonly FakeServerOptions _options;
    private int _healthCalls;
    private int _connections;

    private FakeServer(WebApplication app, FakeServerOptions options)
    {
        _app = app;
        _options = options;
    }

    public int Connections => Volatile.Read(ref _connections);

    public Uri Url => new(_app.Urls.First());

    public static async Task<FakeServer> StartAsync(FakeServerOptions? options = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var server = new FakeServer(builder.Build(), options ?? new FakeServerOptions());
        server.Map();
        await server._app.StartAsync();
        return server;
    }

    public RemoteServer Client(string key = Key) =>
        new(new HttpClient(), new RemoteServerOptions(Url, key)) { PollInterval = TimeSpan.FromMilliseconds(20) };

    private void Map()
    {
        _app.UseWebSockets();
        _app.MapGet(RemoteProtocol.HealthPath, () =>
            Interlocked.Increment(ref _healthCalls) > _options.StartingHealthChecks ? Results.Ok("ready") : Results.StatusCode(503));

        _app.MapPost(RemoteProtocol.TranslatePath, (HttpContext http, TranslateRequest request) =>
            Authorized(http)
                ? Results.Json(new TranslationResult($"{request.TargetLanguage}:{request.Text}", request.SourceLanguage), RemoteProtocol.Json)
                : Results.Unauthorized());

        _app.MapGet(RemoteProtocol.TranscribePath, async (HttpContext http) =>
        {
            if (!Authorized(http)) return Results.Unauthorized();
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            await TranscribeAsync(socket, Interlocked.Increment(ref _connections));
            return Results.Empty;
        });
    }

    private static bool Authorized(HttpContext http) => http.Request.Headers.Authorization == $"Bearer {Key}";

    private async Task TranscribeAsync(WebSocket socket, int connection)
    {
        var buffer = new byte[64 * 1024];
        long id = 0;
        int silent = 0;
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    if (Encoding.UTF8.GetString(buffer, 0, result.Count) != RemoteProtocol.EndOfAudio) continue;
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, RemoteProtocol.DoneCloseReason, CancellationToken.None);
                    return;
                }

                var samples = RemoteProtocol.Pcm16ToFloat(buffer.AsSpan(0, result.Count));
                if (samples.Any(s => s != 0))
                {
                    silent = 0;
                    var segment = new TranscriptSegment($"s{id}", IsFinal: true, Id: id++);
                    await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(segment, RemoteProtocol.Json),
                        WebSocketMessageType.Text, true, CancellationToken.None);
                    if (_options.DropFirstConnection && connection == 1)
                    {
                        await Task.Delay(100); // let the final reach the client before the line goes dead
                        socket.Abort();
                        return;
                    }
                }
                else if (_options.IdleAfterSilentMessages > 0 && ++silent >= _options.IdleAfterSilentMessages)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, RemoteProtocol.IdleCloseReason, CancellationToken.None);
                    return;
                }
            }
        }
        catch (WebSocketException) { } // client went away
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
