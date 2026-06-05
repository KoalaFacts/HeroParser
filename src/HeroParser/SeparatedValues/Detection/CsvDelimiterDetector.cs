using System.Buffers;
using System.Text;

namespace HeroParser.SeparatedValues.Detection;

/// <summary>
/// Provides automatic detection of CSV delimiter characters.
/// </summary>
/// <remarks>
/// <para>
/// This class analyzes a sample of CSV data to determine the most likely delimiter
/// character. It supports common delimiters: comma (,), semicolon (;), pipe (|), and tab (\t).
/// </para>
/// <para>
/// The detection algorithm:
/// 1. Samples the first N rows (default 10)
/// 2. Counts occurrences of candidate delimiters in each row
/// 3. Selects the delimiter with the most consistent count across rows
/// 4. Calculates confidence based on consistency (standard deviation)
/// </para>
/// <para>
/// Thread-Safety: All methods are thread-safe as they operate on local state only.
/// </para>
/// </remarks>
public static class CsvDelimiterDetector
{
    /// <summary>
    /// Common delimiter characters to test during detection.
    /// </summary>
    private static readonly char[] candidateDelimiters = [',', ';', '|', '\t'];

    /// <summary>
    /// Default number of rows to sample for delimiter detection.
    /// </summary>
    public const int DEFAULT_SAMPLE_ROWS = 10;

    // Detection only inspects the first few rows, so the byte-overload truncates oversized inputs
    // before UTF-16 decoding to keep the worst-case allocation bounded.
    private const int MAX_DETECTION_BYTES = 1 * 1024 * 1024;

    /// <summary>
    /// Detects the most likely delimiter character in the CSV data.
    /// </summary>
    /// <param name="data">The CSV data to analyze (UTF-16).</param>
    /// <param name="sampleRows">Number of rows to sample (default 10).</param>
    /// <returns>The detected delimiter character.</returns>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    public static char DetectDelimiter(string data, int sampleRows = DEFAULT_SAMPLE_ROWS)
    {
        ArgumentNullException.ThrowIfNull(data);
        return DetectDelimiter(data.AsSpan(), sampleRows);
    }

    /// <summary>
    /// Detects the most likely delimiter character in the CSV data.
    /// </summary>
    /// <param name="data">The CSV data to analyze (UTF-16).</param>
    /// <param name="sampleRows">Number of rows to sample (default 10).</param>
    /// <returns>The detected delimiter character.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    public static char DetectDelimiter(ReadOnlySpan<char> data, int sampleRows = DEFAULT_SAMPLE_ROWS)
    {
        Span<int> totalCounts = stackalloc int[4];
        if (!DetectInternal(data, sampleRows, totalCounts, out char detectedDelimiter, out _, out _, out _))
        {
            throw new InvalidOperationException(
                "Cannot detect delimiter: no consistent delimiter pattern found. " +
                "The data may not be properly delimited or may require manual delimiter specification.");
        }
        return detectedDelimiter;
    }

    /// <summary>
    /// Detects the most likely delimiter character in UTF-8 encoded CSV data.
    /// </summary>
    /// <param name="data">The CSV data to analyze (UTF-8).</param>
    /// <param name="sampleRows">Number of rows to sample (default 10).</param>
    /// <returns>The detected delimiter character.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    public static char DetectDelimiter(ReadOnlySpan<byte> data, int sampleRows = DEFAULT_SAMPLE_ROWS)
    {
        // Delimiter detection only needs the first few rows. Slice the input so a giant attacker-
        // controlled byte span doesn't force a multi-MB UTF-16 decode allocation.
        if (data.Length > MAX_DETECTION_BYTES)
        {
            data = data[..MAX_DETECTION_BYTES];
        }

        int charCount = Encoding.UTF8.GetCharCount(data);
        char[]? rentedArray = null;
        Span<char> chars = charCount <= 4096
            ? stackalloc char[charCount]
            : (rentedArray = ArrayPool<char>.Shared.Rent(charCount));

        Encoding.UTF8.GetChars(data, chars);

        char detected;
        try
        {
            detected = DetectDelimiter(chars[..charCount], sampleRows);
        }
        finally
        {
            if (rentedArray is not null)
                ArrayPool<char>.Shared.Return(rentedArray);
        }
        return detected;
    }

