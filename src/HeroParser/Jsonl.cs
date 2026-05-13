namespace HeroParser;

/// <summary>
/// Provides factory methods for reading and writing JSONL (JSON Lines) data — one JSON object per line.
/// </summary>
/// <remarks>
/// <para>
/// JSONL is the de-facto interchange format for LLM fine-tuning datasets (OpenAI, Anthropic, HuggingFace),
/// model evaluations, and streamed AI responses. <see cref="Jsonl"/> mirrors the <see cref="Csv"/> entry-point
/// pattern with reader/writer builders, a <see cref="System.Data.Common.DbDataReader"/> adapter, and async
/// streaming support for <see cref="System.IO.Pipelines.PipeReader"/>.
/// </para>
/// <para>
/// Deserialization is performed by <c>System.Text.Json</c>. Pass a
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> (from a
/// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>) for AOT/trimming-safe usage.
/// </para>
/// </remarks>
public static partial class Jsonl
{
}
