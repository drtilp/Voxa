using Voxa.Core.Asr;
using Voxa.Core.Vad;

namespace Voxa.Core.Tests;

/// <summary>Deterministic VAD: |sample| &gt; 0.1 is speech, a segment closes after <see cref="MinSilenceSamples"/> of silence.</summary>
internal sealed class ThresholdVad(int minSilenceSamples = 3_200) : IVoiceActivityDetector
{
    private readonly List<float> _speech = [];
    private long _position;
    private long _speechStart;
    private int _silenceRun;

    public int MinSilenceSamples => minSilenceSamples;
    public bool IsSpeechActive { get; private set; }
    public bool Disposed { get; private set; }

    public IReadOnlyList<SpeechSegment> Accept(ReadOnlySpan<float> samples)
    {
        var finished = new List<SpeechSegment>();
        foreach (var sample in samples)
        {
            bool loud = Math.Abs(sample) > 0.1f;
            if (loud && !IsSpeechActive)
            {
                IsSpeechActive = true;
                _speechStart = _position;
                _speech.Clear();
            }

            if (IsSpeechActive)
            {
                _speech.Add(sample);
                _silenceRun = loud ? 0 : _silenceRun + 1;
                if (_silenceRun >= minSilenceSamples)
                {
                    finished.Add(Close());
                }
            }
            _position++;
        }
        return finished;
    }

    public IReadOnlyList<SpeechSegment> Flush() => IsSpeechActive ? [Close()] : [];

    private SpeechSegment Close()
    {
        var trimmed = _speech.Take(_speech.Count - _silenceRun).ToArray(); // trailing silence isn't speech
        IsSpeechActive = false;
        _silenceRun = 0;
        return new SpeechSegment(_speechStart, trimmed);
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Returns "N samples" so tests can check exactly what audio reached the engine.</summary>
internal sealed class RecordingEngine : IAsrEngine
{
    public List<int> Calls { get; } = [];
    public Exception? ThrowOnCall { get; set; }

    public string Name => "fake";

    public Task<AsrResult> TranscribeAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken ct = default)
    {
        lock (Calls) Calls.Add(samples.Length);
        if (ThrowOnCall is not null) throw ThrowOnCall;
        var duration = TimeSpan.FromSeconds((double)samples.Length / sampleRate);
        return Task.FromResult(new AsrResult($"{samples.Length} samples", null, duration, TimeSpan.Zero));
    }

    public void Dispose() { }
}

internal static class Signal
{
    public static float[] Speech(double seconds) => Enumerable.Repeat(0.5f, Samples(seconds)).ToArray();

    public static float[] Silence(double seconds) => new float[Samples(seconds)];

    public static int Samples(double seconds) => (int)(seconds * 16_000);
}
