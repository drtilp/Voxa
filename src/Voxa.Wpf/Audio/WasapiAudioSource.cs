using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Voxa.Wpf.Audio;

public delegate void SamplesAvailableHandler(ReadOnlySpan<float> samples);

public enum AudioInput
{
    /// <summary>What is playing on the default output device (videos, calls, ...).</summary>
    SystemAudio,

    /// <summary>The default recording device (Windows sound settings).</summary>
    Microphone,
}

/// <summary>
/// WASAPI capture converted to 16 kHz mono float - the format the VAD and ASR models expect.
/// </summary>
/// <remarks>
/// NAudio push → pull bridge: DataAvailable pushes raw bytes into a <see cref="BufferedWaveProvider"/>,
/// then everything available is pulled through the WDL resampler (proper low-pass, no aliasing).
/// Loopback delivers no data while nothing plays - see LiveTranscriptionSession for the silence filler.
/// </remarks>
public sealed class WasapiAudioSource : IDisposable
{
    public const int TargetSampleRate = 16_000;

    private readonly WasapiCapture _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _resampled;
    private readonly int _channels;
    private readonly float[] _readBuffer;
    private readonly float[] _mono;

    private WasapiAudioSource(WasapiCapture capture)
    {
        _capture = capture;
        var format = _capture.WaveFormat;
        _channels = format.Channels;
        _buffer = new BufferedWaveProvider(format)
        {
            ReadFully = false, // Read returns only what was captured, never pads with zeros
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true,
        };
        _resampled = new WdlResamplingSampleProvider(_buffer.ToSampleProvider(), TargetSampleRate);
        _readBuffer = new float[TargetSampleRate / 10 * _channels]; // 100 ms per read
        _mono = new float[TargetSampleRate / 10];

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    /// <summary>Uses the device that is default right now (Windows sound settings).</summary>
    public static WasapiAudioSource Create(AudioInput input) => input switch
    {
        AudioInput.SystemAudio => new WasapiAudioSource(new WasapiLoopbackCapture()),
        AudioInput.Microphone => new WasapiAudioSource(new WasapiCapture(
            new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console))),
        _ => throw new ArgumentOutOfRangeException(nameof(input), input, null),
    };

    /// <summary>Raised on the NAudio capture thread. Handlers must not block.</summary>
    public event SamplesAvailableHandler? SamplesAvailable;

    /// <summary>Capture ended (device unplugged, exclusive mode, ...). Error is null on a normal stop.</summary>
    public event EventHandler<Exception?>? Stopped;

    public void Start() => _capture.StartRecording();

    public void Stop() => _capture.StopRecording();

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        int read;
        while ((read = _resampled.Read(_readBuffer, 0, _readBuffer.Length)) > 0)
        {
            int frames = read / _channels;
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0;
                for (int ch = 0; ch < _channels; ch++)
                    sum += _readBuffer[frame * _channels + ch];
                _mono[frame] = sum / _channels;
            }
            SamplesAvailable?.Invoke(_mono.AsSpan(0, frames));
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) => Stopped?.Invoke(this, e.Exception);

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose(); // stops and joins the capture thread
    }
}
