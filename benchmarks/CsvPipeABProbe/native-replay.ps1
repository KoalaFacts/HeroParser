param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$PerfTool,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+$')][string]$OriginRun,
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-native/replay'
)
$ErrorActionPreference = 'Stop'
if (!$IsLinux) { throw 'Native artifact replay requires Linux perf; no workload is launched.' }
. (Join-Path $PSScriptRoot 'control.ps1')
. (Join-Path $PSScriptRoot 'native-evidence.ps1')
$InputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$PerfTool = (Resolve-Path -LiteralPath $PerfTool).Path
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Do not overwrite replay evidence.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$origin = Get-Content -LiteralPath (Join-Path $InputDirectory 'native-summary.json') -Raw | ConvertFrom-Json
if ($origin.Manifest.Protocol -ne 'csv-pipe-nativecpu-v1-diagnostic-only' -or $origin.Manifest.DiagnosticOnly -ne $true -or
    $origin.Manifest.Clock -ne 'mono' -or $origin.Manifest.PerfMapStubGranularity -ne 2 -or
    $origin.Manifest.WorkloadSha -ne '89c06810e76c4623ebad3cfc89c4bcdef41acd59') { throw 'Unsupported replay provenance.' }
$environments = foreach ($side in @('a', 'b')) {
    $records = Get-Content -LiteralPath (Join-Path $InputDirectory "$side/worker.ndjson") | ConvertFrom-Json
    $environment = @($records | Where-Object kind -eq 'worker-environment')
    if ($environment.Count -ne 1) { throw 'Missing original worker identity.' }
    Assert-CsvPipeWorkerVerification ($records | Where-Object kind -eq 'worker-verified')
    $environment[0]
}
Assert-CsvPipeWorkerEnvironment $environments[0] $environments[1] $origin.Manifest.WorkloadSha $origin.Manifest.WorkloadSha
$results = @()
$state = 'incomplete-native-replay-not-performance-approval'
$failure = $null
function Invoke-ReplayPerf([string[]]$Arguments, [string]$Name) {
    & sudo -n $PerfTool @Arguments 1> "$Name-stdout.txt" 2> "$Name-stderr.txt"
    if ($LASTEXITCODE -ne 0) { throw "Replay tool failed: $(Split-Path -Leaf $Name). Retaining original data." }
}
try {
    foreach ($side in @('a', 'b')) {
        $input = Join-Path $InputDirectory $side
        $raw = Join-Path $input 'cpu.perf.data'
        if (!(Test-Path -LiteralPath $raw)) { continue }
        $records = Get-Content -LiteralPath (Join-Path $input 'worker.ndjson') | ConvertFrom-Json
        $owner = ($records | Where-Object kind -eq 'worker-environment').pid
        $batch = @($records | Where-Object { $_.kind -eq 'worker-batch' -and $_.repeats -eq 65536 })
        if ($batch.Count -ne 1 -or $batch[0].pid -ne $owner -or $batch[0].transport -ne 'Segmented128' -or $batch[0].batchMs -lt 32000) {
            throw 'Original capture batch correlation failed.'
        }
        $directory = Join-Path $OutputDirectory $side
        $null = New-Item -ItemType Directory -Path $directory
        $metadata = Get-Content -LiteralPath (Join-Path $input 'thread-owners.json') -Raw | ConvertFrom-Json
        if ($metadata.Pid -ne $owner) { throw 'Thread ownership provenance mismatch.' }
        $dumpPath = Join-Path $input "jit-$owner.dump"
        $dump = Read-CsvPipeJitDump $dumpPath $owner
        $runtimeDump = "/tmp/jit-$owner.dump"
        $runtimeMap = "/tmp/perf-$owner.map"
        if ((Test-Path -LiteralPath $runtimeDump) -or (Test-Path -LiteralPath $runtimeMap)) { throw 'Refusing to replace existing runtime metadata.' }
        Copy-Item -LiteralPath $dumpPath -Destination $runtimeDump
        Copy-Item -LiteralPath (Join-Path $input "perf-$owner.map") -Destination $runtimeMap
        Push-Location $directory
        try {
            $injected = Join-Path $directory 'cpu.jit.perf.data'
            Invoke-ReplayPerf @('inject', '--jit', '-i', $raw, '-o', $injected) (Join-Path $directory 'inject')
            Invoke-ReplayPerf @('evlist', '-i', $injected) (Join-Path $directory 'events')
            if (!(Select-String -LiteralPath (Join-Path $directory 'events-stdout.txt') -Pattern '^cpu-clock:u\s*$' -Quiet)) { throw 'Unexpected replay event.' }
            Invoke-ReplayPerf @('script', '-i', $injected, '--show-lost-events') (Join-Path $directory 'stacks')
            if (@(Select-String -LiteralPath (Join-Path $directory 'stacks-stdout.txt'), (Join-Path $directory 'stacks-stderr.txt'),
                (Join-Path $input 'record-stderr.txt') -Pattern '\bLOST\b|lost\s+[1-9]\d*\s+events').Count) { throw 'Original trace lost samples.' }
            Invoke-ReplayPerf @('report', '--stdio', '--no-children', '--call-graph', 'none', '--percent-limit', '0', '--show-nr-samples',
                '--show-total-period', '--field-separator', '|', '--sort', 'pid,symbol,dso', '-i', $injected) (Join-Path $directory 'exclusive')
            $report = Read-CsvPipePerfReport (Get-Content -LiteralPath (Join-Path $directory 'exclusive-stdout.txt')) $owner $metadata.Tids
            $count = 0
            foreach ($symbol in @($report.Workload | Sort-Object Period -Descending | Select-Object -First 3 -ExpandProperty Symbol -Unique)) {
                $count++
                Invoke-ReplayPerf @('annotate', '--stdio', '--symbol', $symbol, '-i', $injected) (Join-Path $directory "assembly-$count")
                if (!(Select-String -LiteralPath (Join-Path $directory "assembly-$count-stdout.txt") -Pattern '\b(ret|mov|cmp|call|jmp|lea)\b' -Quiet)) {
                    throw 'Replayed hotspot has no native assembly.'
                }
            }
            $results += @{ Pid = $owner; Samples = $report.Samples; Period = $report.Period; UnknownFraction = $report.UnknownFraction
                NativeAssemblyCount = $count; JitMethodCount = $dump.Methods.Count; Hotspots = $report.Hotspots
                RawHash = (Get-FileHash -LiteralPath $raw -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
        finally { Pop-Location }
    }
    if (!$results.Count) { throw 'No original native capture available.' }
    $state = 'native-replay-attribution-complete-not-full-two-worker-collection-or-performance-approval'
}
catch { $failure = $_.Exception.Message; throw }
finally {
    & sudo -n chown -R "$((& id -u).Trim()):$((& id -g).Trim())" $OutputDirectory
    if ($LASTEXITCODE -ne 0) { Write-Warning 'Could not restore replay artifact ownership.' }
    @{ State = $state; Failure = $failure; DiagnosticOnly = $true; OriginRun = $OriginRun
        OriginManifest = $origin.Manifest; ReplaySha = (& git rev-parse HEAD).Trim(); Runs = $results } |
        ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'replay-summary.json')
}
