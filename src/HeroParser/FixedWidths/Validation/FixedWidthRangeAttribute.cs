namespace HeroParser.FixedWidths.Validation;

/// <summary>
/// Validates that a numeric fixed-width field value is within the specified range.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FixedWidthRangeAttribute : FixedWidthValidationAttribute
{
    /// <summary>
    /// Gets the minimum value (inclusive).
    /// </summary>
    public object Minimum { get; }

    /// <summary>
    /// Gets the maximum value (inclusive).
    /// </summary>
    public object Maximum { get; }

    /// <summary>
    /// Gets the type of the range operands.
    /// </summary>
    public Type OperandType { get; }

    /// <summary>
    /// Creates a new range validator for integer values.
    /// </summary>
    /// <param name="minimum">The minimum allowed value (inclusive).</param>
    /// <param name="maximum">The maximum allowed value (inclusive).</param>
    public FixedWidthRangeAttribute(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
        OperandType = typeof(int);
    }

    /// <summary>
    /// Creates a new range validator for double values.
    /// </summary>
    /// <param name="minimum">The minimum allowed value (inclusive).</param>
    /// <param name="maximum">The maximum allowed value (inclusive).</param>
    public FixedWidthRangeAttribute(double minimum, double maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
        OperandType = typeof(double);
    }

    /// <summary>
    /// Creates a new range validator for long values.
    /// </summary>
    /// <param name="minimum">The minimum allowed value (inclusive).</param>
    /// <param name="maximum">The maximum allowed value (inclusive).</param>
    public FixedWidthRangeAttribute(long minimum, long maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
        OperandType = typeof(long);
    }

    /// <inheritdoc/>
    public override bool IsValid(object? value, string? rawValue)
    {
        // Null values are valid (use [FixedWidthRequired] for null validation)
        if (value is null)
        {
            return true;
        }

        try
        {
            var comparable = (IComparable)Convert.ChangeType(value, OperandType);
            var min = (IComparable)Minimum;
            var max = (IComparable)Maximum;

            return comparable.CompareTo(min) >= 0 && comparable.CompareTo(max) <= 0;
        }
        catch
        {
            // If conversion fails, validation fails
            return false;
        }
    }

    /// <inheritdoc/>
    protected override string GetDefaultErrorMessage(string fieldName, object? value)
    {
        return $"'{fieldName}' must be between {Minimum} and {Maximum}.";
    }
}
