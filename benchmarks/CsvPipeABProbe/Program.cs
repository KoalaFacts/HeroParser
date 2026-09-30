extern alias baseline;
extern alias baselineModels;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BaselineCsv = baseline::HeroParser.Csv;
using BaselineOptions = baseline::HeroParser.SeparatedValues.Core.CsvReadOptions;
using BaselineRecord = baselineModels::CsvPipeABModels.PipeRecord;
using BaselineRecordOptions = baseline::HeroParser.SeparatedValues.Reading.Records.CsvRecordOptions;
using CandidateCsv = HeroParser.Csv;
using CandidateOptions = HeroParser.SeparatedValues.Core.CsvReadOptions;
using CandidateRecord = CsvPipeABModels.PipeRecord;
using CandidateRecordOptions = HeroParser.SeparatedValues.Reading.Records.CsvRecordOptions;

Settings settings;
try
{
    settings = Settings.Parse(args);
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 2;
    return;
}
string? affinity = Environment.GetEnvironmentVariable("HERO_PARSER_AB_AFFINITY");
if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
    && affinity is { Length: > 0 })
{
    using var process = Process.GetCurrentProcess();
    process.ProcessorAffinity = (nint)long.Parse(affinity, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

bool baselineSide = settings.BiasMode is "Swapped" or "CandidateSelf";
bool candidateSide = settings.BiasMode is not ("Swapped" or "BaselineSelf");
string protocol = "csv-pipe-v2-post-warmup-calibration";
if (settings.ProfileSide is not null) protocol = "csv-pipe-profile-v1";
else if (settings.BiasMode is not null) protocol = "csv-pipe-bias-v1-diagnostic-only";
Write(new
{
    kind = "environment",
    runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    processors = Environment.ProcessorCount,
    serverGc = System.Runtime.GCSettings.IsServerGC,
    affinity,
    baselineMvid = typeof(BaselineCsv).Module.ModuleVersionId,
    candidateMvid = typeof(CandidateCsv).Module.ModuleVersionId,
    baselineModelsMvid = typeof(BaselineRecord).Module.ModuleVersionId,
    candidateModelsMvid = typeof(CandidateRecord).Module.ModuleVersionId,
    baselineRef = Environment.GetEnvironmentVariable("HERO_PARSER_AB_BASELINE_REF"),
    candidateRef = Environment.GetEnvironmentVariable("HERO_PARSER_AB_CANDIDATE_REF"),
    protocol,
    settings.BiasMode,
    diagnosticOnly = settings.BiasMode is not null,
    logicalBaselineModule = baselineSide ? "Candidate" : "Baseline",
    logicalCandidateModule = candidateSide ? "Candidate" : "Baseline",
    jitDisasm = Environment.GetEnvironmentVariable("DOTNET_JitDisasm"),
    jitDisasmAssemblies = Environment.GetEnvironmentVariable("DOTNET_JitDisasmAssemblies"),
    minWarmupSeconds = 10,
    calibrationHeadroom = 1.25,
    maxCalibrationRepeats = 65536,
    settings.Rows,
    settings.Pairs,
    settings.WarmupPairs,
    settings.MinSampleMs,
    settings.Scenario,
    settings.Path
});

if (settings.ProfileSide is not null)
{
    await RunProfileAsync(settings);
    return;
}

int invalidCases = 0;
foreach (string scenario in new[] { "Plain", "Escaped", "Unicode", "LongEscaped" }.Where(x => settings.Scenario is null || x == settings.Scenario))
{
    var fixture = Fixture.Create(scenario, settings.Rows);
    foreach (string transport in new[] { "Contiguous", "Segmented128", "Stream4096" })
    {
        foreach (string path in new[] { "Scan", "Decode", "Generated" }.Where(x => settings.Path is null || x == settings.Path))
        {
            var run = new Case(fixture, transport, path);
            var baselineCheck = await run.ReadAsync(baselineSide, inspectSegments: true);
            var candidateCheck = await run.ReadAsync(candidateSide, inspectSegments: true);
            if (!run.IsValid(baselineCheck) || !run.IsValid(candidateCheck))
            {
                invalidCases++;
                Write(new { kind = "invalid", scenario, transport, path, expectedRows = fixture.Rows, expectedChecksum = fixture.Checksum, baseline = baselineCheck, candidate = candidateCheck });
                continue;
            }
            if (transport == "Segmented128" && path != "Generated" && candidateCheck.SplitFields == 0)
                throw new InvalidOperationException("The segmented fixture did not exercise split text fields.");
            Write(new { kind = "verified", scenario, transport, path, fixture.Rows, bytes = fixture.Bytes.Length, candidateCheck.SplitFields });
            if (settings.VerifyOnly) continue;

            // The pilot only sizes warmup batches; final calibration follows the full warmup.
            for (int warm = 0; warm < 8; warm++)
            {
                run.Validate(await run.ReadAsync(baselineSide, inspectSegments: false));
                run.Validate(await run.ReadAsync(candidateSide, inspectSegments: false));
            }
            int repeats = await CalibrateAsync(run, scenario, transport, path, 1, settings.MinSampleMs, "pilot", baselineSide, candidateSide);
            var warmup = Stopwatch.StartNew();
            int warmedPairs = 0;
            do
            {
                bool candidateFirst = (warmedPairs & 1) == 0;
                await run.MeasureAsync(candidateFirst ? candidateSide : baselineSide, repeats);
                await run.MeasureAsync(candidateFirst ? baselineSide : candidateSide, repeats);
                warmedPairs++;
            } while (warmedPairs < settings.WarmupPairs || warmup.Elapsed.TotalSeconds < 10);
            warmup.Stop();
            Write(new { kind = "warmup", scenario, transport, path, pairs = warmedPairs, elapsedSeconds = warmup.Elapsed.TotalSeconds });
            repeats = await CalibrateAsync(run, scenario, transport, path, repeats, settings.MinSampleMs * 1.25, "post-warmup", baselineSide, candidateSide);
            var ratios = new List<double>();
            var before = new List<Measurement>();
            var after = new List<Measurement>();
            int wins = 0;
            for (int pair = 0; pair < settings.Pairs; pair++)
            {
                bool candidateFirst = (pair & 1) == 0;
                var first = await run.MeasureAsync(candidateFirst ? candidateSide : baselineSide, repeats);
                var second = await run.MeasureAsync(candidateFirst ? baselineSide : candidateSide, repeats);
                var a = candidateFirst ? second : first;
                var b = candidateFirst ? first : second;
                double ratio = b.Milliseconds / a.Milliseconds;
                ratios.Add(ratio);
                before.Add(a);
                after.Add(b);
                if (ratio < 1) wins++;
                Write(new
                {
                    kind = "pair",
                    scenario,
                    transport,
                    path,
                    pair,
                    candidateFirst,
                    repeats,
                    baseline = a,
                    candidate = b,
                    baselineBatchMs = a.Milliseconds * repeats,
                    candidateBatchMs = b.Milliseconds * repeats,
                    ratio
                });
            }

            Write(new
            {
                kind = "summary",
                scenario,
                transport,
                path,
                fixture.Rows,
                bytes = fixture.Bytes.Length,
                repeats,
                baselineMs = Percentile(before.Select(x => x.Milliseconds), .5),
                candidateMs = Percentile(after.Select(x => x.Milliseconds), .5),
                baselineBytes = Percentile(before.Select(x => (double)x.AllocatedBytes), .5),
                candidateBytes = Percentile(after.Select(x => (double)x.AllocatedBytes), .5),
                baselineMinBatchMs = before.Min(x => x.Milliseconds) * repeats,
                candidateMinBatchMs = after.Min(x => x.Milliseconds) * repeats,
                medianRatio = Percentile(ratios, .5),
                p10Ratio = Percentile(ratios, .1),
                p90Ratio = Percentile(ratios, .9),
                wins,
                settings.Pairs
            });
        }
    }
}
Write(new { kind = "complete", invalidCases });
Environment.ExitCode = invalidCases == 0 ? 0 : 1;

static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value));

