namespace Voxa.Engines.SherpaOnnx.Tests;

/// <summary>Models live in the gitignored &lt;repo&gt;/models folder; tests skip when a model isn't downloaded.</summary>
internal static class ModelPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Models => Path.Combine(RepoRoot, "models");

    public static string WhisperTurbo(string file) => Path.Combine(Models, "whisper-large-v3-turbo", file);

    public static string ParakeetUltra(string file) => Path.Combine(Models, "parakeet-ultra", file);

    public static string WhisperSmallLid => Path.Combine(Models, "lid", "whisper-small");

    /// <summary>From sherpa-onnx spoken-language-identification-test-wavs (cs-czech.wav, ru-russian.wav...).</summary>
    public static string LidWav(string name) => Path.Combine(Models, "lid", "test_wavs", name);

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Voxa.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Voxa.sln not found above the test output folder.");
    }
}
