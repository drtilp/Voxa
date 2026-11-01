namespace Voxa.Wpf.Audio;

public interface IAudioRecorder : IDisposable
{
    bool IsRecording { get; }
    string RecordingsFolder { get; }

    /// <summary>Raised on start and stop. Can come from a non-UI thread.</summary>
    event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    /// <returns>Path of the file being written.</returns>
    string Start();
    void Stop();
}

public sealed class RecordingStateChangedEventArgs(bool isRecording, string? filePath, Exception? error) : EventArgs
{
    public bool IsRecording { get; } = isRecording;
    public string? FilePath { get; } = filePath;
    public Exception? Error { get; } = error;
}
