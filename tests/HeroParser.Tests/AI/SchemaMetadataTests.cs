using System;
using System.Collections.Generic;
using HeroParser.AI;
using Xunit;

namespace HeroParser.Tests.AI;

public class SchemaMetadataTests
{
    private enum AgentEnum
    {
        Standard,
        Premium
    }

    private sealed class DummyRecord
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class UnregisteredRecord
    {
        public int Id { get; set; }
    }

    private sealed class ValidatedAgentRecord
    {
        [Validate(NotNull = true)]
        public string? RequiredName { get; set; }

        [Validate(NotEmpty = true)]
        public string? NonEmptyDesc { get; set; }

        [Validate(MinLength = 3, MaxLength = 10)]
        public string? Username { get; set; }

        [Validate(RangeMin = 18, RangeMax = 99)]
        public int Age { get; set; }

        [Validate(Pattern = @"^[0-9]{5}$", PatternTimeoutMs = 500)]
        public string? ZipCode { get; set; }

        public AgentEnum Type { get; set; }
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

    [Fact]
    public void MapFromToolCall_ThrowsOnNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(null!));
    }

    [Fact]
    public void MapFromToolCall_WithValidArguments_MapsSuccessfully()
    {
        // Arrange
        var args = new Dictionary<string, object?>
        {
            { "requiredname", "Alice" },
            { "NonEmptyDesc", "A valid description" },
            { "USERNAME", "alice123" },
            { "Age", 30 },
            { "ZipCode", "90210" },
            { "Type", "Premium" }
        };

        // Act
        var result = SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args);

        // Assert
        Assert.Equal("Alice", result.RequiredName);
        Assert.Equal("A valid description", result.NonEmptyDesc);
        Assert.Equal("alice123", result.Username);
        Assert.Equal(30, result.Age);
        Assert.Equal("90210", result.ZipCode);
        Assert.Equal(AgentEnum.Premium, result.Type);
    }

    [Fact]
    public void MapFromToolCall_WithEnumInt_MapsSuccessfully()
    {
        // Arrange
        var args = new Dictionary<string, object?>
        {
            { "RequiredName", "Bob" },
            { "NonEmptyDesc", "Desc" },
            { "Age", 20 },
            { "Type", 1 } // 1 is Premium
        };

        // Act
        var result = SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args);

        // Assert
        Assert.Equal(AgentEnum.Premium, result.Type);
    }

    [Fact]
    public void MapFromToolCall_ThrowsOnFailedConversion()
    {
        // Arrange
        var args = new Dictionary<string, object?>
        {
            { "RequiredName", "Bob" },
            { "Age", "not-a-number" }
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args));
    }

    [Fact]
    public void MapFromToolCall_Validation_NotNull()
    {
        var args = new Dictionary<string, object?>
        {
            { "RequiredName", null },
            { "NonEmptyDesc", "Desc" }
        };

        var ex = Assert.Throws<LlmToolCallValidationException>(() => SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args));
        Assert.Equal("RequiredName", ex.PropertyName);
    }

    [Fact]
    public void MapFromToolCall_Validation_NotEmpty()
    {
        var args = new Dictionary<string, object?>
        {
            { "RequiredName", "Bob" },
            { "NonEmptyDesc", "   " }
        };

        var ex = Assert.Throws<LlmToolCallValidationException>(() => SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args));
        Assert.Equal("NonEmptyDesc", ex.PropertyName);
    }

    [Fact]
    public void MapFromToolCall_Validation_MinLength()
    {
        var args = new Dictionary<string, object?>
        {
            { "RequiredName", "Bob" },
            { "NonEmptyDesc", "Desc" },
            { "Username", "ab" }
        };

        var ex = Assert.Throws<LlmToolCallValidationException>(() => SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args));
        Assert.Equal("Username", ex.PropertyName);
    }

    [Fact]
    public void MapFromToolCall_Validation_MaxLength()
    {
        var args = new Dictionary<string, object?>
        {
            { "RequiredName", "Bob" },
            { "NonEmptyDesc", "Desc" },
            { "Username", "morethantencharacters" }
        };

        var ex = Assert.Throws<LlmToolCallValidationException>(() => SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args));
        Assert.Equal("Username", ex.PropertyName);
    }

    [Fact]
    public void MapFromToolCall_Validation_RangeMin()
    {
        var args = new Dictionary<string, object?>
        {
            { "RequiredName", "Bob" },
            { "NonEmptyDesc", "Desc" },
            { "Age", 17 }
        };

        var ex = Assert.Throws<LlmToolCallValidationException>(() => SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args));
        Assert.Equal("Age", ex.PropertyName);
    }

    [Fact]
    public void MapFromToolCall_Validation_RangeMax()
    {
        var args = new Dictionary<string, object?>
        {
            { "RequiredName", "Bob" },
            { "NonEmptyDesc", "Desc" },
            { "Age", 100 }
        };

        var ex = Assert.Throws<LlmToolCallValidationException>(() => SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args));
        Assert.Equal("Age", ex.PropertyName);
    }

    [Fact]
    public void MapFromToolCall_Validation_Pattern()
    {
        var args = new Dictionary<string, object?>
        {
            { "RequiredName", "Bob" },
            { "NonEmptyDesc", "Desc" },
            { "Age", 25 },
            { "ZipCode", "1234a" }
        };

        var ex = Assert.Throws<LlmToolCallValidationException>(() => SchemaMetadata.MapFromToolCall<ValidatedAgentRecord>(args));
        Assert.Equal("ZipCode", ex.PropertyName);
    }
}
