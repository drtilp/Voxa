using System.IO;
using Voxa.Core.Asr;
using Voxa.Engines.SherpaOnnx;

namespace Voxa.Wpf.Transcription;

/// <summary>A single-file model (translation .gguf, VAD .onnx) found in the models folder or picked by the user.</summary>
public sealed record ModelFile(string Name, string Path)
{
    public static ModelFile FromPath(string path) => new(System.IO.Path.GetFileNameWithoutExtension(path), path);
}

/// <summary>
/// Finds the models in one folder: speech recognition (sherpa-onnx folders), translation (.gguf)
/// and voice activity detection (.onnx with "vad" in the name). The user can point it at any folder;
/// by default it is the "models" folder next to Voxa.exe, which the installer creates.
/// </summary>
public sealed class ModelCatalog(string modelsFolder)
{
    // Models are a few levels deep at most (models/translation/hy-mt2-1.8b/x.gguf). The limit keeps a
    // wrongly chosen folder such as C:\ from scanning the whole disk.
    private static readonly EnumerationOptions Search = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 3,
        IgnoreInaccessible = true,
    };

    public string ModelsFolder { get; } = modelsFolder;

    /// <summary>Saved as the selected model path when the cloud model is chosen (not a real folder).</summary>
    public const string CloudModelPath = "voxa-cloud";

    /// <summary>
    /// Speech recognition on the cloud server; offered next to the local models. The server loads one ASR model,
    /// the one its image ships with (src/Voxa.Api/Dockerfile).
    /// </summary>
    public static AsrModelOption CloudModel { get; } = new("Parakeet TDT 0.6B v3 (int8)", AsrModelLocation.Remote, CloudModelPath);

    /// <summary>
    /// Release: &lt;install dir&gt;\models. Debug: VOXA_MODELS env var or a "models" folder above the exe (repo/models),
    /// so the app run from the IDE finds the models of the repo.
    /// </summary>
    public static string DefaultModelsFolder { get; } = FindDefaultModelsFolder();

    public static ModelCatalog For(string? folder) =>
        new(string.IsNullOrWhiteSpace(folder) ? DefaultModelsFolder : folder);

    public bool Exists => Directory.Exists(ModelsFolder);

    /// <summary>models\lid: Whisper models for spoken language identification (not offered as ASR models).</summary>
    public string LanguageIdFolder => Path.Combine(ModelsFolder, "lid");

    public IReadOnlyList<AsrModelOption> DiscoverAsrModels()
    {
        if (!Exists) return [];

        return Directory.EnumerateDirectories(ModelsFolder, "*", Search)
            .Where(dir => !IsInside(dir, LanguageIdFolder))
            .Select(dir => SherpaModelDescriptor.Detect(dir))
            .OfType<SherpaModelDescriptor>()
            // Parakeet first: faster and more accurate for uk/ru/cs than Whisper (see Rešerše ASR Modelů)
            .OrderBy(d => d.Kind == SherpaModelKind.NemoTransducer ? 0 : 1)
            .ThenBy(d => d.Name)
            .Select(ToOption)
            .ToList();
    }

    /// <summary>Whisper folders under models\lid, "small" first (29/30 languages right; tiny and base confuse
    /// Slovak / Polish / Ukrainian).</summary>
    public IReadOnlyList<string> DiscoverLanguageIdModels() =>
        Directory.Exists(LanguageIdFolder)
            ? Directory.EnumerateDirectories(LanguageIdFolder, "*", Search)
                .Where(dir => SherpaModelDescriptor.Detect(dir) is { Kind: SherpaModelKind.Whisper })
                .OrderBy(dir => dir.Contains("small", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(dir => dir)
                .ToList()
            : [];

    public IReadOnlyList<ModelFile> DiscoverTranslationModels() => FindFiles("*.gguf", _ => true);

    public IReadOnlyList<ModelFile> DiscoverVadModels() =>
        FindFiles("*.onnx", path => Path.GetFileName(path).Contains("vad", StringComparison.OrdinalIgnoreCase));

    public static AsrModelOption? TryCreateOption(string path) =>
        SherpaModelDescriptor.Detect(path) is { } descriptor ? ToOption(descriptor) : null;

    public static SherpaModelDescriptor Describe(AsrModelOption option) =>
        SherpaModelDescriptor.Detect(option.FilePath ?? throw new InvalidOperationException($"{option.Name} has no files."))
        ?? throw new InvalidOperationException($"Model files of {option.Name} not found in {option.FilePath}.");

    private IReadOnlyList<ModelFile> FindFiles(string pattern, Func<string, bool> filter) =>
        Exists
            ? Directory.EnumerateFiles(ModelsFolder, pattern, Search).Where(filter).Order().Select(ModelFile.FromPath).ToList()
            : [];

    private static bool IsInside(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static AsrModelOption ToOption(SherpaModelDescriptor d) => new(d.Name, AsrModelLocation.Local, d.Directory);

    private static string FindDefaultModelsFolder()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("VOXA_MODELS") is { Length: > 0 } fromEnv)
            return fromEnv;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory).Parent; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "models");
            if (Directory.Exists(candidate)) return candidate;
        }
#endif
        return Path.Combine(AppContext.BaseDirectory, "models");
    }
}
