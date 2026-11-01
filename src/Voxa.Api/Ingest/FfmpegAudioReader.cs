using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Options;

namespace Voxa.Api.Ingest;

/// <summary>
/// Decodes any audio/video URL (file, HLS, DASH, live) to 16 kHz mono float samples with ffmpeg, streamed: the first
/// chunks arrive within a second, also for hours-long media. Seeking (-ss before -i) uses HTTP range requests.
/// </summary>
public sealed class FfmpegAudioReader(IOptions<ServerOptions> options)
{
    public const int SampleRate = 16_000;
    private const int ChunkSamples = SampleRate / 10; // 100 ms

    public async IAsyncEnumerable<float[]> ReadAsync(ResolvedMedia media, double startSeconds, TimeSpan maxDuration,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var process = Process.Start(CreateStartInfo(media, startSeconds, maxDuration))
                            ?? throw new IngestException("ffmpeg didn't start.");
        var stderr = process.StandardError.ReadToEndAsync(ct);
        var stdout = process.StandardOutput.BaseStream;
        var buffer = new byte[ChunkSamples * sizeof(float)];
        long produced = 0;
        try
        {
            while (true)
            {
                int filled = await stdout.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
                filled -= filled % sizeof(float);
                if (filled == 0) break;
                produced += filled;
                yield return MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, filled)).ToArray();
            }

            await process.WaitForExitAsync(ct);
            if (produced == 0)
                throw new IngestException($"No audio could be read: {Last(await stderr)}");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true); // client left: stop downloading
        }
    }

    private ProcessStartInfo CreateStartInfo(ResolvedMedia media, double startSeconds, TimeSpan maxDuration)
    {
        var start = new ProcessStartInfo(options.Value.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var args = start.ArgumentList;
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error",
                     // Network protocols only: a playlist can't make ffmpeg read local files.
                     "-protocol_whitelist", "http,https,tcp,tls,crypto" })
            args.Add(arg);
        if (media.Headers.Count > 0)
        {
            args.Add("-headers");
            args.Add(string.Concat(media.Headers.Select(h => $"{h.Key}: {h.Value}\r\n")));
        }
        if (startSeconds > 0 && !media.IsLive)
        {
            args.Add("-ss");
            args.Add(startSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }
        foreach (var arg in new[] { "-t", ((int)maxDuration.TotalSeconds).ToString(), "-i", media.Url.ToString(),
                     "-vn", "-ac", "1", "-ar", SampleRate.ToString(), "-f", "f32le", "pipe:1" })
            args.Add(arg);
        return start;
    }

    private static string Last(string stderr)
    {
        var text = stderr.Trim();
        return text.Length <= 300 ? text : text[^300..];
    }
}
