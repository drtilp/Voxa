namespace Voxa.Core.Translation;

/// <param name="Code">ISO 639-1 (what Azure Translator and M2M100 use).</param>
public sealed record Language(string Code, string Name)
{
    public static Language English { get; } = new("en", "English");

    /// <summary>Target languages offered in the UI (Voxa spec: English default, Czech optional, then neighbours).</summary>
    public static IReadOnlyList<Language> Targets { get; } =
    [
        English,
        new("cs", "Czech"),
        new("uk", "Ukrainian"),
        new("ru", "Russian"),
        new("de", "German"),
        new("pl", "Polish"),
        new("sk", "Slovak"),
        new("fr", "French"),
        new("es", "Spanish"),
        new("ro", "Romanian"),
    ];

    public static Language FromCode(string? code) =>
        Targets.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? English;

    public override string ToString() => Name;
}
