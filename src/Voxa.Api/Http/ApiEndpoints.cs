using Voxa.Api.Ingest;
using Voxa.Core.Remote;
using Voxa.Core.Translation;
using Microsoft.Extensions.Options;

namespace Voxa.Api.Http;

/// <summary>The HTTP surface of the server (contract: <see cref="RemoteProtocol"/>).</summary>
public static class ApiEndpoints
{
    public const string CorsPolicy = "AnyOrigin";

    public static WebApplication MapVoxaApi(this WebApplication app)
    {
        app.MapGet(RemoteProtocol.HealthPath, (ModelHost host) => host.Loaded is not null
                ? Results.Ok("ready")
                : Results.Problem(host.LoadError ?? "Loading models…", statusCode: StatusCodes.Status503ServiceUnavailable))
            .RequireCors(CorsPolicy);

        var api = app.MapGroup("/v1").AddEndpointFilter<ApiKeyFilter>().RequireCors(CorsPolicy);
        api.MapGet("/info", (ModelHost host) => host.Loaded!.Info);
        api.MapPost("/translate", TranslateAsync);
        api.MapGet("/transcribe", TranscribeAsync);
        api.MapGet("/ingest", IngestAsync);
        return app;
    }

    private static async Task<IResult> TranslateAsync(TranslateRequest request, ModelHost host, CancellationToken ct)
    {
        if (host.Loaded!.Translator is not { } translator)
            return Results.Problem("This server has no translation model.", statusCode: StatusCodes.Status501NotImplemented);
        try
        {
            return Results.Ok(await translator.TranslateAsync(request.Text, request.TargetLanguage, request.SourceLanguage, ct));
        }
        catch (TranslationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> TranscribeAsync(HttpContext http, ModelHost host, IOptions<ServerOptions> options,
        ILoggerFactory loggers)
    {
        if (!http.WebSockets.IsWebSocketRequest) return Results.BadRequest("WebSocket only.");
        using var socket = await WebSocketHandshake.AcceptAsync(http);
        await TranscriptionSocket.RunAsync(socket, host.Loaded!, options.Value.IdleTimeout,
            loggers.CreateLogger(typeof(TranscriptionSocket)), http.RequestAborted);
        return Results.Empty;
    }

    private static async Task<IResult> IngestAsync(HttpContext http, string? url, double? start, ModelHost host,
        IngestService ingest)
    {
        if (!http.WebSockets.IsWebSocketRequest) return Results.BadRequest("WebSocket only.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target)) return Results.BadRequest("Missing or invalid url.");
        if (!ingest.TryReserve())
            return Results.Problem("The server is busy with other media, try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
        try
        {
            using var socket = await WebSocketHandshake.AcceptAsync(http);
            await ingest.RunAsync(socket, target, Math.Max(0, start ?? 0), host.Loaded!, http.RequestAborted);
        }
        finally
        {
            ingest.Release();
        }
        return Results.Empty;
    }
}
