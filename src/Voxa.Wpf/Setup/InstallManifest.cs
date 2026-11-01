using System.IO;
using System.Text.Json;
using Voxa.Wpf.Transcription;

namespace Voxa.Wpf.Setup;

/// <summary>
/// Model choice made in the installer (variant B), written by installer/Voxa.iss as
/// &lt;install dir&gt;\install.json. The installer runs elevated, possibly as another account, so it can't
/// write the user's %APPDATA% settings; the app applies this file once, on its first start.
/// </summary>
public sealed record InstallManifest(
    string? ModelsFolder,
    string? Asr,
    string? AsrPath,
    string? Translation,
    string? TranslationPath,
    string? Vad,
    string? VadPath)
{
    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "install.json");

    public static InstallManifest? TryLoad()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(FilePath))
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // a broken manifest only means "no preselection"
        }
    }

    public SetupChoice ToChoice()
    {
        var folder = string.IsNullOrWhiteSpace(ModelsFolder) ? ModelCatalog.DefaultModelsFolder : ModelsFolder;
        var (asr, translation, vad) = (Pick(Asr, ModelRole.SpeechRecognition), Pick(Translation, ModelRole.Translation),
            Pick(Vad, ModelRole.VoiceActivity));
        return new SetupChoice(
            folder,
            ModelOffers.ResolvePath(asr, folder, AsrPath),
            ModelOffers.ResolvePath(translation, folder, TranslationPath),
            ModelOffers.ResolvePath(vad, folder, VadPath),
            translation.IsAvailable ? translation.Translator : null,
            ModelOffers.Downloads(asr, translation, vad));
    }

    private static ModelOffer Pick(string? id, ModelRole role) =>
        ModelOffers.Find(id) is { } offer && offer.Role == role ? offer : ModelOffers.Default(role);
}
