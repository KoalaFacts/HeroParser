param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$SourceRun,
    [Parameter(Mandatory = $true)][string]$AnalyzerDll,
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-history/replay'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'control.ps1')
. (Join-Path $PSScriptRoot 'native-evidence.ps1')
. (Join-Path $PSScriptRoot 'history-evidence.ps1')
if ($SourceRun -ne '36807711817') { throw 'Replay only the retained, predeclared capture; no replacement measurements.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Do not overwrite replay evidence.' }
$manifest = Get-Content (Join-Path $InputDirectory 'history-summary.json') -Raw | ConvertFrom-Json
if ($manifest.Protocol -ne 'csv-pipe-history-v1-diagnostic-only' -or !$manifest.DiagnosticOnly -or
    $manifest.SourceRun -ne '36732093817' -or $manifest.HistoricalCycle -ne 2 -or
    $manifest.SourceSha -ne '89c06810e76c4623ebad3cfc89c4bcdef41acd59' -or
    $manifest.DiagnosticSha -ne '7252c32cf601e3d06b1c1451d196e2ae2e4b8201' -or
    $manifest.StopwatchFrequency -ne 1000000000 -or !$manifest.SchedulingStatisticsEnabled) {
    throw 'Unexpected capture identity or clocks.'
}
$historicalInput = Join-Path $InputDirectory 'historical-input'
$hashes = @{
    '2-a-commands.ndjson' = '54d72f329d11581f15c0d642f7a1a5e5c6731f2cb1cfec7b421521ab2813cd38'
    '2-b-commands.ndjson' = '54d72f329d11581f15c0d642f7a1a5e5c6731f2cb1cfec7b421521ab2813cd38'
    'isolated-aa-2.ndjson' = 'edcda1bca129eeb6409bad58afc3661ddf30aecafe44115654174d670cb4d921'
}
foreach ($name in $hashes.Keys) {
    if ((Get-FileHash (Join-Path $historicalInput $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$name]) { throw 'Historical input changed.' }
}
$records = @(Get-Content (Join-Path $historicalInput 'isolated-aa-2.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
$original = @($records | Where-Object kind -eq 'environment')[0]
$commands = @(Get-Content (Join-Path $historicalInput '2-a-commands.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
$plan = @(Get-CsvPipeHistoryPlan $records $commands @(Get-Content (Join-Path $historicalInput '2-b-commands.ndjson') | ForEach-Object { $_ | ConvertFrom-Json }))
$expected = @('reference-b-first', 'observed-a-first', 'observed-b-first', 'reference-a-first')
if (@($manifest.Results).Count -ne 4) { throw 'Fixed conditions missing.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
Copy-Item -LiteralPath $InputDirectory -Destination (Join-Path $OutputDirectory 'original') -Recurse
$results = @()
foreach ($name in $expected) {
    $condition = @($manifest.Results | Where-Object { $_.Condition.Name -eq $name })
    if ($condition.Count -ne 1) { throw 'Missing or duplicate condition.' }
    $condition = $condition[0]
    $directory = Join-Path $InputDirectory $name
    $output = Join-Path $OutputDirectory $name
    $null = New-Item -ItemType Directory -Path $output
    $pairs = @(Get-Content (Join-Path $directory 'pairs.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
    if ($pairs.Count -ne 90 -or @($pairs.Id | Sort-Object -Unique).Count -ne 90) { throw 'Incomplete or duplicate original measurement history.' }
    $workers = @{}; $owners = @()
    foreach ($side in @('a', 'b')) {
        $workerDirectory = Join-Path $directory $side
        $responses = @(Get-Content (Join-Path $workerDirectory 'worker.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
        $environment = $responses[0]; $owners += $environment
        if ($responses.Count -ne $plan.Count + 1 -or $responses[-1].kind -ne 'worker-stopped') { throw 'Worker transcript incomplete.' }
        Assert-CsvPipeWorkerVerification $responses[1]
        foreach ($field in @('runtime', 'serverGc', 'processors', 'parserHash', 'modelsHash', 'consumerHash')) {
            if ($environment.$field -ne $original.baselineWorker.$field) { throw "Frozen runtime/binary changed: $field" }
        }
        $replayed = @(Get-Content (Join-Path $workerDirectory 'commands.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
        if ($replayed.Count -ne $commands.Count) { throw 'Commands missing.' }
        for ($i = 0; $i -lt $commands.Count; $i++) {
            foreach ($field in @('Id', 'Operation', 'Transport', 'Repeats')) {
                if ($replayed[$i].$field -cne $commands[$i].$field) { throw "Historical command changed: $field" }
            }
            if ($responses[$i + 1].Id -ne $commands[$i].Id -or $responses[$i + 1].pid -ne $environment.pid) { throw 'Response identity mismatch.' }
        }
        $windows = @(Get-Content (Join-Path $workerDirectory 'request-windows.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
        if ($windows.Count -ne $plan.Count - 1) { throw 'Request windows missing.' }
        for ($i = 0; $i -lt $windows.Count; $i++) {
            $window = $windows[$i]; $request = $plan[$i]
            if ($window.Pid -ne $environment.pid -or $window.Id -ne $request.Id -or $window.Stage -ne $request.Stage -or
                $window.Transport -ne $request.Transport -or $window.StartMonotonicTicks -ge $window.EndMonotonicTicks) { throw 'Request window identity/clock mismatch.' }
        }
        foreach ($pair in $pairs) {
            $request = @($plan | Where-Object { $_.Stage -eq 'measured' -and $_.Id -eq $pair.Id })
            $response = if ($side -eq 'a') { $pair.A } else { $pair.B }
            if ($request.Count -ne 1 -or $response.pid -ne $environment.pid -or $response.Id -ne $pair.Id -or
                $pair.Transport -ne $request[0].Transport -or $pair.Pair -ne $request[0].Pair -or
                $pair.CandidateFirst -ne $request[0].CandidateFirst -or $response.repeats -ne $request[0].Repeats -or
                ![double]::IsFinite([double]$pair.Ratio) -or [Math]::Abs($pair.Ratio - $pair.B.measurement.Milliseconds / $pair.A.measurement.Milliseconds) -gt 1e-12) { throw 'Measured pair identity or ratio mismatch.' }
        }
        if ($condition.Condition.Observed) {
            $workerOutput = Join-Path $output $side
            $null = New-Item -ItemType Directory -Path $workerOutput
            & dotnet $AnalyzerDll (Join-Path $workerDirectory 'runtime.nettrace') $environment.pid $workerOutput
            if ($LASTEXITCODE -ne 0) { throw 'Original runtime trace incomplete.' }
            $events = @(Get-Content (Join-Path $workerOutput 'runtime-events.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
            $pauses = @(Get-CsvPipeGcPauses $events $environment.pid)
            if (!$pauses.Count) { throw 'Missing runtime suspensions.' }
            foreach ($window in $windows) {
                [pscustomobject]@{ Pid = $environment.pid; Id = $window.Id; Stage = $window.Stage; Transport = $window.Transport
                    Pair = $window.Pair; RuntimeSuspension = Get-CsvPipePauseOverlap $pauses $window.StartUtcTicks $window.EndUtcTicks
                    ProcessDelta = $window.ProcessDelta; Scheduling = Get-CsvPipeSchedulingDelta $window.Before $window.After } |
                    ConvertTo-Json -Depth 6 -Compress | Add-Content (Join-Path $workerOutput 'correlated-windows.ndjson')
            }
            $dump = Read-CsvPipeJitDump (Join-Path $workerDirectory "jit-$($environment.pid).dump") $environment.pid
            if ($dump.Flags -ne 0) { throw 'Original JIT timestamps use a different clock.' }
            $workers[[string]$environment.pid] = @{ Methods = $dump.Methods; Windows = $windows }
        }
    }
    Assert-CsvPipeWorkerEnvironment $owners[0] $owners[1] $manifest.SourceSha $manifest.SourceSha
    $quality = @()
    if ($condition.Condition.Observed) {
        if (Select-String -Path (Join-Path $directory '*events-*.txt'), (Join-Path $directory '*record-stderr.txt') -Pattern 'PERF_RECORD_LOST|\bLOST(?:_SAMPLES)?\b|lost\s+[1-9]\d*\s+(events|samples)' -Quiet) { throw 'Original perf trace lost events.' }
        foreach ($event in @('sched_switch:', 'sched_wakeup:')) {
            if (!(Select-String (Join-Path $directory 'scheduler-events-stdout.txt') -Pattern $event -Quiet)) { throw 'Original scheduler events missing.' }
        }
        $correlation = Get-CsvPipeCpuCorrelation (Get-Content (Join-Path $directory 'cpu-events-stdout.txt')) $workers $manifest.StopwatchFrequency
        $quality = $correlation.Quality
        $quality | ConvertTo-Json | Set-Content (Join-Path $output 'cpu-correlation-quality.json')
        $correlation.Windows | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content (Join-Path $output 'native-request-windows.ndjson')
        foreach ($worker in $quality) {
            if ($worker.Samples -lt 500 -or $worker.WorkloadAssigned -lt 500) { throw 'Insufficient native attribution.' }
        }
    }
    $results += [pscustomobject]@{ Name = $name; OriginalComplete = $condition.Complete; OriginalError = $condition.Error
        ArtifactAnalysisComplete = $true; Pairs = $pairs.Count; Quality = $quality; Summaries = $condition.Summaries }
}
[pscustomobject]@{ Protocol = 'csv-pipe-history-artifact-replay-v1'; DiagnosticOnly = $true; NoWorkloadExecuted = $true
    SourceRun = $SourceRun; OriginalCaptureSha = $manifest.DiagnosticSha; ReplaySha = (& git rev-parse HEAD).Trim()
    OriginalFailure = $manifest.Failure; CorrectnessCases = 288; MeasuredPairs = 360; MeasuredBatches = 720
    Results = $results; State = 'correlation-not-root-cause-or-timing-acceptance' } |
    ConvertTo-Json -Depth 10 | Set-Content (Join-Path $OutputDirectory 'replay-summary.json')
Write-Host 'PASS: original four-pair history recovered without new workers, profiling, warmup or timings'
