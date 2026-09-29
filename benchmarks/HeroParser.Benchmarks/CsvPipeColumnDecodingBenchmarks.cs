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

    [Params("Plain", "DoubledQuotes", "Escaped", "Unicode", "LongEscaped")]
    public string Scenario { get; set; } = "Plain";

    [GlobalSetup]
    public void Setup()
    {
        string text = Scenario switch
        {
            "Plain" => "an ordinary field without CSV escapes",
            "DoubledQuotes" => "\"she said \"\"hello\"\" to the reader\"",
            "Escaped" => "\"she said \\\"hello\\\" to the reader\"",
            "Unicode" => "\"\u4F60\u597D \"\"\u4E16\u754C\"\" \uD83D\uDE00\"",
            "LongEscaped" => "\"" + string.Concat(Enumerable.Repeat("value\"\" ", 512)) + "\"",
            _ => throw new InvalidOperationException()
        };
        escape = Scenario == "Escaped" ? (byte)'\\' : null;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        owned = new CsvPipeColumn(bytes, 0, bytes.Length, (byte)'"', escape);
        var first = new Segment(bytes.AsMemory(0, bytes.Length / 2));
        var last = first.Append(bytes.AsMemory(bytes.Length / 2));
        segmented = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
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
