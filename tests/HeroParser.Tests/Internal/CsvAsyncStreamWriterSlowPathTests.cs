using System.Globalization;
using System.Text;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers the buffer-exhaustion paths in <see cref="CsvAsyncStreamWriter"/>.
///
/// Each typed WriteFieldAsync overload writes its delimiter inline while the char
/// buffer has room and defers to a private WriteFieldSlowAsync overload when it does
/// not. The buffer is 16K chars and is only drained on that slow path or an explicit
/// flush, so the fallback is unreachable until a row fills it exactly — which no
/// existing test did, leaving every one of those overloads at zero coverage.
///
/// Filling the buffer to the byte with the first field puts the following field's
/// delimiter one char over the edge, which is precisely the slow-path condition.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvAsyncStreamWriterSlowPathTests
{
    /// <summary>Matches CsvAsyncStreamWriter's private DEFAULT_CHAR_BUFFER_SIZE.</summary>
    private const int CHAR_BUFFER_SIZE = 16 * 1024;

    /// <summary>The writer terminates rows with CRLF per RFC 4180, not Environment.NewLine.</summary>
    private const string ROW_TERMINATOR = "\r\n";

    /// <summary>
    /// A field of exactly the buffer's length, containing nothing that needs quoting so
    /// the writer copies it verbatim and lands the position on the boundary.
    /// </summary>
    private static string ExactlyFillsBuffer() => new('a', CHAR_BUFFER_SIZE);

    private static async Task<string> ReadAllAsync(MemoryStream ms)
    {
        ms.Position = 0;
        using var sr = new StreamReader(ms, Encoding.UTF8, leaveOpen: true);
        return await sr.ReadToEndAsync();
    }

    /// <summary>
    /// Writes the buffer-filling field, then <paramref name="writeSecondField"/> on the
    /// full buffer, and returns the second field as it was written out.
    /// </summary>
    private static async Task<string> WriteOnFullBufferAsync(
        Func<CsvAsyncStreamWriter, CancellationToken, ValueTask> writeSecondField)
    {
        var ct = TestContext.Current.CancellationToken;
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(ExactlyFillsBuffer(), ct);
            await writeSecondField(w, ct);
            await w.EndRowAsync(ct);
        }

        string all = await ReadAllAsync(ms);
        int comma = all.IndexOf(',', StringComparison.Ordinal);
        Assert.True(comma >= 0, "the delimiter should have been written on the slow path");
        return all[(comma + 1)..].TrimEnd('\r', '\n');
    }

    [Fact]
    public async Task WriteFieldAsync_Int_OnFullBuffer_FlushesAndWrites()
        => Assert.Equal("42", await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync(42, ct)));

    [Fact]
    public async Task WriteFieldAsync_Long_OnFullBuffer_FlushesAndWrites()
        => Assert.Equal("9007199254740993", await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync(9007199254740993L, ct)));

    [Fact]
    public async Task WriteFieldAsync_Double_OnFullBuffer_FlushesAndWrites()
        => Assert.Equal("1.5", await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync(1.5d, ct)));

    [Fact]
    public async Task WriteFieldAsync_Float_OnFullBuffer_FlushesAndWrites()
        => Assert.Equal("2.5", await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync(2.5f, ct)));

    [Fact]
    public async Task WriteFieldAsync_Decimal_OnFullBuffer_FlushesAndWrites()
        => Assert.Equal("3.25", await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync(3.25m, ct)));

    [Fact]
    public async Task WriteFieldAsync_Bool_OnFullBuffer_FlushesAndWrites()
        => Assert.Equal(true.ToString(CultureInfo.InvariantCulture), await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync(true, ct)));

    [Fact]
    public async Task WriteFieldAsync_DateTime_OnFullBuffer_FlushesAndWrites()
    {
        var value = new DateTime(2024, 3, 17, 8, 30, 0, DateTimeKind.Unspecified);
        string written = await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync(value, ct));
        Assert.Contains("2024", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteFieldAsync_Guid_OnFullBuffer_FlushesAndWrites()
    {
        var value = new Guid("11112222-3333-4444-5555-666677778888");
        Assert.Equal(value.ToString(), await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync(value, ct)));
    }

    [Fact]
    public async Task WriteFieldAsync_String_OnFullBuffer_FlushesAndWrites()
        => Assert.Equal("tail", await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync("tail", ct)));

    [Fact]
    public async Task WriteFieldAsync_Memory_OnFullBuffer_FlushesAndWrites()
        => Assert.Equal("mem", await WriteOnFullBufferAsync((w, ct) => w.WriteFieldAsync("mem".AsMemory(), ct)));

    [Fact]
    public async Task EndRowAsync_OnFullBuffer_FlushesBeforeNewLine()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            // Fills the buffer exactly, so the row terminator is what overflows it.
            await w.WriteFieldAsync(ExactlyFillsBuffer(), ct);
            await w.EndRowAsync(ct);
            await w.WriteFieldAsync("next", ct);
            await w.EndRowAsync(ct);
        }

        string all = await ReadAllAsync(ms);
        Assert.EndsWith("next" + ROW_TERMINATOR, all, StringComparison.Ordinal);
        Assert.Equal(CHAR_BUFFER_SIZE, all.IndexOf('\r') >= 0 ? all.IndexOf('\r') : all.IndexOf('\n'));
    }

    [Fact]
    public async Task WriteFieldAsync_ValueLargerThanBuffer_WritesWholeValue()
    {
        var ct = TestContext.Current.CancellationToken;
        // Twice the buffer forces the writer to drain mid-value rather than at a boundary.
        string huge = new('z', (CHAR_BUFFER_SIZE * 2) + 7);
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(huge, ct);
            await w.WriteFieldAsync(1, ct);
            await w.EndRowAsync(ct);
        }

        string all = await ReadAllAsync(ms);
        Assert.Equal(huge + ",1" + ROW_TERMINATOR, all);
    }
}
