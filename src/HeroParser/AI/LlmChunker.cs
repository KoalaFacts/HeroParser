using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HeroParser.AI;

/// <summary>
/// High-performance utility to chunk tabular streams for ingestion into LLMs and RAG pipelines.
/// </summary>
public static class LlmChunker
{
    /// <summary>
    /// Formats and chunks an asynchronous enumerable of records into LLM-native text blocks.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The source record stream.</param>
    /// <param name="options">Options for chunk sizes, headers, and templates.</param>
    /// <returns>An asynchronous stream of <see cref="LlmChunk"/> objects.</returns>
    public static async IAsyncEnumerable<LlmChunk> ToLlmChunksAsync<T>(
        this IAsyncEnumerable<T> source,
        LlmChunkOptions? options = null) where T : class
    {
        options ??= new LlmChunkOptions();
        var cache = PropertyInfoCache<T>.Instance;

        // Custom template compiler if requested
        TemplateCompiled<T>? compiledTemplate = null;
        if (!string.IsNullOrWhiteSpace(options.CustomTemplate))
        {
            compiledTemplate = new TemplateCompiled<T>(options.CustomTemplate, cache.Accessors);
        }

        var tokenCounter = options.TokenCounter ?? DefaultTokenCounter;

        // Precompute headers for Markdown
        string headersText = "";
        int headersTokens = 0;
        if (compiledTemplate == null && options.RepeatHeaders)
        {
            headersText = cache.HeaderRow + "\n" + cache.SeparatorRow + "\n";
            headersTokens = tokenCounter(headersText);
        }

        var currentChunkBuilder = new StringBuilder();
        int currentChunkTokens = 0;
        int startRow = 1;
        int currentRow = 0;

        // If repeating headers, seed the very first chunk with headers
        if (compiledTemplate == null && options.RepeatHeaders)
        {
            currentChunkBuilder.Append(headersText);
            currentChunkTokens = headersTokens;
        }

        await foreach (var record in source)
        {
            currentRow++;

            // Format this row
            string rowText = compiledTemplate != null
                ? compiledTemplate.Evaluate(record) + "\n"
                : FormatMarkdownRow(record, cache.Accessors) + "\n";

            int rowTokens = tokenCounter(rowText);

            // Check if adding this row would overflow the current chunk budget
            if (currentChunkTokens + rowTokens > options.MaxTokensPerChunk)
            {
                // Yield the current chunk if it has rows
                bool hasDataRows = compiledTemplate != null
                    ? currentChunkBuilder.Length > 0
                    : currentChunkBuilder.Length > headersText.Length;

                if (hasDataRows)
                {
                    yield return new LlmChunk
                    {
                        Content = currentChunkBuilder.ToString().TrimEnd('\n'),
                        TokenCount = currentChunkTokens,
                        StartRow = startRow,
                        EndRow = currentRow - 1
                    };

                    // Reset for the next chunk
                    currentChunkBuilder.Clear();
                    startRow = currentRow;

                    if (compiledTemplate == null && options.RepeatHeaders)
                    {
                        currentChunkBuilder.Append(headersText);
                        currentChunkTokens = headersTokens;
                    }
                    else
                    {
                        currentChunkTokens = 0;
                    }
                }
            }

            // Append row to the chunk
            currentChunkBuilder.Append(rowText);
            currentChunkTokens += rowTokens;
        }

        // Yield final remaining chunk if any
        bool finalHasData = compiledTemplate != null
            ? currentChunkBuilder.Length > 0
            : currentChunkBuilder.Length > headersText.Length;

        if (finalHasData)
        {
            yield return new LlmChunk
            {
                Content = currentChunkBuilder.ToString().TrimEnd('\n'),
                TokenCount = currentChunkTokens,
                StartRow = startRow,
                EndRow = currentRow
            };
        }
    }

    private static string FormatMarkdownRow<T>(T record, List<PropertyAccessor<T>> accessors)
    {
        var sb = new StringBuilder();
        sb.Append("| ");
        for (int i = 0; i < accessors.Count; i++)
        {
            var value = accessors[i].Getter(record);
            sb.Append(EscapeMarkdown(value));
            if (i < accessors.Count - 1)
            {
                sb.Append(" | ");
            }
        }
        sb.Append(" |");
        return sb.ToString();
    }

