using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Voxa.Core.Models;

/// <summary>One file of a downloadable model.</summary>
/// <param name="RelativePath">Where it goes inside the models folder, e.g. "parakeet-ultra/encoder.int8.onnx".</param>
/// <param name="Url">Pinned to a fixed revision, so the hash can't stop matching.</param>
/// <param name="Sha256">Lowercase hex; checked before the file is used.</param>
public sealed record ModelFileSource(string RelativePath, Uri Url, long Size, string Sha256);

/// <summary>A model that can be downloaded: all its files appear at once, after every one of them is verified.</summary>
public sealed record ModelPackage(string Id, string Title, string Description, IReadOnlyList<ModelFileSource> Files)
{
    public long TotalSize => Files.Sum(f => f.Size);
}

public readonly record struct DownloadProgress(long Downloaded, long Total)
{
    public double Fraction => Total > 0 ? (double)Downloaded / Total : 0;
}

public sealed class ModelDownloadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Downloads <see cref="ModelPackage"/>s into the models folder over plain HTTPS (Hugging Face, GitHub releases …).
/// </summary>
/// <remarks>
/// Every file goes to "&lt;name&gt;.part" first. An interrupted download (cancel, network, app closed) continues
/// where it stopped with an HTTP Range request. Only when all files of the package are complete and their SHA-256
/// matches are they renamed to their real names, so the app (which watches the models folder) never sees half a
/// model, e.g. a Parakeet folder without its joiner that would be taken for Whisper.
/// </remarks>
public sealed class ModelDownloader(HttpClient http)
{
    public const string PartSuffix = ".part";

    private const int Attempts = 3;
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>A connection that delivers nothing for this long is dropped and resumed.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>All files present with the right size (hashes are checked when downloading, not on every start).</summary>
    public static bool IsInstalled(ModelPackage package, string modelsFolder) =>
        package.Files.All(f => new FileInfo(FinalPath(modelsFolder, f)) is { Exists: true } file && file.Length == f.Size);

    /// <summary>Bytes already on disk (finished files and .part files), so a resumed download shows real progress.</summary>
    public static long DownloadedBytes(ModelPackage package, string modelsFolder) =>
        package.Files.Sum(f => Math.Min(f.Size, ExistingLength(f, modelsFolder)));

    /// <exception cref="ModelDownloadException">Network, disk or integrity problem, with a message for the user.</exception>
    public async Task DownloadAsync(ModelPackage package, string modelsFolder, IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var missing = package.Files.Where(f => !IsComplete(f, modelsFolder)).ToList();
        long done = DownloadedBytes(package, modelsFolder);
        EnsureFreeSpace(modelsFolder, package.TotalSize - done);

        var reporter = new Reporter(progress, package.TotalSize, done);
        reporter.Report(force: true);

        foreach (var file in missing)
            await DownloadFileAsync(file, PartPath(modelsFolder, file), reporter, ct);

        // All verified: now make the model visible.
        foreach (var file in missing)
            File.Move(PartPath(modelsFolder, file), FinalPath(modelsFolder, file), overwrite: true);
        reporter.Report(force: true);
    }

    /// <summary>Removes the package's files (also unfinished ones) and folders that became empty.</summary>
    public static void Delete(ModelPackage package, string modelsFolder)
    {
        foreach (var file in package.Files)
        {
            File.Delete(FinalPath(modelsFolder, file));
            File.Delete(PartPath(modelsFolder, file));
        }

        foreach (var dir in package.Files.Select(f => Path.GetDirectoryName(FinalPath(modelsFolder, f))).OfType<string>().Distinct())
        {
            for (var current = new DirectoryInfo(dir);
                 current.Exists && !IsSame(current.FullName, modelsFolder) && !current.EnumerateFileSystemInfos().Any();
                 current = current.Parent!)
                current.Delete();
        }
    }

