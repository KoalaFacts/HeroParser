$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'control.ps1')
. (Join-Path $PSScriptRoot 'native-evidence.ps1')
$checks = 0
function Assert-NativeReject([string]$Name, [scriptblock]$Action) {
    $rejected = $false
    try { $null = & $Action } catch { $rejected = $true }
    if (!$rejected) { throw "Native evidence accepted invalid input: $Name" }
    $script:checks++
    Write-Host "PASS: rejects $Name"
}

function New-NativeManifest {
    $runs = foreach ($owner in @(1234, 5678)) {
        [pscustomobject]@{ Environment = [pscustomobject]@{
            protocol = 'csv-pipe-isolated-v4-same-cpu'; pid = $owner; sourceRef = '1' * 40; Rows = 2000
            parserName = 'HeroParser'; modelsName = 'CsvPipeABModels'; parserHash = 'a' * 64
            modelsHash = 'b' * 64; consumerHash = 'c' * 64
            parserMvid = '11111111-1111-1111-1111-111111111111'; modelsMvid = '22222222-2222-2222-2222-222222222222'
            runtime = '.NET 10'; os = 'Linux'; processors = 1; serverGc = $false; affinity = 1
            pinnedCpu = '0'; allowedCpus = '0'; jitDisasm = $null; jitDisasmAssemblies = $null
        }; Batch = [pscustomobject]@{ pid = $owner; transport = 'Segmented128'; repeats = 65536; batchMs = 40000 }
            WarmupSeconds = 10; JitPid = $owner; JitMethodCount = 100; NativeAssemblyCount = 3
            LostSamples = 0; Samples = 1000; UnknownFraction = .01; Hotspots = @('HeroParser.Hot') }
    }
    [pscustomobject]@{ Protocol = 'csv-pipe-nativecpu-v1-diagnostic-only'; DiagnosticOnly = $true
        WorkloadSha = '1' * 40; DiagnosticSha = '2' * 40; Event = 'cpu-clock:u'; Clock = 'mono'; Frequency = 199
        DurationSeconds = 30; PerfMapEnabled = 1; PerfMapStubGranularity = 2; Runs = @($runs) }
}

function New-ProcStatLine {
    $fields = @('R') + @('0') * 21
    $fields[7] = '17'; $fields[9] = '2'; $fields[11] = '100'; $fields[12] = '3'
    $fields[17] = '9'; $fields[19] = '99'; $fields[21] = '42'
    return '1234 (dotnet ) worker) ' + ($fields -join ' ')
}

$statLine = New-ProcStatLine
$stat = Read-CsvPipeProcStat $statLine 1234
if ($stat.MinorFaults -ne 17 -or $stat.MajorFaults -ne 2 -or $stat.UserTicks -ne 100 -or
    $stat.SystemTicks -ne 3 -or $stat.Threads -ne 9 -or $stat.StartTicks -ne 99 -or $stat.RssPages -ne 42) {
    throw 'Process stat fields shifted with parentheses/spaces in comm.'
}
$checks++
foreach ($line in @('1234 dotnet 0', '1234 (dotnet) R 0', ($statLine -replace ' R ', ' 0 '),
    ($statLine -replace ' 17 ', ' -17 '), ($statLine -replace ' 100 ', ' NaN '),
    ($statLine -replace ' 100 ', ' 9223372036854775808 '), ($statLine -replace ' 99 ', ' 0 '))) {
    Assert-NativeReject 'malformed process stat' { Read-CsvPipeProcStat $line 1234 }
}
Assert-NativeReject 'wrong process stat owner' { Read-CsvPipeProcStat $statLine 5678 }
$before = [pscustomobject]@{ Stat = $stat; MonotonicTicks = [Diagnostics.Stopwatch]::Frequency * 10 }
$afterStat = Read-CsvPipeProcStat $statLine 1234
$afterStat.UserTicks = 150; $afterStat.SystemTicks = 13; $afterStat.MinorFaults = 19
$after = [pscustomobject]@{ Stat = $afterStat; MonotonicTicks = [Diagnostics.Stopwatch]::Frequency * 11 }
$delta = Get-CsvPipeProcessDelta $before $after 100
if ($delta.CpuMs -ne 600 -or $delta.ObserverWallMs -ne 1000 -or
    $delta.CpuToObserverWall -ne .6 -or $delta.MinorFaults -ne 2 -or !$delta.DiagnosticOnly) { throw 'Wrong process delta units.' }
