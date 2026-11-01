namespace Voxa.Engines.SherpaOnnx;

public enum SherpaModelKind
{
    Whisper,
    NemoTransducer,
}

/// <summary>
/// A sherpa-onnx model folder recognized by its file names, so the user can just point at a folder (or any
/// .onnx file in it) and the right engine config is built.
/// </summary>
public sealed record SherpaModelDescriptor(
    string Name, SherpaModelKind Kind, string Directory, string Encoder, string Decoder, string? Joiner, string Tokens)
{
    /// <param name="path">Model folder or any file inside it.</param>
    /// <param name="preferInt8">int8 is ~4x faster on CPU; fp32 is meant for GPU.</param>
    public static SherpaModelDescriptor? Detect(string path, bool preferInt8 = true)
    {
        var dir = System.IO.Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (dir is null || !System.IO.Directory.Exists(dir)) return null;

        var onnx = System.IO.Directory.GetFiles(dir, "*.onnx").Select(Path.GetFileName).OfType<string>().ToList();
        var tokens = System.IO.Directory.GetFiles(dir, "*tokens.txt").FirstOrDefault();
        if (tokens is null) return null;

        string? Pick(string part)
        {
            var candidates = onnx.Where(f => f.Contains(part, StringComparison.OrdinalIgnoreCase)).ToList();
            var int8 = candidates.FirstOrDefault(f => f.Contains(".int8.", StringComparison.OrdinalIgnoreCase));
            var fp32 = candidates.FirstOrDefault(f => !f.Contains(".int8.", StringComparison.OrdinalIgnoreCase));
            var chosen = preferInt8 ? int8 ?? fp32 : fp32 ?? int8;
            return chosen is null ? null : Path.Combine(dir, chosen);
        }

        var encoder = Pick("encoder");
        var decoder = Pick("decoder");
        if (encoder is null || decoder is null) return null;

        var joiner = Pick("joiner");
        var kind = joiner is null ? SherpaModelKind.Whisper : SherpaModelKind.NemoTransducer;
        var precision = encoder.Contains(".int8.", StringComparison.OrdinalIgnoreCase) ? "int8" : "fp32";
        var name = $"{Path.GetFileName(dir)} ({precision})";
        return new SherpaModelDescriptor(name, kind, dir, encoder, decoder, joiner, tokens);
    }

    /// <summary>Loads the model (seconds to a minute) - call off the UI thread.</summary>
    public SherpaOnnxEngine CreateEngine(SherpaRuntimeOptions? runtime = null) => Kind switch
    {
        SherpaModelKind.Whisper => SherpaOnnxEngine.Whisper(Name, Encoder, Decoder, Tokens, runtime: runtime),
        SherpaModelKind.NemoTransducer => SherpaOnnxEngine.NemoTransducer(Name, Encoder, Decoder, Joiner!, Tokens, runtime),
        _ => throw new NotSupportedException(Kind.ToString()),
    };
}
