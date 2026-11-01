using System.Net.Http;
using Voxa.Core.Translation;
using Voxa.Remote;
using Voxa.Translators.Azure;
using Voxa.Translators.DeepL;
using Voxa.Translators.Llama;
using Voxa.Wpf.Services;

namespace Voxa.Wpf.Transcription;

/// <summary>Builds the <see cref="ITranslator"/> chosen in the settings (Strategy, like the ASR engines).</summary>
public static class TranslatorFactory
{
    // One HttpClient for the app lifetime: creating one per request exhausts sockets.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <param name="localModelPath">The .gguf chosen in Settings → Models; used only by <see cref="TranslatorKind.Local"/>.</param>
    /// <returns>Null with a reason when translation is off or not configured. Local models load for seconds.</returns>
    public static async Task<(ITranslator? Translator, string? Problem)> CreateAsync(AppSettings settings, string? localModelPath)
    {
        switch (settings.Translator)
        {
            case TranslatorKind.Azure:
                return SettingsStore.Unprotect(settings.AzureKeyProtected) is { Length: > 0 } azureKey
                    ? (new AzureTranslator(Http, new AzureTranslatorOptions(azureKey, settings.AzureRegion)), null)
                    : (null, "Azure Translator: enter the key below.");

            case TranslatorKind.DeepL:
                return SettingsStore.Unprotect(settings.DeepLKeyProtected) is { Length: > 0 } deepLKey
                    ? (new DeepLTranslator(Http, new DeepLTranslatorOptions(deepLKey)), null)
                    : (null, "DeepL: enter the API key below.");

            case TranslatorKind.Local:
                if (localModelPath is not { } modelPath)
                    return (null, "No local translation model (.gguf). Choose one in Settings → Models.");
                // Native library loading is synchronous -> keep the whole load off the UI thread.
                var local = await Task.Run(() => LlamaTranslator.LoadAsync(new LlamaTranslatorOptions(modelPath)));
                return (local, null);

            case TranslatorKind.Cloud:
                var (server, problem) = CloudServer.Create(settings);
                if (server is null) return (null, problem);
                _ = server.IsReadyAsync(); // a sleeping server starts now, not at the first sentence
                return (new RemoteTranslator(server), null);

            default:
                return (null, null);
        }
    }
}
