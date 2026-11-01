using System.Buffers.Binary;

namespace Voxa.Core.Audio;

/// <summary>Decoded WAV: mono float samples in [-1, 1].</summary>
public sealed record WavAudio(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}

/// <summary>
/// Minimal WAV reader without dependencies: PCM 16-bit and IEEE float 32-bit (what WASAPI loopback writes),
/// including WAVE_FORMAT_EXTENSIBLE. Channels are averaged to mono.
/// </summary>
public static class WavFile
{
    private const ushort FormatPcm = 1;
    private const ushort FormatIeeeFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public static WavAudio Read(string path) => Read(File.ReadAllBytes(path));

    public static WavAudio Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a RIFF/WAVE file.");

        ushort format = 0, channels = 0, bitsPerSample = 0;
        int sampleRate = 0;
        ReadOnlySpan<byte> pcm = default;

        int offset = 12;
        while (offset + 8 <= data.Length)
        {
            var id = data.Slice(offset, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4));
            // NAudio leaves size 0 / too large when the file wasn't closed properly -> read to the end.
            var body = data.Slice(offset + 8, Math.Min(size <= 0 ? int.MaxValue : size, data.Length - offset - 8));

            if (id.SequenceEqual("fmt "u8))
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == FormatExtensible && body.Length >= 26)
                    format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]); // first 2 bytes of SubFormat GUID
            }
            else if (id.SequenceEqual("data"u8))
            {
                pcm = body;
            }

            offset += 8 + body.Length + (body.Length & 1); // chunks are word aligned
        }

        if (channels == 0 || sampleRate == 0) throw new InvalidDataException("Missing fmt chunk.");

        var samples = (format, bitsPerSample) switch
        {
            (FormatPcm, 16) => ToMono(pcm, channels, 2, static b => BinaryPrimitives.ReadInt16LittleEndian(b) / 32768f),
            (FormatIeeeFloat, 32) => ToMono(pcm, channels, 4, static b => BinaryPrimitives.ReadSingleLittleEndian(b)),
            _ => throw new NotSupportedException($"WAV format {format} with {bitsPerSample} bits is not supported."),
        };
        return new WavAudio(samples, sampleRate);
    }

    private delegate float SampleReader(ReadOnlySpan<byte> bytes);

    private static float[] ToMono(ReadOnlySpan<byte> pcm, int channels, int bytesPerSample, SampleReader read)
    {
        int frameSize = channels * bytesPerSample;
        var mono = new float[pcm.Length / frameSize];
        for (int frame = 0; frame < mono.Length; frame++)
        {
            float sum = 0;
            for (int ch = 0; ch < channels; ch++)
                sum += read(pcm.Slice(frame * frameSize + ch * bytesPerSample, bytesPerSample));
            mono[frame] = sum / channels;
        }
        return mono;
    }
}
