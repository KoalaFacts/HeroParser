namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Cell type as indicated by the 't' attribute in .xlsx cell elements.
/// </summary>
internal enum XlsxCellType
{
    /// <summary>Numeric value (default, no t attribute or t="n").</summary>
    Number,

    /// <summary>Index into shared string table (t="s").</summary>
    SharedString,

    /// <summary>Inline string value (t="inlineStr").</summary>
    InlineString,

    /// <summary>Boolean value (t="b").</summary>
    Boolean,

    /// <summary>Error value (t="e").</summary>
    Error,

    /// <summary>Formula string result (t="str").</summary>
    String
}