static async Task<int> CalibrateAsync(Case run, string scenario, string transport, string path,
    int repeats, double targetBatchMs, string stage, bool baselineSide, bool candidateSide)
{
    for (int attempt = 0; ; attempt++)
    {
        bool candidateFirst = (attempt & 1) == 0;
        var first = await run.MeasureAsync(candidateFirst ? candidateSide : baselineSide, repeats);
        var second = await run.MeasureAsync(candidateFirst ? baselineSide : candidateSide, repeats);
        var a = candidateFirst ? second : first;
        var b = candidateFirst ? first : second;
        double baselineBatchMs = a.Milliseconds * repeats;
        double candidateBatchMs = b.Milliseconds * repeats;
        if (Math.Min(baselineBatchMs, candidateBatchMs) >= targetBatchMs)
        {
            Write(new { kind = "calibration", scenario, transport, path, stage, repeats, targetBatchMs, baselineBatchMs, candidateBatchMs });
            return repeats;
        }
        if (repeats == 65536)
        {
            Write(new { kind = "calibration-limit", scenario, transport, path, stage, repeats, targetBatchMs, baselineBatchMs, candidateBatchMs });
            throw new InvalidOperationException("Calibration repeat limit reached without meeting the batch target.");
        }
        repeats = Math.Min(repeats * 2, 65536);
    }
}

