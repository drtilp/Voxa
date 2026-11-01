namespace Voxa.Engines.SherpaOnnx.Tests;

public sealed class SherpaModelDescriptorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("voxa-models-").FullName;

    [Fact]
    public void Detects_transducer_and_prefers_int8()
    {
        var model = CreateModel("parakeet-ultra",
            "encoder.onnx", "encoder.int8.onnx", "decoder.onnx", "decoder.int8.onnx", "joiner.onnx", "joiner.int8.onnx", "tokens.txt");

        var descriptor = SherpaModelDescriptor.Detect(model);

        Assert.NotNull(descriptor);
        Assert.Equal(SherpaModelKind.NemoTransducer, descriptor.Kind);
        Assert.Equal("parakeet-ultra (int8)", descriptor.Name);
        Assert.EndsWith("encoder.int8.onnx", descriptor.Encoder);
        Assert.EndsWith("joiner.int8.onnx", descriptor.Joiner);
    }

    [Fact]
    public void Detects_whisper_fp32_when_requested()
    {
        var model = CreateModel("whisper-large-v3-turbo",
            "turbo-encoder.onnx", "turbo-encoder.int8.onnx", "turbo-decoder.onnx", "turbo-decoder.int8.onnx", "turbo-tokens.txt");

        var descriptor = SherpaModelDescriptor.Detect(model, preferInt8: false);

        Assert.NotNull(descriptor);
        Assert.Equal(SherpaModelKind.Whisper, descriptor.Kind);
        Assert.Null(descriptor.Joiner);
        Assert.EndsWith("turbo-encoder.onnx", descriptor.Encoder);
        Assert.Equal("whisper-large-v3-turbo (fp32)", descriptor.Name);
    }

    [Fact]
    public void Accepts_a_file_inside_the_model_folder()
    {
        var model = CreateModel("m", "encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt");

        Assert.NotNull(SherpaModelDescriptor.Detect(Path.Combine(model, "decoder.onnx")));
    }

    [Theory]
    [InlineData("encoder.onnx", "decoder.onnx")]          // no tokens
    [InlineData("encoder.onnx", "tokens.txt")]            // no decoder
    [InlineData("silero_vad.onnx")]                       // not an ASR model
    public void Returns_null_for_incomplete_folders(params string[] files) =>
        Assert.Null(SherpaModelDescriptor.Detect(CreateModel("x", files)));

    private string CreateModel(string name, params string[] files)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
        foreach (var file in files)
            File.WriteAllText(Path.Combine(dir, file), "");
        return dir;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
