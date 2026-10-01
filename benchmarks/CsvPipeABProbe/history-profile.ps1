param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$Workspace,
    [Parameter(Mandatory = $true)][string]$PerfTool,
    [Parameter(Mandatory = $true)][string]$AnalyzerDll,
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-history/study'
)

$ErrorActionPreference = 'Stop'
if (!$IsLinux) { throw 'Historical correlation requires Linux.' }
. (Join-Path $PSScriptRoot 'control.ps1')
. (Join-Path $PSScriptRoot 'native-evidence.ps1')
. (Join-Path $PSScriptRoot 'history-evidence.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Workspace = [IO.Path]::GetFullPath($Workspace)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$PerfTool = (Resolve-Path -LiteralPath $PerfTool).Path
$AnalyzerDll = (Resolve-Path -LiteralPath $AnalyzerDll).Path
$source = '89c06810e76c4623ebad3cfc89c4bcdef41acd59'
$hashes = @{
    '2-a-commands.ndjson' = '54d72f329d11581f15c0d642f7a1a5e5c6731f2cb1cfec7b421521ab2813cd38'
    '2-b-commands.ndjson' = '54d72f329d11581f15c0d642f7a1a5e5c6731f2cb1cfec7b421521ab2813cd38'
    'isolated-aa-2.ndjson' = 'edcda1bca129eeb6409bad58afc3661ddf30aecafe44115654174d670cb4d921'
}
foreach ($name in $hashes.Keys) {
    if ((Get-FileHash (Join-Path $InputDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$name]) {
        throw "Historical artifact hash mismatch: $name"
    }
}
$records = @(Get-Content (Join-Path $InputDirectory 'isolated-aa-2.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
$commandsA = @(Get-Content (Join-Path $InputDirectory '2-a-commands.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
$commandsB = @(Get-Content (Join-Path $InputDirectory '2-b-commands.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
$plan = @(Get-CsvPipeHistoryPlan $records $commandsA $commandsB)
$original = @($records | Where-Object kind -eq 'environment')[0]
if ($original.baselineRef -ne $source -or $original.cycle -ne 2) { throw 'Unexpected historical workload/cycle.' }
if (@(& git -C $root status --porcelain).Count -or $LASTEXITCODE -ne 0) { throw 'Use clean committed diagnostics.' }
$diagnosticSha = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic revision unavailable.' }
if (Test-Path -LiteralPath $Workspace) { throw 'Use a fresh history workspace.' }
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem $OutputDirectory -Force).Count) { throw 'Do not overwrite historical evidence.' }
foreach ($variable in Get-ChildItem Env:) {
    if ($variable.Name -match '^(DOTNET_|COMPlus_)' -and $variable.Name -notin @('DOTNET_ROOT', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE')) {
        throw "Uncontrolled parent runtime override: $($variable.Name)"
    }
}
$clockTicks = (& getconf CLK_TCK | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $clockTicks -notmatch '^[1-9]\d*$') { throw 'Process accounting clock unavailable.' }
$allowed = Get-Content '/proc/self/status' | Select-String '^Cpus_allowed_list:\s*([0-9,-]+)$'
if (@($allowed).Count -ne 1) { throw 'Coordinator CPU permissions unavailable.' }
$cpu = (($allowed.Matches[0].Groups[1].Value -split ',')[0] -split '-')[0]
if ($cpu -notmatch '^\d+$' -or ([IO.File]::ReadAllText('/proc/sys/kernel/sched_schedstats')).Trim() -ne '1') {
    throw 'CPU identity or enabled scheduling statistics unavailable.'
}
$null = New-Item -ItemType Directory -Path $Workspace, $OutputDirectory -Force
$historicalInput = Join-Path $OutputDirectory 'historical-input'
$null = New-Item -ItemType Directory -Path $historicalInput
foreach ($name in $hashes.Keys) { Copy-Item -LiteralPath (Join-Path $InputDirectory $name) -Destination $historicalInput }
$plan | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDirectory 'request-plan.json')
$hashes | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'historical-input-hashes.json')
$workload = Join-Path $Workspace 'workload'
$added = $false
$results = @()
$failure = $null
$conditions = @(
    @{ Name = 'reference-b-first'; Observed = $false; BFirst = $true },
    @{ Name = 'observed-a-first'; Observed = $true; BFirst = $false },
    @{ Name = 'observed-b-first'; Observed = $true; BFirst = $true },
    @{ Name = 'reference-a-first'; Observed = $false; BFirst = $false }
)

function Start-HistoryProcess([string]$File, [string[]]$Arguments, [string]$Directory) {
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.WorkingDirectory = $Directory
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    return [pscustomobject]@{ Process = $process; Out = $process.StandardOutput.ReadToEndAsync(); Error = $process.StandardError.ReadToEndAsync(); Finished = $false }
}

function Finish-HistoryProcess($Child, [string]$Name, [switch]$Interrupt) {
    if ($Child.Finished) { return }
    if ($Interrupt -and !$Child.Process.HasExited) {
        & sudo -n kill -INT $Child.Process.Id
        if ($LASTEXITCODE -ne 0) { throw 'Could not terminate the retained capture gracefully.' }
    }
    try {
        if (!$Child.Process.WaitForExit(120000)) { throw 'Diagnostic child timeout.' }
        $Child.Out.GetAwaiter().GetResult() | Set-Content "$Name-stdout.txt"
        $Child.Error.GetAwaiter().GetResult() | Set-Content "$Name-stderr.txt"
        if ($Child.Process.ExitCode -ne 0 -and !($Interrupt -and $Child.Process.ExitCode -eq 130)) {
            throw "Diagnostic child failed: $($Child.Process.ExitCode)"
        }
    }
    finally {
        if (!$Child.Process.HasExited) { $Child.Process.Kill($true); $Child.Process.WaitForExit() }
        $Child.Process.Dispose()
        $Child.Finished = $true
    }
}

function Get-HistorySnapshot($Worker) {
    $owner = $Worker.Process.Id
    $stat = Read-CsvPipeProcStat ([IO.File]::ReadAllText("/proc/$owner/stat")) $owner
    $tasks = foreach ($directory in Get-ChildItem "/proc/$owner/task" -Directory) {
        $sched = Join-Path $directory.FullName 'schedstat'
        if (Test-Path -LiteralPath $sched) {
            try {
                $threadStat = Read-CsvPipeProcStat ([IO.File]::ReadAllText((Join-Path $directory.FullName 'stat'))) ([int]$directory.Name)
                [pscustomobject]@{ Tid = [int]$directory.Name; StartTicks = $threadStat.StartTicks; SchedStat = [IO.File]::ReadAllText($sched) }
            }
            catch [IO.FileNotFoundException] { continue }
            catch [IO.DirectoryNotFoundException] { continue }
        }
    }
    return [pscustomobject]@{ Stat = $stat; Tasks = @($tasks); MonotonicTicks = [Diagnostics.Stopwatch]::GetTimestamp() }
}

function Read-HistoryWorker($Worker) {
    $task = $Worker.Process.StandardOutput.ReadLineAsync()
    if (!$task.Wait(120000)) { throw 'Historical worker response timed out.' }
    $line = $task.GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($line)) { throw 'Historical worker exited without a response.' }
    $line | Add-Content (Join-Path $Worker.Directory 'worker.ndjson')
    return $line | ConvertFrom-Json
}

function Start-HistoryWorker([string]$Directory, [bool]$Observed) {
    $info = [Diagnostics.ProcessStartInfo]::new('taskset')
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in @('--cpu-list', $cpu, 'dotnet', (Join-Path $Directory 'CsvPipeABProbe.dll'), '--rows', '2000')) { $info.ArgumentList.Add($argument) }
    Set-CsvPipeWorkerEnvironment $info
    $info.Environment['HERO_PARSER_WORKER_REF'] = $source
    $info.Environment['HERO_PARSER_WORKER_PROTOCOL'] = 'csv-pipe-isolated-v4-same-cpu'
    $info.Environment['HERO_PARSER_WORKER_CPU'] = $cpu
    if ($Observed) {
        $info.Environment['DOTNET_PerfMapEnabled'] = '1'
        $info.Environment['DOTNET_PerfMapShowOptimizationTiers'] = '1'
        $info.Environment['DOTNET_PerfMapStubGranularity'] = '2'
        $info.Environment['DOTNET_EnableEventPipe'] = '1'
        $info.Environment['DOTNET_EventPipeConfig'] = 'Microsoft-Windows-DotNETRuntime:11:5'
        $info.Environment['DOTNET_EventPipeOutputPath'] = Join-Path $Directory 'runtime.nettrace'
        $info.Environment['DOTNET_EventPipeCircularMB'] = '40'
    }
    @($info.Environment.Keys | Sort-Object) | ConvertTo-Json | Set-Content (Join-Path $Directory 'worker-environment-keys.json')
    $process = [Diagnostics.Process]::Start($info)
    $worker = [pscustomobject]@{ Process = $process; Directory = $Directory; Environment = $null
        Errors = $process.StandardError.ReadToEndAsync(); Windows = [Collections.Generic.List[object]]::new() }
    try {
        $worker.Environment = Read-HistoryWorker $worker
        if ($worker.Environment.pid -ne $process.Id) { throw 'Historical worker PID mismatch.' }
        foreach ($field in @('parserHash', 'modelsHash', 'consumerHash')) {
            if ($worker.Environment.$field -ne $original.baselineWorker.$field) { throw "Frozen historical binary mismatch: $field" }
        }
        return $worker
    }
    catch { $process.Kill($true); $process.WaitForExit(); $process.Dispose(); throw }
}

function Send-HistoryRequest($Worker, $Request) {
    $before = if ($Request.Operation -ne 'stop') { Get-HistorySnapshot $Worker } else { $null }
    $command = $Request | Select-Object Id, Operation, Transport, Repeats | ConvertTo-Json -Compress
    $command | Add-Content (Join-Path $Worker.Directory 'commands.ndjson')
    $startUtc = [DateTime]::UtcNow.Ticks
    $startMono = [Diagnostics.Stopwatch]::GetTimestamp()
    $Worker.Process.StandardInput.WriteLine($command); $Worker.Process.StandardInput.Flush()
    $response = Read-HistoryWorker $Worker
    $endMono = [Diagnostics.Stopwatch]::GetTimestamp()
    $endUtc = [DateTime]::UtcNow.Ticks
    $kind = switch ($Request.Operation) { 'verify' { 'worker-verified' } 'prepare' { 'worker-prepared' } 'batch' { 'worker-batch' } 'stop' { 'worker-stopped' } }
    if ($response.kind -ne $kind -or $response.Id -ne $Request.Id -or $response.pid -ne $Worker.Process.Id) { throw 'Historical response identity mismatch.' }
    if ($Request.Operation -in @('prepare', 'batch') -and $response.transport -ne $Request.Transport) { throw 'Historical transport mismatch.' }
    if ($Request.Operation -eq 'verify') { Assert-CsvPipeWorkerVerification $response }
    if ($Request.Operation -eq 'batch' -and ($response.repeats -ne $Request.Repeats -or
        ![double]::IsFinite([double]$response.batchMs) -or $response.batchMs -le 0)) { throw 'Invalid historical batch response.' }
    if ($null -ne $before) {
        $after = Get-HistorySnapshot $Worker
        $window = [pscustomobject]@{ Pid = $Worker.Process.Id; Id = $Request.Id; Stage = $Request.Stage
            Transport = $Request.Transport; Pair = $Request.Pair; StartUtcTicks = $startUtc; EndUtcTicks = $endUtc
            StartMonotonicTicks = $startMono; EndMonotonicTicks = $endMono; Response = $response
            Before = $before; After = $after; ProcessDelta = Get-CsvPipeProcessDelta $before $after ([long]$clockTicks) }
        $Worker.Windows.Add($window)
        $window | ConvertTo-Json -Depth 10 -Compress | Add-Content (Join-Path $Worker.Directory 'request-windows.ndjson')
    }
    return $response
}

function Get-HistoryPercentile([double[]]$Values, [double]$Percentile) {
    $sorted = @($Values | Sort-Object)
    $index = ($sorted.Count - 1) * $Percentile
    $lower = [int][Math]::Floor($index)
    return $sorted[$lower] + ($sorted[[int][Math]::Ceiling($index)] - $sorted[$lower]) * ($index - $lower)
}

try {
    & git -C $root worktree add --detach $workload $source
    if ($LASTEXITCODE -ne 0) { throw 'Frozen history checkout failed.' }
    $added = $true
    $probe = Join-Path $workload 'benchmarks/CsvPipeABProbe'
    $parser = Join-Path $workload 'src/HeroParser/bin/Release/net10.0/HeroParser.dll'
    $models = Join-Path $probe 'Models/bin/Release/isolated/CsvPipeABModels.dll'
    $builds = @(
        @((Join-Path $workload 'src/HeroParser/HeroParser.csproj'), '-c', 'Release', '-f', 'net10.0'),
        @((Join-Path $probe 'Models/CsvPipeABModels.csproj'), '-c', 'Release', '-p:AssemblyName=CsvPipeABModels', "-p:ParserDll=$parser", '-p:BaseIntermediateOutputPath=obj/isolated/', '-p:OutputPath=bin/Release/isolated/'),
        @((Join-Path $probe 'CsvPipeABProbe.csproj'), '-c', 'Release', '-p:SingleModule=true', "-p:CandidateModelsDll=$models", '-p:BaseIntermediateOutputPath=obj/isolated/', '-p:OutputPath=bin/Release/isolated/')
    )
    foreach ($arguments in $builds) {
        & dotnet build @arguments --disable-build-servers -p:UseSharedCompilation=false | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Frozen history build failed.' }
    }
    foreach ($condition in $conditions) {
        $workers = @{}; $captures = @(); $pairs = @(); $complete = $false; $conditionError = $null
        $directory = Join-Path $OutputDirectory $condition.Name
        $null = New-Item -ItemType Directory -Path $directory -Force
        try {
            $launch = if ($condition.BFirst) { @('b', 'a') } else { @('a', 'b') }
            foreach ($side in $launch) {
                $workerDirectory = Join-Path $directory $side
                $null = New-Item -ItemType Directory -Path $workerDirectory
                Copy-Item -Path (Join-Path $probe 'bin/Release/isolated/*') -Destination $workerDirectory -Recurse
                $workers[$side] = Start-HistoryWorker $workerDirectory $condition.Observed
            }
            Assert-CsvPipeWorkerEnvironment $workers.a.Environment $workers.b.Environment $source $source
            foreach ($side in @('a', 'b')) {
                Get-Content "/proc/$($workers[$side].Process.Id)/maps" | Set-Content (Join-Path $workers[$side].Directory 'process-maps.txt')
            }
            if ($condition.Observed) {
                $owners = "$($workers.a.Process.Id),$($workers.b.Process.Id)"
                $captures += @{ Child = Start-HistoryProcess 'sudo' @('-n', $PerfTool, 'record', '--clockid', 'mono', '-e', 'cpu-clock:u', '-F', '199', '-p', $owners, '-o', (Join-Path $directory 'cpu.perf.data')) $directory; Name = 'cpu-record' }
                $captures += @{ Child = Start-HistoryProcess 'sudo' @('-n', $PerfTool, 'record', '--clockid', 'mono', '-a', '-C', $cpu, '-e', 'sched:sched_switch', '-e', 'sched:sched_wakeup', '-o', (Join-Path $directory 'scheduler.perf.data')) $directory; Name = 'scheduler-record' }
                # Recorders attach before requests; this delay is diagnostic setup, not extra parser warmup.
                Start-Sleep -Seconds 1
                foreach ($capture in $captures) { if ($capture.Child.Process.HasExited) { throw 'Capture failed before replay.' } }
            }
            foreach ($request in $plan) {
                if ($request.Operation -eq 'stop') { break }
                $order = if ($request.CandidateFirst) { @('b', 'a') } else { @('a', 'b') }
                $responses = @{}
                foreach ($side in $order) { $responses[$side] = Send-HistoryRequest $workers[$side] $request }
                if ($request.Stage -eq 'measured') {
                    $pair = [pscustomobject]@{ DiagnosticOnly = $true; Id = $request.Id; Transport = $request.Transport
                        Pair = $request.Pair; CandidateFirst = $request.CandidateFirst; A = $responses.a; B = $responses.b
                        Ratio = $responses.b.measurement.Milliseconds / $responses.a.measurement.Milliseconds }
                    $pairs += $pair
                    $pair | ConvertTo-Json -Depth 6 -Compress | Add-Content (Join-Path $directory 'pairs.ndjson')
                }
            }
            foreach ($capture in $captures) { Finish-HistoryProcess $capture.Child (Join-Path $directory $capture.Name) -Interrupt }
            $captures = @()
            foreach ($side in @('a', 'b')) {
                $worker = $workers[$side]
                Get-Content "/proc/$($worker.Process.Id)/maps" | Set-Content (Join-Path $worker.Directory 'process-maps-after.txt')
                $null = Send-HistoryRequest $worker $plan[-1]
                if (!$worker.Process.WaitForExit(10000) -or $worker.Process.ExitCode -ne 0) { throw 'Historical worker did not exit cleanly.' }
                if ($condition.Observed) {
                    $owner = $worker.Process.Id
                    $temporary = [Environment]::GetEnvironmentVariable('TMPDIR')
                    if (!$temporary) { $temporary = '/tmp' }
                    $jit = Join-Path $temporary "jit-$owner.dump"
                    Copy-Item -LiteralPath $jit -Destination $worker.Directory
                    Copy-Item -LiteralPath (Join-Path $temporary "perf-$owner.map") -Destination $worker.Directory
                    $dump = Read-CsvPipeJitDump $jit $owner
                    if ($dump.Flags -ne 0) { throw 'JIT timestamps are not monotonic nanoseconds.' }
                    $dump | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $worker.Directory 'jit-methods.json')
                    & dotnet $AnalyzerDll (Join-Path $worker.Directory 'runtime.nettrace') $owner $worker.Directory
                    if ($LASTEXITCODE -ne 0) { throw 'Runtime decoder rejected incomplete GC/JIT evidence.' }
                    $events = @(Get-Content (Join-Path $worker.Directory 'runtime-events.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
                    $pauses = @(Get-CsvPipeGcPauses $events $owner)
                    if (!$pauses.Count) { throw 'Runtime suspension events missing; no zero-pause inference.' }
                    $pauses | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $worker.Directory 'runtime-suspensions.json')
                    foreach ($window in $worker.Windows) {
                        $overlap = Get-CsvPipePauseOverlap $pauses $window.StartUtcTicks $window.EndUtcTicks
                        [pscustomobject]@{ Pid = $owner; Id = $window.Id; Stage = $window.Stage; Transport = $window.Transport
                            Pair = $window.Pair; RuntimeSuspension = $overlap; ProcessDelta = $window.ProcessDelta
                            Scheduling = Get-CsvPipeSchedulingDelta $window.Before $window.After } |
                            ConvertTo-Json -Depth 6 -Compress | Add-Content (Join-Path $worker.Directory 'correlated-windows.ndjson')
                    }
                }
            }
            if ($condition.Observed) {
                & sudo -n chown -R "$((& id -u).Trim()):$((& id -g).Trim())" $directory
                if ($LASTEXITCODE -ne 0) { throw 'Cannot retain readable perf data.' }
                foreach ($event in @('cpu', 'scheduler')) {
                    $fields = if ($event -eq 'cpu') { 'pid,tid,time,event,ip' } else { 'comm,pid,tid,cpu,time,event,trace' }
                    $child = Start-HistoryProcess $PerfTool @('script', '--ns', '--show-lost-events', '-F', $fields, '-i', (Join-Path $directory "$event.perf.data")) $directory
                    Finish-HistoryProcess $child (Join-Path $directory "$event-events")
                    if (Select-String -Path (Join-Path $directory "$event-events-*.txt"), (Join-Path $directory "$event-record-stderr.txt") -Pattern 'PERF_RECORD_LOST|\bLOST(?:_SAMPLES)?\b|lost\s+[1-9]\d*\s+(events|samples)' -Quiet) { throw 'Perf trace lost events; correlation is incomplete.' }
                }
                if (!(Select-String (Join-Path $directory 'scheduler-events-stdout.txt') -Pattern 'sched_switch:' -Quiet) -or
                    !(Select-String (Join-Path $directory 'scheduler-events-stdout.txt') -Pattern 'sched_wakeup:' -Quiet)) { throw 'Scheduling trace has no switch/wakeup events.' }
                $cpuWorkers = @{}
                foreach ($side in @('a', 'b')) {
                    $owner = [string]$workers[$side].Process.Id
                    $methods = Get-Content (Join-Path $workers[$side].Directory 'jit-methods.json') -Raw | ConvertFrom-Json
                    $cpuWorkers[$owner] = @{ Methods = $methods.Methods; Windows = $workers[$side].Windows }
                }
                $correlation = Get-CsvPipeCpuCorrelation (Get-Content (Join-Path $directory 'cpu-events-stdout.txt')) $cpuWorkers ([Diagnostics.Stopwatch]::Frequency)
                $correlation.Quality | ConvertTo-Json | Set-Content (Join-Path $directory 'cpu-correlation-quality.json')
                $correlation.Windows | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content (Join-Path $directory 'native-request-windows.ndjson')
                foreach ($quality in $correlation.Quality) {
                    if ($quality.Samples -lt 500 -or $quality.WorkloadAssigned -lt 500) { throw 'Insufficient same-PID native/request attribution.' }
                }
            }
            $complete = $true
        }
        catch { $conditionError = $_.Exception.Message }
        finally {
            foreach ($capture in $captures) {
                try { Finish-HistoryProcess $capture.Child (Join-Path $directory $capture.Name) -Interrupt }
                catch { if (!$conditionError) { $conditionError = $_.Exception.Message }; $complete = $false }
            }
            foreach ($worker in $workers.Values) {
                if (!$worker.Process.HasExited) { $worker.Process.Kill($true); $worker.Process.WaitForExit() }
                $worker.Errors.GetAwaiter().GetResult() | Set-Content (Join-Path $worker.Directory 'worker-stderr.txt')
                $worker.Process.Dispose()
            }
            & sudo -n chown -R "$((& id -u).Trim()):$((& id -g).Trim())" $directory
            if ($LASTEXITCODE -ne 0) { $complete = $false; $conditionError = 'Partial evidence ownership repair failed.' }
            $summaries = foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
                $selected = @($pairs | Where-Object Transport -eq $transport)
                if ($selected.Count -eq 30) {
                    [pscustomobject]@{ Transport = $transport; Pairs = 30; MedianRatio = Get-HistoryPercentile $selected.Ratio .5
                        P10Ratio = Get-HistoryPercentile $selected.Ratio .1; P90Ratio = Get-HistoryPercentile $selected.Ratio .9
                        MinimumBatchMs = (@($selected.A.batchMs) + @($selected.B.batchMs) | Measure-Object -Minimum).Minimum }
                }
            }
            $result = [pscustomobject]@{ Condition = $condition; Complete = $complete; Error = $conditionError
                Environments = @($workers.Values.Environment); Summaries = @($summaries) }
            $results += $result
            $result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $directory 'condition-summary.json')
        }
    }
    if (@($results | Where-Object { !$_.Complete }).Count) { throw 'Fixed history study incomplete; retained every condition, no retry.' }
}
catch { $failure = $_.Exception.Message; throw }
finally {
    [pscustomobject]@{ Protocol = 'csv-pipe-history-v1-diagnostic-only'; DiagnosticOnly = $true
        SourceRun = '36732093817'; HistoricalCycle = 2; SourceSha = $source; DiagnosticSha = $diagnosticSha
        Cpu = $cpu; ClockTicksPerSecond = [long]$clockTicks; StopwatchFrequency = [Diagnostics.Stopwatch]::Frequency
        SchedulingStatisticsEnabled = $true; Failure = $failure; Results = $results
        State = 'history-evidence-not-timing-acceptance-or-root-cause-proof' } |
        ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutputDirectory 'history-summary.json')
    if ($added) {
        if ((Resolve-Path -LiteralPath $workload).Path -ne $workload -or @(& git -C $workload status --porcelain).Count -or $LASTEXITCODE -ne 0) {
            throw 'Retaining worktree because cleanup safety verification failed.'
        }
        & git -C $root worktree remove $workload
        if ($LASTEXITCODE -ne 0) { throw 'Worktree cleanup failed; no forced removal.' }
    }
}