static async Task RunProfileAsync(Settings settings)
{
    bool candidate = settings.ProfileSide == "Candidate";
    var fixture = Fixture.Create("Plain", settings.Rows);
    var run = new Case(fixture, settings.ProfileTransport!, "Generated");
    var check = await run.ReadAsync(candidate, inspectSegments: false);
    run.Validate(check);
    Write(new { kind = "profile-verified", side = settings.ProfileSide, transport = settings.ProfileTransport, check.Rows, check.Checksum });

    // Keep normal tiering/PGO enabled and attach the sampler only after this warmup.
    var warmup = Stopwatch.StartNew();
    int warmedReads = 0;
    do
    {
        run.Validate(await run.ReadAsync(candidate, inspectSegments: false));
        warmedReads++;
    } while (warmup.Elapsed.TotalSeconds < 10 || warmedReads < 100);
    warmup.Stop();
    string readyFile = settings.ProfileReadyFile!;
    File.WriteAllText(readyFile + ".tmp", JsonSerializer.Serialize(new
    {
        pid = Environment.ProcessId,
        side = settings.ProfileSide,
        transport = settings.ProfileTransport,
        warmupSeconds = warmup.Elapsed.TotalSeconds,
        warmedReads
    }));
    File.Move(readyFile + ".tmp", readyFile);

    using var process = Process.GetCurrentProcess();
    var cpuBefore = process.TotalProcessorTime;
    var duration = Stopwatch.StartNew();
    int reads = 0;
    do
    {
        run.Validate(await run.ReadAsync(candidate, inspectSegments: false));
        reads++;
    } while (duration.Elapsed.TotalSeconds < settings.ProfileSeconds);
    Write(new
    {
        kind = "profile-complete",
        side = settings.ProfileSide,
        transport = settings.ProfileTransport,
        reads,
        warmupSeconds = warmup.Elapsed.TotalSeconds,
        elapsedSeconds = duration.Elapsed.TotalSeconds,
        cpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds,
        invalidCases = 0,
        diagnosticOnly = true
    });
}

