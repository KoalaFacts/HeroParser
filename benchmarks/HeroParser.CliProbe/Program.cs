using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HeroParser.Cli;
using HeroParser.Cli.AI;
using HeroParser.Console;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using SysConsole = System.Console;

namespace HeroParser.CliProbe;

internal static class Program
{
    private sealed class ProbeRow
    {
        public int Id { get; set; }
        public string Region { get; set; } = "North";
        public string Product { get; set; } = "Widget";
        public decimal Amount { get; set; } = 123.45m;
        public bool Flag { get; set; } = true;
        public string Date { get; set; } = "2026-01-01";
        public string Code { get; set; } = "ABC123";
        public string Note { get; set; } = "regular inventory item";
    }

    private sealed class EchoRowsRunner : ILlmCliRunner
    {
        public int Calls { get; private set; }

        public Task<string> RunAsync(string commandName, string arguments, string prompt, CancellationToken cancellationToken)
        {
            Calls++;
            const string rowsMarker = "Input Rows (JSON):";
            int start = prompt.IndexOf(rowsMarker, StringComparison.Ordinal);
            if (start < 0)
                return Task.FromResult("benchmark answer");

            start += rowsMarker.Length;
            start = prompt.IndexOf('[', start);
            if (start < 0)
                throw new InvalidDataException("Translation prompt did not contain the expected row section.");
            int instructions = prompt.IndexOf("Instructions:", start, StringComparison.Ordinal);
            int end = instructions < 0 ? -1 : prompt.LastIndexOf(']', instructions - 1);
            if (end < start)
                throw new InvalidDataException("Translation prompt did not contain the expected row section.");

            using var document = JsonDocument.Parse(prompt.AsMemory(start, end - start + 1));
            var lines = new StringBuilder();
            foreach (var row in document.RootElement.EnumerateArray())
                lines.AppendLine(row.GetRawText());
            return Task.FromResult(lines.ToString());
        }
    }

    public static async Task Main(string[] args)
    {
        bool measure = args.Length > 0 && args[0] == "--measure";
        if (measure)
            args = args[1..];

        if (args.Length != (measure ? 4 : 3) || !int.TryParse(args[2], out int rows) || rows < 1 || rows > 10_000_000 ||
            args[0] is not ("schema" or "profile" or "query" or "translate") ||
            args[1] is not ("utf8" or "utf16" or "xlsx") ||
            (args[0] == "schema" && args[1] == "xlsx") ||
            (args[1] == "xlsx" && rows > 1_048_575))
            throw new ArgumentException("Usage: <schema|profile|query|translate> <utf8|utf16|xlsx> <rows:1-10000000>; schema does not support xlsx, and xlsx allows at most 1048575 data rows");

        if (!measure)
        {
            string extension = args[1] == "xlsx" ? ".xlsx" : ".csv";
            string fixturePath = Path.Join(Path.GetTempPath(), $"heroparser-cli-probe-{Guid.NewGuid():N}{extension}");
            try
            {
                WriteFixture(fixturePath, args[1], rows);
                await RunInFreshProcessAsync(args, fixturePath);
            }
            finally
            {
                File.Delete(fixturePath);
            }
            return;
        }

        string inputPath = args[3];
        string outputPath = Path.Join(Path.GetTempPath(), $"heroparser-cli-probe-{Guid.NewGuid():N}.csv");
        try
        {
            long inputBytes = new FileInfo(inputPath).Length;
            var runner = new EchoRowsRunner();
            var client = new LlmClient(LlmProvider.Ollama, runner: runner);
            var previousOut = SysConsole.Out;
            var previousAnsi = AnsiConsoleApi.Current;
            bool succeeded;
            long baselineBytes;
            long peakBytes;
            long allocatedBytes;
            long elapsedMs;
            try
            {
                SysConsole.SetOut(TextWriter.Null);
                AnsiConsoleApi.Current = new SystemAnsiConsole(TextWriter.Null);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                using var process = Process.GetCurrentProcess();
                process.Refresh();
                baselineBytes = process.WorkingSet64;
                peakBytes = baselineBytes;
                long allocationStart = GC.GetTotalAllocatedBytes(precise: true);
                using var stopSampling = new CancellationTokenSource();
                var sampling = SampleWorkingSetAsync(process, stopSampling.Token, value => peakBytes = Math.Max(peakBytes, value));
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    succeeded = args[0] switch
                    {
                        "schema" => await CliCommands.SchemaAsync(inputPath, ',', false, null, null, null),
                        "profile" => await CliCommands.ProfileAsync(inputPath, ',', null),
                        "query" => await CliCommands.QueryAsync(inputPath, ',', null, "Summarize the data", null, null, null, client),
                        _ => await CliCommands.TranslateAsync(inputPath, ',', null, "Return unchanged rows", outputPath,
                            batchSize: 100, null, null, null, client)
                    };
                }
                finally
                {
                    stopwatch.Stop();
                    stopSampling.Cancel();
                    await sampling;
                }

                process.Refresh();
                peakBytes = Math.Max(peakBytes, process.WorkingSet64);
                elapsedMs = stopwatch.ElapsedMilliseconds;
                allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocationStart;
            }
            finally
            {
                SysConsole.SetOut(previousOut);
                AnsiConsoleApi.Current = previousAnsi;
            }

            if (!succeeded)
                throw new InvalidOperationException("CLI command failed during the performance probe.");
            if (args[0] == "translate" && File.ReadLines(outputPath).LongCount() != rows + 1L)
                throw new InvalidDataException("Translation output row count did not match the input fixture.");

            SysConsole.WriteLine(JsonSerializer.Serialize(new
            {
                scenario = args[0],
                encoding = args[1],
                rows,
                inputBytes,
                elapsedMs,
                baselineWorkingSetBytes = baselineBytes,
                peakWorkingSetBytes = peakBytes,
                allocatedBytes,
                modelCalls = runner.Calls,
                outputBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0
            }));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static async Task RunInFreshProcessAsync(string[] args, string fixturePath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        process.StartInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        process.StartInfo.ArgumentList.Add("--measure");
        foreach (string arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.ArgumentList.Add(fixturePath);

        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"CLI probe failed: {await stderr}");
        SysConsole.Write(await stdout);
    }

    private static long WriteFixture(string path, string encoding, int rows)
    {
        if (encoding == "xlsx")
        {
            HeroParser.Excel.Write<ProbeRow>().ToFile(path,
                Enumerable.Range(0, rows).Select(i => new ProbeRow { Id = i }));
            return new FileInfo(path).Length;
        }

        using var writer = new StreamWriter(path, false,
            encoding == "utf16" ? Encoding.Unicode : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 64 * 1024);
        writer.WriteLine("Id,Region,Product,Amount,Flag,Date,Code,Note");
        for (int i = 0; i < rows; i++)
        {
            writer.Write(i);
            writer.WriteLine(",North,Widget,123.45,true,2026-01-01,ABC123,regular inventory item");
        }
        writer.Flush();
        return new FileInfo(path).Length;
    }

    private static async Task SampleWorkingSetAsync(Process process, CancellationToken cancellationToken, Action<long> record)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            process.Refresh();
            record(process.WorkingSet64);
            try
            {
                await Task.Delay(5, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