$checks++
Assert-NativeReject 'process clock absent' { Get-CsvPipeProcessDelta $before $after 0 }
foreach ($field in @('UserTicks', 'SystemTicks', 'MinorFaults', 'MajorFaults')) {
    foreach ($value in @($null, -1, [double]::NaN, [double]::PositiveInfinity)) {
        Assert-NativeReject "invalid process $field" {
            $copy = [pscustomobject]@{ Stat = Read-CsvPipeProcStat $statLine 1234; MonotonicTicks = $after.MonotonicTicks }
            $copy.Stat.$field = $value
            Get-CsvPipeProcessDelta $before $copy 100
        }
    }
}
Assert-NativeReject 'regressed CPU accounting' {
    $copy = [pscustomobject]@{ Stat = Read-CsvPipeProcStat $statLine 1234; MonotonicTicks = $after.MonotonicTicks }
    $copy.Stat.UserTicks = 99; Get-CsvPipeProcessDelta $before $copy 100
}
Assert-NativeReject 'PID reused between snapshots' {
    $copy = [pscustomobject]@{ Stat = Read-CsvPipeProcStat $statLine 1234; MonotonicTicks = $after.MonotonicTicks }
    $copy.Stat.StartTicks++; Get-CsvPipeProcessDelta $before $copy 100
}
Assert-NativeReject 'non-increasing snapshot clock' { Get-CsvPipeProcessDelta $before $before 100 }
Assert-NativeReject 'nonfinite snapshot clock' {
    $copy = [pscustomobject]@{ Stat = $afterStat; MonotonicTicks = [double]::NaN }
    Get-CsvPipeProcessDelta $before $copy 100
}

Assert-CsvPipeNativeManifest (New-NativeManifest)
$checks++
$contextManifest = New-NativeManifest
$contextManifest | Add-Member -NotePropertyName ContextProtocol -NotePropertyValue 'linux-proc-boundaries-v1'
$contextManifest | Add-Member -NotePropertyName PreparationHistory -NotePropertyValue 'SegmentedOnly'
$contextManifest | Add-Member -NotePropertyName ClockTicksPerSecond -NotePropertyValue 100
foreach ($run in $contextManifest.Runs) {
    $copy = $delta | Select-Object *; $copy.Pid = $run.Environment.pid
    $run | Add-Member -NotePropertyName ProcessDelta -NotePropertyValue $copy
}
Assert-CsvPipeNativeManifest $contextManifest
$checks++
Assert-NativeReject 'missing declared process boundary context' {
    $contextManifest.Runs[0].ProcessDelta = $null; Assert-CsvPipeNativeManifest $contextManifest
}
foreach ($field in @('Samples', 'UnknownFraction', 'WarmupSeconds', 'JitMethodCount', 'NativeAssemblyCount', 'LostSamples')) {
    foreach ($value in @($null, -1, [double]::NaN, [double]::PositiveInfinity)) {
        Assert-NativeReject "invalid $field counter ($value)" {
            $manifest = New-NativeManifest; $manifest.Runs[0].$field = $value; Assert-CsvPipeNativeManifest $manifest
        }
    }
}
Assert-NativeReject 'acceptance-labelled manifest' { $m = New-NativeManifest; $m.DiagnosticOnly = $false; Assert-CsvPipeNativeManifest $m }
Assert-NativeReject 'reserved stub-block export' { $m = New-NativeManifest; $m.PerfMapStubGranularity = 0; Assert-CsvPipeNativeManifest $m }
Assert-NativeReject 'incompatible JIT clock' { $m = New-NativeManifest; $m.Clock = 'default'; Assert-CsvPipeNativeManifest $m }
Assert-NativeReject 'wrong JIT owner' { $m = New-NativeManifest; $m.Runs[0].JitPid++; Assert-CsvPipeNativeManifest $m }
Assert-NativeReject 'different binaries' { $m = New-NativeManifest; $m.Runs[1].Environment.parserHash = 'd' * 64; Assert-CsvPipeNativeManifest $m }
Assert-NativeReject 'short capture batch' { $m = New-NativeManifest; $m.Runs[0].Batch.batchMs = 31000; Assert-CsvPipeNativeManifest $m }
Assert-NativeReject 'nonfinite batch' { $m = New-NativeManifest; $m.Runs[0].Batch.batchMs = [double]::NaN; Assert-CsvPipeNativeManifest $m }
Assert-NativeReject 'lost samples' { $m = New-NativeManifest; $m.Runs[0].LostSamples = 1; Assert-CsvPipeNativeManifest $m }
Assert-NativeReject 'unresolved symbols' { $m = New-NativeManifest; $m.Runs[0].UnknownFraction = .21; Assert-CsvPipeNativeManifest $m }

