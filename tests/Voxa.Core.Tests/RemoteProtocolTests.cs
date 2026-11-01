using Voxa.Core.Remote;

namespace Voxa.Core.Tests;

public class RemoteProtocolTests
{
    [Fact]
    public void Pcm16_is_little_endian_signed()
    {
        byte[] pcm16 = [0x00, 0x00, 0x00, 0x40, 0x00, 0x80, 0xFF, 0x7F];

        Assert.Equal([0f, 0.5f, -1f, 32767 / 32768f], RemoteProtocol.Pcm16ToFloat(pcm16));
    }

    [Fact]
    public void Float_round_trips_through_pcm16_and_clips()
    {
        float[] samples = [0f, 0.25f, -0.5f, 2f, -2f];

        var back = RemoteProtocol.Pcm16ToFloat(RemoteProtocol.FloatToPcm16(samples));

        Assert.Equal([0f, 0.25f, -0.5f, 32767 / 32768f, -1f], back);
    }
}
