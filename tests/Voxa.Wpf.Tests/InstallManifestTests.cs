using System.Text.Json;
using Voxa.Wpf.Setup;
using Voxa.Wpf.Services;
using Voxa.Wpf.Transcription;

namespace Voxa.Wpf.Tests;

public class InstallManifestTests
{
    private const string Folder = @"C:\Voxa\models";

    private static InstallManifest Manifest(
        string[]? asr = null, string[]? translation = null, string? vad = "silero-vad", bool later = false,
        string? asrPath = null, string? translationPath = null, string? vadPath = null) =>
        new(Folder, later, asr, asrPath, translation, translationPath, vad, vadPath);

    [Fact]
    public void Later_chooses_and_downloads_nothing()
    {
        var choice = Manifest(["parakeet-tdt-0.6b-v3"], ["hy-mt2-1.8b"], later: true).ToChoice();

        Assert.Equal(Folder, choice.ModelsFolder);
        Assert.Null(choice.AsrModelPath);
        Assert.Null(choice.TranslationModelPath);
        Assert.Null(choice.VadModelPath);
        Assert.Null(choice.Translator);
        Assert.Null(choice.Downloads);
    }

    [Fact]
    public void Every_ticked_local_model_is_downloaded()
    {
        var choice = Manifest(["parakeet-tdt-0.6b-v3", "whisper-large-v3-turbo"], ["hy-mt2-1.8b"]).ToChoice();

        Assert.Equal(
            ["parakeet-tdt-0.6b-v3", "whisper-large-v3-turbo", "hy-mt2-1.8b", "silero-vad"],
            choice.Downloads);
    }

    [Fact]
    public void First_ticked_model_is_the_one_in_use()
    {
        var choice = Manifest(["whisper-large-v3-turbo", "parakeet-tdt-0.6b-v3"]).ToChoice();

        Assert.Equal(Path.Combine(Folder, "whisper-large-v3-turbo"), choice.AsrModelPath);
    }

    [Fact]
    public void Own_model_wins_over_the_others()
    {
        var choice = Manifest(["parakeet-tdt-0.6b-v3", "custom-asr"], asrPath: @"D:\my-model").ToChoice();

        Assert.Equal(@"D:\my-model", choice.AsrModelPath);
        Assert.Contains("parakeet-tdt-0.6b-v3", choice.Downloads!);
    }

    [Fact]
    public void Remote_only_downloads_nothing_and_uses_the_server()
    {
        var choice = Manifest(["cloud-asr"], ["cloud-translation"], vad: null).ToChoice();

        Assert.Equal(ModelCatalog.CloudModelPath, choice.AsrModelPath);
        Assert.Equal(TranslatorKind.Cloud, choice.Translator);
        Assert.Empty(choice.Downloads!);
        Assert.Null(choice.VadModelPath);
    }

    [Fact]
    public void No_ticked_translation_turns_translation_off()
    {
        var choice = Manifest(["parakeet-tdt-0.6b-v3"], translation: []).ToChoice();

        Assert.Equal(TranslatorKind.Off, choice.Translator);
        Assert.Null(choice.TranslationModelPath);
    }

    [Fact]
    public void Ids_of_another_role_or_unknown_ids_are_ignored()
    {
        var choice = Manifest(["hy-mt2-1.8b", "no-such-model", "parakeet-tdt-0.6b-v3"]).ToChoice();

        Assert.Equal(Path.Combine(Folder, "parakeet-tdt-0.6b-v3"), choice.AsrModelPath);
        Assert.DoesNotContain("hy-mt2-1.8b", choice.Downloads!);
    }

    [Fact]
    public void Manifest_written_by_the_installer_is_read()
    {
        const string json = """
            {
              "ModelsFolder": "C:\\Voxa\\models",
              "Later": false,
              "Asr": ["parakeet-tdt-0.6b-v3", "cloud-asr"],
              "AsrPath": "",
              "Translation": [],
              "TranslationPath": "",
              "Vad": "silero-vad",
              "VadPath": ""
            }
            """;

        var manifest = JsonSerializer.Deserialize<InstallManifest>(json)!;

        Assert.False(manifest.Later);
        Assert.Equal(["parakeet-tdt-0.6b-v3", "cloud-asr"], manifest.Asr!);
        Assert.Empty(manifest.Translation!);
    }
}
