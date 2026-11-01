using Voxa.Core.Translation;

namespace Voxa.Remote.Tests;

public class RemoteTranslatorTests
{
    [Fact(Timeout = 15_000)] // never hang the run on a lost message
    public async Task Translates_through_the_server()
    {
        await using var server = await FakeServer.StartAsync();
        var translator = new RemoteTranslator(server.Client());

        var result = await translator.TranslateAsync("ahoj", "en", "cs", TestContext.Current.CancellationToken);

        Assert.Equal(new TranslationResult("en:ahoj", "cs"), result);
    }

    [Fact(Timeout = 15_000)] // never hang the run on a lost message
    public async Task Wrong_key_is_a_translation_error()
    {
        await using var server = await FakeServer.StartAsync();
        var translator = new RemoteTranslator(server.Client(key: "wrong"));

        var error = await Assert.ThrowsAsync<TranslationException>(
            () => translator.TranslateAsync("ahoj", "en", ct: TestContext.Current.CancellationToken));
        Assert.Contains("API key", error.Message);
    }

    [Theory]
    [InlineData(null, "key", "server address")]
    [InlineData("ftp://x", "key", "not an http(s) address")]
    [InlineData("https://x.example", " ", "API key")]
    public void Options_explain_what_is_missing(string? url, string key, string problem) =>
        Assert.Contains(problem, RemoteServerOptions.TryCreate(url, key).Problem);
}
