using Voxa.Core.Audio;

namespace Voxa.Engines.SherpaOnnx.Tests;

public sealed class WhisperSmallLidFixture : IDisposable
{
    public WhisperLanguageIdentifier? Identifier { get; } = WhisperLanguageIdentifier.TryCreate(ModelPaths.WhisperSmallLid);

    public WhisperLanguageIdentifier Require()
    {
        Assert.SkipUnless(Identifier is not null, $"Model not downloaded: {ModelPaths.WhisperSmallLid}");
        return Identifier!;
    }

    public void Dispose() => Identifier?.Dispose();
}

public class LanguageIdentifierTests(WhisperSmallLidFixture fixture) : IClassFixture<WhisperSmallLidFixture>
{
    // The neighbours that matter for Voxa: Czech vs Slovak vs Polish, Russian vs Ukrainian.
    [Theory]
    [InlineData("cs-czech.wav", "cs")]
    [InlineData("sk-slovak.wav", "sk")]
    [InlineData("pl-polish.wav", "pl")]
    [InlineData("ru-russian.wav", "ru")]
    [InlineData("uk-ukrainian.wav", "uk")]
    [InlineData("en-english.wav", "en")]
    [InlineData("de-german.wav", "de")]
    public async Task Whisper_small_identifies_the_language(string file, string expected)
    {
        var identifier = fixture.Require();
        var path = ModelPaths.LidWav(file);
        Assert.SkipUnless(File.Exists(path), $"Test audio not downloaded: {path}");
        var audio = WavFile.Read(path);

        var language = await identifier.IdentifyAsync(audio.Samples, TestContext.Current.CancellationToken);

        Assert.Equal(expected, language);
    }
}