static double Percentile(IEnumerable<double> values, double percentile)
{
    double[] sorted = [.. values.Order()];
    double index = (sorted.Length - 1) * percentile;
    int lower = (int)index;
    return sorted[lower] + (sorted[(int)Math.Ceiling(index)] - sorted[lower]) * (index - lower);
}

internal sealed record Settings(int Rows, int Pairs, int WarmupPairs, int MinSampleMs, bool VerifyOnly, string? Scenario, string? Path,
    string? ProfileSide, string? ProfileTransport, string? ProfileReadyFile, int ProfileSeconds, string? BiasMode)
{
    public static Settings Parse(string[] args)
    {
        int rows = 2000, pairs = 20, warmup = 6, minSampleMs = 30;
        bool verify = false;
        string? scenario = null, path = null, biasMode = null;
        string? profileSide = null, profileTransport = null, profileReadyFile = null;
        int profileSeconds = 45;
        bool profileRequested = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--verify-only") { verify = true; continue; }
            string option = args[i];
            if (option == "--bias-mode")
            {
                if (i + 1 == args.Length) throw new ArgumentException("Missing value for --bias-mode.");
                biasMode = args[++i];
                if (biasMode is not ("Independent" or "Swapped" or "BaselineSelf" or "CandidateSelf"))
                    throw new ArgumentException("Unknown bias diagnostic mode.");
                continue;
            }
            if (option is "--profile-side" or "--profile-transport" or "--profile-ready-file")
            {
                profileRequested = true;
                if (i + 1 == args.Length) throw new ArgumentException($"Missing value for {option}.");
                string selected = args[++i];
                switch (option)
                {
                    case "--profile-side": profileSide = selected; break;
                    case "--profile-transport": profileTransport = selected; break;
                    case "--profile-ready-file": profileReadyFile = selected; break;
                    default: throw new ArgumentException($"Unknown profiling option: {option}.");
                }
                continue;
            }
            if (option is "--scenario" or "--path")
            {
                if (i + 1 == args.Length) throw new ArgumentException($"Missing value for {option}.");
                string selected = args[++i];
                if (option == "--scenario")
                {
                    if (selected is not ("Plain" or "Escaped" or "Unicode" or "LongEscaped")) throw new ArgumentException("Unknown scenario.");
                    scenario = selected;
                }
                else
                {
                    if (selected is not ("Scan" or "Decode" or "Generated")) throw new ArgumentException("Unknown path.");
                    path = selected;
                }
                continue;
            }
            if (i + 1 == args.Length || !int.TryParse(args[++i], CultureInfo.InvariantCulture, out int value) || value <= 0)
                throw new ArgumentException($"Expected a positive integer after {option}.");
            switch (option)
            {
                case "--rows": rows = value; break;
                case "--pairs": pairs = value; break;
                case "--warmup-pairs": warmup = value; break;
                case "--min-sample-ms": minSampleMs = value; break;
                case "--profile-seconds": profileRequested = true; profileSeconds = value; break;
                default: throw new ArgumentException($"Unknown option: {option}.");
            }
        }
        if (biasMode is not null && (profileRequested ||
            (!verify && (scenario != "Plain" || path != "Generated"))))
            throw new ArgumentException("Bias diagnostics require Plain Generated timing or verification, and cannot profile.");
        if (profileRequested)
        {
            if (profileSide is not ("Baseline" or "Candidate") ||
                profileTransport is not ("Contiguous" or "Segmented128" or "Stream4096") ||
                string.IsNullOrWhiteSpace(profileReadyFile) || profileSeconds > 120 || verify || scenario is not null || path is not null)
                throw new ArgumentException("Profiling requires a side, transport and readiness file, with 1-120 seconds and no benchmark selectors.");
        }
        return new Settings(rows, pairs, warmup, minSampleMs, verify, scenario, path,
            profileSide, profileTransport, profileReadyFile, profileSeconds, biasMode);
    }
}

