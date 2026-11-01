using System.Diagnostics;
using Voxa.Core.Translation;
using Voxa.Translators.Llama;

namespace Voxa.Translators.Tests;

/// <summary>Loads Hy-MT2 once per class (seconds + ~2 GB VRAM); skips when the model isn't downloaded.</summary>
public sealed class HyMt2Fixture : IAsyncLifetime
{
    public static string ModelPath { get; } = Path.Combine(FindRepoRoot(), "models", "translation", "hy-mt2-1.8b",
        "Hy-MT2-1.8B-Q8_0.gguf");

    public LlamaTranslator? Translator { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (File.Exists(ModelPath))
            Translator = await LlamaTranslator.LoadAsync(new LlamaTranslatorOptions(ModelPath));
    }

    public LlamaTranslator Require()
    {
        Assert.SkipUnless(Translator is not null, $"Model not downloaded: {ModelPath}");
        return Translator!;
    }

    public ValueTask DisposeAsync()
    {
        Translator?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Voxa.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("Voxa.sln not found.");
    }
}

public sealed class LlamaTranslatorTests(HyMt2Fixture fixture) : IClassFixture<HyMt2Fixture>
{
    [Theory]
    [InlineData("Добрий день, як справи?", "en", "how are you")]                  // uk -> en
    [InlineData("Сегодня очень хорошая погода.", "en", "weather")]                // ru -> en
    [InlineData("Zítra pojedeme vlakem do Prahy.", "en", "Prague")]               // cs -> en
    [InlineData("The meeting has been moved to Friday.", "cs", "pátek")]          // en -> cs
    public async Task Translates_into_the_target_language(string source, string target, string expectedFragment)
    {
        var translator = fixture.Require();
        var stopwatch = Stopwatch.StartNew();

        var result = await translator.TranslateAsync(source, target, ct: TestContext.Current.CancellationToken);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"[{LlamaTranslator.LoadedBackend}] {stopwatch.ElapsedMilliseconds} ms  {source} -> {result.Text}");
        Assert.Contains(expectedFragment, result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Translate", result.Text); // no echoed instruction
    }

    [Fact]
    public async Task Unsupported_target_language_throws()
    {
        var translator = fixture.Require();

        await Assert.ThrowsAsync<TranslationException>(
            () => translator.TranslateAsync("Hello", "sk", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Knows_which_targets_it_supports()
    {
        Assert.True(LlamaTranslator.SupportsTarget("uk"));
        Assert.False(LlamaTranslator.SupportsTarget("ro"));
    }
}