    /// <summary>
    /// Performs delimiter detection and returns detailed results including confidence scores.
    /// </summary>
    /// <param name="data">The CSV data to analyze.</param>
    /// <param name="sampleRows">Number of rows to sample (default 10).</param>
    /// <returns>Detailed detection results including confidence and candidate counts.</returns>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    public static CsvDelimiterDetectionResult Detect(string data, int sampleRows = DEFAULT_SAMPLE_ROWS)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Detect(data.AsSpan(), sampleRows);
    }

    /// <summary>
    /// Performs delimiter detection and returns detailed results including confidence scores.
    /// </summary>
    /// <param name="data">The CSV data to analyze.</param>
    /// <param name="sampleRows">Number of rows to sample (default 10).</param>
    /// <returns>Detailed detection results including confidence and candidate counts.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    public static CsvDelimiterDetectionResult Detect(ReadOnlySpan<char> data, int sampleRows = DEFAULT_SAMPLE_ROWS)
    {
        Span<int> totalCounts = stackalloc int[4];
        if (!DetectInternal(data, sampleRows, totalCounts, out char detectedDelimiter, out int confidence, out double avgCount, out int sampledRows))
        {
            throw new InvalidOperationException(
                "Cannot detect delimiter: no consistent delimiter pattern found. " +
                "The data may not be properly delimited or may require manual delimiter specification.");
        }

        // Build candidate counts dictionary (only allocated when details are requested)
        var candidateCounts = new Dictionary<char, int>(4);
        for (int d = 0; d < 4; d++)
        {
            candidateCounts[candidateDelimiters[d]] = totalCounts[d];
        }

        return new CsvDelimiterDetectionResult
        {
            DetectedDelimiter = detectedDelimiter,
            Confidence = confidence,
            AverageDelimiterCount = avgCount,
            SampledRows = sampledRows,
            CandidateCounts = candidateCounts
        };
    }

    private static bool DetectInternal(
        ReadOnlySpan<char> data,
        int sampleRows,
        Span<int> totalCounts,
        out char detectedDelimiter,
        out int confidence,
        out double averageDelimiterCount,
        out int sampledRows)
    {
        if (sampleRows < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleRows), "Sample rows must be at least 1");

        if (data.IsEmpty)
            throw new InvalidOperationException("Cannot detect delimiter from empty data");

        // Stack-allocate or rent row counts buffer
        int bufferSize = 4 * sampleRows;
        int[]? rentedArray = null;
        Span<int> countsPerRow = bufferSize <= 512
            ? stackalloc int[bufferSize]
            : (rentedArray = ArrayPool<int>.Shared.Rent(bufferSize));

        countsPerRow.Clear();
        totalCounts.Clear();

        int rowStart = 0;
        int rowCount = 0;

        for (int i = 0; i < data.Length && rowCount < sampleRows; i++)
        {
            bool isLineEnd = false;
            int rowEnd = i;

            if (data[i] == '\n')
            {
                rowEnd = i;
                if (rowEnd > rowStart && data[rowEnd - 1] == '\r')
                    rowEnd--;
                isLineEnd = true;
            }
            else if (data[i] == '\r' && (i + 1 >= data.Length || data[i + 1] != '\n'))
            {
                rowEnd = i;
                isLineEnd = true;
            }

            if (isLineEnd)
            {
                int rowLength = rowEnd - rowStart;
                if (rowLength > 0)
                {
                    ReadOnlySpan<char> row = data.Slice(rowStart, rowLength);
                    for (int d = 0; d < 4; d++)
                    {
                        int count = CountOccurrences(row, candidateDelimiters[d]);
                        countsPerRow[d * sampleRows + rowCount] = count;
                        totalCounts[d] += count;
                    }
                    rowCount++;
                }
                rowStart = i + 1;
            }
        }

        if (rowStart < data.Length && rowCount < sampleRows)
        {
            int rowLength = data.Length - rowStart;
            if (rowLength > 0)
            {
                ReadOnlySpan<char> row = data.Slice(rowStart, rowLength);
                for (int d = 0; d < 4; d++)
                {
                    int count = CountOccurrences(row, candidateDelimiters[d]);
                    countsPerRow[d * sampleRows + rowCount] = count;
                    totalCounts[d] += count;
                }
                rowCount++;
            }
        }

        sampledRows = rowCount;
        if (rowCount == 0)
        {
            if (rentedArray is not null)
                ArrayPool<int>.Shared.Return(rentedArray);
            detectedDelimiter = default;
            confidence = 0;
            averageDelimiterCount = 0;
            return false;
        }

        // Select the best delimiter based on consistency
        int bestDelimiterIndex = -1;
        double bestScore = double.MinValue;

        for (int d = 0; d < 4; d++)
        {
            int totalCount = totalCounts[d];
            if (totalCount == 0)
                continue;

            // Counts for delimiter d
            Span<int> rowCounts = countsPerRow.Slice(d * sampleRows, rowCount);

            // Skip delimiters that don't appear in all rows (inconsistent)
            int nonZeroRows = 0;
            for (int r = 0; r < rowCount; r++)
            {
                if (rowCounts[r] > 0)
                    nonZeroRows++;
            }

            if (nonZeroRows < rowCount * 0.8)
                continue;

            double avg = CalculateAverage(rowCounts);
            double stdDev = CalculateStandardDeviation(rowCounts, avg);

            double consistencyFactor = avg > 0 ? avg / (stdDev + 1.0) : 0;
            double score = avg * 0.3 + consistencyFactor * 0.7;

            if (score > bestScore)
            {
                bestScore = score;
                bestDelimiterIndex = d;
            }
        }

        if (bestDelimiterIndex == -1)
        {
            if (rentedArray is not null)
                ArrayPool<int>.Shared.Return(rentedArray);
            detectedDelimiter = default;
            confidence = 0;
            averageDelimiterCount = 0;
            return false;
        }

        detectedDelimiter = candidateDelimiters[bestDelimiterIndex];
        Span<int> bestRowCounts = countsPerRow.Slice(bestDelimiterIndex * sampleRows, rowCount);
        double bestAvg = CalculateAverage(bestRowCounts);
        confidence = CalculateConfidence(bestRowCounts, bestAvg);
        averageDelimiterCount = bestAvg;

        if (rentedArray is not null)
            ArrayPool<int>.Shared.Return(rentedArray);

        return true;
    }

    private static int CountOccurrences(ReadOnlySpan<char> data, char character)
    {
        int count = 0;
        bool insideQuotes = false;
        for (int i = 0; i < data.Length; i++)
        {
            char c = data[i];
            if (c == '"')
            {
                if (i + 1 < data.Length && data[i + 1] == '"')
                {
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (c == character && !insideQuotes)
            {
                count++;
            }
        }
        return count;
    }

    private static double CalculateAverage(ReadOnlySpan<int> values)
    {
        if (values.Length == 0)
            return 0;

        long sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }
        return (double)sum / values.Length;
    }

    private static double CalculateStandardDeviation(ReadOnlySpan<int> values, double avg)
    {
        if (values.Length <= 1)
            return 0;

        double sumOfSquaredDifferences = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sumOfSquaredDifferences += Math.Pow(values[i] - avg, 2);
        }
        double variance = sumOfSquaredDifferences / values.Length;

        return Math.Sqrt(variance);
    }

    private static int CalculateConfidence(ReadOnlySpan<int> counts, double avg)
    {
        if (counts.Length <= 1)
            return 100; // Single row = 100% confidence by default

        if (avg == 0)
            return 0;

        double stdDev = CalculateStandardDeviation(counts, avg);

        // Confidence = 100% when stdDev is 0 (perfect consistency)
        // Confidence decreases as stdDev increases relative to average
        double variationCoefficient = stdDev / avg;

        // Map variation coefficient to confidence (0-100)
        // 0.0 variation = 100% confidence
        // 0.5 variation = 50% confidence
        // 1.0+ variation = 0% confidence
        int confidence = Math.Max(0, 100 - (int)(variationCoefficient * 100));

        return Math.Clamp(confidence, 0, 100);
    }
}
