using System;
using HeroParser.AI;
using Xunit;

namespace HeroParser.Tests.AI;

public class SchemaMetadataTests
{
    private sealed class DummyRecord
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class UnregisteredRecord
    {
        public int Id { get; set; }
    }

    [Fact]
    public void SchemaMetadata_CanRegisterAndRetrieveSchema()
    {
        // Arrange
        var expectedSchema = "{\"type\": \"object\"}";

        // Act
        SchemaMetadata.RegisterSchema<DummyRecord>(expectedSchema);
        var actualSchema = SchemaMetadata.ToLlmSchema<DummyRecord>();

        // Assert
        Assert.Equal(expectedSchema, actualSchema);
    }

    [Fact]
    public void SchemaMetadata_ThrowOnUnregisteredType()
    {
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(SchemaMetadata.ToLlmSchema<UnregisteredRecord>);
        Assert.Contains("No source-generated LLM schema found for type", ex.Message);
        Assert.Contains(typeof(UnregisteredRecord).FullName!, ex.Message);
    }
}