internal readonly record struct Result(int Rows, ulong Checksum, int SplitFields);
internal readonly record struct Measurement(double Milliseconds, double AllocatedBytes);

internal sealed record Fixture(byte[] Bytes, ReadOnlySequence<byte> Segmented, int Rows, ulong Checksum)
{
    public static Fixture Create(string scenario, int rows)
    {
        var builder = new StringBuilder("Id,Name,Amount,Note\n");
        ulong checksum = 0;
        for (int i = 1; i <= rows; i++)
        {
            string name = scenario == "Unicode" ? $"\u4F60\u597D_{i}\uD83D\uDE00" : $"name_{i}";
            string note = scenario switch
            {
                "Plain" => $"note_{i}",
                "Escaped" => $"said \"hello\", row {i}",
                "Unicode" => $"\u4E16\u754C \"\u4F60\u597D\" \uD83D\uDE00 {i}",
                "LongEscaped" => new string('x', 2048) + $" \"end\", {i}",
                _ => throw new ArgumentException("Unknown scenario.", nameof(scenario))
            };
            int amount = i % 997;
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendField(builder, name);
            builder.Append(',').Append(amount.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendField(builder, note);
            builder.Append('\n');
            checksum = Hash(checksum, i, amount, name, note);
        }
        byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var first = new Segment(bytes.AsMemory(0, Math.Min(128, bytes.Length)));
        var last = first;
        for (int offset = first.Memory.Length; offset < bytes.Length; offset += 128)
            last = last.Append(bytes.AsMemory(offset, Math.Min(128, bytes.Length - offset)));
        return new Fixture(bytes, new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length), rows, checksum);
    }

    private static void AppendField(StringBuilder builder, string value)
    {
        if (value.AsSpan().IndexOfAny(',', '"', '\n') < 0) { builder.Append(value); return; }
        builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
    }

    public static ulong Hash(ulong hash, int id, int amount, string name, string note)
    {
        unchecked
        {
            hash = (hash ^ (uint)id) * 1099511628211UL;
            hash = (hash ^ (uint)amount) * 1099511628211UL;
            foreach (char c in name) hash = (hash ^ c) * 1099511628211UL;
            hash = (hash ^ 0xFFFF) * 1099511628211UL;
            foreach (char c in note) hash = (hash ^ c) * 1099511628211UL;
            return (hash ^ 0xFFFF) * 1099511628211UL;
        }
    }
}

internal sealed class Case(Fixture fixture, string transport, string path)
{
    public bool IsValid(Result result)
        => result.Rows == fixture.Rows && (path == "Scan" || result.Checksum == fixture.Checksum);

    public void Validate(Result result)
    {
        if (!IsValid(result))
            throw new InvalidOperationException($"Incorrect {path} result: {result}.");
    }

    public async Task<Measurement> MeasureAsync(bool candidate, int repeats)
    {
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            var result = await ReadAsync(candidate, inspectSegments: false);
            Validate(result);
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new Measurement(elapsed / repeats, (double)(GC.GetTotalAllocatedBytes(precise: true) - allocated) / repeats);
    }

