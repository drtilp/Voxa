using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voxa.Core.Translation;

namespace Voxa.Translators.DeepL;

/// <param name="Endpoint">Null = picked from the key: Free keys end with ":fx" and must use api-free.deepl.com.</param>
public sealed record DeepLTranslatorOptions(string Key, string? Endpoint = null)
{
    public string ResolvedEndpoint => Endpoint
        ?? (Key.EndsWith(":fx", StringComparison.Ordinal) ? DeepLTranslator.FreeEndpoint : DeepLTranslator.ProEndpoint);
}

/// <summary>
/// <see cref="ITranslator"/> over the DeepL API v2 (/v2/translate). The source language is detected by DeepL.
/// </summary>
public sealed class DeepLTranslator(HttpClient http, DeepLTranslatorOptions options) : ITranslator
{
    public const string FreeEndpoint = "https://api-free.deepl.com";
    public const string ProEndpoint = "https://api.deepl.com";

    public string Name => "DeepL";

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.ResolvedEndpoint.TrimEnd('/')}/v2/translate")
        {
            Content = JsonContent.Create(new Request([text], ToTargetCode(targetLanguage), sourceLanguage?.ToUpperInvariant())),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {options.Key}");

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new TranslationException($"DeepL returned {(int)response.StatusCode}: {ErrorMessage((int)response.StatusCode, body)}");
        }

        var result = await response.Content.ReadFromJsonAsync<Response>(ct);
        var translation = result?.Translations?.FirstOrDefault()
                          ?? throw new TranslationException("DeepL returned no translation.");

        return new TranslationResult(translation.Text,
            translation.DetectedSourceLanguage?.ToLowerInvariant() ?? sourceLanguage);
    }

    /// <summary>ISO 639-1 → DeepL target code. English needs a variant as a target (EN-US / EN-GB).</summary>
    public static string ToTargetCode(string language) => language.ToLowerInvariant() switch
    {
        "en" => "EN-US",
        "pt" => "PT-PT",
        var code => code.ToUpperInvariant(),
    };

    private static string ErrorMessage(int status, string body)
    {
        if (status == 456) return "monthly character quota exceeded.";
        if (status == 403) return "invalid key (Free keys end with ':fx').";
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("message").GetString() ?? body;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return body.Length > 200 ? body[..200] : body;
        }
    }

    public void Dispose() { } // HttpClient is owned by the caller

    private sealed record Request(
        [property: JsonPropertyName("text")] string[] Text,
        [property: JsonPropertyName("target_lang")] string TargetLang,
        [property: JsonPropertyName("source_lang"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceLang);

    private sealed record Response([property: JsonPropertyName("translations")] TranslationItem[]? Translations);

    private sealed record TranslationItem(
        [property: JsonPropertyName("detected_source_language")] string? DetectedSourceLanguage,
        [property: JsonPropertyName("text")] string Text);
}
