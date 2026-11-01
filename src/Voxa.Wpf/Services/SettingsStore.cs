using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voxa.Wpf.Audio;

namespace Voxa.Wpf.Services;

public enum TranslatorKind
{
    Off,
    Azure,
    DeepL,
    Local,

    /// <summary>Voxa Cloud: the translation model on our server (Settings → Cloud server).</summary>
    Cloud,
}

/// <summary>Persisted user settings. Immutable: change with <c>with</c> and save.</summary>
public sealed record AppSettings
{
    public string TargetLanguage { get; init; } = "en";
    public TranslatorKind Translator { get; init; } = TranslatorKind.Azure; // the app is mainly for translation
    public bool ShowOriginal { get; init; }
    public string? AzureRegion { get; init; } = "westeurope";

    /// <summary>DPAPI-encrypted (current Windows user), base64. Never stored in plain text.</summary>
    public string? AzureKeyProtected { get; init; }

    /// <summary>DPAPI-encrypted like the Azure key.</summary>
    public string? DeepLKeyProtected { get; init; }

    /// <summary>Voxa Cloud server (speech recognition and translation on our server).</summary>
    public string? CloudUrl { get; init; }

    /// <summary>Token for the cloud server (the sign-in will store it here); DPAPI-encrypted like the other keys.</summary>
    public string? CloudKeyProtected { get; init; }

    public AudioInput AudioInput { get; init; } = AudioInput.SystemAudio;

    /// <summary>Folder searched for models. Null = <see cref="Transcription.ModelCatalog.DefaultModelsFolder"/>.</summary>
    public string? ModelsFolder { get; init; }

    /// <summary>Selected speech recognition (ASR) model folder.</summary>
    public string? ModelPath { get; init; }

    /// <summary>Selected local translation model (.gguf). Null = the first one found.</summary>
    public string? TranslationModelPath { get; init; }

    /// <summary>Selected voice activity detection model (.onnx). Null = the first one found.</summary>
    public string? VadModelPath { get; init; }

    /// <summary>ASR model folders added from disk outside the models folder; they stay in the list after a restart.</summary>
    public IReadOnlyList<string> CustomModelPaths { get; init; } = [];

    /// <summary>True once the installer's model choice or the first-run wizard has been applied.</summary>
    public bool SetupCompleted { get; init; }

    public FeatureFlags Features { get; init; } = new();

    /// <summary>Ask GitHub Releases for a newer version a few seconds after start.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>"Skip this version" in the update banner; a newer one is offered again.</summary>
    public string? SkippedUpdateVersion { get; init; }
}

/// <summary>
/// Switches for features that are being tried out, instead of a test branch. Edit settings.json or Settings → Experimental.
/// </summary>
/// <param name="FirstRunWizard">Model choice in the app on first start (variant A). Variant B is the installer page,
/// switched by <c>ModelSelectionPage</c> in installer/Voxa.iss.</param>
public sealed record FeatureFlags(bool FirstRunWizard = false);

/// <summary>%APPDATA%\Voxa\settings.json (Voxa-Dev in the Dev channel)</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Extra entropy: another app running as the same user can't decrypt the blob without knowing it.
    private static readonly byte[] Entropy = "Voxa.AzureKey.v1"u8.ToArray();

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.DataFolderName, "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AppSettings(); // corrupted file -> defaults rather than a crash at startup
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Write to a temp file and swap, so a crash mid-write can't leave a half-written settings file.
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
    }

    public static string Protect(string secret) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret)) return null;
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null; // other user / other machine: the key has to be entered again
        }
    }
}
