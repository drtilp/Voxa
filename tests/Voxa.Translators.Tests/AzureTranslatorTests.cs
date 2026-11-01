using System.Net;
using System.Text;
using Voxa.Core.Translation;
using Voxa.Translators.Azure;

namespace Voxa.Translators.Tests;

public class AzureTranslatorTests
{
    [Fact]
    public async Task Sends_text_key_region_and_target_and_parses_the_result()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            """[{"detectedLanguage":{"language":"uk","score":1.0},"translations":[{"text":"Hello, how are you?","to":"en"}]}]""");
        var translator = new AzureTranslator(new HttpClient(handler), new AzureTranslatorOptions("secret", "westeurope"));

        var result = await translator.TranslateAsync("Привіт, як справи?", "en", ct: TestContext.Current.CancellationToken);

        Assert.Equal(new TranslationResult("Hello, how are you?", "uk"), result);
        var request = handler.Requests.Single();
        Assert.Equal("https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=en", request.Url);
        Assert.Equal("secret", request.Headers["Ocp-Apim-Subscription-Key"]);
        Assert.Equal("westeurope", request.Headers["Ocp-Apim-Subscription-Region"]);
        // System.Text.Json escapes non-ASCII (П...) - valid JSON, so compare the parsed value.
        using var body = System.Text.Json.JsonDocument.Parse(request.Body);
        Assert.Equal("Привіт, як справи?", body.RootElement[0].GetProperty("Text").GetString());
    }

    [Fact]
    public async Task Source_language_hint_is_sent_as_from()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """[{"translations":[{"text":"Dobrý den","to":"cs"}]}]""");
        var translator = new AzureTranslator(new HttpClient(handler), new AzureTranslatorOptions("k", Region: null));

        var result = await translator.TranslateAsync("Good day", "cs", sourceLanguage: "en", TestContext.Current.CancellationToken);

        Assert.EndsWith("&to=cs&from=en", handler.Requests.Single().Url);
        Assert.False(handler.Requests.Single().Headers.ContainsKey("Ocp-Apim-Subscription-Region"));
        Assert.Equal("en", result.DetectedLanguage);
    }

    [Fact]
    public async Task Error_response_becomes_TranslationException_with_azure_message()
    {
        var handler = new FakeHandler(HttpStatusCode.Unauthorized,
            """{"error":{"code":401000,"message":"The request is not authorized because credentials are missing or invalid."}}""");
        var translator = new AzureTranslator(new HttpClient(handler), new AzureTranslatorOptions("wrong", "westeurope"));

        var ex = await Assert.ThrowsAsync<TranslationException>(
            () => translator.TranslateAsync("text", "en", ct: TestContext.Current.CancellationToken));

        Assert.Contains("401", ex.Message);
        Assert.Contains("credentials are missing or invalid", ex.Message);
    }

    /// <summary>Real call; set AZURE_TRANSLATOR_KEY and AZURE_TRANSLATOR_REGION to run.</summary>
    [Fact(Explicit = true)]
    public async Task Real_azure_translates_ukrainian_to_english()
    {
        var key = Environment.GetEnvironmentVariable("AZURE_TRANSLATOR_KEY");
        Assert.SkipWhen(string.IsNullOrEmpty(key), "AZURE_TRANSLATOR_KEY not set");
        var translator = new AzureTranslator(new HttpClient(),
            new AzureTranslatorOptions(key!, Environment.GetEnvironmentVariable("AZURE_TRANSLATOR_REGION")));

        var result = await translator.TranslateAsync("Добрий день, як справи?", "en", ct: TestContext.Current.CancellationToken);

        TestContext.Current.TestOutputHelper?.WriteLine($"{result.DetectedLanguage}: {result.Text}");
        Assert.Equal("uk", result.DetectedLanguage);
        Assert.Contains("good", result.Text, StringComparison.OrdinalIgnoreCase);
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
