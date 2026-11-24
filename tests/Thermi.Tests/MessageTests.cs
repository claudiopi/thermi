using Thermi.Core.Messages;

namespace Thermi.Tests;

/// <summary>The payloads the overlay and the window send each other; <c>MessagesWindowsTests</c> sends them between two windows.</summary>
public sealed class MessageTests
{
    // Each kind decodes as it was encoded; a payload cut short, of another version, with bytes left over, a length or an
    // enum that cannot be, broken UTF-8 or past the cap decodes to null.
    [Theory]
    [InlineData("history", true)]
    [InlineData("select", true)]
    [InlineData("edited", true)]
    [InlineData("edited-none", true)]
    [InlineData("gesture", true)]
    [InlineData("raise", true)]
    [InlineData("asked", true)]
    [InlineData("cut", false)]
    [InlineData("version", false)]
    [InlineData("left-over", false)]
    [InlineData("length", false)]
    [InlineData("floats", false)]
    [InlineData("enum", false)]
    [InlineData("utf8", false)]
    [InlineData("kind", false)]
    [InlineData("too-long", false)]
    [InlineData("empty", false)]
    public void APayloadDecodesAsItWasEncodedOrToNull(string row, bool decodes)
    {
        var (sent, payload) = Payload(row);

        var decoded = WindowMessages.Decode(payload);

        Assert.Equal(decodes, decoded is not null);
        if (decoded is not null)
        {
            Assert.Equal(WindowMessages.Encode(sent), WindowMessages.Encode(decoded));
            Assert.Equal(WindowMessages.NameOf(sent), WindowMessages.NameOf(decoded));
        }
    }

    private static (WindowMessage Sent, byte[] Payload) Payload(string row)
    {
        var select = new Select(2, "cpu.temperature", SelectHow.PointIn);
        var good = WindowMessages.Encode(select);
        return row switch
        {
            "history" => Encoded(new History([new ItemHistory("cpu.temperature", 1_000_000, [40, float.NaN, 41.5f]), new ItemHistory("ram.used", 0, [])])),
            "select" => (select, good),
            "edited" => Encoded(new Edited(1, "cpu.temperature", "ram.used")),
            "edited-none" => Encoded(new Edited(null, null, null)),
            "gesture" => Encoded(new Gesture(GestureKind.Merge, "{ \"version\": 2 }", "{ \"version\": 2, \"überall\": 1 }")),
            "raise" => Encoded(new Raise(0, null)),
            "asked" => Encoded(new HistoryAsked()),
            "cut" => (select, good[..^1]),
            "version" => (select, [2, .. good[1..]]),
            "left-over" => (select, [.. good, 0]),
            "length" => (select, [.. good[..6], 0xFF, 0xFF, 0xFF, 0x7F, .. good[10..]]),
            "floats" => (select, [1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F]),
            "enum" => (select, [.. good[..^1], 9]),
            "utf8" => (select, [1, 5, 0, 0, 0, 0, 1, 0, 0, 0, 0xFF]),
            "kind" => (select, [1, 9]),
            "too-long" => (select, [.. good, .. new byte[WindowMessages.MaxLength]]),
            _ => (select, []),
        };
    }

    private static (WindowMessage, byte[]) Encoded(WindowMessage message) => (message, WindowMessages.Encode(message));
}
