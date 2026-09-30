param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceSha,
    [Parameter(Mandatory = $true)][string]$Workspace,
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-series',
    [switch]$PinSameCpu
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'control.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Workspace = [IO.Path]::GetFullPath($Workspace)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((& git -C $root rev-parse HEAD).Trim() -ne $SourceSha -or $LASTEXITCODE -ne 0) { throw 'Controls require the current committed source.' }
if (@(& git -C $root status --porcelain).Count -ne 0 -or $LASTEXITCODE -ne 0) { throw 'Tracked source must be clean.' }
if (Test-Path -LiteralPath $Workspace) { throw 'Use a fresh worker workspace.' }
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
    throw 'Previous evidence must not be overwritten.'
}
$null = New-Item -ItemType Directory -Path $Workspace, $OutputDirectory -Force
$workerDll = Join-Path $PSScriptRoot 'bin/Release/isolated/CsvPipeABProbe.dll'
$modelDll = Join-Path $PSScriptRoot 'Models/bin/Release/isolated/CsvPipeABModels.dll'
$runs = @()
$state = 'incomplete-isolated-controls-no-ab'
$exitCode = 1
$script:workerFingerprint = $null
$protocol = if ($PinSameCpu) { 'csv-pipe-isolated-v4-same-cpu' } else { 'csv-pipe-isolated-v3' }
$pinnedCpu = $null
if ($PinSameCpu) {
    if (!$IsLinux -or !(Get-Command taskset -ErrorAction SilentlyContinue)) { throw 'Pinned controls require Linux taskset.' }
    $allowed = Get-Content -LiteralPath '/proc/self/status' | Select-String '^Cpus_allowed_list:\s*([0-9,-]+)$'
    if (@($allowed).Count -ne 1) { throw 'Could not read coordinator CPU permissions.' }
    $pinnedCpu = (($allowed.Matches[0].Groups[1].Value -split ',')[0] -split '-')[0]
    if ($pinnedCpu -notmatch '^\d+$') { throw 'Could not select an allowed CPU.' }
    Write-Host "Pinned control variable: both workers start on allowed CPU $pinnedCpu."
}

function Build-Probe([string[]]$Arguments) {
    & dotnet build @Arguments --disable-build-servers -p:UseSharedCompilation=false | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Isolated probe build failed.' }
}

function Start-Worker([string]$Directory, [string]$Name) {
    $info = [Diagnostics.ProcessStartInfo]::new($(if ($PinSameCpu) { 'taskset' } else { 'dotnet' }))
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if ($PinSameCpu) {
        foreach ($argument in @('--cpu-list', $pinnedCpu, 'dotnet')) { $info.ArgumentList.Add($argument) }
    }
    $info.ArgumentList.Add((Join-Path $Directory 'CsvPipeABProbe.dll'))
    $info.ArgumentList.Add('--rows')
    $info.ArgumentList.Add('2000')
    $info.Environment['HERO_PARSER_WORKER_REF'] = $SourceSha
    $info.Environment['HERO_PARSER_WORKER_PROTOCOL'] = $protocol
    if ($PinSameCpu) { $info.Environment['HERO_PARSER_WORKER_CPU'] = $pinnedCpu }
    $process = [Diagnostics.Process]::Start($info)
    $worker = [pscustomobject]@{ Process = $process; Sequence = 0; Name = $Name; Environment = $null
        ErrorTask = $process.StandardError.ReadToEndAsync()
        Transcript = Join-Path $OutputDirectory "$Name-worker.ndjson"
        Commands = Join-Path $OutputDirectory "$Name-commands.ndjson" }
    try {
        $worker.Environment = Read-Worker $worker
        if ($worker.Environment.kind -ne 'worker-environment' -or $worker.Environment.pid -ne $process.Id) {
            throw 'Worker did not identify its process.'
        }
        return $worker
    }
    catch { Stop-Worker $worker; throw }
}

function Read-Worker($Worker) {
    $task = $Worker.Process.StandardOutput.ReadLineAsync()
    if (!$task.Wait(120000)) { throw 'Worker response timed out.' }
    $line = $task.GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($line)) { throw 'Worker exited without a response.' }
    $line | Add-Content -LiteralPath $Worker.Transcript
    return ($line | ConvertFrom-Json)
}

function Request-Worker($Worker, [string]$Operation, [string]$Transport = '', [int]$Repeats = 0) {
    $Worker.Sequence++
    $command = @{ Id = $Worker.Sequence; Operation = $Operation; Transport = $Transport; Repeats = $Repeats } | ConvertTo-Json -Compress
    $command | Add-Content -LiteralPath $Worker.Commands
    $Worker.Process.StandardInput.WriteLine($command)
    $Worker.Process.StandardInput.Flush()
    $result = Read-Worker $Worker
    $kind = switch ($Operation) { 'verify' { 'worker-verified' } 'prepare' { 'worker-prepared' } 'batch' { 'worker-batch' } 'stop' { 'worker-stopped' } }
    if ($result.kind -ne $kind -or $result.Id -ne $Worker.Sequence -or $result.pid -ne $Worker.Process.Id) {
        throw 'Worker response does not match its command.'
    }
    if ($Operation -in @('prepare', 'batch') -and $result.transport -ne $Transport) { throw 'Worker measured a different transport.' }
    if ($Operation -eq 'batch' -and $result.repeats -ne $Repeats) { throw 'Worker measured a different repeat count.' }
    return $result
}

