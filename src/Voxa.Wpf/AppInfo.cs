using System.Reflection;

namespace Voxa.Wpf;

/// <summary>Version and where releases are published (installer\Voxa.iss and .github\workflows\release.yml).</summary>
public static class AppInfo
{
    /// <summary>
    /// Private repository: releases are readable only with a token of someone who has access (Settings → Updates).
    /// For public releases, change these two and drop the token.
    /// </summary>
    public const string GitHubOwner = "drtilp";
    public const string GitHubRepo = "Voxa";

    /// <summary>Stable asset name, so .../releases/latest/download/Voxa-Setup.exe (README) always works.</summary>
    public const string InstallerAssetName = "Voxa-Setup.exe";

    /// <summary>From &lt;Version&gt; in the csproj; CI sets it from the git tag.</summary>
    public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static string DisplayVersion => $"{Version.Major}.{Version.Minor}.{Math.Max(Version.Build, 0)}";

    /// <summary>From VOXA_CHANNEL ("Dev"); anything else, including an installed copy, is Stable.</summary>
    public static AppChannel Channel { get; } =
        string.Equals(Environment.GetEnvironmentVariable("VOXA_CHANNEL"), "Dev", StringComparison.OrdinalIgnoreCase)
            ? AppChannel.Dev : AppChannel.Stable;

    /// <summary>Window title and tray tooltip; Dev is marked so the two copies can't be mixed up.</summary>
    public static string DisplayName => Channel == AppChannel.Dev ? "Voxa Dev" : "Voxa";

    /// <summary>Folder under %APPDATA% (settings.json). Separate for Dev, so experiments don't touch the real settings.</summary>
    public static string DataFolderName => Channel == AppChannel.Dev ? "Voxa-Dev" : "Voxa";

    public static Uri ReleasesPage { get; } = new($"https://github.com/{GitHubOwner}/{GitHubRepo}/releases");
}