    public async Task<Result> ReadAsync(bool candidate, bool inspectSegments)
    {
        using var stream = transport == "Stream4096" ? new MemoryStream(fixture.Bytes, writable: false) : null;
        PipeReader pipe = stream is not null
            ? PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 4096, minimumReadSize: 4096, leaveOpen: true))
            : new BufferedReader(transport == "Contiguous" ? new ReadOnlySequence<byte>(fixture.Bytes) : fixture.Segmented);
        try
        {
            return candidate ? await CandidateAsync(pipe, inspectSegments) : await BaselineAsync(pipe, inspectSegments);
        }
        finally
        {
            await pipe.CompleteAsync();
        }
    }

    private async Task<Result> CandidateAsync(PipeReader pipe, bool inspectSegments)
    {
        var options = new CandidateOptions { MaxColumnCount = 4, MaxRowCount = fixture.Rows + 1 };
        int rows = 0, split = 0;
        ulong checksum = 0;
        if (path == "Generated")
        {
            await foreach (var record in CandidateCsv.DeserializeRecordsAsync<CandidateRecord>(pipe, new CandidateRecordOptions { HasHeaderRow = true }, options))
            {
                checksum = Fixture.Hash(checksum, record.Id, record.Amount, record.Name, record.Note);
                rows++;
            }
        }
        else
        {
            await using var reader = CandidateCsv.CreatePipeSequenceReader(pipe, options);
            bool header = true;
            while (await reader.MoveNextAsync())
            {
                var row = reader.Current;
                if (row.ColumnCount != 4) throw new InvalidOperationException("Incorrect column count.");
                if (header) { header = false; continue; }
                if (inspectSegments) split += (row[1].Sequence.IsSingleSegment ? 0 : 1) + (row[3].Sequence.IsSingleSegment ? 0 : 1);
                if (path == "Decode")
                    checksum = Fixture.Hash(checksum, int.Parse(row[0].ToUnquotedString(), CultureInfo.InvariantCulture), int.Parse(row[2].ToUnquotedString(), CultureInfo.InvariantCulture), row[1].ToUnquotedString(), row[3].ToUnquotedString());
                rows++;
            }
        }
        return new Result(rows, checksum, split);
    }

    private async Task<Result> BaselineAsync(PipeReader pipe, bool inspectSegments)
    {
        var options = new BaselineOptions { MaxColumnCount = 4, MaxRowCount = fixture.Rows + 1 };
        int rows = 0, split = 0;
        ulong checksum = 0;
        if (path == "Generated")
        {
            await foreach (var record in BaselineCsv.DeserializeRecordsAsync<BaselineRecord>(pipe, new BaselineRecordOptions { HasHeaderRow = true }, options))
            {
                checksum = Fixture.Hash(checksum, record.Id, record.Amount, record.Name, record.Note);
                rows++;
            }
        }
        else
        {
            await using var reader = BaselineCsv.CreatePipeSequenceReader(pipe, options);
            bool header = true;
            while (await reader.MoveNextAsync())
            {
                var row = reader.Current;
                if (row.ColumnCount != 4) throw new InvalidOperationException("Incorrect column count.");
                if (header) { header = false; continue; }
                if (inspectSegments) split += (row[1].Sequence.IsSingleSegment ? 0 : 1) + (row[3].Sequence.IsSingleSegment ? 0 : 1);
                if (path == "Decode")
                    checksum = Fixture.Hash(checksum, int.Parse(row[0].ToUnquotedString(), CultureInfo.InvariantCulture), int.Parse(row[2].ToUnquotedString(), CultureInfo.InvariantCulture), row[1].ToUnquotedString(), row[3].ToUnquotedString());
                rows++;
            }
        }
        return new Result(rows, checksum, split);
    }
}

internal sealed class Segment : ReadOnlySequenceSegment<byte>
{
    public Segment(ReadOnlyMemory<byte> memory)
    {
        Memory = memory;
    }

    public Segment Append(ReadOnlyMemory<byte> memory)
    {
        var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
        Next = segment;
        return segment;
    }
}

internal sealed class BufferedReader(ReadOnlySequence<byte> buffer) : PipeReader
{
    private ReadOnlySequence<byte> remaining = buffer;
    private bool completed;

    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);
    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => remaining = remaining.Slice(consumed);
    public override void CancelPendingRead() => throw new NotSupportedException();
    public override void Complete(Exception? exception = null) => completed = true;
    public override bool TryRead(out ReadResult result)
    {
        if (completed) throw new InvalidOperationException("Reading a completed pipe.");
        result = new ReadResult(remaining, isCanceled: false, isCompleted: true);
        return true;
    }
    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryRead(out var result);
        return ValueTask.FromResult(result);
    }
}