function Stop-Worker($Worker) {
    if ($null -eq $Worker) { return }
    try {
        if (!$Worker.Process.HasExited) { $Worker.Process.Kill($true); $Worker.Process.WaitForExit() }
        $Worker.ErrorTask.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $OutputDirectory "$($Worker.Name)-stderr.txt")
    }
    finally { $Worker.Process.Dispose() }
}

function Measure-Pair($A, $B, [string]$Transport, [int]$Repeats, [bool]$CandidateFirst) {
    # Wait for the first response before issuing the second command: no competing timed workloads.
    if ($CandidateFirst) { $b = Request-Worker $B 'batch' $Transport $Repeats; $a = Request-Worker $A 'batch' $Transport $Repeats }
    else { $a = Request-Worker $A 'batch' $Transport $Repeats; $b = Request-Worker $B 'batch' $Transport $Repeats }
    return [pscustomobject]@{ A = $a; B = $b }
}

function Calibrate($A, $B, [string]$Transport, [int]$Repeats, [double]$Target, [string]$Stage) {
    for ($attempt = 0; ; $attempt++) {
        $pair = Measure-Pair $A $B $Transport $Repeats (($attempt % 2) -eq 0)
        if ([Math]::Min($pair.A.batchMs, $pair.B.batchMs) -ge $Target) {
            Write-Record @{ kind = 'calibration'; scenario = 'Plain'; transport = $Transport; path = 'Generated'
                stage = $Stage; repeats = $Repeats; targetBatchMs = $Target
                baselineBatchMs = $pair.A.batchMs; candidateBatchMs = $pair.B.batchMs }
            return $Repeats
        }
        if ($Repeats -eq 65536) { throw 'Calibration cap reached without meeting the batch target.' }
        $Repeats = [Math]::Min($Repeats * 2, 65536)
    }
}

function Write-Record($Record) {
    $script:records.Add([pscustomobject]$Record)
    $Record | ConvertTo-Json -Depth 12 -Compress | Add-Content -LiteralPath $script:log
}

function Percentile([double[]]$Values, [double]$P) {
    $sorted = @($Values | Sort-Object)
    $index = ($sorted.Count - 1) * $P
    $lower = [int][Math]::Floor($index)
    return $sorted[$lower] + ($sorted[[int][Math]::Ceiling($index)] - $sorted[$lower]) * ($index - $lower)
}

