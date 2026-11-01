using System.Net.Http;
using Voxa.Remote;
using Voxa.Wpf.Services;

namespace Voxa.Wpf.Transcription;

/// <summary>The Voxa Cloud server from the settings (address, and the sign-in token once that exists).</summary>
public static class CloudServer
{
    // Own client: translations from a CPU server take seconds, longer than the 10 s of the translator client allows
    // when several sentences queue up. Waiting for a waking server polls /health with its own short timeouts.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <returns>Null with a reason when the address or the sign-in is missing.</returns>
    public static (RemoteServer? Server, string? Problem) Create(AppSettings settings)
    {
        // Signing in to the cloud server is not built yet, so there is no token to send.
        var token = SettingsStore.Unprotect(settings.CloudKeyProtected);
        if (token is null && !string.IsNullOrWhiteSpace(settings.CloudUrl))
            return (null, "Voxa Cloud needs a sign-in, which this version doesn't have yet.");

        var (options, problem) = RemoteServerOptions.TryCreate(settings.CloudUrl, token);
        return options is null ? (null, problem) : (new RemoteServer(Http, options), null);
    }
}
