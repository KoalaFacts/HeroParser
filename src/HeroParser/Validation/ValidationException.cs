namespace HeroParser.Validation;

/// <summary>
/// Represents one or more field-level validation errors that occurred during record binding.
/// </summary>
/// <remarks>
/// Thrown automatically by terminal methods (e.g., <c>ToList()</c>) when <see cref="ValidationMode.Strict"/> is active.
/// Inspect <see cref="Errors"/> for the full list of structured validation failures.
/// </remarks>
public sealed class ValidationException : Exception
{
    private const int MAX_INLINE_ERRORS = 3;

    /// <summary>
    /// Gets the validation errors that caused this exception.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="errors">The validation errors. Must not be empty.</param>
    public ValidationException(IReadOnlyList<ValidationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    private static string BuildMessage(IReadOnlyList<ValidationError> errors)
    {
        if (errors.Count == 1)
            return $"Validation failed: {errors[0]}";

        var lines = new List<string>(Math.Min(errors.Count, MAX_INLINE_ERRORS) + 1)
        {
            $"{errors.Count} validation errors occurred:"
        };

        for (int i = 0; i < Math.Min(errors.Count, MAX_INLINE_ERRORS); i++)
            lines.Add($"  {errors[i]}");

        if (errors.Count > MAX_INLINE_ERRORS)
            lines.Add($"  ... and {errors.Count - MAX_INLINE_ERRORS} more.");

        return string.Join(Environment.NewLine, lines);
    }
}
