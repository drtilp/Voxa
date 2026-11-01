using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voxa.Core.Translation;

namespace Voxa.Translators.Azure;

/// <param name="Region">Resource region (e.g. "westeurope"); required for regional and multi-service resources.</param>
public sealed record AzureTranslatorOptions(string Key, string? Region, string Endpoint = AzureTranslator.GlobalEndpoint);

/// <summary>
/// <see cref="ITranslator"/> over Azure AI Translator REST API v3. Without a source language Azure detects it,
/// which is exactly what Voxa needs (the ASR models don't report the language).
/// Free tier F0: 2 million characters per month.
/// </summary>
public sealed class AzureTranslator(HttpClient http, AzureTranslatorOptions options) : ITranslator
{
    public const string GlobalEndpoint = "https://api.cognitive.microsofttranslator.com";

    public string Name => "Azure AI Translator";

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken ct = default)
    {
        var url = $"{options.Endpoint.TrimEnd('/')}/translate?api-version=3.0&to={Uri.EscapeDataString(targetLanguage)}";
        if (sourceLanguage is not null)
            url += $"&from={Uri.EscapeDataString(sourceLanguage)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new[] { new RequestItem(text) }),
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", options.Key);
        if (!string.IsNullOrWhiteSpace(options.Region))
            request.Headers.Add("Ocp-Apim-Subscription-Region", options.Region);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new TranslationException($"Azure Translator returned {(int)response.StatusCode}: {ErrorMessage(body)}");
        }

        var items = await response.Content.ReadFromJsonAsync<ResponseItem[]>(ct);
        var translation = items?.FirstOrDefault()?.Translations?.FirstOrDefault()
                          ?? throw new TranslationException("Azure Translator returned no translation.");

        return new TranslationResult(translation.Text, items![0].DetectedLanguage?.Language ?? sourceLanguage);
    }

    // {"error":{"code":401000,"message":"The request is not authorized ..."}}
    private static string ErrorMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("error").GetProperty("message").GetString() ?? body;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return body.Length > 200 ? body[..200] : body;
        }
    }

    public void Dispose() { } // HttpClient is owned by the caller (shared, see HttpClient guidelines)

    private sealed record RequestItem([property: JsonPropertyName("Text")] string Text);

    private sealed record ResponseItem(DetectedLanguage? DetectedLanguage, TranslationItem[]? Translations);

    private sealed record DetectedLanguage(string Language, double Score);

    private sealed record TranslationItem(string Text, string To);
}
