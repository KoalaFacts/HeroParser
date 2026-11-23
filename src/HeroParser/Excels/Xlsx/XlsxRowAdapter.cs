using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Adapts Excel row data (string arrays) into <see cref="CsvRow{T}">CsvRow&lt;char&gt;</see> for binder compatibility,
/// owning reusable character and column-offset buffers across rows.
/// </summary>
internal sealed class XlsxRowAdapter
{
    /// <summary>Synthetic delimiter character used to separate cells in the buffer.</summary>
    private const char DELIMITER = '\x01';

    private char[] buffer = new char[256];
    private int[] columnEnds = new int[32];

    /// <summary>
    /// Constructs a <see cref="CsvRow{T}">CsvRow&lt;char&gt;</see> from an array of cell values.
    /// Uses a synthetic delimiter (\x01) to join cells into a contiguous reusable char buffer.
    /// </summary>
    /// <param name="cells">The cell values from the Excel row.</param>
    /// <param name="rowNumber">The 1-based row number.</param>
    /// <returns>A <see cref="CsvRow{T}">CsvRow&lt;char&gt;</see> backed by this adapter's buffers.</returns>
    public CsvRow<char> CreateRow(string[] cells, int rowNumber)
    {
        EnsureBuffers(cells);

        // Layout:
        //   buffer: cell0 + \x01 + cell1 + \x01 + ... + cellN
        //   columnEnds[0] = -1 (sentinel)
        //   columnEnds[i+1] = position of delimiter after cell i
        //   For last cell: columnEnds[N] = total length (acts as virtual delimiter position)
        //
        // CsvRow indexer: column i spans buffer[columnEnds[i]+1 .. columnEnds[i+1])

        columnEnds[0] = -1;
        int pos = 0;

        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];

            // Copy cell characters into buffer
            cell.AsSpan().CopyTo(buffer.AsSpan(pos));
            pos += cell.Length;

            if (i < cells.Length - 1)
            {
                // Add delimiter between cells (not after the last one)
                buffer[pos] = DELIMITER;
                columnEnds[i + 1] = pos;
                pos++;
            }
            else
            {
                // Last cell: columnEnds points to total length
                columnEnds[i + 1] = pos;
            }
        }

        return new CsvRow<char>(
            buffer.AsSpan(0, pos),
            columnEnds,
            cells.Length,
            rowNumber,
            rowNumber);
    }

    private void EnsureBuffers(string[] cells)
    {
        int bufferSize = CalculateBufferSize(cells) + 1;
        if (buffer.Length < bufferSize)
            buffer = new char[bufferSize];
        if (columnEnds.Length < cells.Length + 1)
            columnEnds = new int[cells.Length + 1];
    }

    private static int CalculateBufferSize(string[] cells)
    {
        int size = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            size += cells[i].Length;
        }
        // Add space for delimiters between cells
        if (cells.Length > 1)
            size += cells.Length - 1;
        return size;
    }
}
