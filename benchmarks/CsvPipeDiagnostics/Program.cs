using System.Text.Json;
using Microsoft.Diagnostics.Tracing;

if (args.Length != 3 || !int.TryParse(args[1], out int expectedPid) || expectedPid <= 0)
    throw new ArgumentException("Usage: CsvPipeDiagnostics trace.nettrace expected-pid output-directory");

Directory.CreateDirectory(args[2]);
using var source = new EventPipeEventSource(args[0]);
using var output = new StreamWriter(Path.Combine(args[2], "runtime-events.ndjson"));
long gcStarts = 0;
long jitEvents = 0;
long exported = 0;
string[] payloadFields = ["ClrInstanceID", "Count", "Depth", "Reason", "Type", "MethodID",
    "MethodStartAddress", "MethodSize", "MethodNamespace", "MethodName", "MethodSignature", "MethodFlags"];
source.Clr.All += data =>
{
    if (data.ProcessID != expectedPid) throw new InvalidDataException("Runtime event belongs to another PID.");
    string name = data.EventName;
    if (name == "GC/Start") gcStarts++;
    if (name.StartsWith("Method/", StringComparison.Ordinal)) jitEvents++;
    if (!name.StartsWith("GC/", StringComparison.Ordinal) && !name.StartsWith("Method/", StringComparison.Ordinal)) return;
    var payload = new Dictionary<string, object?>();
    foreach (string field in payloadFields)
    {
        int index = Array.IndexOf(data.PayloadNames, field);
        if (index >= 0) payload[field] = data.PayloadValue(index);
    }
    output.WriteLine(JsonSerializer.Serialize(new
    {
        Pid = data.ProcessID,
        Tid = data.ThreadID,
        UtcTicks = data.TimeStamp.ToUniversalTime().Ticks,
        Qpc = data.TimeStampQPC,
        RelativeMs = data.TimeStampRelativeMSec,
        Name = name,
        Payload = payload
    }));
    exported++;
};
source.Process();
output.Flush();
File.WriteAllText(Path.Combine(args[2], "runtime-summary.json"), JsonSerializer.Serialize(new
{
    DiagnosticOnly = true,
    Pid = expectedPid,
    EventsLost = source.EventsLost,
    GcStarts = gcStarts,
    JitEvents = jitEvents,
    ExportedEvents = exported,
    Clock = "EventPipe-QPC-with-trace-derived-UTC",
    DecoderVersion = typeof(EventPipeEventSource).Assembly.GetName().Version?.ToString()
}, new JsonSerializerOptions { WriteIndented = true }));
if (source.EventsLost != 0 || gcStarts == 0 || jitEvents == 0)
    throw new InvalidDataException("Missing or lost runtime events; no complete GC/JIT evidence claim.");
