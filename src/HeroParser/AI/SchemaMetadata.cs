using System;
using System.Collections.Concurrent;

namespace HeroParser.AI;

/// <summary>
/// Provides access to source-generated LLM schemas for record types.
/// </summary>
public static class SchemaMetadata
{
    private static readonly ConcurrentDictionary<Type, string> schemas = new();

    /// <summary>
    /// Registers a JSON Schema for a specific type.
    /// </summary>
    public static void RegisterSchema<T>(string schemaJson)
    {
        schemas[typeof(T)] = schemaJson;
    }

    /// <summary>
    /// Gets the pre-rendered LLM JSON Schema for the specified type.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <returns>The pre-rendered JSON Schema string.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the type does not have a source-generated schema.</exception>
    public static string ToLlmSchema<T>()
    {
        if (schemas.TryGetValue(typeof(T), out var schema))
        {
            return schema;
        }

        throw new InvalidOperationException(
            $"No source-generated LLM schema found for type '{typeof(T).FullName}'. " +
            "Ensure the type is decorated with the [GenerateBinder] attribute and the source generator is active.");
    }
}
