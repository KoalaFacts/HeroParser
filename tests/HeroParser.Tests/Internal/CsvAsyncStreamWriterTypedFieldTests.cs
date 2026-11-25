using System.Globalization;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Exercises <see cref="CsvAsyncStreamWriter"/>'s typed WriteFieldAsync overloads on a
/// buffer with room, plus the quoting styles and injection handling.
///
/// The existing slow-path suite always fills the char buffer first, so it only ever
/// reached each overload's fallback branch — the fast path every real caller takes was
/// never executed, along with the per-overload column-count guard.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvAsyncStreamWriterTypedFieldTests
{
    private static async Task<string> WriteAsync(
        Func<CsvAsyncStreamWriter, CancellationToken, ValueTask> write,
        CsvWriteOptions? options = null)
    {
        var ct = TestContext.Current.CancellationToken;
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, options, leaveOpen: true))
        {
            await write(w, ct);
            await w.EndRowAsync(ct);
        }

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8, leaveOpen: true);
        return (await reader.ReadToEndAsync(ct)).TrimEnd('\r', '\n');
    }

    [Fact]
    public async Task Int_Formats() => Assert.Equal("42", await WriteAsync((w, ct) => w.WriteFieldAsync(42, ct)));

    [Fact]
    public async Task Long_Formats()
        => Assert.Equal("9007199254740993", await WriteAsync((w, ct) => w.WriteFieldAsync(9007199254740993L, ct)));

    [Fact]
    public async Task Double_Formats() => Assert.Equal("1.5", await WriteAsync((w, ct) => w.WriteFieldAsync(1.5d, ct)));

    [Fact]
    public async Task Float_Formats() => Assert.Equal("2.5", await WriteAsync((w, ct) => w.WriteFieldAsync(2.5f, ct)));

    [Fact]
    public async Task Decimal_Formats() => Assert.Equal("3.25", await WriteAsync((w, ct) => w.WriteFieldAsync(3.25m, ct)));

    [Fact]
    public async Task Bool_Formats()
        => Assert.Equal(true.ToString(CultureInfo.InvariantCulture), await WriteAsync((w, ct) => w.WriteFieldAsync(true, ct)));

    [Fact]
    public async Task Guid_Formats()
    {
        var value = new Guid("11112222-3333-4444-5555-666677778888");
        Assert.Equal(value.ToString(), await WriteAsync((w, ct) => w.WriteFieldAsync(value, ct)));
    }

    [Fact]
    public async Task DateTime_Formats()
    {
        string written = await WriteAsync((w, ct) => w.WriteFieldAsync(new DateTime(2024, 3, 17, 8, 30, 0, DateTimeKind.Unspecified), ct));
        Assert.Contains("2024", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typed_FieldsAreSeparatedByTheDelimiter()
    {
        string row = await WriteAsync(async (w, ct) =>
        {
            await w.WriteFieldAsync(1, ct);
            await w.WriteFieldAsync(2L, ct);
            await w.WriteFieldAsync(3.5d, ct);
            await w.WriteFieldAsync(4.5f, ct);
            await w.WriteFieldAsync(5.25m, ct);
            await w.WriteFieldAsync(false, ct);
        });

        Assert.Equal($"1,2,3.5,4.5,5.25,{false.ToString(CultureInfo.InvariantCulture)}", row);
    }

    [Fact]
    public async Task Typed_RespectTheConfiguredCulture()
    {
        var options = new CsvWriteOptions { Culture = CultureInfo.GetCultureInfo("de-DE"), Delimiter = ';' };
        Assert.Equal("1,5", await WriteAsync((w, ct) => w.WriteFieldAsync(1.5d, ct), options));
    }

    // Each typed overload carries its own column-count guard, so the limit is proven
    // per overload rather than once for the class.
    public static TheoryData<string, Func<CsvAsyncStreamWriter, CancellationToken, ValueTask>> OverflowingWrites() => new()
    {
        { "int", (w, ct) => w.WriteFieldAsync(2, ct) },
        { "long", (w, ct) => w.WriteFieldAsync(2L, ct) },
        { "double", (w, ct) => w.WriteFieldAsync(2d, ct) },
        { "float", (w, ct) => w.WriteFieldAsync(2f, ct) },
        { "decimal", (w, ct) => w.WriteFieldAsync(2m, ct) },
        { "bool", (w, ct) => w.WriteFieldAsync(true, ct) },
        { "DateTime", (w, ct) => w.WriteFieldAsync(DateTime.UnixEpoch, ct) },
        { "Guid", (w, ct) => w.WriteFieldAsync(Guid.Empty, ct) },
        { "string", (w, ct) => w.WriteFieldAsync("x", ct) },
        { "memory", (w, ct) => w.WriteFieldAsync("x".AsMemory(), ct) },
    };

    [Theory]
    [MemberData(nameof(OverflowingWrites))]
    public async Task ColumnLimit_IsEnforcedByEveryOverload(
        string label,
        Func<CsvAsyncStreamWriter, CancellationToken, ValueTask> writeSecondField)
    {
        Assert.NotNull(label);
        var ct = TestContext.Current.CancellationToken;
        using var ms = new MemoryStream();
        await using var w = new CsvAsyncStreamWriter(ms, new CsvWriteOptions { MaxColumnCount = 1 }, leaveOpen: true);

        await w.WriteFieldAsync("first", ct);
        var ex = await Assert.ThrowsAsync<CsvException>(async () => await writeSecondField(w, ct));
        Assert.Equal(CsvErrorCode.TooManyColumnsWritten, ex.ErrorCode);
    }

    [Fact]
    public async Task QuoteStyle_Always_QuotesEveryField()
    {
        var options = new CsvWriteOptions { QuoteStyle = QuoteStyle.Always };
        string row = await WriteAsync(async (w, ct) =>
        {
            await w.WriteFieldAsync("plain", ct);
            await w.WriteFieldAsync(string.Empty, ct);
            await w.WriteFieldAsync("has\"quote", ct);
        }, options);

        Assert.Equal("\"plain\",\"\",\"has\"\"quote\"", row);
    }

    [Fact]
    public async Task QuoteStyle_Never_LeavesFieldsBare()
    {
        var options = new CsvWriteOptions { QuoteStyle = QuoteStyle.Never };
        string row = await WriteAsync(async (w, ct) =>
        {
            await w.WriteFieldAsync("has,comma", ct);
            await w.WriteFieldAsync("has\"quote", ct);
        }, options);

        // Never means never, even where the result is no longer round-trippable.
        Assert.Equal("has,comma,has\"quote", row);
    }

    [Fact]
    public async Task QuoteStyle_WhenNeeded_QuotesOnlyWhatRequiresIt()
    {
        string row = await WriteAsync(async (w, ct) =>
        {
            await w.WriteFieldAsync("plain", ct);
            await w.WriteFieldAsync("has,comma", ct);
        });

        Assert.Equal("plain,\"has,comma\"", row);
    }

    [Fact]
    public async Task InjectionProtection_EscapesAFormulaField()
    {
        // A leading '=' makes a spreadsheet evaluate the cell, so it must not survive bare.
        string row = await WriteAsync((w, ct) => w.WriteFieldAsync("=1+1", ct));
        Assert.NotEqual("=1+1", row);
        Assert.Contains("1+1", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InjectionProtection_None_LeavesTheFormulaAlone()
    {
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.None };
        Assert.Equal("=1+1", await WriteAsync((w, ct) => w.WriteFieldAsync("=1+1", ct), options));
    }

    [Fact]
    public async Task NullString_WritesAnEmptyField()
    {
        // NullValue is applied where a value arrives as object? — WriteRowAsync and the
        // record writer. The explicit string overload writes exactly what it is given.
        var options = new CsvWriteOptions { NullValue = "NULL" };
        Assert.Equal(string.Empty, await WriteAsync((w, ct) => w.WriteFieldAsync((string?)null, ct), options));
    }

    [Fact]
    public async Task NullInAnObjectRow_UsesTheConfiguredNullValue()
    {
        var options = new CsvWriteOptions { NullValue = "NULL" };
        object?[] values = ["a", null, "c"];
        Assert.Equal("a,NULL,c", await WriteRowAsync(values, options));
    }

    [Fact]
    public async Task NullInAStringRow_WritesAnEmptyField()
    {
        // The string overload is a straight field write, so NullValue does not apply —
        // only the object overload runs values through the formatter that consults it.
        var options = new CsvWriteOptions { NullValue = "NULL" };
        string?[] values = ["a", null, "c"];
        var ct = TestContext.Current.CancellationToken;

        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, options, leaveOpen: true))
        {
            await w.WriteRowAsync(values, ct);
        }

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8, leaveOpen: true);
        Assert.Equal("a,,c", (await reader.ReadToEndAsync(ct)).TrimEnd('\r', '\n'));
    }

    private static async Task<string> WriteRowAsync(object?[] values, CsvWriteOptions? options = null)
    {
        var ct = TestContext.Current.CancellationToken;
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, options, leaveOpen: true))
        {
            await w.WriteRowAsync(values, ct);
        }

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8, leaveOpen: true);
        return (await reader.ReadToEndAsync(ct)).TrimEnd('\r', '\n');
    }

    [Fact]
    public async Task ObjectRow_FormatsEveryValueTypeItIsGiven()
    {
        // The object overload dispatches on each boxed value's runtime type, so every arm
        // of that switch needs a value to land on.
        object?[] values =
            ["text", 1, 2L, 3.5d, 4.5f, 5.25m, true, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified), Guid.Empty];

        string row = await WriteRowAsync(values);

        Assert.StartsWith("text,1,2,3.5,4.5,5.25,", row, StringComparison.Ordinal);
        Assert.Contains("2024", row, StringComparison.Ordinal);
        Assert.EndsWith(Guid.Empty.ToString(), row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObjectRow_TooManyColumns_IsRejected()
    {
        object?[] values = ["a", "b"];
        var ex = await Assert.ThrowsAsync<CsvException>(
            () => WriteRowAsync(values, new CsvWriteOptions { MaxColumnCount = 1 }));
        Assert.Equal(CsvErrorCode.TooManyColumnsWritten, ex.ErrorCode);
    }

    [Fact]
    public async Task ObjectRow_LargerThanTheBuffer_FallsBackToTheAsyncPath()
    {
        // The fast path writes the whole row into the char buffer; a row that cannot fit
        // has to rewind and rewrite through the async writer without losing anything.
        object?[] values = [new string('x', 20_000), 7];
        string row = await WriteRowAsync(values);

        Assert.Equal(new string('x', 20_000) + ",7", row);
    }

    [Fact]
    public async Task Disposed_WriterRejectsFurtherWrites()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ms = new MemoryStream();
        var w = new CsvAsyncStreamWriter(ms, leaveOpen: true);
        await w.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await w.WriteFieldAsync(1, ct));
    }

    [Fact]
    public async Task Typed_DecimalCommaCultureWithCommaDelimiter_QuotesValue()
    {
        var options = new CsvWriteOptions
        {
            Culture = CultureInfo.GetCultureInfo("de-DE"),
            Delimiter = ','
        };

        string row = await WriteAsync(async (w, ct) =>
        {
            await w.WriteFieldAsync(12.34m, ct);
            await w.WriteFieldAsync(56.78d, ct);
        }, options);

        Assert.Equal("\"12,34\",\"56,78\"", row);
    }

    [Fact]
    public async Task Typed_QuoteStyleAlways_QuotesNumbersDatesAndGuids()
    {
        var options = new CsvWriteOptions { QuoteStyle = QuoteStyle.Always };
        var guid = new Guid("11112222-3333-4444-5555-666677778888");
        string row = await WriteAsync(async (w, ct) =>
        {
            await w.WriteFieldAsync(42, ct);
            await w.WriteFieldAsync(guid, ct);
        }, options);

        Assert.Equal($"\"42\",\"{guid}\"", row);
    }

    [Fact]
    public async Task Typed_InjectionProtection_SmartExemptsNegativeNumbers_AndProtectsDangerousFormattedValues()
    {
        // Negative numbers starting with '-' followed by a digit are smart-exempted by IsDangerousField.
        string neg = await WriteAsync(
            (w, ct) => w.WriteFieldAsync(-42, ct),
            new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.EscapeWithQuote });
        Assert.Equal("-42", neg);

        // DateTime formatted with a leading '=' triggers injection protection directly from charBuffer.
        var dt = new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Unspecified);
        string escapedDate = await WriteAsync(
            (w, ct) => w.WriteFieldAsync(dt, ct),
            new CsvWriteOptions
            {
                DateTimeFormat = "\\=yyyy-MM-dd",
                InjectionProtection = CsvInjectionProtection.EscapeWithQuote
            });
        Assert.Equal("\"'=2024-05-06\"", escapedDate);

        var ex = await Assert.ThrowsAsync<CsvException>(() => WriteAsync(
            (w, ct) => w.WriteFieldAsync(dt, ct),
            new CsvWriteOptions
            {
                DateTimeFormat = "\\=yyyy-MM-dd",
                InjectionProtection = CsvInjectionProtection.Reject
            }));
        Assert.Equal(CsvErrorCode.InjectionDetected, ex.ErrorCode);
    }

    [Fact]
    public async Task Typed_MaxFieldSizeExceeded_ThrowsCsvException()
    {
        var options = new CsvWriteOptions { MaxFieldSize = 3 };
        var ex = await Assert.ThrowsAsync<CsvException>(() => WriteAsync(
            (w, ct) => w.WriteFieldAsync(12345, ct),
            options));
        Assert.Equal(CsvErrorCode.FieldSizeExceeded, ex.ErrorCode);
    }

    [Fact]
    public async Task Typed_AcrossCharBufferBoundary_FlushesCleanly()
    {
        // Write strings right up to DEFAULT_CHAR_BUFFER_SIZE (16384), then write typed fields
        // across every remaining-space edge case (0..64 chars remaining).
        var ct = TestContext.Current.CancellationToken;
        string padding = new('a', 16370);
        string expectedSuffix = "123,456789,3.5,9999999";

        string result = await WriteAsync(async (w, token) =>
        {
            await w.WriteFieldAsync(padding, token);
            await w.WriteFieldAsync(123, token);
            await w.WriteFieldAsync(456789L, token);
            await w.WriteFieldAsync(3.5d, token);
            await w.WriteFieldAsync(9999999m, token);
        });

        Assert.Equal($"{padding},{expectedSuffix}", result);
        _ = ct;
    }
}
