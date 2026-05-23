using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.Excels.Xlsx;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for Excel (.xlsx) reading performance.
/// Validates the zero-allocation performance of the new source-generated character binders.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class ExcelReadBenchmarks
{
    private byte[] xlsxBytes = null!;

    /// <summary>
    /// Generates a valid in-memory Excel spreadsheet containing 10,000 records.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var records = new BenchmarkExcelReadRecord[10_000];
        for (int i = 0; i < records.Length; i++)
        {
            records[i] = new BenchmarkExcelReadRecord
            {
                Name = $"Name{i}",
                Quantity = i,
                Price = i * 9.99m,
                Score = i * 0.01,
                IsActive = i % 2 == 0,
                CreatedDate = DateTime.Today.AddDays(-i % 365),
                Category = $"Category{i % 10}",
                Id = i,
                Rating = i * 0.1f,
                Description = $"Description for record {i}"
            };
        }

        using var ms = new MemoryStream();
        Excel.Write<BenchmarkExcelReadRecord>().ToStream(ms, records);
        xlsxBytes = ms.ToArray();

        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"Excel payload size: {xlsxBytes.Length / 1024.0:F2} KB");
    }

    /// <summary>
    /// Releases the reusable resources after all iterations have completed.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
    }

    /// <summary>
    /// Benchmarks the native, ultra-fast sheet reading using the generated inline character binder.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int ReadWithGeneratedCharBinder()
    {
        using var stream = new MemoryStream(xlsxBytes, writable: false);
        int count = 0;
        foreach (var record in Excel.Read<BenchmarkExcelReadRecord>().FromStream(stream))
        {
            if (record.IsActive)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmarks the sheet reading performance when forced to go through the fallback Char-to-Byte adapter.
    /// </summary>
    [Benchmark]
    public int ReadWithFallbackCharToByteAdapter()
    {
        using var stream = new MemoryStream(xlsxBytes, writable: false);

        // Manually retrieve the byte binder and wrap it in the fallback adapter
        var byteBinder = CsvRecordBinderFactory.GetByteBinder<BenchmarkExcelReadRecord>();
        var fallbackAdapter = new CsvCharToByteBinderAdapter<BenchmarkExcelReadRecord>(byteBinder, ',');

        // We run the excel typed reader but bypass the factory cache to force adapter use
        int count = 0;
        // In order to benchmark just the binder adapter's translation impact,
        // we can fetch the sheet reader manually and parse via the adapter
        using var xlsxReader = new XlsxReader(stream);
        var sheets = xlsxReader.Workbook.Sheets;
        if (sheets.Count > 0)
        {
            using var sheetReader = xlsxReader.OpenSheet(sheets[0]);
            var header = sheetReader.ReadNextRow();
            if (header is not null)
            {
                var headerBuffer = new char[XlsxRowAdapter.CalculateBufferSize(header) + 1];
                var headerColumnEnds = new int[header.Length + 1];
                var headerRow = XlsxRowAdapter.CreateRow(header, 0, headerBuffer, headerColumnEnds);
                fallbackAdapter.BindHeader(headerRow, 0);

                char[] buffer = [];
                int[] columnEnds = [];
                while (true)
                {
                    var cells = sheetReader.ReadNextRow();
                    if (cells is null)
                        break;

                    XlsxRowAdapter.EnsureBuffers(cells, ref buffer, ref columnEnds);
                    var csvRow = XlsxRowAdapter.CreateRow(cells, sheetReader.CurrentRowNumber, buffer, columnEnds);

                    if (fallbackAdapter.TryBind(csvRow, sheetReader.CurrentRowNumber, out var record))
                    {
                        if (record.IsActive)
                            count++;
                    }
                }
            }
        }
        return count;
    }

    /// <summary>
    /// The record type decorated with GenerateBinder to trigger source generation for Excel char binding.
    /// </summary>
    [GenerateBinder]
    public class BenchmarkExcelReadRecord
    {
        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Gets or sets the quantity.</summary>
        public int Quantity { get; set; }
        /// <summary>Gets or sets the price.</summary>
        public decimal Price { get; set; }
        /// <summary>Gets or sets the score.</summary>
        public double Score { get; set; }
        /// <summary>Gets or sets a value indicating whether this record is active.</summary>
        public bool IsActive { get; set; }
        /// <summary>Gets or sets the creation date.</summary>
        public DateTime CreatedDate { get; set; }
        /// <summary>Gets or sets the category.</summary>
        public string Category { get; set; } = "";
        /// <summary>Gets or sets the identifier.</summary>
        public long Id { get; set; }
        /// <summary>Gets or sets the rating.</summary>
        public float Rating { get; set; }
        /// <summary>Gets or sets the description.</summary>
        public string Description { get; set; } = "";
    }
}
