#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
using HeroParser.Configuration;
using System.Buffers;
using System.Text;

namespace HeroParser.Tests.Core;

/// <summary>
/// Tests for Span and Memory overloads in Csv API.
/// </summary>
public class CsvSpanMemoryTests
{
    private const string SimpleCsv = "Name,Age,City\nJohn,25,Boston\nJane,30,Seattle";

    [Fact]
    public void ParseSpan_FromReadOnlySpanOfChars_ParsesCorrectly()
    {
        // Arrange
        ReadOnlySpan<char> span = SimpleCsv.AsSpan();

        // Act
        var result = Csv.ParseContent(span);

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
    public void ParseMemory_FromReadOnlyMemoryOfChars_ParsesCorrectly()
    {
        // Arrange
        ReadOnlyMemory<char> memory = SimpleCsv.AsMemory();

        // Act
        var result = Csv.ParseContent(memory.ToString());

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Jane", result[1][0]);
    }

    [Fact]
    public async Task FromSpan_StreamsDataCorrectly()
    {
        // Arrange
        ReadOnlySpan<char> span = SimpleCsv.AsSpan();

        // Act
        // Convert span to string since current API doesn't support FromSpan
        var result = await Csv.FromContent(span.ToString());

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Seattle", result[1][2]);
    }

    [Fact]
    public async Task FromMemory_StreamsDataCorrectly()
    {
        // Arrange
        ReadOnlyMemory<char> memory = SimpleCsv.AsMemory();

        // Act
        // Convert memory to string since current API doesn't support FromMemory<char>
        var result = await Csv.FromContent(memory.ToString());

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("30", result[1][1]);
    }

    [Fact]
    public void ParseBytes_FromReadOnlySpanOfBytes_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        ReadOnlySpan<byte> span = bytes.AsSpan();

        // Act
        var result = Csv.ParseContent(span);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void ParseBytes_FromReadOnlyMemoryOfBytes_ParsesCorrectly()
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

    [Fact]
    public async Task FromBytes_WithReadOnlySpan_StreamsCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        ReadOnlySpan<byte> span = bytes.AsSpan();

        // Act
        var result = await Csv.FromContent(span.ToArray());

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("30", result[1][1]);
    }

    [Fact]
    public async Task FromBytes_WithReadOnlyMemory_StreamsCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        ReadOnlyMemory<byte> memory = bytes.AsMemory();

        // Act
        var result = await Csv.FromContent(memory);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void ParseSpan_WithConfiguration_AppliesSettings()
    {
        // Arrange
        var csv = "Name;Age;City\nJohn;25;Boston";
        ReadOnlySpan<char> span = csv.AsSpan();
        var config = new CsvReadConfiguration { Delimiter = ';' };

        // Act
        var result = Csv.ParseContent(span.ToString(), delimiter: config.Delimiter);

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void ParseBytes_WithEncoding_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF32.GetBytes(SimpleCsv);
        ReadOnlySpan<byte> span = bytes.AsSpan();

        // Act
        var result = Csv.ParseContent(span.ToArray(), encoding: Encoding.UTF32);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
    }

    [Fact]
    public void ParseSpan_EmptySpan_ReturnsEmptyArray()
    {
        // Arrange
        ReadOnlySpan<char> span = ReadOnlySpan<char>.Empty;

        // Act
        var result = Csv.ParseContent(span);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ParseMemory_EmptyMemory_ReturnsEmptyArray()
    {
        // Arrange
        ReadOnlyMemory<char> memory = ReadOnlyMemory<char>.Empty;

        // Act
        var result = Csv.ParseContent(memory.ToString());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ParseBytes_EmptySpan_ReturnsEmptyArray()
    {
        // Arrange
        ReadOnlySpan<byte> span = ReadOnlySpan<byte>.Empty;

        // Act
        var result = Csv.ParseContent(span);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void CreateReader_FromSpan_CreatesValidReader()
    {
        // Arrange
        ReadOnlySpan<char> span = SimpleCsv.AsSpan();

        // Act
        using var reader = Csv.OpenContent(span.ToString());

        // Assert
        Assert.NotNull(reader);
        var record = reader.ReadRecord();
        Assert.NotNull(record);
        Assert.Equal("John", record[0]);
    }

    [Fact]
    public void CreateReader_FromMemory_CreatesValidReader()
    {
        // Arrange
        ReadOnlyMemory<char> memory = SimpleCsv.AsMemory();

        // Act
        using var reader = Csv.OpenContent(memory.ToString());

        // Assert
        Assert.NotNull(reader);
        var record = reader.ReadRecord();
        Assert.NotNull(record);
        Assert.Equal("John", record[0]);
    }

    [Fact]
    public void CreateReader_FromByteMemory_CreatesValidReader()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        ReadOnlyMemory<byte> memory = bytes.AsMemory();

        // Act
        using var stream = new MemoryStream(memory.ToArray());
        using var reader = Csv.OpenStream(stream);

        // Assert
        Assert.NotNull(reader);
        var record = reader.ReadRecord();
        Assert.NotNull(record);
        Assert.Equal("John", record[0]);
    }

    [Fact]
    public void ParseSpan_WithStackAllocatedData_WorksCorrectly()
    {
        // Arrange
        Span<char> buffer = stackalloc char[100];
        var csvData = "Name,Age\nJohn,25";
        csvData.AsSpan().CopyTo(buffer);
        var dataSpan = buffer.Slice(0, csvData.Length);

        // Act
        var result = Csv.ParseContent(dataSpan);

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
    }

    [Fact]
    public void ParseBytes_WithPooledMemory_WorksCorrectly()
    {
        // Arrange
        var pool = ArrayPool<byte>.Shared;
        var csvData = "Name,Age\nJohn,25";
        var bytes = Encoding.UTF8.GetBytes(csvData);
        var buffer = pool.Rent(bytes.Length);

        try
        {
            bytes.CopyTo(buffer, 0);
            var span = buffer.AsSpan(0, bytes.Length);

            // Act
            var result = Csv.ParseContent(span);

            // Assert
            Assert.Single(result);
            Assert.Equal("John", result[0][0]);
            Assert.Equal("25", result[0][1]);
        }
        finally
        {
            pool.Return(buffer);
        }
    }
}
#endif