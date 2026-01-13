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
    private static readonly char[] s_candidateDelimiters = [',', ';', '|', '\t'];

    /// <summary>
    /// Default number of rows to sample for delimiter detection.
    /// </summary>
    public const int DEFAULT_SAMPLE_ROWS = 10;

    /// <summary>
    /// Detects the most likely delimiter character in the CSV data.
    /// </summary>
    /// <param name="data">The CSV data to analyze (UTF-16).</param>
    /// <param name="sampleRows">Number of rows to sample (default 10).</param>
    /// <returns>The detected delimiter character.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    /// <example>
    /// <code>
    /// var csv = "Name;Age;City\nJohn;30;NYC\nJane;25;LA";
    /// char delimiter = CsvDelimiterDetector.DetectDelimiter(csv);
    /// // Returns ';'
    /// </code>
    /// </example>
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
        var result = Detect(data, sampleRows);
        return result.DetectedDelimiter;
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
        // Decode UTF-8 to UTF-16 for analysis
        // For delimiter detection, we only need to decode a small sample
        var charCount = Encoding.UTF8.GetCharCount(data);
        Span<char> chars = charCount <= 4096
            ? stackalloc char[charCount]
            : new char[charCount];

        Encoding.UTF8.GetChars(data, chars);
        return DetectDelimiter(chars, sampleRows);
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
    /// <example>
    /// <code>
    /// var result = CsvDelimiterDetector.Detect(csvData);
    /// if (result.Confidence &lt; 50)
    /// {
    ///     Console.WriteLine($"Low confidence ({result.Confidence}%), manual verification recommended");
    /// }
    /// </code>
    /// </example>
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
        if (sampleRows < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleRows), "Sample rows must be at least 1");

        if (data.IsEmpty)
        {
            throw new InvalidOperationException(
                "Cannot detect delimiter from empty data");
        }

        // Analyze delimiters in a single pass without allocating row storage
        var (delimiterStats, rowCount) = AnalyzeDelimitersInSinglePass(data, sampleRows);

        if (rowCount == 0)
        {
            throw new InvalidOperationException(
                "Cannot detect delimiter: no rows found in data");
        }

        // Select the best delimiter based on consistency
        var bestDelimiter = SelectBestDelimiter(delimiterStats, rowCount);

        if (!bestDelimiter.HasValue)
        {
            throw new InvalidOperationException(
                "Cannot detect delimiter: no consistent delimiter pattern found. " +
                "The data may not be properly delimited or may require manual delimiter specification.");
        }

        // Calculate confidence and prepare result
        var stats = delimiterStats[bestDelimiter.Value];
        var confidence = CalculateConfidence(stats.CountsPerRow);
        var avgCount = stats.CountsPerRow.Count > 0
            ? stats.CountsPerRow.Average()
            : 0;

        // Build candidate counts dictionary
        var candidateCounts = new Dictionary<char, int>();
        foreach (var kvp in delimiterStats)
        {
            candidateCounts[kvp.Key] = kvp.Value.TotalCount;
        }

        return new CsvDelimiterDetectionResult
        {
            DetectedDelimiter = bestDelimiter.Value,
            Confidence = confidence,
            AverageDelimiterCount = avgCount,
            SampledRows = rowCount,
            CandidateCounts = candidateCounts
        };
    }

    private static (Dictionary<char, DelimiterStats> stats, int rowCount) AnalyzeDelimitersInSinglePass(
        ReadOnlySpan<char> data,
        int maxRows)
    {
        var stats = new Dictionary<char, DelimiterStats>();

        // Initialize stats for each candidate
        foreach (var delimiter in s_candidateDelimiters)
        {
            stats[delimiter] = new DelimiterStats();
        }

        int rowStart = 0;
        int rowCount = 0;

        for (int i = 0; i < data.Length && rowCount < maxRows; i++)
        {
            bool isLineEnd = false;
            int rowEnd = i;

            // Check for line ending (LF, CR, or CRLF)
            if (data[i] == '\n')
            {
                rowEnd = i;
                // Trim trailing CR if present (CRLF case)
                if (rowEnd > rowStart && data[rowEnd - 1] == '\r')
                    rowEnd--;
                isLineEnd = true;
            }
            else if (data[i] == '\r' && (i + 1 >= data.Length || data[i + 1] != '\n'))
            {
                // CR without LF (old Mac format)
                rowEnd = i;
                isLineEnd = true;
            }

            if (isLineEnd)
            {
                var rowLength = rowEnd - rowStart;
                if (rowLength > 0) // Skip empty lines
                {
                    var row = data.Slice(rowStart, rowLength);

                    // Count each candidate delimiter in this row
                    foreach (var delimiter in s_candidateDelimiters)
                    {
                        int count = CountOccurrences(row, delimiter);
                        stats[delimiter].CountsPerRow.Add(count);
                        stats[delimiter].TotalCount += count;
                    }

                    rowCount++;
                }

                rowStart = i + 1;
            }
        }

        // Process last row if data doesn't end with newline
        if (rowStart < data.Length && rowCount < maxRows)
        {
            var rowLength = data.Length - rowStart;
            if (rowLength > 0)
            {
                var row = data.Slice(rowStart, rowLength);

                foreach (var delimiter in s_candidateDelimiters)
                {
                    int count = CountOccurrences(row, delimiter);
                    stats[delimiter].CountsPerRow.Add(count);
                    stats[delimiter].TotalCount += count;
                }

                rowCount++;
            }
        }

        return (stats, rowCount);
    }

    private static int CountOccurrences(ReadOnlySpan<char> data, char character)
    {
        int count = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == character)
                count++;
        }
        return count;
    }

    private static char? SelectBestDelimiter(Dictionary<char, DelimiterStats> stats, int rowCount)
    {
        char? bestDelimiter = null;
        double bestScore = double.MinValue;

        foreach (var kvp in stats)
        {
            var delimiter = kvp.Key;
            var delimiterStats = kvp.Value;

            // Skip delimiters that never appear
            if (delimiterStats.TotalCount == 0)
                continue;

            // Skip delimiters that don't appear in all rows (inconsistent)
            var nonZeroRows = delimiterStats.CountsPerRow.Count(c => c > 0);
            if (nonZeroRows < rowCount * 0.8) // Allow 20% tolerance for header/footer rows
                continue;

            // Calculate consistency score
            // Score = average count * consistency factor
            // Higher average count is better (more columns)
            // Lower standard deviation is better (more consistent)
            var avg = delimiterStats.CountsPerRow.Average();
            var stdDev = CalculateStandardDeviation(delimiterStats.CountsPerRow);

            // Avoid division by zero
            var consistencyFactor = avg > 0 ? avg / (stdDev + 1.0) : 0;

            // Weighted score: favor consistency over count
            var score = avg * 0.3 + consistencyFactor * 0.7;

            if (score > bestScore)
            {
                bestScore = score;
                bestDelimiter = delimiter;
            }
        }

        return bestDelimiter;
    }

    private static int CalculateConfidence(List<int> counts)
    {
        if (counts.Count <= 1)
            return 100; // Single row = 100% confidence by default

        var avg = counts.Average();
        if (avg == 0)
            return 0;

        var stdDev = CalculateStandardDeviation(counts);

        // Confidence = 100% when stdDev is 0 (perfect consistency)
        // Confidence decreases as stdDev increases relative to average
        var variationCoefficient = stdDev / avg;

        // Map variation coefficient to confidence (0-100)
        // 0.0 variation = 100% confidence
        // 0.5 variation = 50% confidence
        // 1.0+ variation = 0% confidence
        var confidence = Math.Max(0, 100 - (int)(variationCoefficient * 100));

        return Math.Clamp(confidence, 0, 100);
    }

    private static double CalculateStandardDeviation(List<int> values)
    {
        if (values.Count <= 1)
            return 0;

        var avg = values.Average();
        var sumOfSquaredDifferences = values.Sum(val => Math.Pow(val - avg, 2));
        var variance = sumOfSquaredDifferences / values.Count;

        return Math.Sqrt(variance);
    }

    private sealed class DelimiterStats
    {
        public List<int> CountsPerRow { get; } = [];
        public int TotalCount { get; set; }
    }
}
