using Voxa.Wpf.Services;

namespace Voxa.Wpf.ViewModels;

/// <summary>
/// A row of Settings → Translation: a translator, plus the .gguf for <see cref="TranslatorKind.Local"/>.
/// <see cref="Group"/> is the header it's listed under (Local / Remote / API; "" = no header).
/// </summary>
public sealed record TranslationOption(string Name, string Group, TranslatorKind Kind, string? ModelPath = null)
{
    public const string LocalGroup = "Local";
    public const string RemoteGroup = "Remote";
    public const string ApiGroup = "API";

    public static TranslationOption Off { get; } = new("No translation", "", TranslatorKind.Off);

    /// <summary>Hy-MT2 on the cloud server (the model the server image ships with).</summary>
    public static TranslationOption Cloud { get; } = new("Hy-MT2 1.8B", RemoteGroup, TranslatorKind.Cloud);

    public static IReadOnlyList<TranslationOption> Apis { get; } =
    [
        new("DeepL", ApiGroup, TranslatorKind.DeepL),
        new("Azure Translator", ApiGroup, TranslatorKind.Azure),
    ];

    public bool IsRemote => Kind == TranslatorKind.Cloud;
}
