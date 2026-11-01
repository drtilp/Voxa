using System.Net;
using Voxa.Api.Ingest;

namespace Voxa.Api.Tests;

public class PublicUrlGuardTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)] // cloud metadata
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    public void Classifies_addresses(string address, bool isPublic) =>
        Assert.Equal(isPublic, PublicUrlGuard.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task Rejects_a_host_that_resolves_to_a_private_address()
    {
        var guard = new PublicUrlGuard((_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.215.14"), IPAddress.Parse("10.0.0.5") }));

        Assert.Contains("not on the public internet",
            await guard.CheckAsync(new Uri("https://rebind.example/video"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("file:///etc/passwd", "Only http and https")]
    [InlineData("http://127.0.0.1:8080/health", "not on the public internet")]
    [InlineData("http://[::1]/", "not on the public internet")]
    public async Task Rejects_local_urls(string url, string problem) =>
        Assert.Contains(problem, await new PublicUrlGuard().CheckAsync(new Uri(url), TestContext.Current.CancellationToken));

    [Fact]
    public async Task Accepts_a_public_host() =>
        Assert.Null(await new PublicUrlGuard((_, _) => Task.FromResult(new[] { IPAddress.Parse("142.250.74.110") }))
            .CheckAsync(new Uri("https://www.youtube.com/watch?v=x"), TestContext.Current.CancellationToken));
}
