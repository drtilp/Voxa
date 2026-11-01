using Voxa.Core.Asr;

namespace Voxa.Engines.SherpaOnnx.Tests;

/// <summary>Loads a model once per test class (loading takes tens of seconds). Null when the model is missing.</summary>
public abstract class EngineFixture : IDisposable
{
    protected EngineFixture(string[] requiredFiles, Func<IAsrEngine> create)
    {
        MissingFile = requiredFiles.FirstOrDefault(f => !File.Exists(f));
        if (MissingFile is null)
            Engine = create();
    }

    public IAsrEngine? Engine { get; }

    public string? MissingFile { get; }

    public IAsrEngine RequireEngine()
    {
        Assert.SkipUnless(Engine is not null, $"Model not downloaded: {MissingFile}");
        return Engine!;
    }

    public void Dispose() => Engine?.Dispose();
}

public sealed class WhisperTurboInt8Fixture() : EngineFixture(
    [ModelPaths.WhisperTurbo("turbo-encoder.int8.onnx"), ModelPaths.WhisperTurbo("turbo-decoder.int8.onnx"),
     ModelPaths.WhisperTurbo("turbo-tokens.txt")],
    () => SherpaOnnxEngine.Whisper("Whisper large-v3-turbo int8",
        ModelPaths.WhisperTurbo("turbo-encoder.int8.onnx"),
        ModelPaths.WhisperTurbo("turbo-decoder.int8.onnx"),
        ModelPaths.WhisperTurbo("turbo-tokens.txt"),
        runtime: new SherpaRuntimeOptions(NumThreads: Environment.ProcessorCount / 2)));

public sealed class ParakeetUltraInt8Fixture() : EngineFixture(
    [ModelPaths.ParakeetUltra("encoder.int8.onnx"), ModelPaths.ParakeetUltra("decoder.int8.onnx"),
     ModelPaths.ParakeetUltra("joiner.int8.onnx"), ModelPaths.ParakeetUltra("tokens.txt")],
    () => SherpaOnnxEngine.NemoTransducer("Parakeet Ultra int8",
        ModelPaths.ParakeetUltra("encoder.int8.onnx"),
        ModelPaths.ParakeetUltra("decoder.int8.onnx"),
        ModelPaths.ParakeetUltra("joiner.int8.onnx"),
        ModelPaths.ParakeetUltra("tokens.txt"),
        new SherpaRuntimeOptions(NumThreads: Environment.ProcessorCount / 2)));
