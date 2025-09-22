using System;
using System.Text;

namespace HeroParser.Configuration
{
    /// <summary>
    /// Immutable parser configuration with validated builder pattern and runtime optimization.
    /// Provides fluent API for configuring CSV and fixed-length parsing behavior.
    /// </summary>
    public sealed class ParserConfiguration
    {
        /// <summary>
        /// Gets the field delimiter character (default: comma).
        /// </summary>
        public char Delimiter { get; }

        /// <summary>
        /// Gets the quote character for escaping fields (default: double quote).
        /// </summary>
        public char QuoteCharacter { get; }

        /// <summary>
        /// Gets the escape character for literal quotes (default: double quote).
        /// </summary>
        public char EscapeCharacter { get; }

        /// <summary>
        /// Gets whether comments are allowed (lines starting with #).
        /// </summary>
        public bool AllowComments { get; }

        /// <summary>
        /// Gets whether to trim whitespace from field values.
        /// </summary>
        public bool TrimWhitespace { get; }

        /// <summary>
        /// Gets whether parallel processing is enabled for large files (>10MB).
        /// </summary>
        public bool EnableParallelProcessing { get; }

        /// <summary>
        /// Gets whether SIMD optimizations are enabled.
        /// </summary>
        public bool SIMDOptimization { get; }

        /// <summary>
        /// Gets the buffer size hint for optimal I/O operations.
        /// </summary>
        public int BufferSizeHint { get; }

        /// <summary>
        /// Gets the encoding used for text parsing (default: UTF-8).
        /// </summary>
        public Encoding Encoding { get; }

        /// <summary>
        /// Gets whether to enable strict RFC 4180 compliance mode.
        /// </summary>
        public bool StrictRfc4180Mode { get; }

        /// <summary>
        /// Gets whether to enable tolerant parsing with error recovery.
        /// </summary>
        public bool TolerantMode { get; }

        /// <summary>
        /// Gets the maximum number of errors to collect before halting.
        /// </summary>
        public int MaxErrors { get; }

        /// <summary>
        /// Gets whether to skip empty lines during parsing.
        /// </summary>
        public bool SkipEmptyLines { get; }

        /// <summary>
        /// Gets the threshold for switching to async processing (default: 1MB).
        /// </summary>
        public int AsyncThreshold { get; }

        private ParserConfiguration(Builder builder)
        {
            Delimiter = builder.Delimiter;
            QuoteCharacter = builder.QuoteCharacter;
            EscapeCharacter = builder.EscapeCharacter;
            AllowComments = builder.AllowComments;
            TrimWhitespace = builder.TrimWhitespace;
            EnableParallelProcessing = builder.EnableParallelProcessing;
            SIMDOptimization = builder.SIMDOptimization;
            BufferSizeHint = builder.BufferSizeHint;
            Encoding = builder.Encoding;
            StrictRfc4180Mode = builder.StrictRfc4180Mode;
            TolerantMode = builder.TolerantMode;
            MaxErrors = builder.MaxErrors;
            SkipEmptyLines = builder.SkipEmptyLines;
            AsyncThreshold = builder.AsyncThreshold;
        }

        /// <summary>
        /// Creates a new configuration builder with default settings.
        /// </summary>
        /// <returns>New configuration builder</returns>
        public static Builder CreateBuilder() => new Builder();

        /// <summary>
        /// Gets the default parser configuration optimized for performance.
        /// </summary>
        public static ParserConfiguration Default { get; } = CreateBuilder().Build();

        /// <summary>
        /// Gets a strict RFC 4180 compliant configuration.
        /// </summary>
        public static ParserConfiguration Rfc4180Strict { get; } = CreateBuilder()
            .WithStrictRfc4180Mode(true)
            .WithTolerantMode(false)
            .Build();

        /// <summary>
        /// Gets a high-performance configuration for large files.
        /// </summary>
        public static ParserConfiguration HighPerformance { get; } = CreateBuilder()
            .WithParallelProcessing(true)
            .WithSIMDOptimization(true)
            .WithBufferSizeHint(1024 * 1024) // 1MB buffers
            .Build();

        /// <summary>
        /// Fluent builder for creating immutable parser configurations.
        /// </summary>
        public sealed class Builder
        {
            internal char Delimiter { get; private set; } = ',';
            internal char QuoteCharacter { get; private set; } = '"';
            internal char EscapeCharacter { get; private set; } = '"';
            internal bool AllowComments { get; private set; } = false;
            internal bool TrimWhitespace { get; private set; } = true;
            internal bool EnableParallelProcessing { get; private set; } = false;
            internal bool SIMDOptimization { get; private set; } = true;
            internal int BufferSizeHint { get; private set; } = 64 * 1024; // 64KB default
            internal Encoding Encoding { get; private set; } = Encoding.UTF8;
            internal bool StrictRfc4180Mode { get; private set; } = false;
            internal bool TolerantMode { get; private set; } = true;
            internal int MaxErrors { get; private set; } = 100;
            internal bool SkipEmptyLines { get; private set; } = true;
            internal int AsyncThreshold { get; private set; } = 1024 * 1024; // 1MB

            internal Builder() { }

            /// <summary>
            /// Sets the field delimiter character.
            /// </summary>
            /// <param name="delimiter">Delimiter character</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithDelimiter(char delimiter)
            {
                Delimiter = delimiter;
                return this;
            }

            /// <summary>
            /// Sets the quote character for field escaping.
            /// </summary>
            /// <param name="quoteChar">Quote character</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithQuoteCharacter(char quoteChar)
            {
                QuoteCharacter = quoteChar;
                return this;
            }

