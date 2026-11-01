using Voxa.Core.LanguageId;
using Voxa.Core.Remote;
using Voxa.Core.Translation;
using Voxa.Engines.SherpaOnnx;
using Voxa.Translators.Llama;
using Microsoft.Extensions.Options;

namespace Voxa.Api;

/// <summary>
/// Loads the models once in the background (tens of seconds) while Kestrel already answers /health with 503,
/// so the container platform sees the process alive and waits for the startup probe instead of killing it.
/// The engine, language identifier and translator are shared by all connections (each one serializes its own
/// native calls); only the VAD is per connection.
/// </summary>
public sealed class ModelHost(IOptions<ServerOptions> options, ILogger<ModelHost> logger) : BackgroundService
{
    private readonly ServerOptions _options = options.Value;
    private volatile Models? _models;

    public Models? Loaded => _models;

    public string? LoadError { get; private set; }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(async () =>
    {
        try
        {
            _models = await LoadAsync(stoppingToken);
            logger.LogInformation("Models loaded: {Info}", _models.Info);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LoadError = ex.Message;
            logger.LogError(ex, "Loading models from {Folder} failed", _options.ModelsFolder);
        }
    }, stoppingToken);

    private async Task<Models> LoadAsync(CancellationToken ct)
    {
        var root = _options.ModelsFolder;
        var runtime = new SherpaRuntimeOptions(_options.NumThreads, _options.Provider);
        // int8 is ~4x faster on CPU, but CUDA has no kernels for its quantized ops (they fall back to the CPU).
        bool preferInt8 = !_options.Provider.Equals("cuda", StringComparison.OrdinalIgnoreCase);

        var asr = FindAsrModel(Path.Combine(root, "asr"), preferInt8)
                  ?? throw new InvalidOperationException($"No sherpa-onnx model under {root}/asr.");
        logger.LogInformation("Loading ASR {Model} ({Provider}, {Threads} threads)", asr.Name, runtime.Provider, runtime.NumThreads);
        var engine = asr.CreateEngine(runtime);

        var vad = FindFiles(Path.Combine(root, "vad"), "*.onnx")
                      .FirstOrDefault(f => Path.GetFileName(f).Contains("vad", StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidOperationException($"No VAD model (*vad*.onnx) under {root}/vad.");

        // Optional models: a failure is logged and the server runs without them (ServerInfo shows null), because a
        // server that never gets healthy would only be restarted by the platform over and over.
        var lidFolder = Directory.Exists(Path.Combine(root, "lid"))
            ? Directory.EnumerateDirectories(Path.Combine(root, "lid")).Order().FirstOrDefault()
            : null;
        var languageIdentifier = lidFolder is null ? null : TryLoad("language ID", () => WhisperLanguageIdentifier.TryCreate(lidFolder));

        ITranslator? translator = null;
        if (FindFiles(Path.Combine(root, "translation"), "*.gguf").FirstOrDefault() is { } gguf)
        {
            logger.LogInformation("Loading translation model {Model}", Path.GetFileName(gguf));
            translator = await TryLoadAsync("translation", () => LlamaTranslator.LoadAsync(new LlamaTranslatorOptions(gguf), ct));
        }

        var backend = translator is null ? _options.Provider
            : $"{_options.Provider}, translation {(LlamaTranslator.LoadedBackend == "CPU" ? "cpu" : "gpu")}";
        var info = new ServerInfo(engine.Name, languageIdentifier is null ? null : Path.GetFileName(lidFolder),
            translator?.Name, backend);
        return new Models(engine, vad, languageIdentifier, translator, info);
    }

    private T? TryLoad<T>(string what, Func<T?> load) where T : class
    {
        try { return load(); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Loading the {What} model failed, continuing without it", what);
            return null;
        }
    }

    private async Task<T?> TryLoadAsync<T>(string what, Func<Task<T>> load) where T : class
    {
        try { return await load(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Loading the {What} model failed, continuing without it", what);
            return null;
        }
    }

    private SherpaModelDescriptor? FindAsrModel(string folder, bool preferInt8)
    {
        if (!Directory.Exists(folder)) return null;
        var models = Directory.EnumerateDirectories(folder)
            .Select(dir => SherpaModelDescriptor.Detect(dir, preferInt8))
            .OfType<SherpaModelDescriptor>()
            .ToList();
        if (_options.AsrModel is { Length: > 0 } wanted)
            return models.FirstOrDefault(m => Path.GetFileName(m.Directory).Equals(wanted, StringComparison.OrdinalIgnoreCase));
        // Parakeet first, like the app's ModelCatalog: faster and more accurate for uk/ru/cs than Whisper.
        return models.OrderBy(m => m.Kind == SherpaModelKind.NemoTransducer ? 0 : 1).ThenBy(m => m.Name).FirstOrDefault();
    }

    private static IEnumerable<string> FindFiles(string folder, string pattern) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories).Order() : [];

    public override void Dispose()
    {
        base.Dispose();
        _models?.Dispose();
    }
}

/// <param name="VadModelPath">The VAD is stateful, so every connection creates its own from this file.</param>
public sealed record Models(
    SherpaOnnxEngine Engine, string VadModelPath, ILanguageIdentifier? LanguageIdentifier, ITranslator? Translator,
    ServerInfo Info) : IDisposable
{
    public void Dispose()
    {
        Engine.Dispose();
        LanguageIdentifier?.Dispose();
        Translator?.Dispose();
    }
}
