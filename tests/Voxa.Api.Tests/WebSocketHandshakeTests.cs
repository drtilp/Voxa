using Voxa.Api.Http;
using Microsoft.AspNetCore.Http;

namespace Voxa.Api.Tests;

public class WebSocketHandshakeTests
{
    [Fact]
    public void Finds_the_key_among_offered_subprotocols()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.SecWebSocketProtocol = "voxa.v1, voxa.key.abc-DEF_123";

        Assert.Equal("abc-DEF_123", WebSocketHandshake.KeyFromProtocols(request));
        Assert.Equal(["voxa.v1", "voxa.key.abc-DEF_123"], WebSocketHandshake.OfferedProtocols(request));
    }

    [Fact]
    public void No_key_without_the_protocol() =>
        Assert.Null(WebSocketHandshake.KeyFromProtocols(new DefaultHttpContext().Request));
}
