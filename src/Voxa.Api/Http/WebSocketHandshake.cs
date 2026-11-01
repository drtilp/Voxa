using System.Net.WebSockets;
using Voxa.Core.Remote;

namespace Voxa.Api.Http;

/// <summary>Subprotocols of a browser WebSocket: the API marker and the API key (see <see cref="RemoteProtocol"/>).</summary>
public static class WebSocketHandshake
{
    /// <summary>The subprotocols the client offered ("Sec-WebSocket-Protocol", comma-separated, maybe repeated).</summary>
    public static IEnumerable<string> OfferedProtocols(HttpRequest request) =>
        request.Headers.SecWebSocketProtocol
            .SelectMany(value => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>The key a browser sent as "voxa.key.&lt;key&gt;", null when there is none.</summary>
    public static string? KeyFromProtocols(HttpRequest request) =>
        OfferedProtocols(request)
            .FirstOrDefault(p => p.StartsWith(RemoteProtocol.KeySubProtocolPrefix, StringComparison.Ordinal))
            ?[RemoteProtocol.KeySubProtocolPrefix.Length..];

    /// <summary>Accepts with <see cref="RemoteProtocol.SubProtocol"/> when offered (browsers expect one of theirs back),
    /// never with the key protocol: the key must not come back in a response header.</summary>
    public static Task<WebSocket> AcceptAsync(HttpContext http) =>
        http.WebSockets.AcceptWebSocketAsync(OfferedProtocols(http.Request).Contains(RemoteProtocol.SubProtocol)
            ? RemoteProtocol.SubProtocol
            : null);
}
