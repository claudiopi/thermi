namespace Thermi.Core.Pipe;

/// <summary>A whole line read from a pipe, without its end.</summary>
public delegate void LineHandler(ReadOnlySpan<byte> line);

/// <summary>
/// Cuts the bytes read from a pipe into lines. Bytes arrive in arbitrary pieces: a line, or a character, can span two
/// reads, and one read can hold several lines. Only the new bytes are searched and only a line's unfinished start is kept,
/// so a line that comes a byte at a time costs what one that comes whole does. A newline is never inside a character of
/// UTF-8, so lines are cut before any decoding.
/// </summary>
/// <param name="maxLength">Past this many bytes, a line is longer than anything the other end may send.</param>
public sealed class LineBuffer(int maxLength)
{
    private byte[] _pending = [];
    private int _length;

    /// <summary>
    /// Hands <paramref name="line"/> each line <paramref name="bytes"/> completes. Throws <see cref="InvalidDataException"/>
    /// once a line outgrows the longest allowed: whatever is on the other end does not speak this protocol.
    /// </summary>
    public void Add(ReadOnlySpan<byte> bytes, LineHandler line)
    {
        ArgumentNullException.ThrowIfNull(line);
        int end;
        while ((end = bytes.IndexOf((byte)'\n')) >= 0)
        {
            if (_length == 0)
            {
                line(bytes[..end]);
            }
            else
            {
                Keep(bytes[..end]);
                line(_pending.AsSpan(0, _length));
                _length = 0;
            }

            bytes = bytes[(end + 1)..];
        }

        Keep(bytes);
    }

    private void Keep(ReadOnlySpan<byte> bytes)
    {
        if (_length + bytes.Length > maxLength)
        {
            throw new InvalidDataException("A line from the pipe is longer than any the other end may send.");
        }

        if (_length + bytes.Length > _pending.Length)
        {
            Array.Resize(ref _pending, Math.Min(maxLength, Math.Max(_length + bytes.Length, _pending.Length * 2)));
        }

        bytes.CopyTo(_pending.AsSpan(_length));
        _length += bytes.Length;
    }
}
