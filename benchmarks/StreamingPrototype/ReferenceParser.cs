namespace StreamingPrototype;

internal static class ReferenceParser
{
    public static RowParseResult ParseRow(ReadOnlySpan<byte> data, byte delimiter = (byte)',', byte quote = (byte)'"')
    {
        if (data.IsEmpty)
            return new RowParseResult(1, 0, 0);

        bool inQuotes = false;
        int columnCount = 0;
        int rowLength = 0;
        int charsConsumed = 0;

        for (int i = 0; i < data.Length; i++)
        {
            byte c = data[i];

            if (c == quote)
            {
                if (inQuotes && i + 1 < data.Length && data[i + 1] == quote)
                {
                    i++; // skip escaped quote
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
                continue;

            if (c == delimiter)
            {
                columnCount++;
                continue;
            }

            if (c == '\n' || c == '\r')
            {
                rowLength = i;
                charsConsumed = i + 1;
                if (c == '\r' && i + 1 < data.Length && data[i + 1] == '\n')
                {
                    charsConsumed++;
                }
                goto Finish;
            }
        }

        rowLength = data.Length;
        charsConsumed = rowLength;

Finish:
        columnCount++; // last column
        return new RowParseResult(columnCount, rowLength, charsConsumed);
    }
}
