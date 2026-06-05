using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Async line splitter over a <see cref="PipeReader"/>. Yields <see cref="JsonlLine"/> values
/// holding owned UTF-8 byte arrays (one allocation per produced line). Strips a leading UTF-8 BOM
/// from the first line, trims trailing <c>\r</c>, and enforces <see cref="JsonlReadOptions.MaxLineSizeBytes"/>.
/// </summary>
internal static class JsonlPipeLineReader
{
    private const byte LF = (byte)'\n';
    private const byte CR = (byte)'\r';

    public static async IAsyncEnumerable<JsonlLine> ReadLinesAsync(
        PipeReader reader,
        JsonlReadOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        long lineNumber = 0;
        bool firstLineEmitted = false;

        while (true)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            while (TryReadLine(ref buffer, out ReadOnlySequence<byte> lineSeq))
            {
                if (lineSeq.Length > options.MaxLineSizeBytes)
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    throw new JsonlException(
                        JsonlErrorCode.LineTooLong,
                        $"A single line exceeds the configured MaxLineSizeBytes of {options.MaxLineSizeBytes:N0} bytes.",
                        lineNumber + 1);
                }

                byte[] lineBytes = CopyLineToArray(lineSeq, ref firstLineEmitted);
                lineNumber++;
                yield return new JsonlLine(lineBytes, lineNumber);
            }

            if (result.IsCompleted)
            {
                if (!buffer.IsEmpty)
                {
                    if (buffer.Length > options.MaxLineSizeBytes)
                    {
                        reader.AdvanceTo(buffer.End);
                        throw new JsonlException(
                            JsonlErrorCode.LineTooLong,
                            $"A single line exceeds the configured MaxLineSizeBytes of {options.MaxLineSizeBytes:N0} bytes.",
                            lineNumber + 1);
                    }

                    byte[] lineBytes = CopyLineToArray(buffer, ref firstLineEmitted);
                    lineNumber++;
                    reader.AdvanceTo(buffer.End);
                    yield return new JsonlLine(lineBytes, lineNumber);
                    yield break;
                }

                reader.AdvanceTo(buffer.End);
                yield break;
            }

            if (buffer.Length > options.MaxLineSizeBytes)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                throw new JsonlException(
                    JsonlErrorCode.LineTooLong,
                    $"A single line exceeds the configured MaxLineSizeBytes of {options.MaxLineSizeBytes:N0} bytes.",
                    lineNumber + 1);
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        SequencePosition? newlinePos = buffer.PositionOf(LF);
        if (newlinePos is null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, newlinePos.Value);

        if (!line.IsEmpty)
        {
            ReadOnlySequence<byte> trimmable = line;
            if (TryGetLastByte(trimmable, out byte lastByte) && lastByte == CR)
                line = line.Slice(0, line.Length - 1);
        }

        buffer = buffer.Slice(buffer.GetPosition(1, newlinePos.Value));
        return true;
    }

    private static bool TryGetLastByte(ReadOnlySequence<byte> seq, out byte last)
    {
        if (seq.IsEmpty)
        {
            last = 0;
            return false;
        }

        if (seq.IsSingleSegment)
        {
            ReadOnlySpan<byte> span = seq.FirstSpan;
            last = span[^1];
            return true;
        }

        SequencePosition pos = seq.GetPosition(seq.Length - 1);
        ReadOnlySequence<byte> tail = seq.Slice(pos);
        last = tail.FirstSpan[0];
        return true;
    }

    private static byte[] CopyLineToArray(ReadOnlySequence<byte> seq, ref bool firstLineEmitted)
    {
        int length = checked((int)seq.Length);
        int offset = 0;

        if (!firstLineEmitted)
        {
            firstLineEmitted = true;
            if (length >= 3)
            {
                Span<byte> peek = stackalloc byte[3];
                seq.Slice(0, 3).CopyTo(peek);
                if (peek[0] == 0xEF && peek[1] == 0xBB && peek[2] == 0xBF)
                    offset = 3;
            }
        }

        int finalLength = length - offset;
        if (finalLength <= 0)
            return [];

        byte[] result = new byte[finalLength];
        seq.Slice(offset, finalLength).CopyTo(result);
        return result;
    }
}
