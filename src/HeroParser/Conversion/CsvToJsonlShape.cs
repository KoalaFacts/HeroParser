namespace HeroParser.Conversion;

/// <summary>
/// Describes how each CSV row should be projected when converting to JSONL.
/// </summary>
/// <remarks>
/// Use the static factory methods <see cref="FlatObject"/>, <see cref="OpenAiChat"/>, or
/// <see cref="AnthropicMessages"/>. These shape descriptors carry no SDK dependencies — they only
/// describe the JSON layout. The <see cref="CsvToJsonlConverter"/> consumes them.
/// </remarks>
public abstract record CsvToJsonlShape
{
    /// <summary>
    /// Internal constructor — prevents external subclasses.
    /// </summary>
    private protected CsvToJsonlShape() { }

    /// <summary>
    /// Maps each row to a flat JSON object with one property per CSV column.
    /// </summary>
    public static CsvToJsonlShape FlatObject() => FlatObjectShape.Instance;

    /// <summary>
    /// Maps each row to an OpenAI chat-completion fine-tuning record of the form
    /// <c>{"messages": [{"role":"system",...},{"role":"user",...},{"role":"assistant",...}]}</c>.
    /// </summary>
    /// <param name="systemColumn">Optional system prompt column. Omitted from output when <see langword="null"/>.</param>
    /// <param name="userColumn">Required user-message column name.</param>
    /// <param name="assistantColumn">Required assistant-message column name.</param>
    public static CsvToJsonlShape OpenAiChat(string? systemColumn, string userColumn, string assistantColumn)
    {
        ArgumentException.ThrowIfNullOrEmpty(userColumn);
        ArgumentException.ThrowIfNullOrEmpty(assistantColumn);
        return new OpenAiChatShape(systemColumn, userColumn, assistantColumn);
    }

    /// <summary>
    /// Maps each row to an Anthropic-style messages array
    /// <c>{"messages": [{"role":"user",...},{"role":"assistant",...}]}</c> (no system role).
    /// </summary>
    public static CsvToJsonlShape AnthropicMessages(string userColumn, string assistantColumn)
    {
        ArgumentException.ThrowIfNullOrEmpty(userColumn);
        ArgumentException.ThrowIfNullOrEmpty(assistantColumn);
        return new AnthropicMessagesShape(userColumn, assistantColumn);
    }

    internal sealed record FlatObjectShape : CsvToJsonlShape
    {
        public static readonly FlatObjectShape Instance = new();
        private FlatObjectShape() { }
    }

    internal sealed record OpenAiChatShape(string? SystemColumn, string UserColumn, string AssistantColumn) : CsvToJsonlShape;

    internal sealed record AnthropicMessagesShape(string UserColumn, string AssistantColumn) : CsvToJsonlShape;
}
