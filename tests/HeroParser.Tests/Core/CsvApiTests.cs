using HeroParser.Configuration;
using System.Text;

namespace HeroParser.Tests.Core;

/// <summary>
/// Tests for the Csv static API methods - updated for new HeroParser API design.
/// </summary>
public class CsvApiTests
{
    private const string SimpleCsv = "Name,Age,City\nJohn,25,Boston\nJane,30,Seattle";
    private const string SimpleCsvNoHeader = "John,25,Boston\nJane,30,Seattle";

    #region ParseContent Tests (Synchronous)

    [Fact]
    public void ParseContent_String_ParsesCorrectly()
    {
        // Act
        var result = Csv.ParseContent(SimpleCsv);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("30", result[1][1]);
        Assert.Equal("Seattle", result[1][2]);
    }

    [Fact]
    public void ParseContent_String_WithoutHeaders_ParsesCorrectly()
    {
        // Act
        var result = Csv.ParseContent(SimpleCsvNoHeader, hasHeaders: false);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("30", result[1][1]);
        Assert.Equal("Seattle", result[1][2]);
    }

    [Fact]
    public void ParseContent_String_CustomDelimiter_ParsesCorrectly()
    {
        // Arrange
        var csv = "Name;Age;City\nJohn;25;Boston\nJane;30;Seattle";

        // Act
        var result = Csv.ParseContent(csv, delimiter: ';');

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void ParseContent_ByteArray_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);

        // Act
        var result = Csv.ParseContent(bytes);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void ParseContent_ByteArray_WithEncoding_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF32.GetBytes(SimpleCsv);

        // Act
        var result = Csv.ParseContent(bytes, encoding: Encoding.UTF32);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void ParseContent_ReadOnlyMemoryByte_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        ReadOnlyMemory<byte> memory = bytes.AsMemory();

        // Act
        var result = Csv.ParseContent(memory);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("Seattle", result[1][2]);
    }

#if NET5_0_OR_GREATER
    [Fact]
    public void ParseContent_ReadOnlySpanChar_ParsesCorrectly()
    {
        // Arrange
        ReadOnlySpan<char> span = SimpleCsv.AsSpan();

        // Act
        var result = Csv.ParseContent(span);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void ParseContent_ReadOnlySpanByte_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        ReadOnlySpan<byte> span = bytes.AsSpan();

        // Act
        var result = Csv.ParseContent(span);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("Seattle", result[1][2]);
    }
#endif

    #endregion

    #region FromContent Tests (Asynchronous)

    [Fact]
    public async Task FromContent_String_ParsesCorrectly()
    {
        // Act
        var result = await Csv.FromContent(SimpleCsv);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public async Task FromContent_String_WithCancellation_ParsesCorrectly()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        var result = await Csv.FromContent(SimpleCsv, cancellationToken: cts.Token);

        // Assert
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public async Task FromContent_ByteArray_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);

        // Act
        var result = await Csv.FromContent(bytes);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public async Task FromContent_ReadOnlyMemoryByte_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        ReadOnlyMemory<byte> memory = bytes.AsMemory();

        // Act
        var result = await Csv.FromContent(memory);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("Seattle", result[1][2]);
    }

    #endregion

    #region File Operations Tests

    [Fact]
    public void ParseFile_ParsesCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, SimpleCsv);

            // Act
            var result = Csv.ParseFile(tempFile);

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("John", result[0][0]);
            Assert.Equal("Boston", result[0][2]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FromFile_ParsesCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, SimpleCsv);

            // Act
            var result = await Csv.FromFile(tempFile);

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("Jane", result[1][0]);
            Assert.Equal("Seattle", result[1][2]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Stream Operations Tests

    [Fact]
    public async Task FromStream_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        using var stream = new MemoryStream(bytes);

        // Act
        var result = await Csv.FromStream(stream);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public async Task FromStream_WithEncoding_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF32.GetBytes(SimpleCsv);
        using var stream = new MemoryStream(bytes);

        // Act
        var result = await Csv.FromStream(stream, encoding: Encoding.UTF32);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("Seattle", result[1][2]);
    }

    #endregion

    #region Advanced Reader Tests

    [Fact]
    public void OpenContent_CreatesReaderCorrectly()
    {
        // Act
        using var reader = Csv.OpenContent(SimpleCsv);

        // Assert
        Assert.NotNull(reader);
        Assert.False(reader.EndOfCsv);
        // Configuration is a value type and always non-null
    }

    [Fact]
    public void OpenFile_CreatesReaderCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, SimpleCsv);

            // Act
            using var reader = Csv.OpenFile(tempFile);

            // Assert
            Assert.NotNull(reader);
            Assert.False(reader.EndOfCsv);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void OpenStream_CreatesReaderCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        using var stream = new MemoryStream(bytes);

        // Act
        using var reader = Csv.OpenStream(stream);

        // Assert
        Assert.NotNull(reader);
        Assert.False(reader.EndOfCsv);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void ParseContent_NullContent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Csv.ParseContent((string)null!));
    }

    [Fact]
    public void ParseContent_NullByteArray_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Csv.ParseContent((byte[])null!));
    }

    [Fact]
    public async Task FromContent_NullContent_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Csv.FromContent((string)null!));
    }

    [Fact]
    public void ParseFile_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Csv.ParseFile(null!));
    }

    [Fact]
    public async Task FromFile_NullPath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Csv.FromFile(null!));
    }

    [Fact]
    public async Task FromStream_NullStream_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Csv.FromStream(null!));
    }

    [Fact]
    public void OpenContent_NullContent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Csv.OpenContent(null!));
    }

    [Fact]
    public void OpenFile_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Csv.OpenFile(null!));
    }

    [Fact]
    public void OpenStream_NullStream_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Csv.OpenStream(null!));
    }

    #endregion

    #region Performance and Edge Cases

    [Fact]
    public void ParseContent_EmptyString_ReturnsEmptyArray()
    {
        // Act
        var result = Csv.ParseContent("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ParseContent_HeaderOnly_ReturnsEmptyArray()
    {
        // Act
        var result = Csv.ParseContent("Name,Age,City");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ParseContent_SingleRecord_ParsesCorrectly()
    {
        // Act
        var result = Csv.ParseContent("Name,Age,City\nJohn,25,Boston");

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public async Task FromContent_LargeContent_ParsesCorrectly()
    {
        // Arrange
        var sb = new StringBuilder();
        sb.AppendLine("Name,Age,City");
        for (int i = 0; i < 1000; i++)
        {
            sb.AppendLine($"Person{i},{20 + (i % 50)},City{i % 10}");
        }

        // Act
        var result = await Csv.FromContent(sb.ToString());

        // Assert
        Assert.Equal(1000, result.Length);
        Assert.Equal("Person0", result[0][0]);
        Assert.Equal("Person999", result[999][0]);
    }

    #endregion
}