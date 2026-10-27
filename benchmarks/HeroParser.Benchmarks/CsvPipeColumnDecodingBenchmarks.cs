using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace HeroParser.Benchmarks;

[MemoryDiagnoser]
public class CsvPipeColumnDecodingBenchmarks
{
    private CsvPipeColumn owned;
    private ReadOnlySequence<byte> segmented;
    private byte? escape;

    [Params("Plain", "DoubledQuotes", "Escaped", "Unicode", "LongEscaped", "InvalidUtf8")]
    public string Scenario { get; set; } = "Plain";

    [GlobalSetup]
    public void Setup()
    {
        string? text = Scenario switch
        {
            "Plain" => "an ordinary field without CSV escapes",
            "DoubledQuotes" => "\"she said \"\"hello\"\" to the reader\"",
            "Escaped" => "\"she said \\\"hello\\\" to the reader\"",
            "Unicode" => "\"你好 \"\"世界\"\" 😀\"",
            "LongEscaped" => "\"" + string.Concat(Enumerable.Repeat("value\"\" ", 512)) + "\"",
            // The malformed UTF-8 lead byte must be decoded before CSV unescaping.
            "InvalidUtf8" => null,
            _ => throw new InvalidOperationException()
        };
        escape = Scenario is "Escaped" or "InvalidUtf8" ? (byte)'\\' : null;
        byte[] bytes = text is null
            ? [(byte)'"', 0xC3, (byte)'\\', 0xA9, (byte)'x', (byte)'"']
            : Encoding.UTF8.GetBytes(text);

        // Force the two-segment borrowed field to cross the actual escape
        // token or UTF-8 code point rather than an arbitrary halfway index.
        int split = Scenario switch
        {
            "DoubledQuotes" => SplitBetween(bytes, (byte)'"', (byte)'"'),
            "Escaped" => SplitBetween(bytes, (byte)'\\', (byte)'"'),
            "Unicode" => Array.FindIndex(bytes, static b => b >= 0xC2) + 1,
            "LongEscaped" => SplitBetween(bytes, (byte)'"', (byte)'"', bytes.Length / 2),
            "InvalidUtf8" => 2,
            _ => bytes.Length / 2
        };
        if (split <= 0 || split >= bytes.Length)
            throw new InvalidOperationException($"Invalid segment boundary for {Scenario}.");

        owned = new CsvPipeColumn(bytes, 0, bytes.Length, (byte)'"', escape);
        var first = new Segment(bytes.AsMemory(0, split));
        var last = first.Append(bytes.AsMemory(split));
        segmented = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        // Validate the complete decoded result from both real benchmark
        // methods, including Unicode and UTF-8 replacement before unescape.
        string expected = Scenario switch
        {
            "Plain" => "an ordinary field without CSV escapes",
            "DoubledQuotes" or "Escaped" => "she said \"hello\" to the reader",
            "Unicode" => "你好 \"世界\" 😀",
            "LongEscaped" => string.Concat(Enumerable.Repeat("value\" ", 512)),
            "InvalidUtf8" => "\uFFFD\uFFFDx",
            _ => throw new InvalidOperationException()
        };
        if (OwnedColumn() != expected || SegmentedColumn() != expected)
            throw new InvalidOperationException($"CSV Pipe column decoded incorrectly for {Scenario}.");
    }

    private static int SplitBetween(byte[] bytes, byte left, byte right, int start = 0)
    {
        for (int i = start; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == left && bytes[i + 1] == right)
                return i + 1;
        }

        throw new InvalidOperationException("Required CSV token was not present in benchmark fixture.");
    }

    [Benchmark]
    public string OwnedColumn() => owned.ToUnquotedString();

    [Benchmark]
    public string SegmentedColumn()
        => new CsvPipeSequenceColumn(segmented, (byte)'"', escape).ToUnquotedString();

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}
