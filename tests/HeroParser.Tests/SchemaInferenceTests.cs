using HeroParser.SeparatedValues.Detection;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for CSV schema inference (auto-detecting column types from sample data).
/// </summary>
public class SchemaInferenceTests
{
    #region Basic Type Detection

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_IntegerColumn_DetectsInt()
    {
        var csv = "Id\n1\n2\n3\n42";
        var schema = Csv.InferSchema(csv);

        Assert.Single(schema.Columns);
        Assert.Equal("Id", schema.Columns[0].Name);
        Assert.Equal(CsvInferredType.Integer, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_DecimalColumn_DetectsDecimal()
    {
        var csv = "Price\n1.99\n2.50\n3.14";
        var schema = Csv.InferSchema(csv);

        Assert.Single(schema.Columns);
        Assert.Equal(CsvInferredType.Decimal, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_BooleanColumn_DetectsBoolean()
    {
        var csv = "Active\ntrue\nfalse\ntrue\nTrue";
        var schema = Csv.InferSchema(csv);

        Assert.Single(schema.Columns);
        Assert.Equal(CsvInferredType.Boolean, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_DateTimeColumn_DetectsDateTime()
    {
        var csv = "Created\n2024-01-15\n2024-02-20\n2024-03-25";
        var schema = Csv.InferSchema(csv);

        Assert.Single(schema.Columns);
        Assert.Equal(CsvInferredType.DateTime, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_GuidColumn_DetectsGuid()
    {
        var csv = "Id\n550e8400-e29b-41d4-a716-446655440000\n6ba7b810-9dad-11d1-80b4-00c04fd430c8";
        var schema = Csv.InferSchema(csv);

        Assert.Single(schema.Columns);
        Assert.Equal(CsvInferredType.Guid, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_StringColumn_DetectsString()
    {
        var csv = "Name\nAlice\nBob\nCharlie";
        var schema = Csv.InferSchema(csv);

        Assert.Single(schema.Columns);
        Assert.Equal(CsvInferredType.String, schema.Columns[0].InferredType);
    }

    #endregion

    #region Multiple Columns

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_MultipleColumns_DetectsEachType()
    {
        var csv = "Name,Age,Balance,Active,Created\nAlice,30,1500.50,true,2024-01-15\nBob,25,2300.75,false,2024-02-20";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(5, schema.Columns.Count);
        Assert.Equal(CsvInferredType.String, schema.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.Integer, schema.Columns[1].InferredType);
        Assert.Equal(CsvInferredType.Decimal, schema.Columns[2].InferredType);
        Assert.Equal(CsvInferredType.Boolean, schema.Columns[3].InferredType);
        Assert.Equal(CsvInferredType.DateTime, schema.Columns[4].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_PreservesColumnNames()
    {
        var csv = "First Name,Last Name,Email\nAlice,Smith,alice@test.com\nBob,Jones,bob@test.com";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(3, schema.Columns.Count);
        Assert.Equal("First Name", schema.Columns[0].Name);
        Assert.Equal("Last Name", schema.Columns[1].Name);
        Assert.Equal("Email", schema.Columns[2].Name);
    }

    #endregion

    #region Nullable Detection

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_ColumnWithNulls_DetectsNullable()
    {
        var csv = "Age\n30\n\n25\n";
        var schema = Csv.InferSchema(csv);

        Assert.Single(schema.Columns);
        Assert.Equal(CsvInferredType.Integer, schema.Columns[0].InferredType);
        Assert.True(schema.Columns[0].IsNullable);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_NoNulls_NotNullable()
    {
        var csv = "Age\n30\n25\n42";
        var schema = Csv.InferSchema(csv);

        Assert.Single(schema.Columns);
        Assert.False(schema.Columns[0].IsNullable);
    }

    #endregion

    #region Mixed Types Fall Back

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_MixedIntAndDecimal_FallsBackToDecimal()
    {
        var csv = "Value\n1\n2.5\n3\n4.7";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(CsvInferredType.Decimal, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_MixedIntAndString_FallsBackToString()
    {
        var csv = "Value\n1\nhello\n3\nworld";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(CsvInferredType.String, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_MixedBoolAndString_FallsBackToString()
    {
        var csv = "Value\ntrue\nmaybe\nfalse";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(CsvInferredType.String, schema.Columns[0].InferredType);
    }

    #endregion

    #region Custom Delimiter

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_SemicolonDelimited_DetectsTypes()
    {
        var csv = "Name;Age\nAlice;30\nBob;25";
        var schema = Csv.InferSchema(csv, new CsvSchemaInferenceOptions { Delimiter = ';' });

        Assert.Equal(2, schema.Columns.Count);
        Assert.Equal(CsvInferredType.String, schema.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.Integer, schema.Columns[1].InferredType);
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_HeaderOnly_ReturnsColumnsAsString()
    {
        var csv = "Name,Age,City";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(3, schema.Columns.Count);
        // With no data rows, all columns default to String
        Assert.All(schema.Columns, c => Assert.Equal(CsvInferredType.String, c.InferredType));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_EmptyInput_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() => Csv.InferSchema(""));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_SampleRowsLimit_OnlySamplesSpecifiedRows()
    {
        // First 2 data rows are integers, remaining are strings
        var csv = "Value\n1\n2\nhello\nworld";
        var schema = Csv.InferSchema(csv, new CsvSchemaInferenceOptions { SampleRows = 2 });

        // Should detect Integer because it only sampled the first 2 data rows
        Assert.Equal(CsvInferredType.Integer, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_AllEmpty_DetectsString()
    {
        var csv = "Value\n\n\n";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(CsvInferredType.String, schema.Columns[0].InferredType);
        Assert.True(schema.Columns[0].IsNullable);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_NegativeIntegers_DetectsInteger()
    {
        var csv = "Value\n-1\n-42\n100";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(CsvInferredType.Integer, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_LargeIntegers_DetectsLong()
    {
        var csv = "Value\n9999999999999\n1234567890123";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(CsvInferredType.Long, schema.Columns[0].InferredType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_DateTimeVariousFormats_DetectsDateTime()
    {
        var csv = "Date\n2024-01-15T10:30:00\n2024-02-20 14:00:00\n01/15/2024";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(CsvInferredType.DateTime, schema.Columns[0].InferredType);
    }

    #endregion

    #region Column Statistics

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_ReportsRowCount()
    {
        var csv = "Name,Age\nAlice,30\nBob,25\nCharlie,35";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(3, schema.SampledRowCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferSchema_ReportsMaxLength()
    {
        var csv = "Name\nAl\nBob\nCharlie";
        var schema = Csv.InferSchema(csv);

        Assert.Equal(7, schema.Columns[0].MaxLength);
    }

    #endregion
}
