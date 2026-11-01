using Voxa.Api.Ingest;

namespace Voxa.Api.Tests;

public class MediaResolverTests
{
    [Fact]
    public void Reads_the_selected_format_with_its_headers()
    {
        var media = MediaResolver.Parse("""
            {"title":"Talk","is_live":false,"duration":125.5,"url":"https://cdn.example/a.m4a",
             "http_headers":{"User-Agent":"UA","Referer":"https://site.example/"}}
            """);

        Assert.Equal(new Uri("https://cdn.example/a.m4a"), media.Url);
        Assert.Equal("UA", media.Headers["User-Agent"]);
        Assert.Equal(("Talk", false, 125.5), (media.Title, media.IsLive, media.Duration));
    }

    [Fact]
    public void Live_stream_without_duration_and_split_formats()
    {
        var media = MediaResolver.Parse("""
            {"title":"Live","is_live":true,"duration":null,
             "requested_formats":[{"url":"https://cdn.example/live.m3u8"},{"url":"https://cdn.example/v.m3u8"}]}
            """);

        Assert.Equal(new Uri("https://cdn.example/live.m3u8"), media.Url);
        Assert.True(media.IsLive);
        Assert.Null(media.Duration);
    }

    [Fact]
    public void Page_without_media_is_an_ingest_error() =>
        Assert.Throws<IngestException>(() => MediaResolver.Parse("""{"title":"Text only"}"""));
}
