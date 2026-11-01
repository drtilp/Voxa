using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Voxa.Api.Http;

/// <summary>
/// /v1/* needs the server's API key: "Authorization: Bearer &lt;key&gt;" (apps, fetch) or, for browser WebSockets that
/// can't set headers, the subprotocol "voxa.key.&lt;key&gt;". Also answers 503 while the models load.
/// </summary>
public sealed class ApiKeyFilter(IOptions<ServerOptions> options, ModelHost models) : IEndpointFilter
{
    private const string BearerPrefix = "Bearer ";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (options.Value.ApiKey is { Length: > 0 } key && !Matches(key, PresentedKey(request)))
            return Results.Unauthorized();
        if (models.Loaded is null)
            return Results.Problem("Models are still loading.", statusCode: StatusCodes.Status503ServiceUnavailable);
        return await next(context);
    }

    private static string? PresentedKey(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith(BearerPrefix, StringComparison.Ordinal)
            ? authorization[BearerPrefix.Length..]
            : WebSocketHandshake.KeyFromProtocols(request);
    }

    // Constant time: the comparison must not tell how many leading characters were right.
    private static bool Matches(string expected, string? presented) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
}
