namespace HeroParser.Excels.Core;

/// <summary>
/// Specifies how to handle potential formula-injection (CSV/spreadsheet injection) attacks
/// when writing string values to an .xlsx file.
/// </summary>
/// <remarks>
/// Spreadsheet applications interpret cells whose text starts with <c>=</c>, <c>+</c>, <c>-</c>,
/// <c>@</c>, tab (<c>\t</c>), or carriage return (<c>\r</c>) as formulas. A malicious value
/// such as <c>=cmd|'/c calc'!A1</c> can trigger code execution when the workbook is opened.
/// </remarks>
public enum ExcelInjectionProtection
{
    /// <summary>
    /// No protection — values are written verbatim.
    /// </summary>
    /// <remarks>
    /// Only safe when the value source is fully trusted.
    /// </remarks>
    None = 0,

    /// <summary>
    /// Prefix dangerous values with a single apostrophe (Excel's literal-text marker).
    /// </summary>
    /// <remarks>
    /// Example: <c>=SUM(A1)</c> becomes <c>'=SUM(A1)</c>. The apostrophe itself is not displayed
    /// in the cell when opened in Excel, but it is stored in the underlying shared string.
    /// </remarks>
    EscapeWithApostrophe = 1,

    /// <summary>
    /// Strip dangerous leading characters from values.
    /// </summary>
    /// <remarks>
    /// Example: <c>=SUM(A1)</c> becomes <c>SUM(A1)</c>. Lossy — modifies the data.
    /// </remarks>
    Sanitize = 2,

    /// <summary>
    /// Throw an exception when a dangerous prefix is detected.
    /// </summary>
    Reject = 3
}
