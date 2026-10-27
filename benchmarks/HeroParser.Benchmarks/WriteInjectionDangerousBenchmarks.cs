using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.Benchmarks;

/// <summary>
/// Measures the real write-side formula-protection paths on the same
/// dangerous logical input. The safe-only fast path is measured separately.
/// Output differs intentionally between policies and is verified before timing.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class WriteInjectionDangerousBenchmarks
{
    private static readonly string[] dangerousRow = ["=cmd", "-100", "hello,world", "+42"];

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (Rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Rows));
        }

        // These strings are independent of the writer under test. Verify every
        // row, including both safe signed numbers and delimiter-quoted text.
        VerifyOutput(Write_NoProtection(), "=cmd,-100,\"hello,world\",+42\r\n");
        VerifyOutput(Write_Sanitize(), "cmd,-100,\"hello,world\",+42\r\n");
        VerifyOutput(Write_EscapeWithQuote(), "\"'=cmd\",-100,\"hello,world\",+42\r\n");
        VerifyOutput(Write_EscapeWithTab(), "\"\t=cmd\",-100,\"hello,world\",+42\r\n");

        // Reject is not a throughput peer: timing an expected exception would
        // benchmark exception handling, not successful CSV serialization.
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var writer = new CsvStreamWriter(output,
            new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.Reject }, leaveOpen: true);
        bool rejected = false;
        try
        {
            writer.WriteRow(dangerousRow);
        }
        catch (CsvException error) when (error.ErrorCode == CsvErrorCode.InjectionDetected)
        {
            rejected = true;
        }

        if (!rejected)
        {
            throw new InvalidOperationException("Reject failed to detect the dangerous formula prefix.");
        }
    }

    [Benchmark(Baseline = true)]
    public string Write_NoProtection() => Write(CsvInjectionProtection.None);

    [Benchmark]
    public string Write_Sanitize() => Write(CsvInjectionProtection.Sanitize);

    [Benchmark]
    public string Write_EscapeWithQuote() => Write(CsvInjectionProtection.EscapeWithQuote);

    [Benchmark]
    public string Write_EscapeWithTab() => Write(CsvInjectionProtection.EscapeWithTab);

    private string Write(CsvInjectionProtection protection)
    {
        var options = new CsvWriteOptions { InjectionProtection = protection };
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var writer = new CsvStreamWriter(output, options, leaveOpen: true);

        for (int row = 0; row < Rows; row++)
        {
            writer.WriteRow(dangerousRow);
        }

        writer.Flush();
        return output.ToString();
    }

    private void VerifyOutput(string actual, string expectedRow)
    {
        int rowLength = expectedRow.Length;
        int expectedLength = checked(Rows * rowLength);
        if (actual.Length != expectedLength)
        {
            throw new InvalidOperationException(
                $"Writer produced {actual.Length} characters; expected {expectedLength} for {Rows} rows.");
        }

        for (int row = 0; row < Rows; row++)
        {
            if (!actual.AsSpan(row * rowLength, rowLength).SequenceEqual(expectedRow.AsSpan()))
            {
                throw new InvalidOperationException($"Write-side protection output differs at row {row}.");
            }
        }
    }
}
