param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$BaselineSha,
    [Parameter(Mandatory = $true)][string]$Workspace,
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-series',
    [switch]$ControlsOnly,
    [switch]$BiasDiagnostics
)

$ErrorActionPreference = 'Stop'
if ($ControlsOnly -and $BiasDiagnostics) { throw 'Choose controls-only or bias diagnostics, not both.' }
. (Join-Path $PSScriptRoot 'control.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Workspace = [IO.Path]::GetFullPath($Workspace)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$baselineDirectory = Join-Path $Workspace 'baseline'
if (Test-Path -LiteralPath $baselineDirectory) { throw 'Use a fresh workspace for the independent baseline.' }
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
    throw 'Use an empty output directory; previous evidence must not be overwritten.'
}
$null = New-Item -ItemType Directory -Path $Workspace, $OutputDirectory -Force
$candidateSha = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify candidate revision.' }
if (($ControlsOnly -or $BiasDiagnostics) -and $BaselineSha -ne $candidateSha) { throw 'Same-source diagnostics/controls require the candidate SHA as their baseline.' }
$resolvedBaseline = (& git -C $root rev-parse --verify "$BaselineSha^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $resolvedBaseline -ne $BaselineSha) { throw 'Baseline SHA is not an available commit.' }
$dirty = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Candidate tracked files must match the committed source.' }
$pwsh = (Get-Process -Id $PID).Path
$program = Join-Path $PSScriptRoot 'bin/Release/net10.0/CsvPipeABProbe.dll'
$baselineDll = Join-Path $baselineDirectory 'src/HeroParser/bin/Release/net10.0/HeroParser.Baseline.dll'
$controls = @()
$comparisons = @()
$biasRuns = @()
$jitRun = $null
$state = 'incomplete'
$exitCode = 1
$worktreeAdded = $false
$script:fingerprint = $null

function Prepare-Probe([string]$Ref, [string]$Phase, [switch]$FullMatrix) {
    & git -C $baselineDirectory checkout --detach $Ref
    if ($LASTEXITCODE -ne 0) { throw 'Baseline checkout failed.' }
    & dotnet build (Join-Path $baselineDirectory 'src/HeroParser/HeroParser.csproj') -c Release -f net10.0 `
        '-p:AssemblyName=HeroParser.Baseline' '-t:Rebuild' --disable-build-servers | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Independent baseline build failed.' }
    $preflight = Join-Path $OutputDirectory "$Phase-verify.ndjson"
    $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'run.ps1'),
        '-BaselineDll', $baselineDll, '-BaselineRef', $Ref, '-CandidateRef', $candidateSha,
        '-VerifyOnly', '-DisableBuildServers', '-Rows', '2000', '-Output', $preflight)
    if (!$FullMatrix) { $arguments += @('-Scenario', 'Plain') }
    & $pwsh @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Correctness preflight failed: $Phase." }
    $records = @(Get-Content -LiteralPath $preflight | ForEach-Object { $_ | ConvertFrom-Json })
    $expected = if ($FullMatrix) { 36 } else { 9 }
    if (@($records | Where-Object kind -eq 'verified').Count -ne $expected -or
        @($records | Where-Object kind -eq 'invalid').Count -ne 0 -or
        @($records | Where-Object kind -eq 'complete').Count -ne 1 -or $records[-1].invalidCases -ne 0) {
        throw "Incomplete correctness matrix: $Phase."
    }
    $env:HERO_PARSER_AB_BASELINE_REF = $Ref
    $env:HERO_PARSER_AB_CANDIDATE_REF = $candidateSha
    Start-Sleep -Seconds 10
}

function Measure-Series([string]$Phase, [switch]$Control) {
    $runs = @()
    $runCount = if ($ControlsOnly) { 3 } else { 2 }
    for ($run = 1; $run -le $runCount; $run++) {
        $log = Join-Path $OutputDirectory "$Phase-$run.ndjson"
        & dotnet $program --rows 2000 --pairs 30 --warmup-pairs 10 --min-sample-ms 100 `
            --scenario Plain --path Generated | Tee-Object -FilePath $log | Out-Host
        if ($LASTEXITCODE -ne 0) {
            if (!$ControlsOnly) { throw "Measurement failed: $Phase run $run." }
            $runs += [pscustomobject]@{ Phase = $Phase; Run = $run; Valid = $false; Stable = $false; Cases = @(); Error = 'Measurement process failed.' }
            continue
        }
        $records = @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json })
        $environment = @($records | Where-Object kind -eq 'environment')
        $expectedBaseline = if ($Control) { $candidateSha } else { $BaselineSha }
        if ($environment.Count -ne 1 -or $environment[0].candidateRef -ne $candidateSha -or
            $environment[0].baselineRef -ne $expectedBaseline) {
            throw 'Measurement environment or candidate source is missing.'
        }
        $fingerprint = $environment[0] | Select-Object runtime, os, processors, serverGc, affinity,
            candidateRef, candidateMvid, candidateModelsMvid, protocol | ConvertTo-Json -Compress
        if ($null -ne $script:fingerprint -and $script:fingerprint -ne $fingerprint) {
            throw 'Candidate modules or runtime settings changed during the paired series.'
        }
        $script:fingerprint = $fingerprint
        if ($Control) {
            try {
                $result = Get-CsvPipeControlResult -Records $records
                $runs += [pscustomobject]@{ Phase = $Phase; Run = $run; Valid = $true; Stable = $result.Stable; Cases = $result.Cases }
            }
            catch {
                if (!$ControlsOnly) { throw }
                $runs += [pscustomobject]@{ Phase = $Phase; Run = $run; Valid = $false; Stable = $false; Cases = @(); Error = $_.Exception.Message }
            }
        }
        else {
            $null = Get-CsvPipeTimingResult -Records $records
            if (@($records | Where-Object kind -eq 'summary').Count -ne 3 -or
                @($records | Where-Object kind -eq 'pair').Count -ne 90 -or
                $records[-1].kind -ne 'complete' -or $records[-1].invalidCases -ne 0) {
                throw 'Incomplete A/B measurements.'
            }
            $runs += [pscustomobject]@{ Phase = $Phase; Run = $run; Cases = @($records | Where-Object kind -eq 'summary') }
        }
    }
    return $runs
}

