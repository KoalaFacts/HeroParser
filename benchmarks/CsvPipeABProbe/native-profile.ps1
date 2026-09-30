param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{40}$')][string]$WorkloadSha,
    [Parameter(Mandatory = $true)][string]$Workspace,
    [Parameter(Mandatory = $true)][string]$PerfTool,
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-native'
)

$ErrorActionPreference = 'Stop'
if (!$IsLinux) { throw 'Native CPU evidence requires Linux.' }
. (Join-Path $PSScriptRoot 'native-evidence.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Workspace = [IO.Path]::GetFullPath($Workspace)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$PerfTool = (Resolve-Path -LiteralPath $PerfTool).Path
if (Test-Path -LiteralPath $Workspace) { throw 'Use a fresh native profiling workspace.' }
if (Test-Path (Join-Path $OutputDirectory 'native-summary.json')) { throw 'Previous native evidence must not be overwritten.' }
$diagnosticSha = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or @(& git -C $root status --porcelain).Count) { throw 'Diagnostics require clean committed source.' }
$null = New-Item -ItemType Directory -Path $Workspace, $OutputDirectory -Force
$workload = Join-Path $Workspace 'workload'
$runs = @()
$workers = @()
$state = 'incomplete-native-evidence-not-performance-approval'
$failure = $null
$added = $false
$perfVersion = (& $PerfTool --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Perf executable unavailable.' }
$allowed = Get-Content /proc/self/status | Select-String '^Cpus_allowed_list:\s*([0-9,-]+)$'
if (@($allowed).Count -ne 1) { throw 'CPU permissions unavailable.' }
$cpu = (($allowed.Matches[0].Groups[1].Value -split ',')[0] -split '-')[0]
$manifest = [pscustomobject]@{ Protocol = 'csv-pipe-nativecpu-v1-diagnostic-only'; DiagnosticOnly = $true
    DiagnosticSha = $diagnosticSha; WorkloadSha = $WorkloadSha; PerfVersion = $perfVersion
    Event = 'cpu-clock:u'; Frequency = 199; DurationSeconds = 30; PerfMapEnabled = 1; Cpu = $cpu; Runs = @() }

function Build-Workload([string[]]$Arguments) {
    & dotnet build @Arguments --disable-build-servers -p:UseSharedCompilation=false | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Frozen workload build failed.' }
}

function Start-CaptureProcess([string]$File, [string[]]$Arguments, [string]$Directory) {
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WorkingDirectory = $Directory
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    return [pscustomobject]@{ Process = $process; Out = $process.StandardOutput.ReadToEndAsync(); Error = $process.StandardError.ReadToEndAsync() }
}

function Finish-CaptureProcess($Child, [string]$Name) {
    try {
        if (!$Child.Process.WaitForExit(90000)) { throw 'Native tool timed out.' }
        if ($Child.Process.ExitCode -ne 0) { throw "Native tool failed: $Name (exit $($Child.Process.ExitCode)). See retained stderr." }
    }
    finally {
        if (!$Child.Process.HasExited) { $Child.Process.Kill($true); $Child.Process.WaitForExit() }
        $Child.Out.GetAwaiter().GetResult() | Set-Content -LiteralPath "$Name-stdout.txt"
        $Child.Error.GetAwaiter().GetResult() | Set-Content -LiteralPath "$Name-stderr.txt"
        $Child.Process.Dispose()
    }
}

function Invoke-Perf([string[]]$Arguments, [string]$Name, [string]$Directory) {
    Finish-CaptureProcess (Start-CaptureProcess 'sudo' (@('-n', $PerfTool) + $Arguments) $Directory) $Name
}

function Read-Worker($Worker) {
    $task = $Worker.Process.StandardOutput.ReadLineAsync()
    if (!$task.Wait(180000)) { throw 'Diagnostic worker response timed out.' }
    $line = $task.GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($line)) { throw 'Worker exited without a response.' }
    $line | Add-Content -LiteralPath (Join-Path $Worker.Directory 'worker.ndjson')
    return ($line | ConvertFrom-Json)
}

function Send-Worker($Worker, [string]$Operation, [int]$Repeats = 0, [switch]$NoWait) {
    $Worker.Sequence++
    $command = @{ Id = $Worker.Sequence; Operation = $Operation; Transport = 'Segmented128'; Repeats = $Repeats } | ConvertTo-Json -Compress
    $command | Add-Content -LiteralPath (Join-Path $Worker.Directory 'commands.ndjson')
    $Worker.Process.StandardInput.WriteLine($command)
    $Worker.Process.StandardInput.Flush()
    if ($NoWait) { return }
    $response = Read-Worker $Worker
    Assert-Response $Worker $response $Operation $Repeats
    return $response
}

function Assert-Response($Worker, $Response, [string]$Operation, [int]$Repeats) {
    $kind = switch ($Operation) { 'verify' { 'worker-verified' } 'prepare' { 'worker-prepared' } 'batch' { 'worker-batch' } 'stop' { 'worker-stopped' } }
    if ($Response.Id -ne $Worker.Sequence -or $Response.pid -ne $Worker.Process.Id -or $Response.kind -ne $kind -or
        ($Operation -eq 'batch' -and ($Response.transport -ne 'Segmented128' -or $Response.repeats -ne $Repeats))) {
        throw 'Diagnostic worker response correlation failed.'
    }
}

