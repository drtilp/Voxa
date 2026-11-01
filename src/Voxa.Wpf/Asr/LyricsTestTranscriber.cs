using System.Runtime.CompilerServices;

using Voxa.Core.Asr;

namespace Voxa.Wpf.Asr;

/// <summary>
/// Fake streaming ASR for testing the overlay: emits each line word by word as partial segments,
/// the last word of a line as the final segment - the same shape a real engine will produce.
/// </summary>
public sealed class LyricsTestTranscriber
{
    private static readonly TimeSpan WordDelay = TimeSpan.FromMilliseconds(380);
    private static readonly TimeSpan LinePause = TimeSpan.FromMilliseconds(1100);

    // Test data - edit freely.
    // TODO: complete the remaining verses of "Prší, prší" (folk song).
    private static readonly string[] Lines =
    [
        "Prší, prší, jen se leje,",
        "kde já budu spáti?",
        "Prší, prší, jen se leje,",
        "kde já budu spáti?",
        "Tohle je delší testovací řádek, který ověří zalamování textu v overlayi na menším monitoru.",
        "And one English line to check the font with a different language.",
    ];

    public async IAsyncEnumerable<TranscriptSegment> RunAsync([EnumeratorCancellation] CancellationToken ct)
    {
        for (int id = 0; id < Lines.Length; id++)
        {
            var words = Lines[id].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int count = 1; count <= words.Length; count++)
            {
                await Task.Delay(WordDelay, ct);
                yield return new TranscriptSegment(string.Join(' ', words, 0, count), IsFinal: count == words.Length, Id: id);
            }

            await Task.Delay(LinePause, ct);
        }
    }
}