function Invoke-BiasProbe([string]$Mode, [int]$Cycle, [switch]$Verify, [switch]$Jit) {
    $name = if ($Verify) { "bias-$Mode-verify" } elseif ($Jit) { 'bias-independent-jit' } else { "bias-$Cycle-$Mode" }
    $log = Join-Path $OutputDirectory "$name.ndjson"
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in @($program, '--rows', '2000', '--pairs', '30', '--warmup-pairs', '10',
        '--min-sample-ms', '100', '--bias-mode', $Mode)) { $info.ArgumentList.Add($argument) }
    if ($Verify) { $info.ArgumentList.Add('--verify-only') }
    else {
        foreach ($argument in @('--scenario', 'Plain', '--path', 'Generated')) { $info.ArgumentList.Add($argument) }
    }
    $jitFile = Join-Path $OutputDirectory 'bias-independent-jit.asm'
    if ($Jit) {
        $info.Environment['DOTNET_JitDisasm'] = '*'
        $info.Environment['DOTNET_JitDisasmAssemblies'] = 'HeroParser;HeroParser.Baseline;CsvPipeABModels.Candidate;CsvPipeABModels.Baseline;CsvPipeABProbe'
        $info.Environment['DOTNET_JitDisasmDiffable'] = '1'
        $info.Environment['DOTNET_JitStdOutFile'] = $jitFile
    }
    $child = [Diagnostics.Process]::Start($info)
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    try {
        if (!$child.WaitForExit(300000) -or $child.ExitCode -ne 0) { throw 'Diagnostic process failed or timed out.' }
        $records = @($stdout.GetAwaiter().GetResult() -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
        $environment = @($records | Where-Object kind -eq 'environment')
        if ($environment.Count -ne 1 -or $environment[0].baselineRef -ne $candidateSha -or
            $environment[0].candidateRef -ne $candidateSha -or $environment[0].BiasMode -ne $Mode -or
            $environment[0].protocol -ne 'csv-pipe-bias-v1-diagnostic-only' -or $environment[0].diagnosticOnly -ne $true) {
            throw 'Wrong diagnostic source or mode.'
        }
        $fingerprint = $environment[0] | Select-Object runtime, os, processors, serverGc, affinity,
            baselineRef, baselineMvid, baselineModelsMvid, candidateRef, candidateMvid, candidateModelsMvid |
            ConvertTo-Json -Compress
        if ($null -ne $script:fingerprint -and $script:fingerprint -ne $fingerprint) { throw 'Diagnostic module/runtime fingerprint changed.' }
        $script:fingerprint = $fingerprint
        if ($Verify) {
            if (@($records | Where-Object kind -eq 'verified').Count -ne 36 -or
                @($records | Where-Object kind -eq 'invalid').Count -ne 0 -or
                @($records | Where-Object kind -eq 'complete').Count -ne 1 -or
                $records[-1].kind -ne 'complete' -or $records[-1].invalidCases -ne 0) { throw 'Incomplete diagnostic correctness matrix.' }
            Write-Host "Verified all 36 cases: $Mode"
            return
        }
        $result = Get-CsvPipeBiasResult -Records $records -Mode $Mode -JitDiagnostic:$Jit
        if ($Jit -and (!(Test-Path -LiteralPath $jitFile) -or
            !(Select-String -LiteralPath $jitFile -Pattern 'Assembly listing for method' -Quiet))) { throw 'No JIT assembly was produced.' }
        Write-Host "Completed diagnostic cycle $Cycle mode $Mode (JIT=$Jit); within original bounds: $($result.Stable)"
        return [pscustomobject]@{ Mode = $Mode; Cycle = $Cycle; Valid = $true; Jit = [bool]$Jit;
            WithinOriginalBounds = $result.Stable; Environment = $environment[0]; Cases = $result.Cases }
    }
    catch {
        if ($Verify) { throw }
        Write-Host "Invalid diagnostic cycle $Cycle mode ${Mode}: $($_.Exception.Message)"
        return [pscustomobject]@{ Mode = $Mode; Cycle = $Cycle; Valid = $false; Jit = [bool]$Jit; Error = $_.Exception.Message; Cases = @() }
    }
    finally {
        if (!$child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath $log
        $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $OutputDirectory "$name-stderr.txt")
        $child.Dispose()
    }
}

