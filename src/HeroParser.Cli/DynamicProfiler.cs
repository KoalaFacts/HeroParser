using System;
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
}

internal static class DynamicProfiler
{
    public static List<DynamicColumnStats> Analyze(string[] headers, List<string[]> rows)
    {
        var statsList = new List<DynamicColumnStats>();
        foreach (var header in headers)
        {
            statsList.Add(new DynamicColumnStats { Name = header });
        }

        foreach (var row in rows)
        {
            for (int i = 0; i < Math.Min(headers.Length, row.Length); i++)
            {
                var val = row[i];
                var stats = statsList[i];

                if (string.IsNullOrWhiteSpace(val))
                {
                    stats.NullCount++;
                }
                else
                {
                    stats.NonNullCount++;
                    ObserveValue(val, stats);
                }
            }

            // Mark missing fields as null
            for (int i = row.Length; i < headers.Length; i++)
            {
                statsList[i].NullCount++;
            }
        }

        return statsList;
    }

    public static string GenerateContextCard(string datasetName, string[] headers, List<string[]> rows)
    {
        var statsList = Analyze(headers, rows);
        return RenderMarkdownCard(datasetName, rows.Count, statsList);
    }

    private static void ObserveValue(string value, DynamicColumnStats stats)
    {
        // Check Boolean
        if (bool.TryParse(value, out var bVal))
        {
            stats.BoolCount++;
            if (bVal) stats.TrueCount++;
            else stats.FalseCount++;
            TrackCategory(value, stats);
            return;
        }

        // Check Integer (Int32)
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iVal))
        {
            stats.IntCount++;
            UpdateNumericRange(iVal, stats);
            return;
        }

        // Check Long (Int64)
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lVal))
        {
            stats.LongCount++;
            UpdateNumericRange(lVal, stats);
            return;
        }

        // Check Decimal/Double
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dVal))
        {
            stats.DecimalCount++;
            UpdateNumericRange(dVal, stats);
            return;
        }

        // Check Guid
        if (Guid.TryParse(value, out _))
        {
            stats.GuidCount++;
            TrackCategory(value, stats);
            return;
        }

        // Check DateTime
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            stats.DateTimeCount++;
            TrackCategory(value, stats);
            return;
        }

        // Fallback: String
        stats.StringCount++;
        TrackCategory(value, stats);
    }

    private static void UpdateNumericRange(double value, DynamicColumnStats stats)
    {
        if (value < stats.Min) stats.Min = value;
        if (value > stats.Max) stats.Max = value;
        stats.Sum += value;
    }

    private static void TrackCategory(string value, DynamicColumnStats stats)
    {
        // Limit unique tracker to 100 to avoid memory overflow for large fields
        if (stats.ValueCounts.Count >= 100 && !stats.ValueCounts.ContainsKey(value))
        {
            return;
        }

        stats.ValueCounts[value] = stats.ValueCounts.TryGetValue(value, out var count) ? count + 1 : 1;
    }

    private static string RenderMarkdownCard(string datasetName, int totalRows, List<DynamicColumnStats> statsList)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### Dataset Profile: {datasetName} ({totalRows:N0} rows)");

        foreach (var stats in statsList)
        {
            double nullPct = totalRows > 0 ? (double)stats.NullCount / totalRows * 100 : 0;
            string nullStr = stats.NullCount > 0 ? $", {nullPct:F1}% Null" : ", 0% Null";
            string typeName = InferTypeName(stats);

            sb.Append($"- **{stats.Name}** ({typeName}{nullStr}): ");

            if (totalRows == 0)
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
                double truePct = totalRows > 0 ? (double)stats.TrueCount / totalRows * 100 : 0;
                sb.AppendLine($"Boolean. True: {stats.TrueCount:N0} ({truePct:F1}%), False: {stats.FalseCount:N0} ({100 - truePct:F1}%).");
            }
            else
            {
                int distinctCount = stats.ValueCounts.Count;
                sb.Append($"{distinctCount:N0} distinct categories.");
                if (distinctCount > 0)
                {
                    var topValues = stats.ValueCounts.OrderByDescending(v => v.Value).Take(3).ToList();
                    sb.Append(" Top values: ");
                    for (int j = 0; j < topValues.Count; j++)
                    {
                        double pct = (double)topValues[j].Value / totalRows * 100;
                        sb.Append($"\"{topValues[j].Key}\" ({pct:F1}%)");
                        if (j < topValues.Count - 1) sb.Append(", ");
                    }
                }
                sb.AppendLine(".");
            }
        }

        return sb.ToString();
    }

    public static string InferTypeName(DynamicColumnStats stats)
    {
        if (stats.NonNullCount == 0)
            return "String";

        if (stats.StringCount > 0)
            return "String";

        if (stats.BoolCount > 0 && stats.IntCount == 0 && stats.LongCount == 0 && stats.DecimalCount == 0 && stats.DateTimeCount == 0 && stats.GuidCount == 0)
            return "Boolean";

        if (stats.GuidCount > 0 && stats.IntCount == 0 && stats.LongCount == 0 && stats.DecimalCount == 0 && stats.DateTimeCount == 0 && stats.BoolCount == 0)
            return "Guid";

        if (stats.DateTimeCount > 0 && stats.IntCount == 0 && stats.LongCount == 0 && stats.DecimalCount == 0 && stats.GuidCount == 0 && stats.BoolCount == 0)
            return "DateTime";

        if (stats.IntCount > 0 || stats.LongCount > 0 || stats.DecimalCount > 0)
        {
            if (stats.DecimalCount > 0)
                return "Decimal";
            return "Integer";
        }

        return "String";
    }
}
