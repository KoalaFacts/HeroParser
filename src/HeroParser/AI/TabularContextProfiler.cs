using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.AI;

/// <summary>
/// Provides zero-allocation, reflection-safe statistics profiling for streams of tabular records to generate markdown system prompt cards for LLMs.
/// </summary>
public static class TabularContextProfiler
{
    private sealed class ColumnStats
    {
        public string Name { get; set; } = string.Empty;
        public Type Type { get; set; } = typeof(object);
        public int NullCount { get; set; }

        // Numeric Stats
        public double Min { get; set; } = double.MaxValue;
        public double Max { get; set; } = double.MinValue;
        public double Sum { get; set; }
        public bool IsNumeric { get; set; }

        // Boolean Stats
        public int TrueCount { get; set; }
        public int FalseCount { get; set; }
        public bool IsBoolean { get; set; }

        // Categorical Stats
        public Dictionary<string, int> ValueCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsCategorical { get; set; }
    }

    /// <summary>
    /// Asynchronously profiles a stream of records and generates a Markdown Context Card describing type distributions, ranges, and densities.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The asynchronous stream of records.</param>
    /// <param name="datasetName">Optional display name of the dataset. Defaults to the class name of <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown context card summarizing the dataset.</returns>
    public static async Task<string> GenerateContextCardAsync<T>(
        this IAsyncEnumerable<T> source,
        string? datasetName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var statsList = InitializeStats(properties);

        int totalRows = 0;
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (item == null) continue;
            totalRows++;
            ProcessItem(item, properties, statsList);
        }

        return RenderContextCard<T>(totalRows, statsList, datasetName);
    }

    /// <summary>
    /// Synchronously profiles a collection of records and generates a Markdown Context Card describing type distributions, ranges, and densities.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The collection of records.</param>
    /// <param name="datasetName">Optional display name of the dataset. Defaults to the class name of <typeparamref name="T"/>.</param>
    /// <returns>A Markdown context card summarizing the dataset.</returns>
    public static string GenerateContextCard<T>(
        this IEnumerable<T> source,
        string? datasetName = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var statsList = InitializeStats(properties);

        int totalRows = 0;
        foreach (var item in source)
        {
            if (item == null) continue;
            totalRows++;
            ProcessItem(item, properties, statsList);
        }

        return RenderContextCard<T>(totalRows, statsList, datasetName);
    }

    private static List<ColumnStats> InitializeStats(PropertyInfo[] properties)
    {
        var statsList = new List<ColumnStats>();
        foreach (var prop in properties)
        {
            // Resolve column name from TabularMap or fallback to property name
            string name = prop.GetCustomAttribute<TabularMapAttribute>() is { } map && !string.IsNullOrWhiteSpace(map.Name)
                ? map.Name
                : prop.Name;

            var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            bool isNumeric = type == typeof(int) || type == typeof(long) || type == typeof(double) ||
                             type == typeof(float) || type == typeof(decimal) || type == typeof(short) || type == typeof(byte);
            bool isBoolean = type == typeof(bool);
            bool isCategorical = type == typeof(string) || type.IsEnum || type == typeof(char);

            statsList.Add(new ColumnStats
            {
                Name = name,
                Type = prop.PropertyType,
                IsNumeric = isNumeric,
                IsBoolean = isBoolean,
                IsCategorical = isCategorical
            });
        }
        return statsList;
    }

    private static void ProcessItem<T>(T item, PropertyInfo[] properties, List<ColumnStats> statsList)
    {
        for (int i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            var stats = statsList[i];
            var val = prop.GetValue(item);

            if (val == null)
            {
                stats.NullCount++;
                continue;
            }

            if (stats.IsNumeric)
            {
                double numVal = Convert.ToDouble(val);
                if (numVal < stats.Min) stats.Min = numVal;
                if (numVal > stats.Max) stats.Max = numVal;
                stats.Sum += numVal;
            }
            else if (stats.IsBoolean)
            {
                if ((bool)val) stats.TrueCount++;
                else stats.FalseCount++;
            }
            else if (stats.IsCategorical)
            {
                string strVal = val.ToString() ?? string.Empty;
                stats.ValueCounts[strVal] = stats.ValueCounts.TryGetValue(strVal, out int count) ? count + 1 : 1;
            }
        }
    }

    private static string RenderContextCard<T>(int totalRows, List<ColumnStats> statsList, string? datasetName)
    {
        // Render Markdown Context Card
        var sb = new StringBuilder();
        datasetName ??= typeof(T).Name;
        sb.AppendLine($"### Dataset Profile: {datasetName} ({totalRows:N0} rows)");

        foreach (var stats in statsList)
        {
            double nullPct = totalRows > 0 ? (double)stats.NullCount / totalRows * 100 : 0;
            string nullStr = stats.NullCount > 0 ? $", {nullPct:F1}% Null" : ", 0% Null";

            sb.Append($"- **{stats.Name}** ({GetFriendlyTypeName(stats.Type)}{nullStr}): ");

            if (totalRows == 0)
            {
                sb.AppendLine("No data available.");
                continue;
            }

            if (stats.IsNumeric)
            {
                double minVal = stats.Min == double.MaxValue ? 0 : stats.Min;
                double maxVal = stats.Max == double.MinValue ? 0 : stats.Max;
                double avg = totalRows - stats.NullCount > 0 ? stats.Sum / (totalRows - stats.NullCount) : 0;
                sb.AppendLine($"Numeric range [{minVal:N2} to {maxVal:N2}], Avg: {avg:N2}.");
            }
            else if (stats.IsBoolean)
            {
                double truePct = totalRows > 0 ? (double)stats.TrueCount / totalRows * 100 : 0;
                sb.AppendLine($"Boolean. True: {stats.TrueCount:N0} ({truePct:F1}%), False: {stats.FalseCount:N0} ({100 - truePct:F1}%).");
            }
            else if (stats.IsCategorical)
            {
                int distinctCount = stats.ValueCounts.Count;
                sb.Append($"{distinctCount:N0} distinct categories.");
                if (distinctCount > 0)
                {
                    var topValues = new List<KeyValuePair<string, int>>(stats.ValueCounts);
                    topValues.Sort((x, y) => y.Value.CompareTo(x.Value));

                    sb.Append(" Top values: ");
                    int limit = Math.Min(3, topValues.Count);
                    for (int j = 0; j < limit; j++)
                    {
                        double pct = (double)topValues[j].Value / totalRows * 100;
                        sb.Append($"\"{topValues[j].Key}\" ({pct:F1}%)");
                        if (j < limit - 1) sb.Append(", ");
                    }
                }
                sb.AppendLine(".");
            }
            else
            {
                sb.AppendLine("Complex or other type.");
            }
        }

        return sb.ToString();
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        string name = underlying.Name;
        if (underlying == typeof(string)) name = "String";
        else if (underlying == typeof(int)) name = "Integer";
        else if (underlying == typeof(long)) name = "Integer";
        else if (underlying == typeof(double)) name = "Decimal";
        else if (underlying == typeof(float)) name = "Decimal";
        else if (underlying == typeof(decimal)) name = "Decimal";
        else if (underlying == typeof(bool)) name = "Boolean";

        if (Nullable.GetUnderlyingType(type) != null)
        {
            name += "?";
        }
        return name;
    }
}
