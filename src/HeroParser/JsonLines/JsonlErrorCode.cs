namespace HeroParser.JsonLines;

/// <summary>
/// Error codes for JSONL parsing and writing failures.
/// </summary>
public enum JsonlErrorCode
{
    /// <summary>
    /// Invalid configuration on <see cref="Reading.JsonlReadOptions"/> or <see cref="Writing.JsonlWriteOptions"/>.
    /// </summary>
    InvalidOptions = 1,

    /// <summary>
    /// A single JSONL line exceeded the configured maximum size.
    /// </summary>
    LineTooLong = 2,

    /// <summary>
    /// The maximum row count was exceeded.
    /// </summary>
    TooManyRows = 3,

    /// <summary>
    /// A line failed JSON deserialization.
    /// </summary>
    DeserializeError = 99,

    /// <summary>
    /// Output exceeded the configured maximum size during writing.
    /// </summary>
    OutputSizeExceeded = 100,

    /// <summary>
    /// A record failed JSON serialization during writing.
    /// </summary>
    SerializeError = 101
}
