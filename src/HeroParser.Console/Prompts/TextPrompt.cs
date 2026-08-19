using System;
using System.IO;

namespace HeroParser.Console.Prompts;

/// <summary>
/// Represents the result of a validation check in a TextPrompt.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the validation check succeeded.
    /// </summary>
    public bool Successful { get; }

    /// <summary>
    /// Gets the validation error message, if unsuccessful.
    /// </summary>
    public string Message { get; }

    private ValidationResult(bool successful, string message)
    {
        Successful = successful;
        Message = message ?? string.Empty;
    }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success() => new(true, string.Empty);

    /// <summary>
    /// Creates a failed validation result with the specified error message.
    /// </summary>
    public static ValidationResult Error(string message) => new(false, message);
}

/// <summary>
/// A prompt that requests text input from the user, offering validation and type conversion.
/// </summary>
/// <typeparam name="T">The type of the returned value.</typeparam>
public class TextPrompt<T>
{
    private string title = string.Empty;
    private T? defaultValue;
    private bool hasDefaultValue;
    private Func<string, T>? converter;
    private Func<T, ValidationResult>? validationFunc;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextPrompt{T}"/> class.
    /// </summary>
    public TextPrompt()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextPrompt{T}"/> class with a title.
    /// </summary>
    public TextPrompt(string title)
    {
        this.title = title ?? string.Empty;
    }

    /// <summary>
    /// Configures the prompt title.
    /// </summary>
    public TextPrompt<T> Title(string promptTitle)
    {
        title = promptTitle ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Sets a default value that will be returned if the user enters empty input.
    /// </summary>
    public TextPrompt<T> DefaultValue(T value)
    {
        defaultValue = value;
        hasDefaultValue = true;
        return this;
    }

    /// <summary>
    /// Sets a custom converter function to translate string input into the target type.
    /// </summary>
    public TextPrompt<T> WithConverter(Func<string, T> customConverter)
    {
        converter = customConverter;
        return this;
    }

    /// <summary>
    /// Sets a custom validation function returning a <see cref="ValidationResult"/>.
    /// </summary>
    public TextPrompt<T> Validate(Func<T, ValidationResult> customValidator)
    {
        validationFunc = customValidator;
        return this;
    }

    /// <summary>
    /// Renders the prompt and collects/validates user input.
    /// </summary>
    public T Show() => Show(AnsiConsole.Current);

    /// <summary>
    /// Shows the prompt on the supplied console and returns the converted value.
    /// </summary>
    /// <param name="console">Console to render to and read input from.</param>
    public T Show(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        // Statically map common primitive types if no converter was provided
        converter ??= typeof(T) == typeof(string)
            ? ((input) => (T)(object)input)
            : typeof(T) == typeof(int)
            ? ((input) =>
            {
                if (int.TryParse(input, out var res)) return (T)(object)res;
                throw new FormatException();
            })
            : typeof(T) == typeof(double)
            ? ((input) =>
            {
                if (double.TryParse(input, out var res)) return (T)(object)res;
                throw new FormatException();
            })
            : throw new InvalidOperationException(
                $"No built-in converter for type '{typeof(T).Name}'. Please register a custom converter using WithConverter().");

        while (true)
        {
            if (hasDefaultValue)
            {
                console.Markup($"{title} [grey][[default: {defaultValue}]][/]: ");
            }
            else
            {
                console.Markup($"{title}: ");
            }

            string? input = console.ReadLine();

            if (input is null)
            {
                // Input is exhausted — a closed or redirected stream with nothing left.
                // Retrying would spin forever, so take the default if there is one and
                // report the exhaustion otherwise.
                if (hasDefaultValue)
                {
                    return defaultValue!;
                }

                throw new EndOfStreamException($"No input is available to answer the prompt '{title}'.");
            }

            // Return default value if empty
            if (string.IsNullOrWhiteSpace(input) && hasDefaultValue)
            {
                return defaultValue!;
            }

            try
            {
                T converted = converter(input);
                if (validationFunc != null)
                {
                    var validationResult = validationFunc(converted);
                    if (!validationResult.Successful)
                    {
                        console.MarkupLine(string.IsNullOrEmpty(validationResult.Message) ? "[red]Invalid input.[/]" : validationResult.Message);
                        continue;
                    }
                }
                return converted;
            }
            catch
            {
                console.MarkupLine("[red]Invalid input format.[/]");
            }
        }
    }
}
