using System.Text.Json;
using HeroParser.SeparatedValues.Validation;

namespace HeroParser.Cli;

internal static class CsvValidationReport
{
    public static void Save(string path, CsvValidationResult result)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteBoolean("valid", result.IsValid);
        writer.WriteBoolean("stoppedEarly", result.StoppedEarly);
        writer.WriteNumber("validatedRows", result.TotalRows);
        writer.WriteNumber("columnCount", result.ColumnCount);
        writer.WriteString("delimiter", result.Delimiter.ToString());
        writer.WriteStartArray("errors");
        foreach (var error in result.Errors)
        {
            writer.WriteStartObject();
            writer.WriteString("type", error.ErrorType.ToString());
            writer.WriteString("message", error.Message);
            writer.WriteNumber("rowNumber", error.RowNumber);
            writer.WriteNumber("columnNumber", error.ColumnNumber);
            if (error.Expected is not null) writer.WriteString("expected", error.Expected);
            if (error.Actual is not null) writer.WriteString("actual", error.Actual);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
