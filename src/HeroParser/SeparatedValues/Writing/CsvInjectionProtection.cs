namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// Specifies how to handle potential CSV injection (formula injection) attacks when writing CSV data.
/// </summary>
/// <remarks>
/// CSV injection occurs when user-supplied data begins with characters like <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>,
/// tab (<c>\t</c>), or carriage return (<c>\r</c>) that spreadsheet applications interpret as formulas.
/// This can lead to security vulnerabilities when CSV files are opened in Excel or Google Sheets.
/// </remarks>
public enum CsvInjectionProtection
{
    /// <summary>
    /// No injection protection (default for backward compatibility).
    /// </summary>
    /// <remarks>
    /// Use this when you trust the data source or when the CSV will not be opened in spreadsheet applications.
    /// </remarks>
    None = 0,

    /// <summary>
    /// Prefix dangerous values with a single quote character inside quotes.
    /// </summary>
    /// <remarks>
    /// <para>Example: <c>=SUM(A1)</c> becomes <c>"'=SUM(A1)"</c></para>
    /// <para>This prevents formula execution while preserving the original data (minus the quote prefix).</para>
    /// </remarks>
    EscapeWithQuote = 1,

    /// <summary>
    /// Prefix dangerous values with a tab character inside quotes (OWASP recommended).
    /// </summary>
    /// <remarks>
    /// <para>Example: <c>=SUM(A1)</c> becomes <c>"	=SUM(A1)"</c> (tab + original)</para>
    /// <para>This is the OWASP-recommended approach for CSV injection prevention.</para>
    /// </remarks>
    EscapeWithTab = 2,

    /// <summary>
    /// Strip dangerous leading characters from values.
    /// </summary>
    /// <remarks>
    /// <para>Example: <c>=SUM(A1)</c> becomes <c>SUM(A1)</c></para>
    /// <para>Warning: This modifies the original data and may cause data loss.</para>
    /// </remarks>
    Sanitize = 3,

    /// <summary>
    /// Throw an exception when injection patterns are detected.
    /// </summary>
    /// <remarks>
    /// <para>Use this for strict security requirements where any potentially dangerous data should be rejected.</para>
    /// <para>Throws <see cref="CsvException"/> with <see cref="CsvErrorCode.InjectionDetected"/>.</para>
    /// </remarks>
    Reject = 4
}
