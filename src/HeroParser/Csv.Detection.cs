using HeroParser.SeparatedValues.Detection;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Automatically detects the delimiter character used in CSV data.
    /// </summary>
    /// <param name="data">The CSV data to analyze.</param>
    /// <param name="sampleRows">Number of rows to sample for detection (default 10).</param>
    /// <returns>The detected delimiter character.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method samples the first N rows of the CSV data and analyzes common delimiters
    /// (comma, semicolon, pipe, tab) to determine which is most consistent across rows.
    /// </para>
    /// <para>
    /// For more detailed results including confidence scores, use <see cref="DetectDelimiterWithDetails(string, int)"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var csvData = File.ReadAllText("data.csv");
    /// char delimiter = Csv.DetectDelimiter(csvData);
    /// var records = Csv.Read&lt;MyRecord&gt;()
    ///     .WithDelimiter(delimiter)
    ///     .FromText(csvData);
    /// </code>
    /// </example>
    public static char DetectDelimiter(string data, int sampleRows = CsvDelimiterDetector.DEFAULT_SAMPLE_ROWS)
    {
        return CsvDelimiterDetector.DetectDelimiter(data, sampleRows);
    }

    /// <summary>
    /// Automatically detects the delimiter character used in CSV data.
    /// </summary>
    /// <param name="data">The CSV data to analyze (UTF-16).</param>
    /// <param name="sampleRows">Number of rows to sample for detection (default 10).</param>
    /// <returns>The detected delimiter character.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    public static char DetectDelimiter(ReadOnlySpan<char> data, int sampleRows = CsvDelimiterDetector.DEFAULT_SAMPLE_ROWS)
    {
        return CsvDelimiterDetector.DetectDelimiter(data, sampleRows);
    }

    /// <summary>
    /// Automatically detects the delimiter character used in UTF-8 encoded CSV data.
    /// </summary>
    /// <param name="data">The CSV data to analyze (UTF-8).</param>
    /// <param name="sampleRows">Number of rows to sample for detection (default 10).</param>
    /// <returns>The detected delimiter character.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    public static char DetectDelimiter(ReadOnlySpan<byte> data, int sampleRows = CsvDelimiterDetector.DEFAULT_SAMPLE_ROWS)
    {
        return CsvDelimiterDetector.DetectDelimiter(data, sampleRows);
    }

    /// <summary>
    /// Automatically detects the delimiter character and returns detailed results including confidence scores.
    /// </summary>
    /// <param name="data">The CSV data to analyze.</param>
    /// <param name="sampleRows">Number of rows to sample for detection (default 10).</param>
    /// <returns>Detailed detection results including confidence score and candidate counts.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    /// <remarks>
    /// Use this method when you need to assess the reliability of the detection.
    /// A confidence score below 50% indicates the detection may be unreliable and
    /// manual verification is recommended.
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = Csv.DetectDelimiterWithDetails(csvData);
    /// if (result.Confidence &lt; 50)
    /// {
    ///     Console.WriteLine($"Low confidence ({result.Confidence}%), please verify delimiter manually");
    /// }
    /// var records = Csv.Read&lt;MyRecord&gt;()
    ///     .WithDelimiter(result.DetectedDelimiter)
    ///     .FromText(csvData);
    /// </code>
    /// </example>
    public static CsvDelimiterDetectionResult DetectDelimiterWithDetails(string data, int sampleRows = CsvDelimiterDetector.DEFAULT_SAMPLE_ROWS)
    {
        return CsvDelimiterDetector.Detect(data, sampleRows);
    }

    /// <summary>
    /// Automatically detects the delimiter character and returns detailed results including confidence scores.
    /// </summary>
    /// <param name="data">The CSV data to analyze (UTF-16).</param>
    /// <param name="sampleRows">Number of rows to sample for detection (default 10).</param>
    /// <returns>Detailed detection results including confidence score and candidate counts.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable delimiter could be detected.
    /// </exception>
    public static CsvDelimiterDetectionResult DetectDelimiterWithDetails(ReadOnlySpan<char> data, int sampleRows = CsvDelimiterDetector.DEFAULT_SAMPLE_ROWS)
    {
        return CsvDelimiterDetector.Detect(data, sampleRows);
    }
}
