using System.IO;
using Voxa.Wpf.Services;
using Voxa.Wpf.Transcription;

namespace Voxa.Wpf.Setup;

public enum ModelRole
{
    SpeechRecognition,
    Translation,
    VoiceActivity,
}

public enum ModelSource
{
    /// <summary>One of our models; downloaded into the models folder.</summary>
    Download,

    /// <summary>The user's own file or folder.</summary>
    Custom,

    /// <summary>Runs on a server (Voxa Cloud, DeepL, Azure); nothing to download.</summary>
    Remote,

    /// <summary>The role is switched off (e.g. no translation).</summary>
    None,
}

/// <summary>One choice in the model wizard / installer page.</summary>
/// <param name="Id">Stable id, also written by the installer into install.json.</param>
/// <param name="RelativePath">Where a <see cref="ModelSource.Download"/> model lives inside the models folder.</param>
/// <param name="Translator">Translator to switch to when this translation choice is picked.</param>
/// <param name="IsAvailable">False for choices whose backend doesn't exist yet: shown, selectable, does nothing.</param>
public sealed record ModelOffer(
    string Id,
    ModelRole Role,
    ModelSource Source,
    string Title,
    string Description,
    string? Size = null,
    bool IsRecommended = false,
    string? RelativePath = null,
    TranslatorKind? Translator = null,
    bool IsAvailable = true)
{
    public string SourceLabel => Source switch
    {
        ModelSource.Download => "Local",
        ModelSource.Custom => "Own",
        ModelSource.Remote => "Cloud",
        _ => "",
    };
}

/// <summary>
/// What we offer. Keep the ids in sync with installer/Voxa.iss.
/// Later this list comes from a models.json next to the releases, so new models don't need a new app version.
/// </summary>
public static class ModelOffers
{
    public static IReadOnlyList<ModelOffer> All { get; } =
    [
        // Speech recognition
        new("parakeet-tdt-0.6b-v3", ModelRole.SpeechRecognition, ModelSource.Download,
            "Parakeet TDT 0.6B v3",
            "25 European languages incl. Czech, Ukrainian and Russian. Fast and accurate on the CPU.",
            "≈ 670 MB", IsRecommended: true, RelativePath: "parakeet-tdt-0.6b-v3"),
        new("whisper-large-v3-turbo", ModelRole.SpeechRecognition, ModelSource.Download,
            "Whisper large-v3 turbo",
            "99 languages. About 4× slower on the CPU and may invent text during silence.",
            "≈ 1.0 GB", RelativePath: "whisper-large-v3-turbo"),
        new("custom-asr", ModelRole.SpeechRecognition, ModelSource.Custom,
            "Own model…", "A sherpa-onnx model folder on this PC (encoder/decoder .onnx + tokens.txt)."),
        new("cloud-asr", ModelRole.SpeechRecognition, ModelSource.Remote,
            "Parakeet TDT 0.6B v3 (cloud)", "Recognition runs on our server, nothing to download. Needs a sign-in."),

        // Translation
        new("hy-mt2-1.8b", ModelRole.Translation, ModelSource.Download,
            "Hy-MT2 1.8B",
            "33 languages, runs offline on the GPU. About 0.4 s per sentence on an RTX 4060.",
            "≈ 1.9 GB", IsRecommended: true, RelativePath: @"translation\hy-mt2-1.8b\Hy-MT2-1.8B-Q8_0.gguf",
            Translator: TranslatorKind.Local),
        new("deepl", ModelRole.Translation, ModelSource.Remote,
            "DeepL API", "Your own DeepL key (Free plan: 500,000 characters / month). Enter it in Settings.",
            Translator: TranslatorKind.DeepL),
        new("azure", ModelRole.Translation, ModelSource.Remote,
            "Azure Translator", "Your own Azure key (F0: 2M characters / month). Enter it in Settings.",
            Translator: TranslatorKind.Azure),
        new("cloud-translation", ModelRole.Translation, ModelSource.Remote,
            "Hy-MT2 1.8B (cloud)", "Translation runs on our server, nothing to download. Needs a sign-in.",
            Translator: TranslatorKind.Cloud),
        new("custom-translation", ModelRole.Translation, ModelSource.Custom,
            "Own model…", "A .gguf translation model on this PC.", Translator: TranslatorKind.Local),
        new("no-translation", ModelRole.Translation, ModelSource.None,
            "No translation", "The overlay shows only the transcript.", Translator: TranslatorKind.Off),

        // Voice activity detection
        new("silero-vad", ModelRole.VoiceActivity, ModelSource.Download,
            "Silero VAD",
            "Finds where speech starts and ends, so sentences are recognized as a whole.",
            "≈ 2 MB", IsRecommended: true, RelativePath: @"vad\silero_vad.onnx"),
        new("custom-vad", ModelRole.VoiceActivity, ModelSource.Custom,
            "Own model…", "A voice activity detection .onnx model on this PC."),
    ];

    public static IReadOnlyList<ModelOffer> For(ModelRole role) => All.Where(o => o.Role == role).ToList();

    public static ModelOffer Default(ModelRole role) => All.First(o => o.Role == role && o.IsRecommended);

    public static ModelOffer? Find(string? id) => All.FirstOrDefault(o => o.Id == id);

    /// <summary>Ids of the chosen offers that the app downloads.</summary>
    public static IReadOnlyList<string> Downloads(params ModelOffer[] chosen) =>
        chosen.Where(o => o.Source == ModelSource.Download && ModelDownloads.Find(o.Id) is not null).Select(o => o.Id).ToList();

    /// <summary>File or folder the choice points to; null for remote / none, or a custom choice without a path.
    /// Voxa Cloud speech recognition is saved as <see cref="ModelCatalog.CloudModelPath"/>.</summary>
    public static string? ResolvePath(ModelOffer offer, string modelsFolder, string? customPath) => offer.Source switch
    {
        ModelSource.Download => Path.Combine(modelsFolder, offer.RelativePath!),
        ModelSource.Custom => string.IsNullOrWhiteSpace(customPath) ? null : customPath,
        ModelSource.Remote when offer.Role == ModelRole.SpeechRecognition => ModelCatalog.CloudModelPath,
        _ => null,
    };
}

/// <summary>The result of the wizard or of the installer page, applied to the settings in one go.</summary>
/// <param name="Downloads">Ids of <see cref="ModelDownloads"/> to download (chosen models that are ours).</param>
public sealed record SetupChoice(
    string? ModelsFolder,
    string? AsrModelPath,
    string? TranslationModelPath,
    string? VadModelPath,
    TranslatorKind? Translator,
    IReadOnlyList<string>? Downloads = null);