            /// <summary>
            /// Sets the escape character for literal quotes.
            /// </summary>
            /// <param name="escapeChar">Escape character</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithEscapeCharacter(char escapeChar)
            {
                EscapeCharacter = escapeChar;
                return this;
            }

            /// <summary>
            /// Enables or disables comment support (lines starting with #).
            /// </summary>
            /// <param name="allowComments">Whether to allow comments</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithCommentsAllowed(bool allowComments)
            {
                AllowComments = allowComments;
                return this;
            }

            /// <summary>
            /// Enables or disables automatic whitespace trimming.
            /// </summary>
            /// <param name="trimWhitespace">Whether to trim whitespace</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithWhitespaceTrimming(bool trimWhitespace)
            {
                TrimWhitespace = trimWhitespace;
                return this;
            }

            /// <summary>
            /// Enables or disables parallel processing for large files.
            /// </summary>
            /// <param name="enabled">Whether to enable parallel processing</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithParallelProcessing(bool enabled)
            {
                EnableParallelProcessing = enabled;
                return this;
            }

            /// <summary>
            /// Enables or disables SIMD optimizations.
            /// </summary>
            /// <param name="enabled">Whether to enable SIMD</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithSIMDOptimization(bool enabled)
            {
                SIMDOptimization = enabled;
                return this;
            }

            /// <summary>
            /// Sets the buffer size hint for I/O operations.
            /// </summary>
            /// <param name="sizeHint">Buffer size in bytes</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithBufferSizeHint(int sizeHint)
            {
                if (sizeHint <= 0)
                    throw new ArgumentOutOfRangeException(nameof(sizeHint), "Buffer size must be positive");

                BufferSizeHint = sizeHint;
                return this;
            }

            /// <summary>
            /// Sets the text encoding for parsing.
            /// </summary>
            /// <param name="encoding">Text encoding</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithEncoding(Encoding encoding)
            {
                Encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
                return this;
            }

            /// <summary>
            /// Enables or disables strict RFC 4180 compliance mode.
            /// </summary>
            /// <param name="strict">Whether to enforce strict compliance</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithStrictRfc4180Mode(bool strict)
            {
                StrictRfc4180Mode = strict;
                return this;
            }

            /// <summary>
            /// Enables or disables tolerant parsing with error recovery.
            /// </summary>
            /// <param name="tolerant">Whether to enable tolerant mode</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithTolerantMode(bool tolerant)
            {
                TolerantMode = tolerant;
                return this;
            }

            /// <summary>
            /// Sets the maximum number of errors before halting.
            /// </summary>
            /// <param name="maxErrors">Maximum error count</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithMaxErrors(int maxErrors)
            {
                if (maxErrors < 0)
                    throw new ArgumentOutOfRangeException(nameof(maxErrors), "Max errors cannot be negative");

                MaxErrors = maxErrors;
                return this;
            }

            /// <summary>
            /// Enables or disables skipping of empty lines.
            /// </summary>
            /// <param name="skipEmptyLines">Whether to skip empty lines</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithSkipEmptyLines(bool skipEmptyLines)
            {
                SkipEmptyLines = skipEmptyLines;
                return this;
            }

            /// <summary>
            /// Sets the threshold for switching to async processing.
            /// </summary>
            /// <param name="threshold">Async threshold in bytes</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithAsyncThreshold(int threshold)
            {
                if (threshold <= 0)
                    throw new ArgumentOutOfRangeException(nameof(threshold), "Async threshold must be positive");

                AsyncThreshold = threshold;
                return this;
            }

            /// <summary>
            /// Analyzes input characteristics and automatically optimizes configuration.
            /// </summary>
            /// <param name="estimatedFileSize">Estimated input size in bytes</param>
            /// <param name="estimatedRecordCount">Estimated number of records</param>
            /// <returns>Builder for chaining</returns>
            public Builder WithAutoOptimization(long estimatedFileSize, long estimatedRecordCount = 0)
            {
                // Enable parallel processing for files > 10MB
                if (estimatedFileSize > 10 * 1024 * 1024)
                {
                    EnableParallelProcessing = true;
                }

                // Optimize buffer size based on file size
                if (estimatedFileSize > 100 * 1024 * 1024) // > 100MB
                {
                    BufferSizeHint = 1024 * 1024; // 1MB buffers
                }
                else if (estimatedFileSize > 10 * 1024 * 1024) // > 10MB
                {
                    BufferSizeHint = 256 * 1024; // 256KB buffers
                }

                return this;
            }

            /// <summary>
            /// Builds the immutable configuration with validation.
            /// </summary>
            /// <returns>Validated parser configuration</returns>
            public ParserConfiguration Build()
            {
                ValidateConfiguration();
                return new ParserConfiguration(this);
            }

            /// <summary>
            /// Validates the configuration settings for consistency.
            /// </summary>
            private void ValidateConfiguration()
            {
                if (Delimiter == QuoteCharacter)
                    throw new InvalidOperationException("Delimiter and quote character cannot be the same");

                if (BufferSizeHint < 1024)
                    throw new InvalidOperationException("Buffer size hint must be at least 1KB");

                if (StrictRfc4180Mode && TolerantMode)
                    throw new InvalidOperationException("Cannot enable both strict RFC 4180 mode and tolerant mode");

                if (StrictRfc4180Mode && AllowComments)
                    throw new InvalidOperationException("Comments are not allowed in strict RFC 4180 mode");
            }
        }
    }
}