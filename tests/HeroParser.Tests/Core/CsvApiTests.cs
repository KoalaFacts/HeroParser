using HeroParser.Configuration;
using System.Text;

namespace HeroParser.Tests.Core;

/// <summary>
/// Tests for the Csv static API methods added in F1 Cycle 2.
/// </summary>
public class CsvApiTests
{
    private const string SimpleCsv = "Name,Age,City\nJohn,25,Boston\nJane,30,Seattle";
    private const string SimpleCsvNoHeader = "John,25,Boston\nJane,30,Seattle";

    #region FromXXX Streaming Methods Tests

    [Fact]
    public void FromString_StreamsDataCorrectly()
    {
        // Arrange
        var csv = SimpleCsv;

        // Act
        var result = Csv.FromString(csv).ToArray();

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
    public void FromString_StreamingEnumeratesOnce()
    {
        // Arrange
        var csv = SimpleCsv;
        var enumerated = false;

        // Act
        var enumerable = Csv.FromString(csv);

        foreach (var record in enumerable)
        {
            if (!enumerated)
            {
                enumerated = true;
                Assert.Equal("John", record[0]);
            }
            // Break after first to verify streaming
            break;
        }

        // Assert - verify we can enumerate again (new enumeration)
        var secondEnumeration = enumerable.ToArray();
        Assert.Equal(2, secondEnumeration.Length);
    }

    [Fact]
    public void FromReader_StreamsDataCorrectly()
    {
        // Arrange
        using var reader = new StringReader(SimpleCsv);

        // Act
        var result = Csv.FromReader(reader).ToArray();

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
    }

    [Fact]
    public void FromStream_StreamsDataCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        using var stream = new MemoryStream(bytes);

        // Act
        var result = Csv.FromStream(stream).ToArray();

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void FromFile_StreamsDataCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, SimpleCsv);

            // Act
            var result = Csv.FromFile(tempFile).ToArray();

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("Jane", result[1][0]);
            Assert.Equal("30", result[1][1]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void FromBytes_StreamsDataCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);

