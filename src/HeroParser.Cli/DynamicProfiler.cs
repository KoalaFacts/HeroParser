using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HeroParser.Cli;

internal sealed class DynamicColumnStats
{
    public string Name { get; set; } = string.Empty;
    public int NullCount { get; set; }
    public int NonNullCount { get; set; }

    // Type observations
    public int IntCount { get; set; }
    public int LongCount { get; set; }
    public int DecimalCount { get; set; }
    public int BoolCount { get; set; }
    public int DateTimeCount { get; set; }
    public int GuidCount { get; set; }
    public int StringCount { get; set; }

    // Numeric ranges
    public double Min { get; set; } = double.MaxValue;
    public double Max { get; set; } = double.MinValue;
    public double Sum { get; set; }

    // Boolean counts
    public int TrueCount { get; set; }
    public int FalseCount { get; set; }

    // Categorical frequency
    public Dictionary<string, int> ValueCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool CategoriesTruncated { get; set; }

    internal Utf8ProfileCache? Utf8Cache;

    public void Observe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            NullCount++;
        else
        {
            NonNullCount++;
            ObserveValue(value);
        }
    }

    public void ObserveUtf8(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            NullCount++;
            return;
        }

        if (Utf8Cache is { } cache && ValueCounts.Count <= 16 &&
            cache.TryGet(value, out var cachedKind, out var cachedText))
        {
            ReplayCachedValue(cachedKind, cachedText);
            return;
        }

        byte first = value[0];
        if (first is (>= (byte)'0' and <= (byte)'9') or (byte)'+' or (byte)'-' or (byte)'.')
        {
            if (Utf8Parser.TryParse(value, out int intValue, out int consumed) && consumed == value.Length)
            {
                NonNullCount++;
                IntCount++;
                UpdateNumericRange(intValue);
                return;
            }

            if (Utf8Parser.TryParse(value, out long longValue, out consumed) && consumed == value.Length)
            {
                NonNullCount++;
                LongCount++;
                UpdateNumericRange(longValue);
                return;
            }

            if (Utf8Parser.TryParse(value, out double doubleValue, out consumed) && consumed == value.Length)
            {
                NonNullCount++;
                DecimalCount++;
                UpdateNumericRange(doubleValue);
                return;
            }
        }

        string text = Encoding.UTF8.GetString(value);
        ProfileValueKind kind;
        if (string.IsNullOrWhiteSpace(text))
        {
            NullCount++;
            kind = ProfileValueKind.Null;
        }
        else
        {
            NonNullCount++;
            kind = ObserveValue(text);
        }

        if (kind != ProfileValueKind.Uncached && value.Length <= 256 &&
            ValueCounts.Count <= 16 && !CategoriesTruncated)
        {
            (Utf8Cache ??= new Utf8ProfileCache()).Store(value, kind, text);
        }
    }

    public string InferTypeName()
    {
        if (NonNullCount == 0)
            return "String";

        if (StringCount > 0)
            return "String";

        if (BoolCount > 0 && IntCount == 0 && LongCount == 0 && DecimalCount == 0 && DateTimeCount == 0 && GuidCount == 0)
            return "Boolean";

        if (GuidCount > 0 && IntCount == 0 && LongCount == 0 && DecimalCount == 0 && DateTimeCount == 0 && BoolCount == 0)
            return "Guid";

        if (DateTimeCount > 0 && IntCount == 0 && LongCount == 0 && DecimalCount == 0 && GuidCount == 0 && BoolCount == 0)
            return "DateTime";

        if (IntCount > 0 || LongCount > 0 || DecimalCount > 0)
        {
            if (DecimalCount > 0)
                return "Decimal";
            return "Integer";
        }

        return "String";
    }

    private void ReplayCachedValue(ProfileValueKind kind, string? text)
    {
        switch (kind)
        {
            case ProfileValueKind.Null:
                NullCount++;
                return;
            case ProfileValueKind.True:
                BoolCount++;
                TrueCount++;
                break;
            case ProfileValueKind.False:
                BoolCount++;
                FalseCount++;
                break;
            case ProfileValueKind.Guid:
                GuidCount++;
                break;
            case ProfileValueKind.DateTime:
                DateTimeCount++;
                break;
            case ProfileValueKind.String:
                StringCount++;
                break;
            case ProfileValueKind.Uncached:
            default:
                throw new InvalidOperationException("Only classified non-null values can be replayed.");
        }
        NonNullCount++;
        TrackCategory(text!);
    }

    private ProfileValueKind ObserveValue(string value)
    {
        // Check Boolean
        if (bool.TryParse(value, out var bVal))
        {
            BoolCount++;
            if (bVal) TrueCount++;
            else FalseCount++;
            TrackCategory(value);
            return bVal ? ProfileValueKind.True : ProfileValueKind.False;
        }

        // Check Integer (Int32)
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iVal))
        {
            IntCount++;
            UpdateNumericRange(iVal);
            return ProfileValueKind.Uncached;
        }

        // Check Long (Int64)
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lVal))
        {
            LongCount++;
            UpdateNumericRange(lVal);
            return ProfileValueKind.Uncached;
        }

        // Check Decimal/Double
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dVal))
        {
            DecimalCount++;
            UpdateNumericRange(dVal);
            return ProfileValueKind.Uncached;
        }

        // Check Guid
        if (Guid.TryParse(value, out _))
        {
            GuidCount++;
            TrackCategory(value);
            return ProfileValueKind.Guid;
        }

        // Check DateTime
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            DateTimeCount++;
            TrackCategory(value);
            return ProfileValueKind.DateTime;
        }

        // Fallback: String
        StringCount++;
        TrackCategory(value);
        return ProfileValueKind.String;
    }

    private void UpdateNumericRange(double value)
    {
        if (value < Min) Min = value;
        if (value > Max) Max = value;
        Sum += value;
    }

    private void TrackCategory(string value)
    {
        // The profile retains bounded examples, not an exact distinct-count index.
        if (value.Length > 256 || (ValueCounts.Count >= 100 && !ValueCounts.ContainsKey(value)))
        {
            CategoriesTruncated = true;
            return;
        }

        ValueCounts[value] = ValueCounts.TryGetValue(value, out var count) ? count + 1 : 1;
    }
}

