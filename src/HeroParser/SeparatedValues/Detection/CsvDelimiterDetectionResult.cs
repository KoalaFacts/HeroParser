namespace HeroParser.SeparatedValues.Detection;

/// <summary>
/// Represents the result of automatic delimiter detection.
/// </summary>
/// <remarks>
/// Thread-Safety: This is an immutable record type and is safe to share across threads.
/// </remarks>
public sealed record CsvDelimiterDetectionResult
{
    /// <summary>
    /// Gets the detected delimiter character.
    /// </summary>
    public char DetectedDelimiter { get; init; }

    /// <summary>
    /// Gets the confidence score (0-100) for the detected delimiter.
    /// </summary>
    /// <remarks>
    /// Higher scores indicate more confidence in the detection.
    /// A score of 100 indicates perfect consistency across all sampled rows.
    /// A score below 50 indicates low confidence and manual verification is recommended.
    /// </remarks>
    public int Confidence { get; init; }

    /// <summary>
    /// Gets the average count of the detected delimiter per row.
    /// </summary>
    public double AverageDelimiterCount { get; init; }

    /// <summary>
    /// Gets the number of rows sampled for detection.
    /// </summary>
    public int SampledRows { get; init; }

    /// <summary>
    /// Gets the counts for all candidate delimiters found during detection.
    /// </summary>
    /// <remarks>
    /// This dictionary contains the total occurrences of each candidate delimiter
    /// across all sampled rows. Useful for understanding why a particular delimiter
    /// was chosen and for debugging detection issues.
    /// </remarks>
    public IReadOnlyDictionary<char, int> CandidateCounts { get; init; } = new Dictionary<char, int>();
}
