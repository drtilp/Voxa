using System.Diagnostics;
using System.Runtime.InteropServices;
using Voxa.Core.Asr;
using SherpaOnnx;

namespace Voxa.Engines.SherpaOnnx;

/// <summary>
/// <see cref="IAsrEngine"/> adapter over sherpa-onnx <see cref="OfflineRecognizer"/>.
/// One class serves every offline model sherpa supports; only the config differs (see factory methods).
/// </summary>
/// <remarks>
/// Loading takes seconds to a minute (GBs of weights), so create the engine once and reuse it.
/// Decoding is a blocking native call: it runs on the thread pool and is limited by <see cref="_decodeGate"/>,
/// because every decode already uses <c>NumThreads</c> cores and parallel decodes would only fight for them.
/// </remarks>
public sealed class SherpaOnnxEngine : IAsrEngine
{
    private readonly OfflineRecognizer _recognizer;
    private readonly SemaphoreSlim _decodeGate;

    private SherpaOnnxEngine(string name, OfflineRecognizerConfig config, int maxParallelDecodes)
    {
        Name = name;
        _recognizer = new OfflineRecognizer(config);
        _decodeGate = new SemaphoreSlim(maxParallelDecodes, maxParallelDecodes);
    }

    public string Name { get; }

    /// <summary>Whisper (encoder + decoder). Empty language = automatic language detection.</summary>
    public static SherpaOnnxEngine Whisper(string name, string encoder, string decoder, string tokens,
        string language = "", SherpaRuntimeOptions? runtime = null)
    {
        runtime ??= new SherpaRuntimeOptions();
        var config = CreateConfig(tokens, runtime);
        config.ModelConfig.Whisper.Encoder = RequireFile(encoder);
        config.ModelConfig.Whisper.Decoder = RequireFile(decoder);
        config.ModelConfig.Whisper.Language = language;
        config.ModelConfig.Whisper.Task = "transcribe";
        return new SherpaOnnxEngine(name, config, runtime.MaxParallelDecodes);
    }

    /// <summary>NeMo transducer (Parakeet TDT): encoder + decoder + joiner, 128 mel features.</summary>
    public static SherpaOnnxEngine NemoTransducer(string name, string encoder, string decoder, string joiner,
        string tokens, SherpaRuntimeOptions? runtime = null)
    {
        runtime ??= new SherpaRuntimeOptions();
        var config = CreateConfig(tokens, runtime);
        config.FeatConfig.FeatureDim = 128;
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.Transducer.Encoder = RequireFile(encoder);
        config.ModelConfig.Transducer.Decoder = RequireFile(decoder);
        config.ModelConfig.Transducer.Joiner = RequireFile(joiner);
        return new SherpaOnnxEngine(name, config, runtime.MaxParallelDecodes);
    }

    public async Task<AsrResult> TranscribeAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken ct = default)
    {
        await _decodeGate.WaitAsync(ct);
        try
        {
            // The native decode can't be interrupted; cancellation only works before it starts.
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() => Decode(samples, sampleRate), ct);
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private AsrResult Decode(ReadOnlyMemory<float> samples, int sampleRate)
    {
        var stopwatch = Stopwatch.StartNew();

        // sherpa wants float[]; avoid the copy when the memory is a whole array.
        float[] buffer = MemoryMarshal.TryGetArray(samples, out var segment)
                         && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array
            : samples.ToArray();

        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(sampleRate, buffer);
        _recognizer.Decode(stream);
        var text = stream.Result.Text.Trim();

        return new AsrResult(text, Language: null,
            AudioDuration: TimeSpan.FromSeconds((double)buffer.Length / sampleRate),
            ProcessingTime: stopwatch.Elapsed);
    }

    private static OfflineRecognizerConfig CreateConfig(string tokens, SherpaRuntimeOptions runtime)
    {
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Tokens = RequireFile(tokens);
        config.ModelConfig.NumThreads = runtime.NumThreads;
        config.ModelConfig.Provider = runtime.Provider;
        config.ModelConfig.Debug = 0;
        return config;
    }

    // sherpa-onnx only logs a missing file and then crashes natively -> fail early with a .NET exception.
    private static string RequireFile(string path) =>
        File.Exists(path) ? path : throw new FileNotFoundException("Model file not found.", path);

    public void Dispose()
    {
        _recognizer.Dispose();
        _decodeGate.Dispose();
    }
}

/// <param name="Provider">"cpu" or "cuda" (CUDA needs the GPU build of the sherpa-onnx native libraries).</param>
public sealed record SherpaRuntimeOptions(int NumThreads = 4, string Provider = "cpu", int MaxParallelDecodes = 1);
