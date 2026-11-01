using Voxa.Core.Vad;
using SherpaOnnx;
using SherpaSpeechSegment = SherpaOnnx.SpeechSegment;
using SpeechSegment = Voxa.Core.Vad.SpeechSegment;

namespace Voxa.Engines.SherpaOnnx;

/// <summary><see cref="IVoiceActivityDetector"/> adapter over the Silero VAD model in sherpa-onnx.</summary>
public sealed class SileroVoiceActivityDetector : IVoiceActivityDetector
{
    private readonly VoiceActivityDetector _vad;

    public SileroVoiceActivityDetector(string modelPath, SileroVadOptions? options = null)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Silero VAD model not found.", modelPath);

        options ??= new SileroVadOptions();
        var config = new VadModelConfig();
        config.SampleRate = 16_000;
        config.SileroVad.Model = modelPath;
        config.SileroVad.Threshold = options.Threshold;
        config.SileroVad.MinSilenceDuration = (float)options.MinSilence.TotalSeconds;
        config.SileroVad.MinSpeechDuration = (float)options.MinSpeech.TotalSeconds;
        config.SileroVad.MaxSpeechDuration = (float)options.MaxSpeech.TotalSeconds;
        _vad = new VoiceActivityDetector(config, bufferSizeInSeconds: (float)options.MaxSpeech.TotalSeconds * 2);
    }

    public bool IsSpeechActive => _vad.IsSpeechDetected();

    public IReadOnlyList<SpeechSegment> Accept(ReadOnlySpan<float> samples)
    {
        _vad.AcceptWaveform(samples.ToArray());
        return DrainSegments();
    }

    public IReadOnlyList<SpeechSegment> Flush()
    {
        _vad.Flush();
        return DrainSegments();
    }

    private List<SpeechSegment> DrainSegments()
    {
        var segments = new List<SpeechSegment>();
        while (!_vad.IsEmpty())
        {
            SherpaSpeechSegment front = _vad.Front();
            segments.Add(new SpeechSegment(front.Start, front.Samples));
            _vad.Pop();
        }
        return segments;
    }

    public void Dispose() => _vad.Dispose();
}

/// <param name="MinSilence">Pause that ends a segment. Shorter = faster finals, more chopped sentences.</param>
/// <param name="MaxSpeech">Hard cut for long monologues (Parakeet/Whisper work best on &lt; 30 s).</param>
public sealed record SileroVadOptions(
    float Threshold = 0.5f,
    TimeSpan MinSilence = default,
    TimeSpan MinSpeech = default,
    TimeSpan MaxSpeech = default)
{
    public TimeSpan MinSilence { get; init; } = MinSilence == default ? TimeSpan.FromMilliseconds(400) : MinSilence;
    public TimeSpan MinSpeech { get; init; } = MinSpeech == default ? TimeSpan.FromMilliseconds(250) : MinSpeech;
    public TimeSpan MaxSpeech { get; init; } = MaxSpeech == default ? TimeSpan.FromSeconds(15) : MaxSpeech;
}
