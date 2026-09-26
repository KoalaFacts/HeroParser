namespace HeroParser.Cli;

internal enum ProfileValueKind
{
    Uncached,
    Null,
    True,
    False,
    Guid,
    DateTime,
    String
}

internal sealed class Utf8ProfileCache
{
    private const int CAPACITY = 16;

    private readonly Entry[] entries = new Entry[CAPACITY];
    private int count;

    public bool IsDisabled { get; private set; }

    public bool TryGet(ReadOnlySpan<byte> value, out ProfileValueKind kind, out string? text)
    {
        if (IsDisabled)
        {
            kind = ProfileValueKind.Uncached;
            text = null;
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            ref Entry entry = ref entries[i];
            if (entry.Length != value.Length || !value.SequenceEqual(entry.Bytes.AsSpan(0, entry.Length)))
                continue;

            kind = entry.Kind;
            text = entry.Text;
            return true;
        }

        kind = ProfileValueKind.Uncached;
        text = null;
        return false;
    }

    public void Store(ReadOnlySpan<byte> value, ProfileValueKind kind, string text)
    {
        if (IsDisabled)
            return;
        if (count == CAPACITY)
        {
            IsDisabled = true;
            return;
        }

        ref Entry entry = ref entries[count];
        if (entry.Bytes is null || entry.Bytes.Length < value.Length)
            entry.Bytes = new byte[value.Length];

        value.CopyTo(entry.Bytes);
        entry.Length = value.Length;
        entry.Kind = kind;
        entry.Text = text;

        count++;
    }

    private struct Entry
    {
        public byte[]? Bytes;
        public int Length;
        public ProfileValueKind Kind;
        public string? Text;
    }
}