try {
    & git -C $root worktree add --detach $workload $WorkloadSha
    if ($LASTEXITCODE -ne 0) { throw 'Frozen workload checkout failed.' }
    $added = $true
    $probeRoot = Join-Path $workload 'benchmarks/CsvPipeABProbe'
    . (Join-Path $probeRoot 'control.ps1')
    $parser = Join-Path $workload 'src/HeroParser/bin/Release/net10.0/HeroParser.dll'
    $models = Join-Path $probeRoot 'Models/bin/Release/isolated/CsvPipeABModels.dll'
    $program = Join-Path $probeRoot 'bin/Release/isolated/CsvPipeABProbe.dll'
    Build-Workload @((Join-Path $workload 'src/HeroParser/HeroParser.csproj'), '-c', 'Release', '-f', 'net10.0')
    Build-Workload @((Join-Path $probeRoot 'Models/CsvPipeABModels.csproj'), '-c', 'Release', '-p:AssemblyName=CsvPipeABModels',
        "-p:ParserDll=$parser", '-p:BaseIntermediateOutputPath=obj/isolated/', '-p:OutputPath=bin/Release/isolated/')
    Build-Workload @((Join-Path $probeRoot 'CsvPipeABProbe.csproj'), '-c', 'Release', '-p:SingleModule=true',
        "-p:CandidateModelsDll=$models", '-p:BaseIntermediateOutputPath=obj/isolated/', '-p:OutputPath=bin/Release/isolated/')
    foreach ($side in @('a', 'b')) {
        $directory = Join-Path $OutputDirectory $side
        $null = New-Item -ItemType Directory -Path $directory
        Copy-Item -Path (Join-Path (Split-Path -Parent $program) '*') -Destination $directory -Recurse
        $info = [Diagnostics.ProcessStartInfo]::new('taskset')
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardInput = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($argument in @('--cpu-list', $cpu, 'dotnet', (Join-Path $directory 'CsvPipeABProbe.dll'), '--rows', '2000')) { $info.ArgumentList.Add($argument) }
        $info.Environment['HERO_PARSER_WORKER_REF'] = $WorkloadSha
        $info.Environment['HERO_PARSER_WORKER_PROTOCOL'] = 'csv-pipe-isolated-v4-same-cpu'
        $info.Environment['HERO_PARSER_WORKER_CPU'] = $cpu
        $info.Environment['DOTNET_PerfMapEnabled'] = '1'
        $info.Environment['DOTNET_PerfMapShowOptimizationTiers'] = '1'
        $process = [Diagnostics.Process]::Start($info)
        $worker = [pscustomobject]@{ Process = $process; Directory = $directory; Sequence = 0; Environment = $null
            Errors = $process.StandardError.ReadToEndAsync() }
        $workers += $worker
        $worker.Environment = Read-Worker $worker
        if ($worker.Environment.pid -ne $process.Id) { throw 'Worker PID mismatch.' }
        $verification = Send-Worker $worker 'verify'
        Assert-CsvPipeWorkerVerification $verification
        $null = Send-Worker $worker 'prepare'
    }
    Assert-CsvPipeWorkerEnvironment $workers[0].Environment $workers[1].Environment $WorkloadSha $WorkloadSha
    $warmup = [Diagnostics.Stopwatch]::StartNew()
    $pairs = 0
    do {
        $order = if (($pairs % 2) -eq 0) { @(1, 0) } else { @(0, 1) }
        foreach ($index in $order) { $null = Send-Worker $workers[$index] 'batch' 512 }
        $pairs++
    } while ($pairs -lt 10 -or $warmup.Elapsed.TotalSeconds -lt 10)
    $warmup.Stop()
    foreach ($worker in $workers) {
        $directory = $worker.Directory
        $processId = $worker.Process.Id
        Get-Content -LiteralPath "/proc/$processId/maps" | Set-Content -LiteralPath (Join-Path $directory 'process-maps.txt')
        Get-Content -LiteralPath "/proc/$processId/status" | Set-Content -LiteralPath (Join-Path $directory 'process-status.txt')
        # One long diagnostic batch, not measured A/A pairs; keep the peer idle throughout capture.
        Send-Worker $worker 'batch' 65536 -NoWait
        $raw = Join-Path $directory 'cpu.perf.data'
        $capture = Start-CaptureProcess 'sudo' @('-n', $PerfTool, 'record', '-e', 'cpu-clock:u', '-F', '199',
            '--call-graph', 'dwarf,16384', '--timestamp', '-p', "$processId", '-o', $raw, '--', 'sleep', '30') $directory
        Finish-CaptureProcess $capture (Join-Path $directory 'record')
        $response = Read-Worker $worker
        Assert-Response $worker $response 'batch' 65536
        if ($response.batchMs -lt 32000) { throw 'Diagnostic workload did not span the full capture window.' }
        foreach ($file in @("/tmp/jit-$processId.dump", "/tmp/perf-$processId.map", "/tmp/perfinfo-$processId.map")) {
            if (!(Test-Path -LiteralPath $file)) { throw "Missing runtime native metadata: $(Split-Path -Leaf $file)" }
            Copy-Item -LiteralPath $file -Destination $directory
        }
        $null = Send-Worker $worker 'stop'
        if (!$worker.Process.WaitForExit(10000) -or $worker.Process.ExitCode -ne 0) { throw 'Native worker did not exit cleanly.' }
        $dump = Read-CsvPipeJitDump -Path (Join-Path $directory "jit-$processId.dump") -ExpectedPid $processId
        $dump | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $directory 'jit-methods.json')
        $injected = Join-Path $directory 'cpu.jit.perf.data'
        Invoke-Perf @('inject', '--jit', '-i', $raw, '-o', $injected) (Join-Path $directory 'inject') $directory
        Invoke-Perf @('evlist', '-i', $injected) (Join-Path $directory 'events') $directory
        if (!(Select-String -LiteralPath (Join-Path $directory 'events-stdout.txt') -Pattern '^cpu-clock:u\s*$' -Quiet)) { throw 'Unexpected native sample event.' }
        Invoke-Perf @('script', '-i', $injected, '--show-lost-events') (Join-Path $directory 'stacks') $directory
        if (@(Select-String -Path (Join-Path $directory 'stacks-*.txt'), (Join-Path $directory 'record-stderr.txt') -Pattern '\bLOST\b|lost\s+[1-9]\d*\s+events').Count) {
            throw 'Native trace lost samples; refusing hotspot acceptance.'
        }
        Invoke-Perf @('report', '--stdio', '--no-children', '--percent-limit', '0', '--show-nr-samples', '--show-total-period',
            '--field-separator', '|', '--sort', 'pid,symbol,dso', '-i', $injected) (Join-Path $directory 'exclusive') $directory
        $report = Read-CsvPipePerfReport -Lines (Get-Content -LiteralPath (Join-Path $directory 'exclusive-stdout.txt')) -ExpectedPid $processId
        $assemblyCount = 0
        foreach ($symbol in @($report.Workload | Sort-Object Period -Descending | Select-Object -First 3 -ExpandProperty Symbol -Unique)) {
            $assemblyCount++
            Invoke-Perf @('annotate', '--stdio', '--symbol', $symbol, '-i', $injected) (Join-Path $directory "assembly-$assemblyCount") $directory
            if (!(Select-String -LiteralPath (Join-Path $directory "assembly-$assemblyCount-stdout.txt") -Pattern '\b(ret|mov|cmp|call|jmp|lea)\b' -Quiet)) {
                throw 'Hotspot annotation has no native assembly.'
            }
        }
        Get-ChildItem -Path "/tmp/jitted-$processId-*.so" -ErrorAction SilentlyContinue | Copy-Item -Destination $directory
        & sudo -n chown -R "$((& id -u).Trim()):$((& id -g).Trim())" $directory
        if ($LASTEXITCODE -ne 0) { throw 'Could not retain readable native artifacts.' }
        $runs += [pscustomobject]@{ Environment = $worker.Environment; Batch = $response; WarmupSeconds = $warmup.Elapsed.TotalSeconds
            JitPid = $dump.Pid; JitMethodCount = $dump.Methods.Count; NativeAssemblyCount = $assemblyCount; LostSamples = 0
            Samples = $report.Samples; UnknownFraction = $report.UnknownFraction; Hotspots = $report.Hotspots }
    }
    $manifest.Runs = $runs
    Assert-CsvPipeNativeManifest $manifest
    $state = 'native-evidence-complete-not-performance-approval'
}
catch {
    $failure = $_.Exception.Message
    throw
}
finally {
    foreach ($worker in $workers) {
        try {
            if (!$worker.Process.HasExited) { $worker.Process.Kill($true); $worker.Process.WaitForExit() }
            $worker.Errors.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $worker.Directory 'worker-stderr.txt')
        }
        finally { $worker.Process.Dispose() }
        # Perf creates root-owned files; partial traces must remain uploadable too.
        & sudo -n chown -R "$((& id -u).Trim()):$((& id -g).Trim())" $worker.Directory
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Could not restore native artifact ownership.' }
    }
    $manifest.Runs = $runs
    @{ State = $state; Failure = $failure; Manifest = $manifest } | ConvertTo-Json -Depth 12 |
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'native-summary.json')
    if ($added) {
        if ((Resolve-Path -LiteralPath $workload).Path -ne $workload -or @(& git -C $workload status --porcelain).Count -or $LASTEXITCODE -ne 0) {
            throw 'Retaining frozen worktree: cleanup safety verification failed.'
        }
        & git -C $root worktree remove $workload
        if ($LASTEXITCODE -ne 0) { throw 'Frozen worktree cleanup failed; no forced removal attempted.' }
    }
}
