using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Voxa.Core.Models;

namespace Voxa.Core.Tests;

public sealed class ModelDownloaderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("voxa-models-").FullName;
    private readonly byte[] _encoder = RandomNumberGenerator.GetBytes(300_000);
    private readonly byte[] _tokens = "a 0\nb 1\n"u8.ToArray();

    private ModelPackage Package => new("test", "Test model", "", [
        File("m/encoder.onnx", _encoder),
        File("m/tokens.txt", _tokens),
    ]);

    private static ModelFileSource File(string path, byte[] data) =>
        new(path, new Uri($"https://models.example/{path}"), data.Length, Convert.ToHexStringLower(SHA256.HashData(data)));

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Downloads_verifies_and_reports_progress_to_the_total()
    {
        var server = new FakeFiles { ["m/encoder.onnx"] = _encoder, ["m/tokens.txt"] = _tokens };
        var reports = new List<DownloadProgress>();

        await Downloader(server).DownloadAsync(Package, _folder, new SyncProgress(reports.Add), Ct);

        Assert.Equal(_encoder, System.IO.File.ReadAllBytes(Path.Combine(_folder, "m", "encoder.onnx")));
        Assert.True(ModelDownloader.IsInstalled(Package, _folder));
        Assert.Empty(Directory.GetFiles(_folder, "*" + ModelDownloader.PartSuffix, SearchOption.AllDirectories));
        Assert.Equal(Package.TotalSize, reports[^1].Downloaded);
    }

    [Fact]
    public async Task Files_get_their_names_only_when_the_whole_package_is_verified()
    {
        var server = new FakeFiles { ["m/encoder.onnx"] = _encoder }; // tokens missing on the server

        await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(server).DownloadAsync(Package, _folder, ct: Ct));

        Assert.False(System.IO.File.Exists(Path.Combine(_folder, "m", "encoder.onnx")));
        Assert.True(System.IO.File.Exists(Path.Combine(_folder, "m", "encoder.onnx" + ModelDownloader.PartSuffix)));
    }

    [Fact]
    public async Task Resumes_a_partial_file_with_a_range_request()
    {
        WritePart("m/encoder.onnx", _encoder[..100_000]);
        var server = new FakeFiles { ["m/encoder.onnx"] = _encoder, ["m/tokens.txt"] = _tokens };

        await Downloader(server).DownloadAsync(Package, _folder, ct: Ct);

        Assert.Equal(100_000, server.RangeStarts["m/encoder.onnx"]);
        Assert.Equal(_encoder, System.IO.File.ReadAllBytes(Path.Combine(_folder, "m", "encoder.onnx")));
    }

    [Fact]
    public async Task Starts_over_when_the_server_ignores_the_range()
    {
        WritePart("m/encoder.onnx", _encoder[..100_000]);
        var server = new FakeFiles { ["m/encoder.onnx"] = _encoder, ["m/tokens.txt"] = _tokens, IgnoreRange = true };

        await Downloader(server).DownloadAsync(Package, _folder, ct: Ct);

        Assert.Equal(_encoder, System.IO.File.ReadAllBytes(Path.Combine(_folder, "m", "encoder.onnx")));
    }

    [Fact]
    public async Task Continues_after_the_connection_breaks()
    {
        var server = new FakeFiles { ["m/encoder.onnx"] = _encoder, ["m/tokens.txt"] = _tokens, BreakFirstAfter = 50_000 };

        await Downloader(server).DownloadAsync(Package, _folder, ct: Ct);

        Assert.Equal(_encoder, System.IO.File.ReadAllBytes(Path.Combine(_folder, "m", "encoder.onnx")));
        Assert.Equal(50_000, server.RangeStarts["m/encoder.onnx"]);
    }

    [Fact]
    public async Task Damaged_file_is_deleted_and_reported()
    {
        var damaged = (byte[])_encoder.Clone();
        damaged[123] ^= 0xFF;
        var server = new FakeFiles { ["m/encoder.onnx"] = damaged, ["m/tokens.txt"] = _tokens };

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(server).DownloadAsync(Package, _folder, ct: Ct));

        Assert.Contains("checksum", error.Message);
        Assert.Empty(Directory.GetFiles(_folder, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Delete_removes_files_and_empty_folders_but_not_the_models_folder()
    {
        var server = new FakeFiles { ["m/encoder.onnx"] = _encoder, ["m/tokens.txt"] = _tokens };
        await Downloader(server).DownloadAsync(Package, _folder, ct: Ct);

        ModelDownloader.Delete(Package, _folder);

        Assert.False(ModelDownloader.IsInstalled(Package, _folder));
        Assert.False(Directory.Exists(Path.Combine(_folder, "m")));
        Assert.True(Directory.Exists(_folder));
    }

    private ModelDownloader Downloader(FakeFiles server) => new(new HttpClient(server)) { RetryDelay = TimeSpan.Zero };

    private void WritePart(string relativePath, byte[] data)
    {
        var path = Path.Combine(_folder, relativePath) + ModelDownloader.PartSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, data);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // Progress<T> posts to the thread pool; the test needs every report in order.
    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    /// <summary>Static file server with Range support and scripted failures.</summary>
    private sealed class FakeFiles : HttpMessageHandler, IEnumerable<KeyValuePair<string, byte[]>>
    {
        private readonly Dictionary<string, byte[]> _files = [];
        private bool _broken;

        public byte[] this[string path] { set => _files[path] = value; }

        public bool IgnoreRange { get; init; }

        /// <summary>The first response ends with an error after this many bytes.</summary>
        public int? BreakFirstAfter { get; init; }

        public Dictionary<string, long> RangeStarts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (!_files.TryGetValue(path, out var data))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            long from = 0;
            if (!IgnoreRange && request.Headers.Range?.Ranges.First().From is { } start)
            {
                RangeStarts[path] = from = start;
            }

            var body = data[(int)from..];
            Stream stream = new MemoryStream(body);
            if (BreakFirstAfter is { } limit && !_broken)
            {
                _broken = true;
                stream = new BreakingStream(body, limit);
            }

            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
            if (from > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, data.Length - 1, data.Length);
            return Task.FromResult(response);
        }

        public IEnumerator<KeyValuePair<string, byte[]>> GetEnumerator() => _files.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class BreakingStream(byte[] data, int limit) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= limit) throw new IOException("Connection reset.");
            return base.Read(buffer, offset, (int)Math.Min(count, limit - Position));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (Position >= limit) throw new IOException("Connection reset.");
            return base.ReadAsync(buffer[..(int)Math.Min(buffer.Length, limit - Position)], ct);
        }
    }
}
