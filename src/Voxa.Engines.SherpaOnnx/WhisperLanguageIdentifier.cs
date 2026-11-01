using Voxa.Core.LanguageId;
using SherpaOnnx;

namespace Voxa.Engines.SherpaOnnx;

/// <summary>
/// <see cref="ILanguageIdentifier"/> over sherpa-onnx spoken language identification with a multilingual Whisper
/// model. Only 80-mel models work (tiny, base, small, medium); large-v3 / turbo use 128 mel bins and fail.
/// Measured on sherpa-onnx's 30 test clips (int8, 4 threads): tiny 24/30 (Slovak → Polish), base 27/30
/// (Polish → Ukrainian), small 29/30 (only Norwegian → Nynorsk), also from 3 s of audio; small ~0.4 s per clip.
/// </summary>
public sealed class WhisperLanguageIdentifier : ILanguageIdentifier
{
    private readonly SpokenLanguageIdentification _slid;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WhisperLanguageIdentifier(string encoder, string decoder, int numThreads = 2)
    {
        if (!File.Exists(encoder)) throw new FileNotFoundException("Whisper encoder not found.", encoder);
        if (!File.Exists(decoder)) throw new FileNotFoundException("Whisper decoder not found.", decoder);

        var config = new SpokenLanguageIdentificationConfig();
        config.Whisper.Encoder = encoder;
        config.Whisper.Decoder = decoder;
        config.NumThreads = numThreads;
        config.Provider = "cpu";
        _slid = new SpokenLanguageIdentification(config);
    }

    /// <summary>A Whisper folder (e.g. models/lid/whisper-tiny) with *-encoder / *-decoder .onnx; int8 preferred.</summary>
    public static WhisperLanguageIdentifier? TryCreate(string folder)
    {
        if (SherpaModelDescriptor.Detect(folder) is not { Kind: SherpaModelKind.Whisper } whisper) return null;
        return new WhisperLanguageIdentifier(whisper.Encoder, whisper.Decoder);
    }

    public async Task<string?> IdentifyAsync(ReadOnlyMemory<float> samples16kMono, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var samples = samples16kMono.ToArray();
            return await Task.Run(() =>
            {
                using var stream = _slid.CreateStream();
                stream.AcceptWaveform(16_000, samples);
                var language = _slid.Compute(stream).Lang;
                return string.IsNullOrEmpty(language) ? null : language;
            }, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Waits for a running identification: freeing the model mid-compute would crash in native code.
    public void Dispose()
    {
        _gate.Wait();
        _slid.Dispose();
        _gate.Dispose();
    }
}
