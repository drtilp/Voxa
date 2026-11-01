using System.Threading.Channels;
using Voxa.Core.Asr;
using Voxa.Core.LanguageId;
using Voxa.Core.Vad;

namespace Voxa.Core.Pipeline;

public sealed record TranscriptionPipelineOptions
{
    /// <summary>How often a partial result is produced while someone keeps talking.</summary>
    public TimeSpan PartialInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// False = finals only. For recorded media processed faster than real time: nobody watches a sentence grow there,
    /// and every partial re-transcribes the sentence so far.
    /// </summary>
    public bool EmitPartials { get; init; } = true;

    /// <summary>Audio kept before the VAD reports speech, so partials don't miss the first syllable.</summary>
    public TimeSpan PreRoll { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Queued audio chunks before <see cref="TranscriptionPipeline.TryWriteAudio"/> starts dropping.</summary>
    public int InputCapacity { get; init; } = 512;

    /// <summary>
    /// Partials are skipped while more chunks than this wait in the queue. Every partial re-transcribes the whole
    /// sentence so far (cost grows with its length); when the engine falls behind, finals matter more.
    /// </summary>
    public int MaxBacklogForPartials { get; init; } = 3;

    /// <summary>Shorter finals are not worth a language guess (too little audio, mostly wrong).</summary>
    public TimeSpan MinLanguageIdDuration { get; init; } = TimeSpan.FromSeconds(1.5);
}

/// <summary>
/// Audio in, transcript segments out:
/// <code>
/// audio callback ─TryWriteAudio─► [input channel] ─► worker: VAD ─► IAsrEngine ─► [output channel] ─► ReadSegmentsAsync
/// </code>
/// One worker task owns the VAD (not thread-safe) and calls the engine sequentially, so partials can never
/// overtake the final of the same sentence. Producers only touch the channel and never block the audio thread.
/// </summary>
public sealed class TranscriptionPipeline : ITranscriber
{
    public const int SampleRate = 16_000;

    private readonly IVoiceActivityDetector _vad;
    private readonly IAsrEngine _engine;
    private readonly ILanguageIdentifier? _languageIdentifier;
    private readonly TranscriptionPipelineOptions _options;
    private readonly Channel<float[]> _input;
    private readonly Channel<TranscriptSegment> _output = Channel.CreateUnbounded<TranscriptSegment>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    // Worker-only state
    private readonly List<float> _preRoll = [];
    private readonly List<float> _currentSpeech = [];
    private long _currentSpeechStart;
    private long _samplesSinceLastPartial;
    private long _totalSamples;
    private long _sentenceId;

    private long _droppedChunks;

    /// <param name="vad">Owned by the pipeline (disposed with it), one per stream.</param>
    /// <param name="engine">Shared and expensive to load; not disposed by the pipeline.</param>
    /// <param name="languageIdentifier">Optional, shared like the engine: fills <see cref="TranscriptSegment.Language"/> of finals.</param>
    public TranscriptionPipeline(IVoiceActivityDetector vad, IAsrEngine engine, TranscriptionPipelineOptions? options = null,
        ILanguageIdentifier? languageIdentifier = null)
    {
        _vad = vad;
        _engine = engine;
        _languageIdentifier = languageIdentifier;
        _options = options ?? new TranscriptionPipelineOptions();
        _input = Channel.CreateBounded<float[]>(new BoundedChannelOptions(_options.InputCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait, // TryWrite returns false instead of waiting
        });
        _worker = Task.Run(RunAsync);
    }

    /// <summary>Chunks lost because the engine couldn't keep up (input queue full).</summary>
    public long DroppedChunks => Interlocked.Read(ref _droppedChunks);

    /// <summary>Completes when all audio is processed; faults when the engine throws.</summary>
    public Task Completion => _worker;

    /// <summary>Non-blocking, safe to call from an audio callback. Samples are copied.</summary>
    public bool TryWriteAudio(ReadOnlySpan<float> samples16kMono)
    {
        if (_input.Writer.TryWrite(samples16kMono.ToArray())) return true;
        Interlocked.Increment(ref _droppedChunks);
        return false;
    }

