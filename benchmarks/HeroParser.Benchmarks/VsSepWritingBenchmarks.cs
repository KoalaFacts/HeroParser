using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using CsvHelper;
using CsvHelper.Configuration;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using nietras.SeparatedValues;
using Sylvan.Data.Csv;
using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Head-to-head CSV WRITING comparison: HeroParser vs Sep library.
/// Sep by nietras (https://github.com/nietras/Sep) is currently one of the fastest CSV parsers for .NET.
/// These benchmarks ensure HeroParser remains competitive with Sep's writing performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class VsSepWritingBenchmarks
{
    // Pre-generated data to write
    private string[][] data = null!;

    [Params(100, 1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 25, 50)]
    public int Columns { get; set; }

    [Params(false, true)]
    public bool WithQuotes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Pre-generate the data to isolate writing performance from data generation
        data = new string[Rows][];
        for (int r = 0; r < Rows; r++)
        {
            data[r] = new string[Columns];
            for (int c = 0; c < Columns; c++)
            {
                // 50% of values contain commas (requiring quoting) when WithQuotes is true
                if (WithQuotes && (r * Columns + c) % 2 == 0)
                {
                    data[r][c] = $"value,{r},{c}"; // Contains comma, needs quoting
                }
                else
                {
                    data[r][c] = $"value_{r}_{c}";
                }
            }
        }
    }

    [Benchmark(Baseline = true, Description = "Sep")]
    public string Sep_Write()
    {
        using var writer = Sep.Writer().ToText();

        for (int r = 0; r < data.Length; r++)
        {
            using var row = writer.NewRow();
            var rowData = data[r];
            for (int c = 0; c < rowData.Length; c++)
            {
                row[$"Col{c}"].Set(rowData[c]);
            }
        }

        return writer.ToString();
    }

    [Benchmark(Description = "Sylvan")]
    public string Sylvan_Write()
    {
        using var stringWriter = new StringWriter();
        using var writer = CsvDataWriter.Create(stringWriter);

        var headers = new string[Columns];
        for (int c = 0; c < Columns; c++)
        {
            headers[c] = $"Col{c}";
        }

        using var dataReader = new ArrayDataReader(data, headers);
        writer.Write(dataReader);

        return stringWriter.ToString();
    }

    [Benchmark(Description = "CsvHelper")]
    public string CsvHelper_Write()
    {
        using var stringWriter = new StringWriter();
        using var csvHelper = new CsvWriter(stringWriter, new CsvConfiguration(CultureInfo.InvariantCulture));

        for (int c = 0; c < Columns; c++)
        {
            csvHelper.WriteField($"Col{c}");
        }
        csvHelper.NextRecord();

        for (int r = 0; r < data.Length; r++)
        {
            var rowData = data[r];
            for (int c = 0; c < rowData.Length; c++)
            {
                csvHelper.WriteField(rowData[c]);
            }
            csvHelper.NextRecord();
        }

        csvHelper.Flush();
        return stringWriter.ToString();
    }

    [Benchmark(Description = "HeroParser (row-by-row)")]
    public string HeroParser_WriteRowByRow()
    {
        using var stringWriter = new StringWriter();
        using var writer = Csv.CreateWriter(stringWriter, leaveOpen: true);

        // Write header
        var headers = new string[Columns];
        for (int c = 0; c < Columns; c++)
        {
            headers[c] = $"Col{c}";
        }
        writer.WriteRow(headers);

        // Write data rows
        for (int r = 0; r < data.Length; r++)
        {
            writer.WriteRow(data[r]);
        }

        writer.Flush();
        return stringWriter.ToString();
    }

    [Benchmark(Description = "HeroParser (WriteRow object[])")]
    public string HeroParser_WriteRowObjects()
    {
        using var stringWriter = new StringWriter();
        using var writer = Csv.CreateWriter(stringWriter, leaveOpen: true);

        // Write header
        var headers = new object[Columns];
        for (int c = 0; c < Columns; c++)
        {
            headers[c] = $"Col{c}";
        }
        writer.WriteRow(headers);

        // Write data rows (using object[] overload)
        var rowObjects = new object[Columns];
        for (int r = 0; r < data.Length; r++)
        {
            var rowData = data[r];
            for (int c = 0; c < rowData.Length; c++)
            {
                rowObjects[c] = rowData[c];
            }
            writer.WriteRow(rowObjects);
        }

        writer.Flush();
        return stringWriter.ToString();
    }

    private class ArrayDataReader : DbDataReader
    {
        private readonly string[][] data;
        private readonly string[] headers;
        private int index = -1;

        public ArrayDataReader(string[][] data, string[] headers)
        {
            this.data = data;
            this.headers = headers;
        }

        public override int FieldCount => headers.Length;

        public override bool Read()
        {
            index++;
            return index < data.Length;
        }

        public override string GetName(int ordinal) => headers[ordinal];

        public override string GetString(int ordinal) => data[index][ordinal];

        public override object GetValue(int ordinal) => GetString(ordinal);

        public override bool HasRows => data.Length > 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override int Depth => 0;
        public override bool NextResult() => false;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override Type GetFieldType(int ordinal) => typeof(string);
        public override string GetDataTypeName(int ordinal) => "text";
        public override int GetValues(object[] values)
        {
            int count = Math.Min(FieldCount, values.Length);
            for (int i = 0; i < count; i++)
            {
                values[i] = GetValue(i);
            }
            return count;
        }
        public override bool IsDBNull(int ordinal) => false;
        public override System.Data.DataTable GetSchemaTable()
        {
            var dt = new System.Data.DataTable();
            dt.Columns.Add("ColumnName", typeof(string));
            dt.Columns.Add("ColumnOrdinal", typeof(int));
            dt.Columns.Add("DataType", typeof(Type));
            dt.Columns.Add("AllowDBNull", typeof(bool));
            for (int i = 0; i < headers.Length; i++)
            {
                dt.Rows.Add(headers[i], i, typeof(string), true);
            }
            return dt;
        }
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotImplementedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotImplementedException();
        public override Guid GetGuid(int ordinal) => throw new NotImplementedException();
        public override short GetInt16(int ordinal) => throw new NotImplementedException();
        public override int GetInt32(int ordinal) => throw new NotImplementedException();
        public override long GetInt64(int ordinal) => throw new NotImplementedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotImplementedException();
        public override decimal GetDecimal(int ordinal) => throw new NotImplementedException();
        public override double GetDouble(int ordinal) => throw new NotImplementedException();
        public override float GetFloat(int ordinal) => throw new NotImplementedException();
        public override char GetChar(int ordinal) => throw new NotImplementedException();
        public override byte GetByte(int ordinal) => throw new NotImplementedException();
        public override bool GetBoolean(int ordinal) => throw new NotImplementedException();
        public override IEnumerator GetEnumerator() => throw new NotImplementedException();
        public override int GetOrdinal(string name)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i] == name) return i;
            }
            return -1;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine();
        Console.WriteLine("=== Writing Comparison Analysis ===");
        Console.WriteLine($"Rows: {Rows:N0}, Columns: {Columns}");
        Console.WriteLine($"Data contains values requiring quotes: {WithQuotes}");
        Console.WriteLine($"Total values written: {Rows * Columns:N0}");
        Console.WriteLine();
    }
}
