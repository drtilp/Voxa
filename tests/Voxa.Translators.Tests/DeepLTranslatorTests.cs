using System.Net;
using System.Text;
using System.Text.Json;
using Voxa.Core.Translation;
using Voxa.Translators.DeepL;

namespace Voxa.Translators.Tests;

public class DeepLTranslatorTests
{
    [Fact]
    public async Task Free_key_goes_to_free_endpoint_with_auth_header_and_parses_result()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            """{"translations":[{"detected_source_language":"UK","text":"Good afternoon, how are you?"}]}""");
        var translator = new DeepLTranslator(new HttpClient(handler), new DeepLTranslatorOptions("abc:fx"));

        var result = await translator.TranslateAsync("Добрий день, як справи?", "en", ct: TestContext.Current.CancellationToken);

        Assert.Equal(new TranslationResult("Good afternoon, how are you?", "uk"), result);
        var request = handler.Requests.Single();
        Assert.Equal("https://api-free.deepl.com/v2/translate", request.Url);
        Assert.Equal("DeepL-Auth-Key abc:fx", request.Headers["Authorization"]);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("Добрий день, як справи?", body.RootElement.GetProperty("text")[0].GetString());
        Assert.Equal("EN-US", body.RootElement.GetProperty("target_lang").GetString());
        Assert.False(body.RootElement.TryGetProperty("source_lang", out _));
    }

    [Fact]
    public void Pro_key_uses_pro_endpoint() =>
        Assert.Equal(DeepLTranslator.ProEndpoint, new DeepLTranslatorOptions("pro-key").ResolvedEndpoint);

    [Theory]
    [InlineData("en", "EN-US")]
    [InlineData("cs", "CS")]
    [InlineData("uk", "UK")]
    [InlineData("pt", "PT-PT")]
    public void Maps_target_codes(string iso, string deepl) => Assert.Equal(deepl, DeepLTranslator.ToTargetCode(iso));

    [Fact]
    public async Task Quota_exceeded_gives_readable_error()
    {
        var handler = new FakeHandler((HttpStatusCode)456, """{"message":"Quota exceeded"}""");
        var translator = new DeepLTranslator(new HttpClient(handler), new DeepLTranslatorOptions("k:fx"));

        var ex = await Assert.ThrowsAsync<TranslationException>(
            () => translator.TranslateAsync("text", "en", ct: TestContext.Current.CancellationToken));

        Assert.Contains("quota", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Real call; set DEEPL_API_KEY to run.</summary>
    [Fact(Explicit = true)]
    public async Task Real_deepl_translates_ukrainian_to_english()
    {
        var key = Environment.GetEnvironmentVariable("DEEPL_API_KEY");
        Assert.SkipWhen(string.IsNullOrEmpty(key), "DEEPL_API_KEY not set");
        var translator = new DeepLTranslator(new HttpClient(), new DeepLTranslatorOptions(key!));

        var result = await translator.TranslateAsync("Добрий день, як справи?", "en", ct: TestContext.Current.CancellationToken);

        TestContext.Current.TestOutputHelper?.WriteLine($"{result.DetectedLanguage}: {result.Text}");
        Assert.Equal("uk", result.DetectedLanguage);
    }

    private sealed class FakeHandler(HttpStatusCode status, string responseJson) : HttpMessageHandler
    {
        public List<(string Url, Dictionary<string, string> Headers, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
            Requests.Add((request.RequestUri!.ToString(), headers, await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") };
        }
    }
}
