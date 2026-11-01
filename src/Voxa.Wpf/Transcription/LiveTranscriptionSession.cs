using System.Diagnostics;
using Voxa.Core.Asr;
using Voxa.Core.LanguageId;
using Voxa.Core.Pipeline;
using Voxa.Engines.SherpaOnnx;
using Voxa.Wpf.Audio;

namespace Voxa.Wpf.Transcription;

/// <summary>
/// One live run: system audio or microphone → a transcriber: <see cref="Local"/> (<see cref="TranscriptionPipeline"/>,
/// new Silero VAD per run, shared engine) or <see cref="Remote"/> (Voxa Cloud).
/// Create on the UI thread; read <see cref="ReadSegmentsAsync"/> there too and no Dispatcher is needed.
/// </summary>
public sealed class LiveTranscriptionSession : IAsyncDisposable
{
    private static readonly TimeSpan SilenceTick = TimeSpan.FromMilliseconds(200);

    private readonly WasapiAudioSource _source;
    private readonly ITranscriber _transcriber;
    private readonly Timer _silenceFiller;
    private readonly float[] _silenceChunk = new float[(int)(WasapiAudioSource.TargetSampleRate * SilenceTick.TotalSeconds)];
    private long _lastAudioTimestamp = Stopwatch.GetTimestamp();

    /// <param name="transcriber">Owned by the session (disposed with it).</param>
    private LiveTranscriptionSession(ITranscriber transcriber, AudioInput input)
    {
        _transcriber = transcriber;
        try
        {
            _source = WasapiAudioSource.Create(input);
        }
        catch
        {
            _ = transcriber.DisposeAsync(); // no device: a remote transcriber would otherwise keep connecting
            throw;
        }
        _source.SamplesAvailable += OnSamplesAvailable;
        _source.Stopped += OnSourceStopped;
        _source.Start();
        _silenceFiller = new Timer(OnSilenceTick, null, SilenceTick, SilenceTick);
    }

    /// <param name="languageIdentifier">Optional; shared like the engine (not disposed here).</param>
    public static LiveTranscriptionSession Local(IAsrEngine engine, string vadModelPath, AudioInput input,
        ILanguageIdentifier? languageIdentifier = null) =>
        new(new TranscriptionPipeline(new SileroVoiceActivityDetector(vadModelPath), engine,
            languageIdentifier: languageIdentifier), input);

    /// <summary>VAD, recognition and language identification run on the server.</summary>
    public static LiveTranscriptionSession Remote(ITranscriber remote, AudioInput input) => new(remote, input);

    public long DroppedChunks => _transcriber.DroppedChunks;

    public IAsyncEnumerable<TranscriptSegment> ReadSegmentsAsync(CancellationToken ct) => _transcriber.ReadSegmentsAsync(ct);

    // Capture thread
    private void OnSamplesAvailable(ReadOnlySpan<float> samples)
    {
        Volatile.Write(ref _lastAudioTimestamp, Stopwatch.GetTimestamp());
        _transcriber.TryWriteAudio(samples);
    }

    // Timer thread: loopback sends nothing while nothing plays, so the VAD (here or on the server) would never see
    // the end of a sentence.
    // (A microphone sends data all the time, so for it this never fires.)
    private void OnSilenceTick(object? state)
    {
        var idle = Stopwatch.GetElapsedTime(Volatile.Read(ref _lastAudioTimestamp));
        if (idle < SilenceTick) return;

        Volatile.Write(ref _lastAudioTimestamp, Stopwatch.GetTimestamp());
        _transcriber.TryWriteAudio(_silenceChunk);
    }

    private void OnSourceStopped(object? sender, Exception? error) => _transcriber.Complete();

    public async ValueTask DisposeAsync()
    {
        await _silenceFiller.DisposeAsync();
        _source.SamplesAvailable -= OnSamplesAvailable;
        _source.Stopped -= OnSourceStopped;
        _source.Dispose();
        await _transcriber.DisposeAsync();
    }
}
