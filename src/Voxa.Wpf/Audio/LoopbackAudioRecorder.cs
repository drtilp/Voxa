using System.IO;
using NAudio.Wave;

namespace Voxa.Wpf.Audio;

/// <summary>
/// Records what is currently playing on the default output device (WASAPI loopback) into a WAV file.
/// </summary>
/// <remarks>
/// DataAvailable runs on the NAudio capture thread, Start/Stop on the UI thread, so the
/// capture and writer are guarded by <see cref="_gate"/>.
/// WASAPI loopback delivers no data while nothing plays, so silent parts are missing from the file.
/// </remarks>
public sealed class LoopbackAudioRecorder : IAudioRecorder
{
    private readonly object _gate = new();
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _writer;
    private string? _currentPath;

    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    public string RecordingsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Voxa");

    public bool IsRecording
    {
        get { lock (_gate) return _capture is not null; }
    }

    public string Start()
    {
        string path;
        lock (_gate)
        {
            if (_capture is not null)
                throw new InvalidOperationException("Recording is already running.");

            Directory.CreateDirectory(RecordingsFolder);
            path = Path.Combine(RecordingsFolder, $"Voxa_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

            var capture = new WasapiLoopbackCapture();
            var writer = new WaveFileWriter(path, capture.WaveFormat);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            try
            {
                capture.StartRecording();
            }
            catch
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                writer.Dispose();
                capture.Dispose();
                File.Delete(path);
                throw;
            }

            _capture = capture;
            _writer = writer;
            _currentPath = path;
        }

        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(true, path, null));
        return path;
    }

    // Finishing (closing the file) happens in OnRecordingStopped, after the capture thread ends.
    public void Stop()
    {
        lock (_gate) _capture?.StopRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate) _writer?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        string? path;
        lock (_gate)
        {
            // Already cleaned up by Dispose (app exit while recording).
            if (sender != _capture) return;
            path = ReleaseCapture();
        }

        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(false, path, e.Exception));
    }

    private string? ReleaseCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose(); // stops and joins the capture thread
        }

        _writer?.Dispose(); // writes the final WAV header sizes
        var path = _currentPath;
        _capture = null;
        _writer = null;
        _currentPath = null;
        return path;
    }

    public void Dispose()
    {
        // On app exit the posted RecordingStopped would never run, so finish the file synchronously.
        lock (_gate) ReleaseCapture();
    }
}
