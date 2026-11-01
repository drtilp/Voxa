using System.IO;
using System.Text.Json;
using Voxa.Wpf.Services;
using Voxa.Wpf.Transcription;

namespace Voxa.Wpf.Setup;

/// <summary>
/// Model choice made in the installer (variant B), written by installer/Voxa.iss as
/// &lt;install dir&gt;\install.json. The installer runs elevated, possibly as another account, so it can't
/// write the user's %APPDATA% settings; the app applies this file once, on its first start.
/// </summary>
/// <param name="Later">"Select and download later": nothing is chosen or downloaded, the user picks in Settings.</param>
/// <param name="Asr">Ids of the ticked speech recognition models, in the order of the installer page.</param>
/// <param name="Translation">Ids of the ticked translation models; empty means no translation.</param>
/// <param name="Vad">Id of the voice activity model; empty when no local speech recognition was ticked.</param>
public sealed record InstallManifest(
    string? ModelsFolder,
    bool Later,
    string[]? Asr,
    string? AsrPath,
    string[]? Translation,
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

    /// <summary>
    /// All ticked local models are downloaded. Of the ticked models of a role, the app starts with the
    /// user's own model if there is one (they went to the trouble of picking it), else the first in the list.
    /// </summary>
    public SetupChoice ToChoice()
    {
        var folder = string.IsNullOrWhiteSpace(ModelsFolder) ? ModelCatalog.DefaultModelsFolder : ModelsFolder;
        if (Later) return new SetupChoice(folder, null, null, null, null);

        var asr = Pick(Asr, ModelRole.SpeechRecognition);
        var translation = Pick(Translation, ModelRole.Translation);
        var vad = Pick(Vad is null ? null : [Vad], ModelRole.VoiceActivity);
        var (activeAsr, activeTranslation, activeVad) = (Active(asr), Active(translation), Active(vad));
        return new SetupChoice(
            folder,
            activeAsr is null ? null : ModelOffers.ResolvePath(activeAsr, folder, AsrPath),
            activeTranslation is null ? null : ModelOffers.ResolvePath(activeTranslation, folder, TranslationPath),
            activeVad is null ? null : ModelOffers.ResolvePath(activeVad, folder, VadPath),
            activeTranslation is null ? TranslatorKind.Off : activeTranslation.IsAvailable ? activeTranslation.Translator : null,
            ModelOffers.Downloads([.. asr, .. translation, .. vad]));
    }

    private static ModelOffer? Active(IReadOnlyList<ModelOffer> offers) =>
        offers.FirstOrDefault(o => o.Source == ModelSource.Custom) ?? offers.FirstOrDefault();

    // Unknown ids and ids of another role are ignored (a newer installer than the app).
    private static List<ModelOffer> Pick(IEnumerable<string>? ids, ModelRole role) =>
        (ids ?? []).Select(ModelOffers.Find).OfType<ModelOffer>().Where(o => o.Role == role).DistinctBy(o => o.Id).ToList();
}
