using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Voxa.Api.Ingest;

/// <param name="Url">What ffmpeg opens: a direct audio/video file or an HLS/DASH playlist.</param>
/// <param name="Headers">Headers the site requires for that URL (User-Agent, Referer, cookies of the extractor).</param>
public sealed record ResolvedMedia(Uri Url, IReadOnlyDictionary<string, string> Headers, string? Title, bool IsLive, double? Duration);

public sealed class IngestException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Page URL (YouTube, Twitch, a news site …) → the audio stream behind it, via yt-dlp (it knows ~1800 sites and
/// plain media URLs). Only metadata is asked for (-J); ffmpeg downloads the audio itself, which lets it seek.
/// </summary>
public sealed class MediaResolver(IOptions<ServerOptions> options, ILogger<MediaResolver> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<ResolvedMedia> ResolveAsync(Uri page, CancellationToken ct)
    {
        var start = new ProcessStartInfo(options.Value.YtDlpPath)
        {
            ArgumentList = { "-J", "--no-playlist", "--no-warnings", "-f", "bestaudio/best", page.ToString() },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        using var process = Process.Start(start) ?? throw new IngestException("yt-dlp didn't start.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new IngestException(Explain(await stderr));
            return Parse(await stdout);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IngestException("Looking up the media took too long.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    internal static ResolvedMedia Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // One selected format: its fields are on the root. Separate audio + video: the first of requested_formats.
        var format = root.TryGetProperty("url", out _) ? root
            : root.TryGetProperty("requested_formats", out var formats) && formats.GetArrayLength() > 0 ? formats[0]
            : throw new IngestException("No playable audio found on that page.");

        var url = new Uri(format.GetProperty("url").GetString()!);
        var headers = format.TryGetProperty("http_headers", out var h) && h.ValueKind == JsonValueKind.Object
            ? h.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "")
            : new Dictionary<string, string>();

        return new ResolvedMedia(url, headers,
            Title: root.TryGetProperty("title", out var title) ? title.GetString() : null,
            IsLive: root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True,
            Duration: root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null);
    }

    private string Explain(string stderr)
    {
        logger.LogWarning("yt-dlp failed: {Error}", stderr);
        var line = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(l => l.Contains("ERROR")) ?? stderr;
        return line.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase)
            ? "The site blocks downloads from this server (bot check)."
            : $"The page can't be read: {line.Replace("ERROR: ", "").Trim()}";
    }
}