try {
    Build-Probe @((Join-Path $root 'src/HeroParser/HeroParser.csproj'), '-c', 'Release', '-f', 'net10.0')
    Build-Probe @((Join-Path $PSScriptRoot 'Models/CsvPipeABModels.csproj'), '-c', 'Release',
        '-p:AssemblyName=CsvPipeABModels', "-p:ParserDll=$(Join-Path $root 'src/HeroParser/bin/Release/net10.0/HeroParser.dll')",
        '-p:BaseIntermediateOutputPath=obj/isolated/', '-p:OutputPath=bin/Release/isolated/')
    Build-Probe @((Join-Path $PSScriptRoot 'CsvPipeABProbe.csproj'), '-c', 'Release', '-p:SingleModule=true',
        "-p:CandidateModelsDll=$modelDll", '-p:BaseIntermediateOutputPath=obj/isolated/', '-p:OutputPath=bin/Release/isolated/')
    foreach ($side in @('a', 'b')) {
        $directory = Join-Path $Workspace $side
        $null = New-Item -ItemType Directory -Path $directory
        Copy-Item -Path (Join-Path (Split-Path -Parent $workerDll) '*') -Destination $directory -Recurse
    }
    # Exactly three fresh process pairs, including reverse launch order, regardless of valid-but-unstable results.
    for ($cycle = 1; $cycle -le 3; $cycle++) {
        $a = $null; $b = $null
        $script:log = Join-Path $OutputDirectory "isolated-aa-$cycle.ndjson"
        $script:records = [Collections.Generic.List[object]]::new()
        try {
            if ($cycle -eq 2) {
                $b = Start-Worker (Join-Path $Workspace 'b') "$cycle-b"
                $a = Start-Worker (Join-Path $Workspace 'a') "$cycle-a"
            }
            else {
                $a = Start-Worker (Join-Path $Workspace 'a') "$cycle-a"
                $b = Start-Worker (Join-Path $Workspace 'b') "$cycle-b"
            }
            Write-Record @{ kind = 'environment'; protocol = $protocol; baselineRef = $SourceSha; candidateRef = $SourceSha
                Rows = 2000; Pairs = 30; WarmupPairs = 10; MinSampleMs = 100; Scenario = 'Plain'; Path = 'Generated'
                minWarmupSeconds = 10; calibrationHeadroom = 1.25; maxCalibrationRepeats = 65536
                baselineWorker = $a.Environment; candidateWorker = $b.Environment; cycle = $cycle; candidateLaunchedFirst = ($cycle -eq 2) }
            $null = Assert-CsvPipeWorkerEnvironment $a.Environment $b.Environment $SourceSha $SourceSha
            $fingerprint = $a.Environment | Select-Object sourceRef, runtime, os, processors, serverGc, affinity,
                protocol, pinnedCpu, allowedCpus, parserName, modelsName, parserMvid, modelsMvid, parserHash, modelsHash, consumerHash | ConvertTo-Json -Compress
            if ($null -ne $script:workerFingerprint -and $script:workerFingerprint -ne $fingerprint) {
                throw 'Worker artifacts or runtime settings changed between fixed control cycles.'
            }
            $script:workerFingerprint = $fingerprint
            foreach ($worker in @($a, $b)) {
                $verification = Request-Worker $worker 'verify'
                Assert-CsvPipeWorkerVerification $verification
                Write-Record @{ kind = 'worker-verification'; response = $verification }
            }
            foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
                $null = Request-Worker $a 'prepare' $transport
                $null = Request-Worker $b 'prepare' $transport
                Write-Record @{ kind = 'verified'; scenario = 'Plain'; transport = $transport; path = 'Generated'; Rows = 2000 }
                $repeats = Calibrate $a $b $transport 1 100 'pilot'
                $warmup = [Diagnostics.Stopwatch]::StartNew()
                $warmed = 0
                do {
                    $null = Measure-Pair $a $b $transport $repeats (($warmed % 2) -eq 0)
                    $warmed++
                } while ($warmed -lt 10 -or $warmup.Elapsed.TotalSeconds -lt 10)
                $warmup.Stop()
                Write-Record @{ kind = 'warmup'; scenario = 'Plain'; transport = $transport; path = 'Generated'
                    pairs = $warmed; elapsedSeconds = $warmup.Elapsed.TotalSeconds }
                $repeats = Calibrate $a $b $transport $repeats 125 'post-warmup'
                $samples = @()
                for ($i = 0; $i -lt 30; $i++) {
                    $pair = Measure-Pair $a $b $transport $repeats (($i % 2) -eq 0)
                    $sample = @{ kind = 'pair'; scenario = 'Plain'; transport = $transport; path = 'Generated'
                        pair = $i; candidateFirst = (($i % 2) -eq 0); repeats = $repeats
                        baseline = $pair.A.measurement; candidate = $pair.B.measurement
                        baselineBatchMs = $pair.A.batchMs; candidateBatchMs = $pair.B.batchMs
                        baselineResponse = $pair.A; candidateResponse = $pair.B
                        ratio = $pair.B.measurement.Milliseconds / $pair.A.measurement.Milliseconds }
                    $samples += [pscustomobject]$sample
                    Write-Record $sample
                }
                Write-Record @{ kind = 'summary'; scenario = 'Plain'; transport = $transport; path = 'Generated'
                    Rows = 2000; Pairs = 30; repeats = $repeats
                    medianRatio = (Percentile $samples.ratio .5); p10Ratio = (Percentile $samples.ratio .1); p90Ratio = (Percentile $samples.ratio .9)
                    baselineMinBatchMs = ($samples.baselineBatchMs | Measure-Object -Minimum).Minimum
                    candidateMinBatchMs = ($samples.candidateBatchMs | Measure-Object -Minimum).Minimum }
            }
            foreach ($worker in @($a, $b)) {
                $null = Request-Worker $worker 'stop'
                if (!$worker.Process.WaitForExit(10000) -or $worker.Process.ExitCode -ne 0) { throw 'Worker did not exit cleanly.' }
            }
            Write-Record @{ kind = 'complete'; invalidCases = 0 }
            $result = Get-CsvPipeIsolatedControlResult -Records $script:records.ToArray()
            $runs += [pscustomobject]@{ Run = $cycle; Valid = $true; Stable = $result.Stable; Cases = $result.Cases }
            Write-Host "Isolated A/A $cycle valid; within original bounds: $($result.Stable)"
        }
        catch {
            $runs += [pscustomobject]@{ Run = $cycle; Valid = $false; Stable = $false; Cases = @(); Error = $_.Exception.Message }
            Write-Host "Invalid isolated A/A ${cycle}: $($_.Exception.Message)"
        }
        finally { Stop-Worker $a; Stop-Worker $b }
    }
    $state = if (@($runs | Where-Object { !$_.Valid }).Count -ne 0) { 'invalid-isolated-controls-no-ab' }
        elseif (@($runs | Where-Object { !$_.Stable }).Count -ne 0) { 'unstable-isolated-controls-no-ab' }
        else { 'stable-isolated-controls-no-ab' }
    $exitCode = if ($state -eq 'stable-isolated-controls-no-ab') { 0 } else { 2 }
}
finally {
    @{ Protocol = $protocol; SourceSha = $SourceSha; PinnedCpu = $pinnedCpu; State = $state; Controls = $runs; Comparisons = @() } |
        ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'series-summary.json')
}
exit $exitCode
