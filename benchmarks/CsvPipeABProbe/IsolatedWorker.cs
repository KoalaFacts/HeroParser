#if SINGLE_MODULE
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class IsolatedWorker
{
    public static async Task RunAsync(Settings settings)
    {
        if (settings.BiasMode is not null || settings.ProfileSide is not null || settings.VerifyOnly ||
            settings.Scenario is not null || settings.Path is not null)
            throw new ArgumentException("Isolated workers accept commands on stdin, not dual-module selectors.");

        var parser = typeof(HeroParser.Csv).Assembly;
        var models = typeof(CsvPipeABModels.PipeRecord).Assembly;
        var consumer = typeof(IsolatedWorker).Assembly;
        if (parser.GetName().Name != "HeroParser" || models.GetName().Name != "CsvPipeABModels")
            throw new InvalidOperationException("Workers require the normal parser and model identities.");
        string protocol = Environment.GetEnvironmentVariable("HERO_PARSER_WORKER_PROTOCOL") ?? "csv-pipe-isolated-v3";
        string? pinnedCpu = Environment.GetEnvironmentVariable("HERO_PARSER_WORKER_CPU");
        string? allowedCpus = OperatingSystem.IsLinux()
            ? File.ReadLines("/proc/self/status").Single(line => line.StartsWith("Cpus_allowed_list:", StringComparison.Ordinal)).Split(':')[1].Trim()
            : null;
        if (protocol is not ("csv-pipe-isolated-v3" or "csv-pipe-isolated-v4-same-cpu") ||
            (protocol == "csv-pipe-isolated-v4-same-cpu" && (pinnedCpu is null || allowedCpus != pinnedCpu)))
            throw new InvalidOperationException("Pinned workers must inherit exactly the requested CPU at startup.");
        Write(new
        {
            kind = "worker-environment",
            protocol,
            pinnedCpu,
            allowedCpus,
            pid = Environment.ProcessId,
            sourceRef = Environment.GetEnvironmentVariable("HERO_PARSER_WORKER_REF"),
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            serverGc = System.Runtime.GCSettings.IsServerGC,
            affinity = Environment.GetEnvironmentVariable("HERO_PARSER_AB_AFFINITY"),
            parserName = parser.GetName().Name,
            modelsName = models.GetName().Name,
            parserMvid = parser.ManifestModule.ModuleVersionId,
            modelsMvid = models.ManifestModule.ModuleVersionId,
            parserHash = Hash(parser),
            modelsHash = Hash(models),
            consumerHash = Hash(consumer),
            jitDisasm = Environment.GetEnvironmentVariable("DOTNET_JitDisasm"),
            jitDisasmAssemblies = Environment.GetEnvironmentVariable("DOTNET_JitDisasmAssemblies"),
            settings.Rows
        });

        bool verified = false;
        int sequence = 0;
        Case? run = null;
        string? transport = null;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            var command = JsonSerializer.Deserialize<Command>(line)
                ?? throw new InvalidOperationException("Missing worker command.");
            if (command.Id != ++sequence) throw new InvalidOperationException("Commands must be sequential.");
            switch (command.Operation)
            {
                case "verify":
                    if (verified || run is not null) throw new InvalidOperationException("Verify once before timing.");
                    var checks = new List<object>();
                    foreach (string scenario in new[] { "Plain", "Escaped", "Unicode", "LongEscaped" })
                    {
                        var fixture = Fixture.Create(scenario, settings.Rows);
                        foreach (string selected in new[] { "Contiguous", "Segmented128", "Stream4096" })
                        {
                            foreach (string path in new[] { "Scan", "Decode", "Generated" })
                            {
                                var checkCase = new Case(fixture, selected, path);
                                var check = await checkCase.ReadAsync(candidate: true, inspectSegments: true);
                                checkCase.Validate(check);
                                if (selected == "Segmented128" && path != "Generated" && check.SplitFields == 0)
                                    throw new InvalidOperationException("Segmented correctness must exercise split fields.");
                                checks.Add(new { scenario, transport = selected, path, check.Rows, check.Checksum, check.SplitFields });
                            }
                        }
                    }
                    verified = true;
                    Write(new { kind = "worker-verified", command.Id, pid = Environment.ProcessId, checks });
                    break;
                case "prepare":
                    if (!verified || command.Transport is not ("Contiguous" or "Segmented128" or "Stream4096"))
                        throw new InvalidOperationException("Prepare requires full correctness and a known transport.");
                    transport = command.Transport;
                    run = new Case(Fixture.Create("Plain", settings.Rows), transport, "Generated");
                    for (int i = 0; i < 8; i++) run.Validate(await run.ReadAsync(candidate: true, inspectSegments: false));
                    Write(new { kind = "worker-prepared", command.Id, pid = Environment.ProcessId, transport });
                    break;
                case "batch":
                    if (run is null || command.Transport != transport || command.Repeats is < 1 or > 65536)
                        throw new InvalidOperationException("Batch requires a prepared transport and bounded repeats.");
                    // The existing consumer measures only parser work; command IPC is outside its stopwatch.
                    var measurement = await run.MeasureAsync(candidate: true, command.Repeats);
                    Write(new
                    {
                        kind = "worker-batch",
                        command.Id,
                        pid = Environment.ProcessId,
                        transport,
                        repeats = command.Repeats,
                        measurement,
                        batchMs = measurement.Milliseconds * command.Repeats
                    });
                    break;
                case "stop":
                    Write(new { kind = "worker-stopped", command.Id, pid = Environment.ProcessId });
                    return;
                default:
                    throw new InvalidOperationException("Unknown worker command.");
            }
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is { } name &&
                (name.StartsWith("HeroParser.Baseline", StringComparison.Ordinal) ||
                 name.StartsWith("CsvPipeABModels.", StringComparison.Ordinal))))
                throw new InvalidOperationException("A second parser/model identity entered the worker.");
        }
        throw new InvalidOperationException("Worker input ended without a stop command.");
    }

    private static string Hash(Assembly assembly) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
    private static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value));
    private sealed record Command(int Id, string Operation, string? Transport, int Repeats);
}
#endif