    private async Task DownloadFileAsync(ModelFileSource file, string partPath, Reporter reporter, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (PartLength(partPath) < file.Size)
                    await TransferAsync(file, partPath, reporter, ct);
                if (PartLength(partPath) < file.Size)
                    throw new IOException("The connection ended early.");
                break;
            }
            catch (Exception ex) when (IsNetworkError(ex, ct) && attempt < Attempts)
            {
                await Task.Delay(RetryDelay, ct); // resumes from what arrived
            }
            catch (Exception ex) when (IsNetworkError(ex, ct))
            {
                throw new ModelDownloadException(
                    $"Downloading {Path.GetFileName(file.RelativePath)} failed: {ex.Message} Press Download again to continue.", ex);
            }
        }

        if (PartLength(partPath) != file.Size || !string.Equals(await HashAsync(partPath, ct), file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            reporter.Add(-PartLength(partPath));
            File.Delete(partPath);
            throw new ModelDownloadException(
                $"{Path.GetFileName(file.RelativePath)} arrived damaged (checksum mismatch) and was deleted. Press Download to try again.");
        }
    }

    private async Task TransferAsync(ModelFileSource file, string partPath, Reporter reporter, CancellationToken ct)
    {
        long existing = PartLength(partPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The .part doesn't fit the file on the server: start this file over.
            reporter.Add(-existing);
            File.Delete(partPath);
            throw new HttpRequestException("The partial download didn't match, starting over.");
        }
        if ((int)response.StatusCode >= 500)
            throw new HttpRequestException($"The server returned {(int)response.StatusCode}."); // worth a retry
        if (!response.IsSuccessStatusCode)
            throw new ModelDownloadException($"Downloading {Path.GetFileName(file.RelativePath)} failed: the server returned {(int)response.StatusCode}.");

        bool append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append && existing > 0)
        {
            reporter.Add(-existing); // the server ignored Range and sends the whole file
            existing = 0;
        }

        await using var target = new FileStream(partPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write,
            FileShare.None, 1 << 16, useAsync: true);
        await using var source = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[1 << 16];
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (true)
        {
            stall.CancelAfter(StallTimeout);
            int read;
            try
            {
                read = await source.ReadAsync(buffer, stall.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException($"No data for {StallTimeout.TotalSeconds:0} s.");
            }
            if (read == 0) break;

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            reporter.Add(read);
            if (target.Position > file.Size) // a wrong file (or an error page) can't fill the disk
                throw new ModelDownloadException($"{Path.GetFileName(file.RelativePath)} is bigger than expected; download aborted.");
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    private static void EnsureFreeSpace(string modelsFolder, long needed)
    {
        Directory.CreateDirectory(modelsFolder);
        var root = Path.GetPathRoot(Path.GetFullPath(modelsFolder));
        if (string.IsNullOrEmpty(root)) return;
        long free;
        try
        {
            free = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return; // network share etc.: let the write fail with its own message if it must
        }
        if (free < needed)
            throw new ModelDownloadException($"Not enough disk space: {FormatSize(needed)} needed, {FormatSize(free)} free on {root}.");
    }

    public static string FormatSize(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.0} GB"
        : $"{bytes / (double)(1L << 20):0} MB";

    private static bool IsNetworkError(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is HttpRequestException or IOException or TaskCanceledException;

    private static bool IsComplete(ModelFileSource file, string modelsFolder) =>
        new FileInfo(FinalPath(modelsFolder, file)) is { Exists: true } f && f.Length == file.Size;

    private static long ExistingLength(ModelFileSource file, string modelsFolder) =>
        IsComplete(file, modelsFolder) ? file.Size : PartLength(PartPath(modelsFolder, file));

    private static long PartLength(string partPath) => new FileInfo(partPath) is { Exists: true } f ? f.Length : 0;

    private static string FinalPath(string modelsFolder, ModelFileSource file) =>
        Path.GetFullPath(Path.Combine(modelsFolder, file.RelativePath));

    private static string PartPath(string modelsFolder, ModelFileSource file) => FinalPath(modelsFolder, file) + PartSuffix;

    private static bool IsSame(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Progress at most every 200 ms: a 2 GB download would otherwise post ~30,000 updates to the UI thread.
    private sealed class Reporter(IProgress<DownloadProgress>? progress, long total, long done)
    {
        private readonly Stopwatch _sinceReport = Stopwatch.StartNew();
        private long _done = done;

        public void Add(long bytes)
        {
            _done += bytes;
            Report(force: false);
        }

        public void Report(bool force)
        {
            if (progress is null || (!force && _sinceReport.Elapsed < ReportInterval)) return;
            _sinceReport.Restart();
            progress.Report(new DownloadProgress(Math.Clamp(_done, 0, total), total));
        }
    }
}
