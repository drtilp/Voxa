using Voxa.Api;
using Voxa.Api.Ingest;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Voxa.Api.Tests;

/// <summary>Real ffmpeg (skipped when it isn't installed) against a WAV served over HTTP.</summary>
public sealed class FfmpegAudioReaderTests : IAsyncLifetime
{
    private WebApplication? _server;
    private Uri _wav = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _server = builder.Build();
        var wav = Sine(seconds: 3, sampleRate: 44_100);
        _server.MapGet("/tone.wav", () => Results.Bytes(wav, "audio/wav", enableRangeProcessing: true));
        await _server.StartAsync();
        _wav = new Uri(new Uri(_server.Urls.First()), "/tone.wav");
    }

    [Fact]
    public async Task Decodes_to_16_kHz_mono_floats_and_seeks()
    {
        Assert.SkipUnless(FfmpegInstalled(), "ffmpeg is not installed");
        var reader = new FfmpegAudioReader(Options.Create(new ServerOptions()));
        var media = new ResolvedMedia(_wav, new Dictionary<string, string>(), "tone", IsLive: false, Duration: 3);

        var whole = await ReadAllAsync(reader, media, start: 0);
        var tail = await ReadAllAsync(reader, media, start: 2);

        Assert.InRange(whole.Length, 47_000, 49_000); // 3 s × 16 kHz
        Assert.InRange(tail.Length, 15_000, 17_000);  // from 2 s on
        Assert.InRange(whole.Max(), 0.4f, 0.6f);      // amplitude 0.5 survived
    }

    [Fact]
    public async Task Unreadable_url_is_an_ingest_error()
    {
        Assert.SkipUnless(FfmpegInstalled(), "ffmpeg is not installed");
        var reader = new FfmpegAudioReader(Options.Create(new ServerOptions()));
        var media = new ResolvedMedia(new Uri(_wav, "/missing.wav"), new Dictionary<string, string>(), null, false, null);

        await Assert.ThrowsAsync<IngestException>(() => ReadAllAsync(reader, media, 0));
    }

    private static async Task<float[]> ReadAllAsync(FfmpegAudioReader reader, ResolvedMedia media, double start)
    {
        var samples = new List<float>();
        await foreach (var chunk in reader.ReadAsync(media, start, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken))
            samples.AddRange(chunk);
        return samples.ToArray();
    }

    private static bool FfmpegInstalled() =>
        Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Any(dir => File.Exists(Path.Combine(dir, "ffmpeg")) || File.Exists(Path.Combine(dir, "ffmpeg.exe"))) == true;

    private static byte[] Sine(int seconds, int sampleRate)
    {
        int n = seconds * sampleRate;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVEfmt "u8);
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate * 2);
        w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(n * 2);
        for (int i = 0; i < n; i++) w.Write((short)(0.5 * short.MaxValue * Math.Sin(2 * Math.PI * 440 * i / sampleRate)));
        return stream.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
    }
}
