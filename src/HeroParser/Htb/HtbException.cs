namespace HeroParser.Htbs;

/// <summary>
/// Error codes representing specific types of failures during HTB processing.
/// </summary>
public enum HtbErrorCode
{
    /// <summary>
    /// The file header magic bytes are missing or corrupt.
    /// </summary>
    InvalidHeader,

    /// <summary>
    /// The HTB file version is not supported by this version of HeroParser.
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// The stream structure does not match the target C# record schema.
    /// </summary>
    SchemaMismatch,

    /// <summary>
    /// A value in the binary stream could not be deserialized.
    /// </summary>
    DeserializationError,

    /// <summary>
    /// A record value could not be serialized to binary.
    /// </summary>
    SerializationError,

    /// <summary>
    /// An input limit (e.g. MaxRowCount or MaxBufferLength) was exceeded.
    /// </summary>
    LimitExceeded,

    /// <summary>
    /// The binary structure is corrupt or truncated mid-row.
    /// </summary>
    CorruptData
}

/// <summary>
/// Exception thrown during High-Throughput Tabular Binary (HTB) reading and writing operations.
/// </summary>
public sealed class HtbException : Exception
{
    /// <summary>
    /// Gets the specific error code associated with this exception.
    /// </summary>
    public HtbErrorCode ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="HtbException"/>.
    /// </summary>
    public HtbException(HtbErrorCode errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="HtbException"/> with an inner exception.
    /// </summary>
    public HtbException(HtbErrorCode errorCode, string message, Exception innerException) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