    private static string EscapeMarkdown(object? val)
    {
        if (val == null) return string.Empty;
        var str = val.ToString() ?? string.Empty;
        // Escape pipeline characters and collapse newlines for clean tables
        return str.Replace("|", "\\|")
                  .Replace("\r\n", " ")
                  .Replace("\n", " ");
    }

    private static int DefaultTokenCounter(string text)
    {
        // standard LLM token estimation heuristic: 1 token ~ 4 characters in English
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private sealed class PropertyInfoCache<T>
    {
        public static readonly PropertyInfoCache<T> Instance = new();

        public List<PropertyAccessor<T>> Accessors { get; } = [];
        public string HeaderRow { get; }
        public string SeparatorRow { get; }

        private PropertyInfoCache()
        {
            var properties = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetMethod is { IsStatic: false })
                .OrderBy(p => p.MetadataToken)
                .ToList();

            var headers = new List<string>();
            foreach (var property in properties)
            {
                var tabularMap = property.GetCustomAttribute<TabularMapAttribute>();
                var headerName = !string.IsNullOrWhiteSpace(tabularMap?.Name) ? tabularMap.Name : property.Name;
                headers.Add(headerName);

                var getter = CreateGetter(property);
                Accessors.Add(new PropertyAccessor<T>(property.Name, headerName, getter));
            }

            HeaderRow = "| " + string.Join(" | ", headers) + " |";
            SeparatorRow = "| " + string.Join(" | ", headers.Select(_ => "---")) + " |";
        }

        private static Func<T, object?> CreateGetter(PropertyInfo property)
        {
            var instanceParam = Expression.Parameter(typeof(T), "instance");
            var propertyAccess = Expression.Property(instanceParam, property);
            Expression body = property.PropertyType.IsValueType
                ? Expression.Convert(propertyAccess, typeof(object))
                : Expression.TypeAs(propertyAccess, typeof(object));

            var lambda = Expression.Lambda<Func<T, object?>>(body, instanceParam);
            return lambda.Compile();
        }
    }

    private sealed class PropertyAccessor<T>(string propertyName, string headerName, Func<T, object?> getter)
    {
        public string PropertyName { get; } = propertyName;
        public string HeaderName { get; } = headerName;
        public Func<T, object?> Getter { get; } = getter;
    }

    private sealed class TemplateCompiled<T>
    {
        private readonly List<Func<T, string>> segments = [];

        public TemplateCompiled(string template, List<PropertyAccessor<T>> accessors)
        {
            int lastIndex = 0;
            while (true)
            {
                int openBrace = template.IndexOf('{', lastIndex);
                if (openBrace < 0)
                {
                    if (lastIndex < template.Length)
                    {
                        var literal = template[lastIndex..];
                        segments.Add(_ => literal);
                    }
                    break;
                }

                if (openBrace > lastIndex)
                {
                    var literal = template[lastIndex..openBrace];
                    segments.Add(_ => literal);
                }

                int closeBrace = template.IndexOf('}', openBrace);
                if (closeBrace < 0)
                {
                    var literal = template[openBrace..];
                    segments.Add(_ => literal);
                    break;
                }

                var propName = template[(openBrace + 1)..closeBrace];
                var accessor = accessors.FirstOrDefault(a => string.Equals(a.PropertyName, propName, StringComparison.OrdinalIgnoreCase)
                                                          || string.Equals(a.HeaderName, propName, StringComparison.OrdinalIgnoreCase));
                if (accessor != null)
                {
                    segments.Add(instance => accessor.Getter(instance)?.ToString() ?? string.Empty);
                }
                else
                {
                    var rawPlaceholder = template[openBrace..(closeBrace + 1)];
                    segments.Add(_ => rawPlaceholder);
                }

                lastIndex = closeBrace + 1;
            }
        }

        public string Evaluate(T instance)
        {
            var sb = new StringBuilder();
            foreach (var segment in segments)
            {
                sb.Append(segment(instance));
            }
            return sb.ToString();
        }
    }
}