    /// <summary>Waits when the queue is full (backpressure) - for file/network sources, not audio callbacks.</summary>
    public ValueTask WriteAudioAsync(ReadOnlyMemory<float> samples16kMono, CancellationToken ct = default) =>
        _input.Writer.WriteAsync(samples16kMono.ToArray(), ct);

    /// <summary>No more audio: the worker flushes the VAD, emits the last final and completes the output.</summary>
    public void Complete() => _input.Writer.TryComplete();

    public IAsyncEnumerable<TranscriptSegment> ReadSegmentsAsync(CancellationToken ct = default) =>
        _output.Reader.ReadAllAsync(ct);

    private async Task RunAsync()
    {
        var ct = _cts.Token;
        try
        {
            await foreach (var chunk in _input.Reader.ReadAllAsync(ct))
                await ProcessChunkAsync(chunk, ct);

            foreach (var segment in _vad.Flush())
                await EmitFinalAsync(segment, ct);

            _output.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _output.Writer.TryComplete(ex); // the consumer's await foreach rethrows it
            throw;
        }
    }

    private async Task ProcessChunkAsync(float[] chunk, CancellationToken ct)
    {
        bool wasSpeech = _vad.IsSpeechActive;
        var finished = _vad.Accept(chunk);

        if (!wasSpeech && _vad.IsSpeechActive)
        {
            // Speech just started: begin with the pre-roll, the VAD needs a few hundred ms to decide.
            _currentSpeech.Clear();
            _currentSpeech.AddRange(_preRoll);
            _currentSpeechStart = _totalSamples - _preRoll.Count;
            _samplesSinceLastPartial = 0;
        }

        _totalSamples += chunk.Length;
        AppendPreRoll(chunk);

        foreach (var segment in finished)
            await EmitFinalAsync(segment, ct);

        if (!_vad.IsSpeechActive) return;

        _currentSpeech.AddRange(chunk);
        _samplesSinceLastPartial += chunk.Length;
        if (_options.EmitPartials
            && _samplesSinceLastPartial >= ToSamples(_options.PartialInterval)
            && _input.Reader.Count <= _options.MaxBacklogForPartials)
        {
            _samplesSinceLastPartial = 0;
            var result = await _engine.TranscribeAsync(_currentSpeech.ToArray(), SampleRate, ct);
            if (result.Text.Length > 0)
                await _output.Writer.WriteAsync(new TranscriptSegment(result.Text, IsFinal: false,
                    ToTime(_currentSpeechStart), ToTime(_currentSpeechStart + _currentSpeech.Count), _sentenceId), ct);
        }
    }

    private async Task EmitFinalAsync(SpeechSegment segment, CancellationToken ct)
    {
        _currentSpeech.Clear();
        _samplesSinceLastPartial = 0;

        // Language in parallel with the transcription (Whisper small ~0.4 s): the final isn't delayed by it.
        var languageTask = _languageIdentifier is not null && segment.Samples.Length >= ToSamples(_options.MinLanguageIdDuration)
            ? _languageIdentifier.IdentifyAsync(segment.Samples, ct)
            : Task.FromResult<string?>(null);
        var result = await _engine.TranscribeAsync(segment.Samples, SampleRate, ct);
        string? language = await languageTask;
        if (result.Text.Length == 0) language = null; // noise, not speech
        // Empty final still matters when partials were shown: it clears them. Skip only when nothing was said.
        await _output.Writer.WriteAsync(new TranscriptSegment(result.Text, IsFinal: true,
            ToTime(segment.StartSample), ToTime(segment.StartSample + segment.Samples.Length), _sentenceId++, language), ct);
    }

    private void AppendPreRoll(float[] chunk)
    {
        _preRoll.AddRange(chunk);
        int excess = _preRoll.Count - (int)ToSamples(_options.PreRoll);
        if (excess > 0) _preRoll.RemoveRange(0, excess);
    }

    private static long ToSamples(TimeSpan time) => (long)(time.TotalSeconds * SampleRate);

    private static TimeSpan ToTime(long samples) => TimeSpan.FromSeconds((double)samples / SampleRate);

    public async ValueTask DisposeAsync()
    {
        Complete();
        await _cts.CancelAsync();
        try { await _worker; }
        catch { /* faults were already reported through the output channel */ }
        _cts.Dispose();
        _vad.Dispose();
    }
}
