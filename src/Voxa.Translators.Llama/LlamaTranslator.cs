using System.Diagnostics;
using System.Text;
using Voxa.Core.Translation;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using LLama.Transformers;

namespace Voxa.Translators.Llama;

/// <param name="ModelPath">GGUF file, e.g. Hy-MT2-1.8B-Q8_0.gguf.</param>
/// <param name="GpuLayers">Layers offloaded to the GPU; a 1.8B model has ~32, so the default puts all of it there.</param>
public sealed record LlamaTranslatorOptions(string ModelPath, int GpuLayers = 999, uint ContextSize = 1024);

/// <summary>
/// Local LLM translation (Tencent Hy-MT2 by default) through LLamaSharp / llama.cpp.
/// </summary>
/// <remarks>
/// Hy-MT2 is a chat model: the request is the model's own prompt ("Translate the following text into X ...")
/// wrapped in its chat template. Greedy decoding keeps subtitles stable (same sentence → same translation).
/// One decode at a time: a second request would only compete for the same GPU.
/// </remarks>
public sealed class LlamaTranslator : ITranslator
{
    // Hy-MT2 supported languages (model card); target names must be English names in an English prompt.
    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English", ["cs"] = "Czech", ["uk"] = "Ukrainian", ["ru"] = "Russian", ["de"] = "German",
        ["pl"] = "Polish", ["fr"] = "French", ["es"] = "Spanish", ["it"] = "Italian", ["pt"] = "Portuguese",
        ["nl"] = "Dutch", ["tr"] = "Turkish", ["zh"] = "Chinese", ["ja"] = "Japanese", ["ko"] = "Korean",
    };

    private readonly LLamaWeights _weights;
    private readonly ModelParams _parameters;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private LlamaTranslator(LLamaWeights weights, ModelParams parameters, string name)
    {
        _weights = weights;
        _parameters = parameters;
        Name = name;
    }

    public string Name { get; }

    /// <summary>Which native backend llama.cpp ended up using (Vulkan / CPU) - useful in logs and the UI.</summary>
    public static string? LoadedBackend { get; private set; }

    public static bool SupportsTarget(string language) => LanguageNames.ContainsKey(language);

    /// <summary>Loads the model (seconds; ~2 GB of VRAM for the 1.8B Q8_0) - call off the UI thread.</summary>
    public static async Task<LlamaTranslator> LoadAsync(LlamaTranslatorOptions options, CancellationToken ct = default)
    {
        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("Translation model not found.", options.ModelPath);

        ConfigureNativeLibrary();
        var parameters = new ModelParams(options.ModelPath)
        {
            ContextSize = options.ContextSize,
            GpuLayerCount = options.GpuLayers,
        };
        var weights = await LLamaWeights.LoadFromFileAsync(parameters, ct);
        return new LlamaTranslator(weights, parameters, Path.GetFileNameWithoutExtension(options.ModelPath));
    }

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken ct = default)
    {
        if (!LanguageNames.TryGetValue(targetLanguage, out var targetName))
            throw new TranslationException($"{Name} can't translate into '{targetLanguage}'.");

        // Prompt from the Hy-MT2 model card ("Default Translation", English variant).
        var instruction = $"Translate the following text into {targetName}. Note that you should only output the " +
                          $"translated result without any additional explanation:\n\n{text}";

        var template = new LLamaTemplate(_weights) { AddAssistant = true };
        template.Add("user", instruction);
        var prompt = PromptTemplateTransformer.ToModelPrompt(template);

        var inference = new InferenceParams
        {
            MaxTokens = Math.Clamp(text.Length * 2, 64, 512), // translations are rarely much longer than the source
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0f, RepeatPenalty = 1.05f },
        };

        await _gate.WaitAsync(ct);
        try
        {
            // Stateless: a fresh KV cache per sentence (tiny for a 1k context), no history leaking between lines.
            var executor = new StatelessExecutor(_weights, _parameters);
            var output = new StringBuilder();
            await foreach (var piece in executor.InferAsync(prompt, inference, ct))
                output.Append(piece);

            return new TranslationResult(Clean(output.ToString()), sourceLanguage);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Chat models sometimes echo end-of-turn markers or wrap the answer in quotes.
    private static string Clean(string output)
    {
        var text = output.Trim();
        int marker = text.IndexOf("<|", StringComparison.Ordinal);
        if (marker >= 0) text = text[..marker].TrimEnd();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
        return text;
    }

    private static int _configured;

    private static void ConfigureNativeLibrary()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1) return;

        NativeLibraryConfig.All
            .WithSearchDirectories(HostNativeDirectories())
            .WithVulkan()
            .WithAutoFallback() // no Vulkan -> CPU
            .WithLogCallback((level, message) =>
            {
                if (level >= LLamaLogLevel.Warning) Debug.Write($"[llama.cpp {level}] {message}");
            });
        NativeApi.llama_empty_call(); // load now, so the chosen backend is known
        LoadedBackend = NativeApi.llama_supports_gpu_offload() ? "GPU (Vulkan)" : "CPU";
    }

    // Single-file publish extracts native libraries to %TEMP%\.net\<app>\<hash>\runtimes\..., but LLamaSharp only
    // looks next to the exe and finds nothing (no translation at all). The .NET host lists the extraction folder
    // in NATIVE_DLL_SEARCH_DIRECTORIES; in a normal (folder) publish that is just the app folder again.
    private static IEnumerable<string> HostNativeDirectories() =>
        (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists);

    // Waits for a running translation: freeing the weights mid-decode would crash in native code.
    public void Dispose()
    {
        _gate.Wait();
        _weights.Dispose();
        _gate.Dispose();
    }
}
