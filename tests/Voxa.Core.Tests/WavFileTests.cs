using System.Buffers.Binary;
using Voxa.Core.Audio;

namespace Voxa.Core.Tests;

public class WavFileTests
{
    [Fact]
    public void Reads_pcm16_mono()
    {
        var wav = BuildWav(format: 1, channels: 1, sampleRate: 16_000, bitsPerSample: 16,
            data: Pcm16(0, 16384, -32768));

        var audio = WavFile.Read(wav);

        Assert.Equal(16_000, audio.SampleRate);
        Assert.Equal([0f, 0.5f, -1f], audio.Samples);
    }

    [Fact]
    public void Reads_float32_stereo_as_mono_average()
    {
        // WASAPI loopback default: 48 kHz stereo IEEE float
        var wav = BuildWav(format: 3, channels: 2, sampleRate: 48_000, bitsPerSample: 32,
            data: Float32(1f, 0f, -0.5f, -0.5f));

        var audio = WavFile.Read(wav);

        Assert.Equal(48_000, audio.SampleRate);
        Assert.Equal([0.5f, -0.5f], audio.Samples);
    }

    [Fact]
    public void Reads_extensible_format_from_subformat()
    {
        var wav = BuildWav(format: 0xFFFE, channels: 1, sampleRate: 48_000, bitsPerSample: 32,
            data: Float32(0.25f), extensibleSubFormat: 3);

        Assert.Equal([0.25f], WavFile.Read(wav).Samples);
    }

    [Fact]
    public void Duration_is_samples_over_rate()
    {
        var audio = new WavAudio(new float[8_000], 16_000);
        Assert.Equal(TimeSpan.FromSeconds(0.5), audio.Duration);
    }

    [Fact]
    public void Rejects_non_wav() =>
        Assert.Throws<InvalidDataException>(() => WavFile.Read("not a wav file"u8));

    [Fact]
    public void Rejects_unsupported_bit_depth()
    {
        var wav = BuildWav(format: 1, channels: 1, sampleRate: 16_000, bitsPerSample: 24, data: new byte[3]);
        Assert.Throws<NotSupportedException>(() => WavFile.Read(wav));
    }

    private static byte[] Pcm16(params short[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);
        return bytes;
    }

    private static byte[] Float32(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    private static byte[] BuildWav(ushort format, ushort channels, int sampleRate, ushort bitsPerSample, byte[] data,
        ushort? extensibleSubFormat = null)
    {
        int fmtSize = extensibleSubFormat is null ? 16 : 40;
        var wav = new byte[12 + 8 + fmtSize + 8 + data.Length];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], wav.Length - 8);
        "WAVE"u8.CopyTo(span[8..]);

        var fmt = span[12..];
        "fmt "u8.CopyTo(fmt);
        BinaryPrimitives.WriteInt32LittleEndian(fmt[4..], fmtSize);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt[8..], format);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt[10..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(fmt[12..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(fmt[16..], sampleRate * channels * bitsPerSample / 8);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt[20..], (ushort)(channels * bitsPerSample / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(fmt[22..], bitsPerSample);
        if (extensibleSubFormat is { } sub)
            BinaryPrimitives.WriteUInt16LittleEndian(fmt[32..], sub); // SubFormat GUID starts at fmt body offset 24

        var dataChunk = span[(20 + fmtSize)..];
        "data"u8.CopyTo(dataChunk);
        BinaryPrimitives.WriteInt32LittleEndian(dataChunk[4..], data.Length);
        data.CopyTo(dataChunk[8..]);
        return wav;
    }
}
