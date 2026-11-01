using System.Net;
using System.Security.Cryptography;
using System.Text;
using Voxa.Core.Updates;

namespace Voxa.Core.Tests;

public class GitHubReleaseCheckerTests
{
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("pretend this is Voxa-Setup.exe");

    private static string ReleaseJson(string tag, string? digest) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/owner/Voxa/releases/tag/{{tag}}",
          "body": "Bigger overlay text.",
          "assets": [
            { "name": "notes.txt", "url": "https://api.example.test/assets/1",
              "browser_download_url": "https://example.test/notes.txt", "size": 5 },
            { "name": "Voxa-Setup.exe", "url": "https://api.example.test/repos/owner/Voxa/releases/assets/2",
              "browser_download_url": "https://example.test/Voxa-Setup.exe",
              "size": {{Installer.Length}}{{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }
          ]
        }
        """;

    private static GitHubReleaseChecker Checker(RoutingHandler handler, string? token = null) =>
        new(new HttpClient(handler), "owner", "Voxa", "Voxa-Setup.exe", () => token);

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.2", "0.2.0")]
    [InlineData("v1.4.2-beta.1", "1.4.2")]
    [InlineData("v1.0.0+abc123", "1.0.0")]
    public void Parses_tags(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), GitHubReleaseChecker.ParseTag(tag));

    [Fact]
    public void Garbage_tag_is_not_a_version() => Assert.Null(GitHubReleaseChecker.ParseTag("nightly"));

    [Fact]
    public async Task Newer_release_is_found_with_installer_and_digest()
    {
        var sha = Convert.ToHexString(SHA256.HashData(Installer));
        var handler = new RoutingHandler { ["/repos/owner/Voxa/releases/latest"] = Json(ReleaseJson("v0.2.0", "sha256:" + sha)) };

        // The exe reports 0.1.0.0 (four parts); the tag has three.
        var release = await Checker(handler).FindNewerAsync(new Version(0, 1, 0, 0), TestContext.Current.CancellationToken);

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 2, 0), release.Version);
        // API asset URL, not browser_download_url: only that one accepts a token (private repository)
        Assert.Equal("https://api.example.test/repos/owner/Voxa/releases/assets/2", release.InstallerUrl.ToString());
        Assert.Equal(sha, release.Sha256);
        Assert.Equal("Bigger overlay text.", release.Notes);
        Assert.Equal("Voxa-Updater/1.0", handler.LastUserAgent); // GitHub returns 403 without one
    }

    [Fact]
    public async Task Token_is_sent_for_a_private_repository()
    {
        var handler = new RoutingHandler { ["/repos/owner/Voxa/releases/latest"] = Json(ReleaseJson("v0.2.0", null)) };

        await Checker(handler, "github_pat_abc").FindNewerAsync(new Version(0, 1, 0), TestContext.Current.CancellationToken);

        Assert.Equal("Bearer github_pat_abc", handler.LastAuthorization);
    }

    [Fact]
    public async Task No_token_sends_no_authorization()
    {
        var handler = new RoutingHandler();

        await Checker(handler).FindNewerAsync(new Version(0, 1, 0), TestContext.Current.CancellationToken);

        Assert.Null(handler.LastAuthorization);
    }

    [Fact]
    public async Task Rejected_token_is_reported()
    {
        var handler = new RoutingHandler { ["/repos/owner/Voxa/releases/latest"] = new(HttpStatusCode.Unauthorized, []) };

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => Checker(handler, "expired").FindNewerAsync(new Version(0, 1, 0), TestContext.Current.CancellationToken));

        Assert.Contains("token", ex.Message);
    }

    [Theory]
    [InlineData("v0.1.0")]   // same version, only written differently than 0.1.0.0
    [InlineData("v0.0.9")]
    public async Task Same_or_older_release_is_not_an_update(string tag)
    {
        var handler = new RoutingHandler { ["/repos/owner/Voxa/releases/latest"] = Json(ReleaseJson(tag, null)) };

        var release = await Checker(handler).FindNewerAsync(new Version(0, 1, 0, 0), TestContext.Current.CancellationToken);

        Assert.Null(release);
    }

    [Fact]
    public async Task No_release_yet_is_not_an_error()
    {
        var handler = new RoutingHandler(); // everything 404, like a repo without releases

        Assert.Null(await Checker(handler).FindNewerAsync(new Version(0, 1, 0), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rate_limit_is_reported()
    {
        var handler = new RoutingHandler { ["/repos/owner/Voxa/releases/latest"] = new(HttpStatusCode.Forbidden, []) };

        await Assert.ThrowsAsync<UpdateException>(
            () => Checker(handler).FindNewerAsync(new Version(0, 1, 0), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Download_with_matching_digest_saves_the_installer()
    {
        var handler = new RoutingHandler { ["/repos/owner/Voxa/releases/assets/2"] = new(HttpStatusCode.OK, Installer) };
        var release = Release(Convert.ToHexString(SHA256.HashData(Installer)));
        var target = Path.Combine(Path.GetTempPath(), $"voxa-test-{Guid.NewGuid():N}.exe");
        var reports = new List<double>();

        try
        {
            await Checker(handler, "github_pat_abc").DownloadAsync(release, target, new SyncProgress(reports.Add), TestContext.Current.CancellationToken);

            Assert.Equal(Installer, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
            Assert.Equal(1.0, reports[^1]);
            Assert.Equal("application/octet-stream", handler.LastAccept); // the file, not the asset's JSON
            Assert.Equal("Bearer github_pat_abc", handler.LastAuthorization);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public async Task Download_with_wrong_digest_leaves_nothing_behind()
    {
        var handler = new RoutingHandler { ["/repos/owner/Voxa/releases/assets/2"] = new(HttpStatusCode.OK, Installer) };
        var release = Release(new string('0', 64));
        var target = Path.Combine(Path.GetTempPath(), $"voxa-test-{Guid.NewGuid():N}.exe");

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => Checker(handler).DownloadAsync(release, target, ct: TestContext.Current.CancellationToken));

        Assert.Contains("SHA-256", ex.Message);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
    }

    private static AppRelease Release(string? sha256) => new(
        new Version(0, 2, 0), "v0.2.0", new Uri("https://api.example.test/repos/owner/Voxa/releases/assets/2"),
        new Uri("https://github.com/owner/Voxa/releases/tag/v0.2.0"), "", sha256, Installer.Length);

    private static (HttpStatusCode, byte[]) Json(string json) => (HttpStatusCode.OK, Encoding.UTF8.GetBytes(json));

    /// <summary>Progress&lt;T&gt; posts to the thread pool; tests need the reports synchronously.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Answers by URL path; unknown paths are 404.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Body)> _routes = [];

        public string? LastUserAgent { get; private set; }

        public string? LastAuthorization { get; private set; }

        public string? LastAccept { get; private set; }

        public (HttpStatusCode Status, byte[] Body) this[string path]
        {
            set => _routes[path] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUserAgent = request.Headers.UserAgent.ToString();
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastAccept = request.Headers.Accept.ToString();
            var (status, body) = _routes.TryGetValue(request.RequestUri!.AbsolutePath, out var route)
                ? route
                : (HttpStatusCode.NotFound, Array.Empty<byte>());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }
}
