namespace Voxa.Core.Asr;

public enum AsrModelLocation
{
    Local,
    Remote,
}

/// <summary>A model the user can pick. <see cref="FilePath"/> is set for models added from disk (.onnx).</summary>
public sealed record AsrModelOption(string Name, AsrModelLocation Location, string? FilePath = null)
{
    public string DisplayName => $"{Name} ({Location})";
}