        // Act
        var result = Csv.FromBytes(bytes).ToArray();

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Seattle", result[1][2]);
    }

    [Fact]
    public void FromBytes_WithDifferentEncoding_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF32.GetBytes(SimpleCsv);

        // Act
        var result = Csv.FromBytes(bytes, Encoding.UTF32).ToArray();

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Jane", result[1][0]);
    }

    #endregion

    #region ParseXXX Immediate Methods Tests

    [Fact]
    public void ParseString_ReturnsArrayImmediately()
    {
        // Act
        var result = Csv.ParseString(SimpleCsv);

        // Assert
        Assert.IsType<string[][]>(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Seattle", result[1][2]);
    }

    [Fact]
    public void ParseReader_ReturnsArrayImmediately()
    {
        // Arrange
        using var reader = new StringReader(SimpleCsv);

        // Act
        var result = Csv.ParseReader(reader);

        // Assert
        Assert.IsType<string[][]>(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("25", result[0][1]);
    }

    [Fact]
    public void ParseStream_ReturnsArrayImmediately()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        using var stream = new MemoryStream(bytes);

        // Act
        var result = Csv.ParseStream(stream);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void ParseFile_ReturnsArrayImmediately()
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
            Assert.Equal("30", result[1][1]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseBytes_ReturnsArrayImmediately()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);

        // Act
        var result = Csv.ParseBytes(bytes);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Jane", result[1][0]);
    }

    #endregion

    #region Async Methods Tests

    [Fact]
    public async Task ParseStringAsync_ParsesCorrectly()
    {
        // Act
        var result = await Csv.ParseStringAsync(SimpleCsv);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Seattle", result[1][2]);
    }

    [Fact]
    public async Task ParseFileAsync_ParsesCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, SimpleCsv);

            // Act
            var result = await Csv.ParseFileAsync(tempFile);

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("Jane", result[1][0]);
            Assert.Equal("30", result[1][1]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseReaderAsync_ParsesCorrectly()
    {
        // Arrange
        using var reader = new StringReader(SimpleCsv);

        // Act
        var result = await Csv.ParseReaderAsync(reader);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public async Task ParseStreamAsync_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        using var stream = new MemoryStream(bytes);

        // Act
        var result = await Csv.ParseStreamAsync(stream);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
    }

    [Fact]
    public async Task ParseBytesAsync_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);

        // Act
        var result = await Csv.ParseBytesAsync(bytes);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Seattle", result[1][2]);
    }

    [Fact]
    public async Task ParseBytesAsync_WithEncoding_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF32.GetBytes(SimpleCsv);

        // Act
        var result = await Csv.ParseBytesAsync(bytes, Encoding.UTF32);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("Jane", result[1][0]);
    }

    #endregion

    #region CreateReader Methods Tests

    [Fact]
    public void CreateReader_FromString_CreatesValidReader()
    {
        // Act
        using var reader = Csv.CreateReader(SimpleCsv);

        // Assert
        Assert.NotNull(reader);
        Assert.False(reader.EndOfCsv);

        var record = reader.ReadRecord();
        Assert.NotNull(record);
        Assert.Equal("John", record[0]);
    }

    [Fact]
    public void CreateReader_FromTextReader_CreatesValidReader()
    {
        // Arrange
        using var textReader = new StringReader(SimpleCsv);

        // Act
        using var csvReader = Csv.CreateReader(textReader);

        // Assert
        Assert.NotNull(csvReader);
        var record = csvReader.ReadRecord();
        Assert.Equal("John", record[0]);
    }

    [Fact]
    public void CreateReader_FromStream_CreatesValidReader()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        using var stream = new MemoryStream(bytes);

        // Act
        using var reader = Csv.CreateReader(stream);

        // Assert
        Assert.NotNull(reader);
        var record = reader.ReadRecord();
        Assert.Equal("John", record[0]);
    }

    [Fact]
    public void CreateReaderFromFile_CreatesValidReader()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, SimpleCsv);

            // Act
            using var reader = Csv.CreateReaderFromFile(tempFile);

            // Assert
            Assert.NotNull(reader);
            var record = reader.ReadRecord();
            Assert.Equal("John", record[0]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void CreateReader_FromBytes_CreatesValidReader()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);

        // Act
        using var reader = Csv.CreateReader(bytes);

        // Assert
        Assert.NotNull(reader);
        var record = reader.ReadRecord();
        Assert.Equal("John", record[0]);
    }

    [Fact]
    public void CreateReader_AllowsCustomIteration()
    {
        // Arrange
        using var reader = Csv.CreateReader(SimpleCsv);
        var recordCount = 0;

        // Act
        while (!reader.EndOfCsv)
        {
            var record = reader.ReadRecord();
            if (record != null)
                recordCount++;
        }

        // Assert
        Assert.Equal(2, recordCount);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void FromString_WithCustomDelimiter_ParsesCorrectly()
    {
        // Arrange
        var csv = "Name;Age;City\nJohn;25;Boston\nJane;30;Seattle";
        var config = new CsvReadConfiguration { Delimiter = ';' };

        // Act
        var result = Csv.FromString(csv, config).ToArray();

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void ParseString_WithNoHeader_ParsesAllRows()
    {
        // Arrange
        var config = new CsvReadConfiguration { HasHeaderRow = false };

        // Act
        var result = Csv.ParseString(SimpleCsvNoHeader, config);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Jane", result[1][0]);
    }

    [Fact]
    public void FromString_WithTrimValues_TrimsWhitespace()
    {
        // Arrange
        var csv = "Name,Age,City\n  John  , 25 , Boston  \n Jane , 30,  Seattle ";
        var config = new CsvReadConfiguration { TrimValues = true };

        // Act
        var result = Csv.FromString(csv, config).ToArray();

        // Assert
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void ParseString_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.ParseString(null));
    }

    [Fact]
    public void FromString_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.FromString(null).ToArray());
    }

    [Fact]
    public void ParseReader_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.ParseReader(null));
    }

    [Fact]
    public void FromReader_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.FromReader(null).ToArray());
    }

    [Fact]
    public void ParseStream_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.ParseStream(null));
    }

    [Fact]
    public void FromStream_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.FromStream(null).ToArray());
    }

    [Fact]
    public void ParseFile_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.ParseFile(null));
    }

    [Fact]
    public void FromFile_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.FromFile(null).ToArray());
    }

    [Fact]
    public void ParseBytes_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.ParseBytes((byte[])null));
    }

    [Fact]
    public void FromBytes_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.FromBytes(null).ToArray());
    }

    [Fact]
    public async Task ParseStringAsync_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => Csv.ParseStringAsync(null));
    }

    [Fact]
    public async Task ParseFileAsync_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => Csv.ParseFileAsync(null));
    }

    [Fact]
    public void CreateReader_NullString_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.CreateReader((string)null));
    }

    [Fact]
    public void CreateReader_NullTextReader_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.CreateReader((TextReader)null));
    }

    [Fact]
    public void ParseString_EmptyString_ReturnsEmptyArray()
    {
        // Act
        var result = Csv.ParseString("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FromString_EmptyString_ReturnsEmptyEnumerable()
    {
        // Act
        var result = Csv.FromString("").ToArray();

        // Assert
        Assert.Empty(result);
    }

    #endregion
}