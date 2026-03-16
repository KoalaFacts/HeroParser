namespace HeroParser;

/// <summary>
/// Entry point for reading and writing Excel (.xlsx) files.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reading:</strong> Use <c>Excel.Read&lt;T&gt;()</c> for typed record reading,
/// <c>Excel.Read()</c> for row-level string-array reading, or <c>Excel.CreateDataReader()</c>
/// for streaming bulk-load via <see cref="System.Data.Common.DbDataReader"/>.
/// </para>
/// <para>
/// <strong>Writing:</strong> Use <c>Excel.Write&lt;T&gt;()</c> for a fluent single-sheet writer,
/// <c>Excel.WriteMultiSheet()</c> for a fluent multi-sheet writer, or the static convenience
/// methods <c>Excel.WriteToFile</c>, <c>Excel.WriteToStream</c>, and <c>Excel.SerializeRecords</c>
/// for one-liner writes with default options.
/// </para>
/// </remarks>
public static partial class Excel { }
