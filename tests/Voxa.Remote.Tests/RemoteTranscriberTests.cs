using Voxa.Core.Asr;

namespace Voxa.Remote.Tests;

public class RemoteTranscriberTests
{
    private static readonly float[] Sound = Enumerable.Repeat(0.3f, 1600).ToArray();   // 100 ms
    private static readonly float[] Silence = new float[1600];

    [Fact(Timeout = 15_000)] // never hang the run on a lost message
    public async Task Streams_audio_and_finishes_after_end()
    {
        await using var server = await FakeServer.StartAsync();
        await using var transcriber = new RemoteTranscriber(server.Client());

        transcriber.TryWriteAudio(Sound);
        transcriber.TryWriteAudio(Silence);
        transcriber.TryWriteAudio(Sound);
        transcriber.Complete();

        var segments = await ReadAllAsync(transcriber, TestContext.Current.CancellationToken);
        Assert.Equal(["s0", "s1"], segments.Select(s => s.Text));
        Assert.Equal([0L, 1L], segments.Select(s => s.Id));
        await transcriber.Completion;
    }

    [Fact(Timeout = 15_000)] // never hang the run on a lost message
    public async Task Waits_for_a_waking_server()
    {
        await using var server = await FakeServer.StartAsync(new FakeServerOptions(StartingHealthChecks: 3));
        await using var transcriber = new RemoteTranscriber(server.Client());
        var states = Track(transcriber);

        transcriber.TryWriteAudio(Sound); // said while the server was starting: still transcribed
        transcriber.Complete();

        Assert.Equal(["s0"], (await ReadAllAsync(transcriber, TestContext.Current.CancellationToken)).Select(s => s.Text));
        Assert.Contains(RemoteTranscriberState.WakingUp, states);
        Assert.Contains(RemoteTranscriberState.Listening, states);
    }

    [Fact(Timeout = 15_000)] // never hang the run on a lost message
    public async Task Pauses_after_idle_close_and_resumes_on_sound_with_new_ids()
    {
        await using var server = await FakeServer.StartAsync(new FakeServerOptions(IdleAfterSilentMessages: 3));
        await using var transcriber = new RemoteTranscriber(server.Client());
        var ct = TestContext.Current.CancellationToken;
        await using var segments = transcriber.ReadSegmentsAsync(ct).GetAsyncEnumerator(ct);

        transcriber.TryWriteAudio(Sound);
        Assert.True(await segments.MoveNextAsync());
        Assert.Equal(0, segments.Current.Id);

        for (int i = 0; i < 3; i++) transcriber.TryWriteAudio(Silence);
        await WaitForAsync(() => transcriber.State == RemoteTranscriberState.Paused, ct);
        transcriber.TryWriteAudio(Silence); // still paused: silence doesn't reconnect
        await Task.Delay(100, ct);
        Assert.Equal(1, server.Connections);

        transcriber.TryWriteAudio(Sound);
        Assert.True(await segments.MoveNextAsync());
        Assert.Equal(2, server.Connections);
        Assert.Equal("s0", segments.Current.Text); // the server counts from 0 again …
        Assert.Equal(1, segments.Current.Id);      // … the caller sees the next id
    }

    [Fact(Timeout = 15_000)] // never hang the run on a lost message
    public async Task Reconnects_after_a_dropped_connection()
    {
        await using var server = await FakeServer.StartAsync(new FakeServerOptions(DropFirstConnection: true));
        await using var transcriber = new RemoteTranscriber(server.Client());
        var ct = TestContext.Current.CancellationToken;
        await using var segments = transcriber.ReadSegmentsAsync(ct).GetAsyncEnumerator(ct);

        transcriber.TryWriteAudio(Sound);
        Assert.True(await segments.MoveNextAsync());
        await WaitForAsync(() => server.Connections == 2 && transcriber.State == RemoteTranscriberState.Listening, ct);

        transcriber.TryWriteAudio(Sound);
        Assert.True(await segments.MoveNextAsync());
        Assert.Equal(1, segments.Current.Id);
    }

    [Fact(Timeout = 15_000)] // never hang the run on a lost message
    public async Task Wrong_key_fails_with_a_clear_error()
    {
        await using var server = await FakeServer.StartAsync();
        await using var transcriber = new RemoteTranscriber(server.Client(key: "wrong"));
        transcriber.TryWriteAudio(Sound);

        var error = await Assert.ThrowsAsync<RemoteServerException>(() => ReadAllAsync(transcriber, TestContext.Current.CancellationToken));
        Assert.Contains("API key", error.Message);
    }

    [Fact]
    public void Rms_of_silence_and_sound() =>
        Assert.Equal((0f, 0.3f), (RemoteTranscriber.Rms(Silence), MathF.Round(RemoteTranscriber.Rms(Sound), 3)));

    private static async Task<List<TranscriptSegment>> ReadAllAsync(RemoteTranscriber transcriber, CancellationToken ct)
    {
        var list = new List<TranscriptSegment>();
        await foreach (var segment in transcriber.ReadSegmentsAsync(ct))
            list.Add(segment);
        return list;
    }

    private static List<RemoteTranscriberState> Track(RemoteTranscriber transcriber)
    {
        var states = new List<RemoteTranscriberState>();
        transcriber.StateChanged += state => { lock (states) states.Add(state); };
        return states;
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(20, ct);
        }
    }
}
