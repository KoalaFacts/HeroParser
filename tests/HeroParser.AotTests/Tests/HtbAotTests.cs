using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using HeroParser.AotTests.Models;
using HeroParser.Conversion;
using HeroParser.Htbs.Records;

namespace HeroParser.AotTests.Tests;

/// <summary>
/// AOT compatibility tests for High-Throughput Tabular Binary (HTB) read/write.
/// </summary>
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2026:Members attributed with RequiresUnreferencedCode may break when trimming",
    Justification = "All record types in this test class are decorated with [GenerateBinder]; the reflection fallback in HTB is never taken.")]
[UnconditionalSuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
    Justification = "All record types in this test class are decorated with [GenerateBinder]; the reflection fallback in HTB is never taken.")]
public static class HtbAotTests
{
    public static void Run(TestRunner runner)
    {
        runner.PrintSection("HTB Tests");

        runner.Run("HTB: Generated binder and writer round-trip", RoundTrip);
        runner.Run("HTB: Nullable types round-trip", NullableRoundTrip);
        runner.Run("HTB: Direct conversion round-trip", DirectConversionRoundTrip);
    }

    private static void RoundTrip()
    {
        using var ms = new MemoryStream();
        Person[] original =
        [
            new Person { Name = "Alice", Age = 30 },
            new Person { Name = "Bob", Age = 25 }
        ];

        Htb.Write<Person>().ToStream(ms, original, leaveOpen: true);
        ms.Position = 0;

        var parsed = Htb.Read<Person>().FromStream(ms).ToList();

        if (parsed.Count != 2)
            throw new Exception($"Expected 2, got {parsed.Count}");
        if (parsed[0].Name != "Alice" || parsed[0].Age != 30)
            throw new Exception("First record mismatch");
        if (parsed[1].Name != "Bob" || parsed[1].Age != 25)
            throw new Exception("Second record mismatch");
    }

    private static void NullableRoundTrip()
    {
        using var ms = new MemoryStream();
        NullableRecord[] original =
        [
            new NullableRecord { Name = "Charlie", Score = 100 },
            new NullableRecord { Name = "Diana", Score = null }
        ];

        Htb.Write<NullableRecord>().ToStream(ms, original, leaveOpen: true);
        ms.Position = 0;

        var parsed = Htb.Read<NullableRecord>().FromStream(ms).ToList();

        if (parsed.Count != 2)
            throw new Exception($"Expected 2, got {parsed.Count}");
        if (parsed[0].Score != 100)
            throw new Exception("Score mismatch");
        if (parsed[1].Score != null)
            throw new Exception("Expected null score");
    }

    private static void DirectConversionRoundTrip()
    {
        var schema = new HtbSchema([
            new HtbColumn("Name", HtbDataType.String, isNullable: true),
            new HtbColumn("Age", HtbDataType.Int32, isNullable: true)
        ]);

        string csvData = "Name,Age\r\n" +
                         "Alice,30\r\n" +
                         "Bob,25\r\n";

        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert(csvData, htbStream, schema);

        htbStream.Position = 0;

        using var csvWriter = new StringWriter();
        HtbToCsvConverter.Convert(htbStream, csvWriter);

        string roundTrippedCsv = csvWriter.ToString();

        string expected = "Name,Age\r\nAlice,30\r\nBob,25\r\n";
        string cleanExpected = expected.Replace("\r\n", "\n").Trim();
        string cleanActual = roundTrippedCsv.Replace("\r\n", "\n").Trim();

        if (cleanExpected != cleanActual)
        {
            throw new Exception($"Direct conversion parity mismatch.\nExpected:\n{cleanExpected}\nActual:\n{cleanActual}");
        }
    }
}