internal sealed class DynamicProfiler
{
    public List<DynamicColumnStats> Columns { get; }
    public int TotalRows { get; private set; }

    public DynamicProfiler(string[] headers)
    {
        Columns = [.. headers.Select(header => new DynamicColumnStats { Name = header })];
    }

    public DynamicProfiler(List<DynamicColumnStats> columns, int totalRows)
    {
        Columns = columns;
        TotalRows = totalRows;
    }

    public void ObserveRow(string[] row)
    {
        for (int i = 0; i < Columns.Count; i++)
            Columns[i].Observe(i < row.Length ? row[i] : null);
        TotalRows++;
    }

    public void ObserveRows(IEnumerable<string[]> rows)
    {
        foreach (var row in rows)
            ObserveRow(row);
    }

    public void ObserveCell(int columnIndex, string? value) => Columns[columnIndex].Observe(value);

    public void ObserveCellUtf8(int columnIndex, ReadOnlySpan<byte> value) => Columns[columnIndex].ObserveUtf8(value);

    public string GenerateContextCard(string datasetName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### Dataset Profile: {datasetName} ({TotalRows:N0} rows)");

        foreach (var stats in Columns)
        {
            double nullPct = TotalRows > 0 ? (double)stats.NullCount / TotalRows * 100 : 0;
            string nullStr = stats.NullCount > 0 ? $", {nullPct:F1}% Null" : ", 0% Null";
            string typeName = stats.InferTypeName();

            sb.Append($"- **{stats.Name}** ({typeName}{nullStr}): ");

            if (TotalRows == 0)
            {
                sb.AppendLine("No data available.");
                continue;
            }

            if (typeName == "Integer" || typeName == "Decimal")
            {
                double minVal = stats.Min == double.MaxValue ? 0 : stats.Min;
                double maxVal = stats.Max == double.MinValue ? 0 : stats.Max;
                double avg = stats.NonNullCount > 0 ? stats.Sum / stats.NonNullCount : 0;
                sb.AppendLine($"Numeric range [{minVal:N2} to {maxVal:N2}], Avg: {avg:N2}.");
            }
            else if (typeName == "Boolean")
            {
                double truePct = TotalRows > 0 ? (double)stats.TrueCount / TotalRows * 100 : 0;
                sb.AppendLine($"Boolean. True: {stats.TrueCount:N0} ({truePct:F1}%), False: {stats.FalseCount:N0} ({100 - truePct:F1}%).");
            }
            else
            {
                int distinctCount = stats.ValueCounts.Count;
                sb.Append(stats.CategoriesTruncated
                    ? $"At least {distinctCount:N0} distinct categories tracked."
                    : $"{distinctCount:N0} distinct categories.");
                if (distinctCount > 0)
                {
                    var topValues = stats.ValueCounts.OrderByDescending(v => v.Value).Take(3).ToList();
                    sb.Append(stats.CategoriesTruncated ? " Top tracked values: " : " Top values: ");
                    for (int j = 0; j < topValues.Count; j++)
                    {
                        double pct = (double)topValues[j].Value / TotalRows * 100;
                        sb.Append($"\"{topValues[j].Key}\" ({pct:F1}%)");
                        if (j < topValues.Count - 1) sb.Append(", ");
                    }
                }
                sb.AppendLine(".");
            }
        }

        return sb.ToString();
    }
}
