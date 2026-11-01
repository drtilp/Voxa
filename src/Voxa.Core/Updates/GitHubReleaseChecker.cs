using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Voxa.Core.Updates;

/// <summary>A published version of the app with its installer.</summary>
/// <param name="InstallerUrl">API URL of the asset: works with a token, also for private repositories
/// (browser_download_url of a private repo needs a browser session, not a token).</param>
/// <param name="Sha256">Hex digest GitHub computes for every uploaded asset; null on old releases.</param>
public sealed record AppRelease(
    Version Version,
    string Tag,
    Uri InstallerUrl,
    Uri PageUrl,
    string Notes,
    string? Sha256,
    long Size);

public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Finds a newer release on GitHub Releases (api.github.com/repos/{owner}/{repo}/releases/latest).
/// "latest" skips drafts and pre-releases, so publishing a pre-release never updates users.
/// A private repository needs <paramref name="token"/> (read access to its contents); without it GitHub answers 404.
/// </summary>
/// <param name="token">Read on every call, so a token entered in Settings works without restarting.</param>
public sealed class GitHubReleaseChecker(
    HttpClient http, string owner, string repo, string installerAssetName, Func<string?>? token = null)
{
    public Uri LatestReleaseApi { get; } = new($"https://api.github.com/repos/{owner}/{repo}/releases/latest");

    /// <returns>The latest release when it is newer than <paramref name="current"/>, otherwise null.</returns>
    public async Task<AppRelease?> FindNewerAsync(Version current, CancellationToken ct = default)
    {
        var latest = await GetLatestAsync(ct);
        return latest is not null && latest.Version > Normalize(current) ? latest : null;
    }

    /// <returns>Null when there is no release yet or it has no installer.</returns>
    public async Task<AppRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(LatestReleaseApi, "application/vnd.github+json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Can't reach GitHub: {ex.Message}", ex);
        }

        using (response)
        {
            // 404: no release published yet (or a private repository and no / too weak token).
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new UpdateException("GitHub rejected the token (expired or revoked). Enter a new one in Settings.");
            if (!response.IsSuccessStatusCode)
                throw new UpdateException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return Parse(json.RootElement, installerAssetName);
        }
    }

    private static AppRelease? Parse(JsonElement release, string installerAssetName)
    {
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (ParseTag(tag) is not { } version) return null;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), installerAssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            string? sha256 = asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d
                             && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? d["sha256:".Length..]
                : null;

            return new AppRelease(
                version,
                tag,
                new Uri(asset.GetProperty("url").GetString()!),
                new Uri(release.GetProperty("html_url").GetString()!),
                release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                sha256,
                asset.GetProperty("size").GetInt64());
        }

        return null;
    }

    private HttpRequestMessage CreateRequest(Uri url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        // GitHub rejects API calls without a User-Agent (403).
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Voxa-Updater", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        if (token?.Invoke() is { Length: > 0 } value)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        return request;
    }

    /// <summary>"v0.2.0" → 0.2.0. Suffixes such as "-beta.1" or "+build" are ignored.</summary>
    public static Version? ParseTag(string tag)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0) text = text[..cut];
        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    /// <summary>0.2 and 0.2.0.0 must equal 0.2.0: Version compares unset parts (-1) as smaller.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Downloads the installer; a wrong SHA-256 deletes the file and throws (never run what we didn't publish).</summary>
    public async Task DownloadAsync(AppRelease release, string targetPath, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var temp = targetPath + ".part";
        try
        {
            // octet-stream: GitHub redirects to the file itself instead of returning the asset's JSON.
            // HttpClient drops the Authorization header on that redirect (to another host), as it must.
            using var request = CreateRequest(release.InstallerUrl, "application/octet-stream");
            using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!response.IsSuccessStatusCode)
                    throw new UpdateException($"Download failed: {(int)response.StatusCode} {response.ReasonPhrase}.");

                long total = response.Content.Headers.ContentLength ?? release.Size;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(temp);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            if (release.Sha256 is { } expected)
            {
                await using var file = File.OpenRead(temp);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException("The downloaded installer is damaged or changed (SHA-256 mismatch).");
            }

            File.Move(temp, targetPath, overwrite: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new UpdateException($"Download failed: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
