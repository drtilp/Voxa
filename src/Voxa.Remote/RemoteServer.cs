using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Voxa.Core.Remote;

namespace Voxa.Remote;

/// <param name="BaseUrl">e.g. https://voxa-api.….azurecontainerapps.io</param>
/// <param name="ApiKey">Sent as "Authorization: Bearer"; the server's Voxa__ApiKey.</param>
public sealed record RemoteServerOptions(Uri BaseUrl, string ApiKey)
{
    /// <summary>Null with a reason when the URL is missing or not http(s).</summary>
    public static (RemoteServerOptions? Options, string? Problem) TryCreate(string? url, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(url)) return (null, "Cloud server: enter the server address in Settings.");
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return (null, $"Cloud server: '{url}' is not an http(s) address.");
        if (string.IsNullOrWhiteSpace(apiKey)) return (null, "Cloud server: enter the API key in Settings.");
        return (new RemoteServerOptions(uri, apiKey.Trim()), null);
    }
}

public sealed class RemoteServerException(string message, HttpStatusCode? status = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// The server as a whole: is it up, what does it run. The server scales to zero when unused, so the first request
/// wakes it up and the models then load for 1-2 minutes; <see cref="WaitUntilReadyAsync"/> covers that.
/// </summary>
public sealed class RemoteServer(HttpClient http, RemoteServerOptions options)
{
    /// <summary>Starting a replica from zero and loading the models took ~110 s on Azure (CPU); GPU images are bigger.</summary>
    public static TimeSpan DefaultReadyTimeout { get; } = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan HealthRequestTimeout = TimeSpan.FromSeconds(15);

    public RemoteServerOptions Options { get; } = options;

    /// <summary>How often <see cref="WaitUntilReadyAsync"/> asks /health.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    internal HttpClient Http { get; } = http;

    public Uri Url(string path) => new(Options.BaseUrl, path);

    internal AuthenticationHeaderValue Authorization => new("Bearer", Options.ApiKey);

    /// <summary>True when the models are loaded. False while starting or loading; also wakes a sleeping server.</summary>
    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HealthRequestTimeout);
        try
        {
            using var response = await Http.GetAsync(Url(RemoteProtocol.HealthPath), timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return false; // the platform holds or drops requests while the replica starts
        }
    }

    /// <summary>Polls /health until the server is ready; <paramref name="waiting"/> is called once when it isn't yet.</summary>
    /// <exception cref="RemoteServerException">Not ready within <paramref name="timeout"/>.</exception>
    public async Task WaitUntilReadyAsync(Action? waiting = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultReadyTimeout);
        bool reported = false;
        while (!await IsReadyAsync(ct))
        {
            if (!reported)
            {
                reported = true;
                waiting?.Invoke();
            }
            if (DateTime.UtcNow >= deadline)
                throw new RemoteServerException($"The cloud server didn't start within {(timeout ?? DefaultReadyTimeout).TotalMinutes:0} minutes.");
            await Task.Delay(PollInterval, ct);
        }
    }

    public async Task<ServerInfo> GetInfoAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(RemoteProtocol.InfoPath));
        request.Headers.Authorization = Authorization;
        using var response = await Http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ServerInfo>(RemoteProtocol.Json, ct)
               ?? throw new RemoteServerException("The cloud server returned no info.");
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        throw new RemoteServerException(DescribeStatus(response.StatusCode, await response.Content.ReadAsStringAsync(ct)),
            response.StatusCode);
    }

    internal static string DescribeStatus(HttpStatusCode status, string? body = null) => status switch
    {
        HttpStatusCode.Unauthorized => "The cloud server refused the API key.",
        HttpStatusCode.ServiceUnavailable => "The cloud server is still starting.",
        _ => $"The cloud server returned {(int)status}{(string.IsNullOrWhiteSpace(body) ? "" : $": {Shorten(body)}")}",
    };

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