$lines = @('# perf report', '', '80.00%|800|800000|1234:dotnet|[.] HeroParser.Hot|jitted.so', '20.00%|200|200000|1234:dotnet|[.] native|libc.so')
$report = Read-CsvPipePerfReport $lines 1234
if ($report.Samples -ne 1000 -or $report.Period -ne 1000000 -or $report.UnknownFraction -ne 0 -or $report.Workload.Count -ne 1) {
    throw 'Native report totals were not calculated correctly.'
}
$checks++
Assert-NativeReject 'wrong sample PID' { Read-CsvPipePerfReport $lines 12 }
Assert-NativeReject 'short report' { Read-CsvPipePerfReport @('100.00%|499|100|1234:dotnet|HeroParser.Hot|jit.so') 1234 }
Assert-NativeReject 'unknown-heavy report' { Read-CsvPipePerfReport @($lines[2], '30.00%|300|300001|1234:dotnet|[unknown]|[unknown]') 1234 }
Assert-NativeReject 'no workload symbol' { Read-CsvPipePerfReport @('100.00%|1000|1000|1234:dotnet|native|libc.so') 1234 }
Assert-NativeReject 'foreign worker thread' { Read-CsvPipePerfReport @('100.00%|1000|1000|5678:dotnet|HeroParser.Hot|jit.so') 1234 }
$threadReport = Read-CsvPipePerfReport @('100.00%|1000|1000|1235:worker|HeroParser.Hot|jit.so') 1234 @(1234, 1235)
if ($threadReport.Hotspots[0].Tid -ne 1235 -or $threadReport.Hotspots[0].Pid -ne 1234) { throw 'Thread/process identity was conflated.' }
$checks++
$ipcReport = Read-CsvPipePerfReport @('100.00%|1000|1000|1234:dotnet|HeroParser.Hot|jit.so|-      -') 1234
if ($ipcReport.Samples -ne 1000) { throw 'Software-event IPC placeholder was misparsed.' }
$checks++
Assert-NativeReject 'unexpected report column' { Read-CsvPipePerfReport @('100.00%|1000|1000|1234:dotnet|HeroParser.Hot|jit.so|unexpected') 1234 }
Assert-NativeReject 'malformed report columns' { Read-CsvPipePerfReport @('100.00%|1000|1234|HeroParser.Hot') 1234 }

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('native-fixture-' + [guid]::NewGuid())
$null = New-Item -ItemType Directory -Path $temporary
try {
    $path = Join-Path $temporary 'fixture.dump'
    $writer = [IO.BinaryWriter]::new([IO.File]::Create($path))
    try {
        foreach ($value in @(0x4A695444, 1, 40, 62, 0, 1234)) { $writer.Write([uint32]$value) }
        $writer.Write([uint64]1); $writer.Write([uint64]0)
        $name = [Text.Encoding]::UTF8.GetBytes('HeroParser.Hot[Tier1]' + [char]0)
        $code = [byte[]]@(0x90, 0x90, 0xc3)
        $writer.Write([uint32]0); $writer.Write([uint32](56 + $name.Length + $code.Length)); $writer.Write([uint64]2)
        $writer.Write([uint32]1234); $writer.Write([uint32]1235)
        foreach ($value in @(4096, 4096, 3, 1)) { $writer.Write([uint64]$value) }
        $writer.Write($name); $writer.Write($code)
    }
    finally { $writer.Dispose() }
    $dump = Read-CsvPipeJitDump $path 1234
    if ($dump.Methods.Count -ne 1 -or $dump.Methods[0].Name -ne 'HeroParser.Hot[Tier1]' -or $dump.Methods[0].Size -ne 3 -or
        $dump.Methods[0].Address -ne '0x1000' -or $dump.Methods[0].CodeHash -notmatch '^[0-9a-f]{64}$') { throw 'JIT fixture decoded incorrectly.' }
    $checks++
    Assert-NativeReject 'wrong jitdump owner' { Read-CsvPipeJitDump $path 5678 }
    $bytes = [IO.File]::ReadAllBytes($path)
    $bad = Join-Path $temporary 'bad.dump'
    Assert-NativeReject 'truncated jitdump header' { [IO.File]::WriteAllBytes($bad, $bytes[0..20]); Read-CsvPipeJitDump $bad 1234 }
    Assert-NativeReject 'truncated code record' { [IO.File]::WriteAllBytes($bad, $bytes[0..($bytes.Length - 2)]); Read-CsvPipeJitDump $bad 1234 }
    Assert-NativeReject 'empty jitdump' { [IO.File]::WriteAllBytes($bad, $bytes[0..39]); Read-CsvPipeJitDump $bad 1234 }
    Assert-NativeReject 'unsupported architecture' { $copy = $bytes.Clone(); $copy[12] = 3; [IO.File]::WriteAllBytes($bad, $copy); Read-CsvPipeJitDump $bad 1234 }
    Assert-NativeReject 'unterminated JIT name' { $copy = $bytes.Clone(); $copy[$copy.Length - 4] = 65; [IO.File]::WriteAllBytes($bad, $copy); Read-CsvPipeJitDump $bad 1234 }
}
finally {
    $resolved = (Resolve-Path -LiteralPath $temporary).Path
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $parent -or [IO.Path]::GetFileName($resolved) -notmatch '^native-fixture-[0-9a-f-]{36}$') {
        throw 'Refusing fixture cleanup outside its verified temporary directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host "PASS: $checks native diagnostic checks; no workload or profiler executed"
