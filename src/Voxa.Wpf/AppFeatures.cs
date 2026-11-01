namespace Voxa.Wpf;

/// <summary>
/// Stable = what users get. Dev = a second copy for trying things out: its own data folder and single-instance mutex
/// (so it runs next to Stable) and the developer tools of <see cref="AppFeatures"/>.
/// </summary>
public enum AppChannel { Stable, Dev }

/// <summary>
/// Developer tools that exist only in the Dev channel. Chosen at start, not saved: the launch profiles in
/// Properties/launchSettings.json set <c>VOXA_CHANNEL</c> and <c>VOXA_FEATURES</c>.
/// Stable ignores both variables, so an installed Voxa never shows them.
/// </summary>
public sealed class AppFeatures
{
    /// <summary>Settings → Audio test (records the output device to WAV) and Record in the tray menu.</summary>
    public bool AudioTest { get; init; }

    /// <summary>"Test ASR Overlay UI": fake captions in the overlay, in Settings and in the tray menu.</summary>
    public bool OverlayTest { get; init; }

    /// <summary>Settings → Experimental (feature flags that are saved in settings.json).</summary>
    public bool Experimental { get; init; }

    public static AppFeatures Current { get; } =
        Resolve(AppInfo.Channel, Environment.GetEnvironmentVariable("VOXA_FEATURES"));

    /// <param name="list">Comma-separated names (AudioTest, OverlayTest, Experimental), "None", or empty = all of them.</param>
    public static AppFeatures Resolve(AppChannel channel, string? list)
    {
        if (channel == AppChannel.Stable) return new AppFeatures();
        if (string.IsNullOrWhiteSpace(list)) return new AppFeatures { AudioTest = true, OverlayTest = true, Experimental = true };

        var names = list.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Has(string name) => names.Contains(name, StringComparer.OrdinalIgnoreCase);
        return new AppFeatures { AudioTest = Has("AudioTest"), OverlayTest = Has("OverlayTest"), Experimental = Has("Experimental") };
    }
}
