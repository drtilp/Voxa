using System.Net.Http.Json;
using Voxa.Core.Remote;
using Voxa.Core.Translation;

namespace Voxa.Remote;

/// <summary><see cref="ITranslator"/> over POST /v1/translate (the server's local model, Hy-MT2).</summary>
public sealed class RemoteTranslator(RemoteServer server) : ITranslator
{
    public string Name => "Cloud server";

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Url(RemoteProtocol.TranslatePath))
        {
            Content = JsonContent.Create(new TranslateRequest(text, targetLanguage, sourceLanguage), options: RemoteProtocol.Json),
        };
        request.Headers.Authorization = server.Authorization;

        HttpResponseMessage response;
        try
        {
            response = await server.Http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException($"Cloud server unreachable: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new TranslationException(RemoteServer.DescribeStatus(response.StatusCode,
                    await response.Content.ReadAsStringAsync(ct)));

            return await response.Content.ReadFromJsonAsync<TranslationResult>(RemoteProtocol.Json, ct)
                   ?? throw new TranslationException("The cloud server returned no translation.");
        }
    }

    public void Dispose() { } // the HttpClient is shared, not ours
}
