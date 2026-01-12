namespace HeroParser.FixedWidths;

internal readonly struct FixedWidthFieldLayout(string name, int start, int length)
{
    public string Name { get; } = name;
    public int Start { get; } = start;
    public int Length { get; } = length;
}

internal static class FixedWidthFieldLayoutValidator
{
    public static void Validate(IReadOnlyList<FixedWidthFieldLayout> fields)
    {
        if (fields.Count == 0)
            return;

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            if (field.Start < 0)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    $"Field '{field.Name}' has invalid start {field.Start}. Start must be non-negative.");
            }

            if (field.Length <= 0)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    $"Field '{field.Name}' has invalid length {field.Length}. Length must be positive.");
            }

            try
            {
                _ = checked(field.Start + field.Length);
            }
            catch (OverflowException ex)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    $"Field '{field.Name}' has an invalid range: start {field.Start}, length {field.Length}.", ex);
            }
        }

        var ordered = new FixedWidthFieldLayout[fields.Count];
        for (int i = 0; i < fields.Count; i++)
            ordered[i] = fields[i];

        Array.Sort(ordered, static (left, right) => left.Start.CompareTo(right.Start));

        var previous = ordered[0];
        int previousEnd = checked(previous.Start + previous.Length);

        for (int i = 1; i < ordered.Length; i++)
        {
            var current = ordered[i];
            int currentEnd = checked(current.Start + current.Length);

            if (current.Start < previousEnd)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    $"Fields '{previous.Name}' [{previous.Start}:{previousEnd}) and '{current.Name}' [{current.Start}:{currentEnd}) overlap.");
            }

            previous = current;
            previousEnd = currentEnd;
        }
    }
}