$oldBaseline = $env:HERO_PARSER_AB_BASELINE_REF
$oldCandidate = $env:HERO_PARSER_AB_CANDIDATE_REF
try {
    & git -C $root worktree add --detach $baselineDirectory $candidateSha
    if ($LASTEXITCODE -ne 0) { throw 'Could not create independent baseline worktree.' }
    $worktreeAdded = $true
    $firstPhase = if ($BiasDiagnostics) { 'bias' } elseif ($ControlsOnly) { 'aa-only' } else { 'aa-before' }
    Prepare-Probe -Ref $candidateSha -Phase $firstPhase -FullMatrix
    if ($BiasDiagnostics) {
        foreach ($arguments in @(@('--bias-mode', 'Other'), @('--bias-mode'),
            @('--bias-mode', 'Independent'), @('--bias-mode', 'Independent', '--scenario', 'Plain', '--path', 'Decode'),
            @('--bias-mode', 'Independent', '--profile-side', 'Baseline', '--profile-transport', 'Contiguous', '--profile-ready-file', 'unused'))) {
            $errorOutput = & dotnet $program @arguments 2>&1 | Out-String
            $errorOutput | Add-Content -LiteralPath (Join-Path $OutputDirectory 'bias-argument-validation.txt')
            if ($LASTEXITCODE -ne 2 -or $errorOutput -notmatch 'bias-mode|bias diagnostic|Bias diagnostics') {
                throw 'Invalid diagnostic arguments were not rejected.'
            }
        }
        $modes = @('Independent', 'Swapped', 'BaselineSelf', 'CandidateSelf')
        foreach ($mode in $modes) { Invoke-BiasProbe -Mode $mode -Cycle 0 -Verify }
        for ($cycle = 1; $cycle -le 3; $cycle++) {
            $order = if ($cycle -eq 2) { @('CandidateSelf', 'BaselineSelf', 'Swapped', 'Independent') } else { $modes }
            foreach ($mode in $order) { $biasRuns += Invoke-BiasProbe -Mode $mode -Cycle $cycle }
        }
        $jitRun = Invoke-BiasProbe -Mode 'Independent' -Cycle 0 -Jit
        $state = if (@($biasRuns | Where-Object { !$_.Valid }).Count -ne 0 -or !$jitRun.Valid) { 'invalid-bias-diagnostics-no-ab' }
            else { 'bias-diagnostics-complete-not-performance-approval' }
        $exitCode = if ($state -eq 'bias-diagnostics-complete-not-performance-approval') { 0 } else { 1 }
    }
    else {
        $controls += @(Measure-Series -Phase $firstPhase -Control)
        if ($ControlsOnly) {
            $state = if (@($controls | Where-Object { !$_.Valid }).Count -ne 0) { 'invalid-controls-only-no-ab' }
                elseif (@($controls | Where-Object { !$_.Stable }).Count -ne 0) { 'unstable-controls-only-no-ab' }
                else { 'stable-controls-only-no-ab' }
            $exitCode = if ($state -eq 'stable-controls-only-no-ab') { 0 } else { 2 }
        }
        elseif (@($controls | Where-Object { !$_.Stable }).Count -ne 0) {
            $state = 'unstable-before-ab-skipped'
            $exitCode = 2
        }
        else {
            Prepare-Probe -Ref $BaselineSha -Phase 'ab'
            $comparisons = @(Measure-Series -Phase 'ab')
            Prepare-Probe -Ref $candidateSha -Phase 'aa-after' -FullMatrix
            $controls += @(Measure-Series -Phase 'aa-after' -Control)
            if (@($controls | Where-Object { !$_.Stable }).Count -ne 0) {
                $state = 'unstable-after-ab-inconclusive'
                $exitCode = 2
            }
            else {
                $state = 'stable-controls-review-ab'
                $exitCode = 0
            }
        }
    }
}
finally {
    $env:HERO_PARSER_AB_BASELINE_REF = $oldBaseline
    $env:HERO_PARSER_AB_CANDIDATE_REF = $oldCandidate
    [pscustomobject]@{
        State = $state; BaselineSha = $BaselineSha; CandidateSha = $candidateSha
        Protocol = if ($BiasDiagnostics) { 'csv-pipe-bias-v1-diagnostic-only' } else { 'csv-pipe-v2-post-warmup-calibration' }
        ControlsOnly = [bool]$ControlsOnly; BiasDiagnostics = [bool]$BiasDiagnostics
        MinWarmupSeconds = 10; CalibrationHeadroom = 1.25; MaxCalibrationRepeats = 65536
        Rows = 2000; Pairs = 30; WarmupPairs = 10; MinSampleMs = 100
        Affinity = $env:HERO_PARSER_AB_AFFINITY
        MedianBounds = @(.95, 1.05); P10Minimum = .90; P90Maximum = 1.10
        Controls = @($controls); Comparisons = @($comparisons); BiasRuns = @($biasRuns); JitRun = $jitRun
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'series-summary.json')
    if ($env:GITHUB_STEP_SUMMARY) {
        $summary = @(
            $(if ($BiasDiagnostics) { '## CSV Pipe Same-Source Bias Diagnostics' } else { '## CSV Pipe Paired Experiment' })
            "State: **$state**."
            "Baseline: ``$BaselineSha``. Candidate: ``$candidateSha``."
            'Correctness and stable controls are not performance approval or authorization to merge.'
            'Scope: full candidate/candidate correctness; Plain Generated timing only.'
            'Timing settings: 2000 rows, 30 pairs, at least 10 warmup pairs and 10 seconds, then final calibration.'
            '100 ms measured-batch floor; final calibration targets 125 ms. Every actual batch is validated.'
            "Controls-only: **$([bool]$ControlsOnly)**. Bias diagnostics: **$([bool]$BiasDiagnostics)**. Neither mode enters production A/B."
            ''
            '### A/A Controls'
            'All runs must have median [0.95, 1.05], p10 >= 0.90 and p90 <= 1.10.'
            '| Phase | Run | Transport | Median | p10 | p90 | Min A Batch (ms) | Min B Batch (ms) | Stable |'
            '|---|---:|---|---:|---:|---:|---:|---:|---|'
        )
        foreach ($control in $controls) {
            foreach ($case in $control.Cases) {
                $summary += '| {0} | {1} | {2} | {3:F4} | {4:F4} | {5:F4} | {6:F2} | {7:F2} | {8} |' -f
                    $control.Phase, $control.Run, $case.Transport, $case.Median, $case.P10, $case.P90,
                    $case.MinBaselineBatchMs, $case.MinCandidateBatchMs, $case.Stable
            }
            if (!$control.Valid) { $summary += "Invalid run $($control.Run): $($control.Error)" }
        }
        $summary += @('', '### A/B Measurements',
            'Ratios are candidate/baseline elapsed time. Above 1 is slower; review both runs and all controls.',
            'Allocation delta is per complete 2000-row read, not per row.',
            '| Run | Transport | Median | p10 | p90 | Allocation Delta (B) |',
            '|---:|---|---:|---:|---:|---:|')
        foreach ($comparison in $comparisons) {
            foreach ($case in $comparison.Cases) {
                $summary += '| {0} | {1} | {2:F4} | {3:F4} | {4:F4} | {5:F2} |' -f
                    $comparison.Run, $case.transport, $case.medianRatio, $case.p10Ratio, $case.p90Ratio,
                    ($case.candidateBytes - $case.baselineBytes)
            }
        }
        if ($BiasDiagnostics) {
            $summary += @('', '### Bias Diagnostics (Not Acceptance)',
                'Fixed three cycles of four fresh-process modes. Reverse mode order in cycle 2; never retry until stable.',
                'Self comparisons route both logical sides through one module/consumer. Swapped reverses module routing, not assembly names.',
                'Original bounds are reported, not waived. These diagnostic records cannot satisfy normal A/A or A/B acceptance.',
                '| Cycle | Mode | Transport | Median | p10 | p90 | Candidate First | Baseline First | First Half | Second Half | Within Bounds |',
                '|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---|')
            foreach ($run in $biasRuns) {
                foreach ($case in $run.Cases) {
                    $summary += '| {0} | {1} | {2} | {3:F4} | {4:F4} | {5:F4} | {6:F4} | {7:F4} | {8:F4} | {9:F4} | {10} |' -f
                        $run.Cycle, $run.Mode, $case.Transport, $case.Median, $case.P10, $case.P90,
                        $case.CandidateFirstMedian, $case.BaselineFirstMedian, $case.FirstHalfMedian, $case.SecondHalfMedian, $case.Stable
                }
                if (!$run.Valid) { $summary += "Invalid cycle $($run.Cycle) mode $($run.Mode): $($run.Error)" }
            }
            $summary += "Separate JIT process valid: $($jitRun.Valid). Its elapsed times are not uninstrumented timing evidence."
        }
        $summary += @('', 'Raw pairs, module IDs, runner context and gate outcomes are retained in the workflow artifact.')
        $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
    if ($worktreeAdded) {
        $resolved = (Resolve-Path -LiteralPath $baselineDirectory).Path
        $dirty = @(& git -C $resolved status --porcelain)
        if ($resolved -ne $baselineDirectory -or $LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) {
            throw 'Baseline cleanup safety check failed; retaining the worktree.'
        }
        & git -C $root worktree remove $resolved
        if ($LASTEXITCODE -ne 0) { throw 'Baseline cleanup failed; no forced deletion attempted.' }
    }
}
exit $exitCode
