using System.Net.Http;
using Voxa.Core.Models;

namespace Voxa.Wpf.Setup;

/// <summary>
/// Models the app can download into the models folder. Files come straight from their public Hugging Face repos
/// (free, CDN, resumable, no token), pinned to a commit and checked by SHA-256. The paths match <see cref="ModelOffers"/>
/// and what <see cref="Transcription.ModelCatalog"/> looks for. Moving the hosting (e.g. to GitHub releases) only means
/// other URLs here; later this list can come from a models.json next to the releases.
/// </summary>
public static class ModelDownloads
{
    // No timeout: a 2 GB file takes long. ModelDownloader drops a connection that stalls instead.
    public static HttpClient Http { get; } = new() { Timeout = Timeout.InfiniteTimeSpan };

    private const string Parakeet = "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8@2bda32ec70b097a55adaa07d9a7173915b43cc78";
    private const string WhisperTurbo = "csukuangfj/sherpa-onnx-whisper-turbo@2ca6ff69fc878651b770880507669577ac41c2ff";
    private const string WhisperSmall = "csukuangfj/sherpa-onnx-whisper-small@8f3c18b358db4d1f2fc1eae49d75cd20989e4309";
    private const string HyMt2 = "tencent/Hy-MT2-1.8B-GGUF@a0c709d9fac510f2c807aa3af52872340dc37a4a";

    public static IReadOnlyList<ModelPackage> All { get; } =
    [
        // NVIDIA's model (int8, the one the cloud server runs). Not Parakeet Ultra: that post-trained export isn't
        // hosted anywhere yet, so its own folder name stays free for it.
        new("parakeet-tdt-0.6b-v3", "Parakeet TDT 0.6B v3 (speech recognition)",
            "25 European languages incl. Czech, Ukrainian and Russian. Fast on the CPU.",
        [
            HuggingFace(Parakeet, "encoder.int8.onnx", "parakeet-tdt-0.6b-v3", 652184281, "acfc2b4456377e15d04f0243af540b7fe7c992f8d898d751cf134c3a55fd2247"),
            HuggingFace(Parakeet, "decoder.int8.onnx", "parakeet-tdt-0.6b-v3", 11845275, "179e50c43d1a9de79c8a24149a2f9bac6eb5981823f2a2ed88d655b24248db4e"),
            HuggingFace(Parakeet, "joiner.int8.onnx", "parakeet-tdt-0.6b-v3", 6355277, "3164c13fc2821009440d20fcb5fdc78bff28b4db2f8d0f0b329101719c0948b3"),
            HuggingFace(Parakeet, "tokens.txt", "parakeet-tdt-0.6b-v3", 93939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
        ]),
        new("whisper-large-v3-turbo", "Whisper large-v3 turbo (speech recognition)",
            "99 languages. Slower on the CPU than Parakeet.",
        [
            HuggingFace(WhisperTurbo, "turbo-encoder.int8.onnx", "whisper-large-v3-turbo", 674716297, "b02dcdf54f348741e93fe732b67d933c8dcb6735655f710640143081db38878b"),
            HuggingFace(WhisperTurbo, "turbo-decoder.int8.onnx", "whisper-large-v3-turbo", 361080764, "20accd02388482eb3a46bd615631adfdc85e1eb2c7db9ea3f02a40ffe6b81547"),
            HuggingFace(WhisperTurbo, "turbo-tokens.txt", "whisper-large-v3-turbo", 816730, "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126"),
        ]),
        new("hy-mt2-1.8b", "Hy-MT2 1.8B (translation)",
            "33 languages, offline. Fast on a GPU (Vulkan), works on the CPU too.",
        [
            HuggingFace(HyMt2, "Hy-MT2-1.8B-Q8_0.gguf", "translation/hy-mt2-1.8b", 1908528192, "5c3fe0b1408a5ceb0143184ef247b11b579c525f4b02b060e6c851bb76fef1a4"),
        ]),
        new("silero-vad", "Silero VAD (voice activity detection)",
            "Finds where sentences start and end. Needed for local speech recognition.",
        [
            new("vad/silero_vad.onnx", new Uri("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx"),
                643854, "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6"),
        ]),
        new("whisper-small-lid", "Whisper small (language detection)",
            "Optional: shows which language is spoken (\"Czech detected\").",
        [
            HuggingFace(WhisperSmall, "small-encoder.int8.onnx", "lid/whisper-small", 112442483, "4cbe7b22fa9026b843b60a68640c747de05bafb1a11b57edc0e66c232d9f33a9"),
            HuggingFace(WhisperSmall, "small-decoder.int8.onnx", "lid/whisper-small", 262226114, "acad50b5c782696e91b55914cc5ab4f756f1532f76e22aa6fc615f39fb69a8ee"),
            HuggingFace(WhisperSmall, "small-tokens.txt", "lid/whisper-small", 816730, "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126"),
        ]),
    ];

    /// <summary>The package behind a <see cref="ModelOffers"/> id (same ids), null for offers that aren't downloads.</summary>
    public static ModelPackage? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

    /// <param name="repo">"owner/name@commit"</param>
    private static ModelFileSource HuggingFace(string repo, string file, string folder, long size, string sha256)
    {
        var (name, commit) = (repo[..repo.IndexOf('@')], repo[(repo.IndexOf('@') + 1)..]);
        return new ModelFileSource($"{folder}/{file}", new Uri($"https://huggingface.co/{name}/resolve/{commit}/{file}"), size, sha256);
    }
}
