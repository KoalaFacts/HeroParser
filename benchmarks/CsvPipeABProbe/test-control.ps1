$ErrorActionPreference = 'Stop'
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -ne 0) { throw "Invalid PowerShell syntax in $($file.Name): $errors" }
}
Write-Host 'PASS: all probe PowerShell scripts parse successfully'
. (Join-Path $PSScriptRoot 'control.ps1')

function New-ControlRecords {
    $records = @(
        [pscustomobject]@{
            kind = 'environment'; baselineRef = '1' * 40; candidateRef = '1' * 40
            baselineMvid = [guid]::NewGuid(); candidateMvid = [guid]::NewGuid()
            baselineModelsMvid = [guid]::NewGuid(); candidateModelsMvid = [guid]::NewGuid()
            Rows = 2000; Pairs = 30; WarmupPairs = 10; MinSampleMs = 100
            Scenario = 'Plain'; Path = 'Generated'
            protocol = 'csv-pipe-v2-post-warmup-calibration'
            BiasMode = $null; diagnosticOnly = $false
            logicalBaselineModule = 'Baseline'; logicalCandidateModule = 'Candidate'
            jitDisasm = $null; jitDisasmAssemblies = $null
            minWarmupSeconds = 10; calibrationHeadroom = 1.25; maxCalibrationRepeats = 65536
        }
    )
    foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
        $records += [pscustomobject]@{ kind = 'verified'; scenario = 'Plain'; transport = $transport; path = 'Generated'; Rows = 2000 }
        $records += [pscustomobject]@{
            kind = 'calibration'; scenario = 'Plain'; transport = $transport; path = 'Generated'
            stage = 'pilot'; repeats = 100; targetBatchMs = 100; baselineBatchMs = 100; candidateBatchMs = 100
        }
        $records += [pscustomobject]@{
            kind = 'warmup'; scenario = 'Plain'; transport = $transport; path = 'Generated'
            pairs = 10; elapsedSeconds = 10
        }
        $records += [pscustomobject]@{
            kind = 'calibration'; scenario = 'Plain'; transport = $transport; path = 'Generated'
            stage = 'post-warmup'; repeats = 100; targetBatchMs = 125; baselineBatchMs = 125; candidateBatchMs = 125
        }
        for ($i = 0; $i -lt 30; $i++) {
            $records += [pscustomobject]@{
                kind = 'pair'; scenario = 'Plain'; transport = $transport; path = 'Generated'
                pair = $i; candidateFirst = ($i % 2) -eq 0; repeats = 100; ratio = 1.0
                baseline = [pscustomobject]@{ Milliseconds = 1.25 }
                candidate = [pscustomobject]@{ Milliseconds = 1.25 }
                baselineBatchMs = 125; candidateBatchMs = 125
            }
        }
        $records += [pscustomobject]@{
            kind = 'summary'; scenario = 'Plain'; transport = $transport; path = 'Generated'
            Rows = 2000; Pairs = 30; repeats = 100; medianRatio = 1.0; p10Ratio = 1.0; p90Ratio = 1.0
            baselineMinBatchMs = 125; candidateMinBatchMs = 125
        }
    }
    $records += [pscustomobject]@{ kind = 'complete'; invalidCases = 0 }
    return $records
}

function Assert-Rejected([string]$Name, [scriptblock]$Mutate) {
    $records = New-ControlRecords
    $records = & $Mutate $records
    $rejected = $false
    try { $null = Get-CsvPipeControlResult -Records $records }
    catch { $rejected = $true }
    if (!$rejected) { throw "Gate accepted malformed evidence: $Name." }
    Write-Host "PASS: rejects $Name"
}

if (!(Get-CsvPipeControlResult -Records (New-ControlRecords)).Stable) { throw 'Valid controls were rejected.' }
Write-Host 'PASS: accepts stable complete controls'
Assert-Rejected 'missing completion' { param($r) $r | Where-Object kind -ne 'complete' }
Assert-Rejected 'invalid case count' { param($r) $r[-1].invalidCases = 1; $r }
Assert-Rejected 'different source revision' { param($r) $r[0].candidateRef = '2' * 40; $r }
Assert-Rejected 'same parser module' { param($r) $r[0].candidateMvid = $r[0].baselineMvid; $r }
Assert-Rejected 'same model module' { param($r) $r[0].candidateModelsMvid = $r[0].baselineModelsMvid; $r }
Assert-Rejected 'different settings' { param($r) $r[0].MinSampleMs = 50; $r }
Assert-Rejected 'missing transport' { param($r) $r | Where-Object transport -ne 'Stream4096' }
Assert-Rejected 'duplicate transport' { param($r) ($r | Where-Object transport -eq 'Stream4096') | ForEach-Object { $_.transport = 'Contiguous' }; $r }
Assert-Rejected 'missing measured pair' { param($r) $r | Where-Object { $_.kind -ne 'pair' -or $_.pair -ne 29 } }
Assert-Rejected 'duplicate pair index' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).pair = 1; $r }
Assert-Rejected 'nonfinite time' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baseline.Milliseconds = [double]::NaN; $r }
Assert-Rejected 'wrong ratio' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).ratio = 2.0; $r }
Assert-Rejected 'dishonest summary' { param($r) ($r | Where-Object kind -eq 'summary' | Select-Object -First 1).medianRatio = 0.5; $r }
Assert-Rejected 'wrong consumer' { param($r) ($r | Where-Object kind -eq 'verified' | Select-Object -First 1).path = 'Scan'; $r }
Assert-Rejected 'old protocol' { param($r) $r[0].protocol = 'csv-pipe-v1'; $r }
Assert-Rejected 'missing calibration' { param($r) $r | Where-Object kind -ne 'calibration' }
Assert-Rejected 'missing warmup' { param($r) $r | Where-Object kind -ne 'warmup' }
Assert-Rejected 'short warmup' { param($r) ($r | Where-Object kind -eq 'warmup' | Select-Object -First 1).elapsedSeconds = 9; $r }
Assert-Rejected 'too few warmup pairs' { param($r) ($r | Where-Object kind -eq 'warmup' | Select-Object -First 1).pairs = 9; $r }
Assert-Rejected 'wrong calibration stage' { param($r) ($r | Where-Object kind -eq 'calibration' | Select-Object -First 1).stage = 'post-warmup'; $r }
Assert-Rejected 'calibration before warmup' {
    param($r)
    $temp = $r[2]; $r[2] = $r[4]; $r[4] = $temp; $r
}
Assert-Rejected 'short calibration' {
    param($r)
    ($r | Where-Object { $_.kind -eq 'calibration' -and $_.stage -eq 'post-warmup' } | Select-Object -First 1).baselineBatchMs = 124; $r
}
Assert-Rejected 'wrong final repeat count' {
    param($r)
    ($r | Where-Object { $_.kind -eq 'calibration' -and $_.stage -eq 'post-warmup' } | Select-Object -First 1).repeats = 200; $r
}
Assert-Rejected 'short measured batch' {
    param($r)
    $sample = $r | Where-Object kind -eq 'pair' | Select-Object -First 1
    $sample.baseline.Milliseconds = .99; $sample.baselineBatchMs = 99
    $sample.ratio = $sample.candidate.Milliseconds / .99; $r
}
Assert-Rejected 'dishonest batch duration' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baselineBatchMs = 126; $r }
Assert-Rejected 'dishonest minimum batch' { param($r) ($r | Where-Object kind -eq 'summary' | Select-Object -First 1).baselineMinBatchMs = 126; $r }

$records = New-ControlRecords
$records[0].baselineRef = '2' * 40
if (!(Get-CsvPipeTimingResult -Records $records).Stable) { throw 'Valid A/B timing evidence was rejected.' }
Write-Host 'PASS: validates A/B timing without requiring identical source revisions'

function New-IsolatedRecords {
    $records = New-ControlRecords
    $records[0].protocol = 'csv-pipe-isolated-v3'
    foreach ($side in @('baseline', 'candidate')) {
        $worker = [pscustomobject]@{ kind = 'worker-environment'; protocol = 'csv-pipe-isolated-v3'; pid = $(if ($side -eq 'baseline') { 101 } else { 102 })
            sourceRef = '1' * 40; Rows = 2000; parserName = 'HeroParser'; modelsName = 'CsvPipeABModels'
            parserMvid = $records[0].candidateMvid; modelsMvid = $records[0].candidateModelsMvid
            parserHash = 'a' * 64; modelsHash = 'b' * 64; consumerHash = 'c' * 64
            runtime = '.NET 10'; os = 'test'; processors = 4; serverGc = $false; affinity = $null
            jitDisasm = $null; jitDisasmAssemblies = $null }
        $records[0] | Add-Member -NotePropertyName "${side}Worker" -NotePropertyValue $worker
    }
    $verifications = foreach ($worker in @($records[0].baselineWorker, $records[0].candidateWorker)) {
        $checks = foreach ($scenario in @('Plain', 'Escaped', 'Unicode', 'LongEscaped')) {
            foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
                foreach ($path in @('Scan', 'Decode', 'Generated')) {
                    [pscustomobject]@{ scenario = $scenario; transport = $transport; path = $path; Rows = 2000; SplitFields = 1; Checksum = 1 }
                }
            }
        }
        [pscustomobject]@{ kind = 'worker-verification'; response = [pscustomobject]@{ kind = 'worker-verified'; Id = 1; pid = $worker.pid; checks = @($checks) } }
    }
    $command = 1
    foreach ($sample in @($records | Where-Object kind -eq 'pair')) {
        $command++
        foreach ($side in @('baseline', 'candidate')) {
            $sample.$side | Add-Member -NotePropertyName AllocatedBytes -NotePropertyValue 256
            $sample | Add-Member -NotePropertyName "${side}Response" -NotePropertyValue ([pscustomobject]@{
                kind = 'worker-batch'; Id = $command; pid = $records[0]."${side}Worker".pid; transport = $sample.transport; repeats = 100
                measurement = [pscustomobject]@{ Milliseconds = 1.25; AllocatedBytes = 256 }; batchMs = 125
            })
        }
    }
    return @($records[0]) + @($verifications) + @($records | Select-Object -Skip 1)
}

function Assert-IsolatedRejected([string]$Name, [scriptblock]$Mutate) {
    $records = & $Mutate (New-IsolatedRecords)
    $rejected = $false
    try { $null = Get-CsvPipeIsolatedControlResult -Records $records }
    catch { $rejected = $true }
    if (!$rejected) { throw "Isolated gate accepted malformed evidence: $Name." }
    Write-Host "PASS: isolated gate rejects $Name"
}

if (!(Get-CsvPipeIsolatedControlResult -Records (New-IsolatedRecords)).Stable) { throw 'Valid isolated controls were rejected.' }
Write-Host 'PASS: isolated gate accepts two processes with identical normal-identity binaries'
Assert-IsolatedRejected 'same process' { param($r) $r[0].candidateWorker.pid = $r[0].baselineWorker.pid; $r }
Assert-IsolatedRejected 'different source labels' { param($r) $r[0].candidateRef = '2' * 40; $r }
Assert-IsolatedRejected 'wrong worker source' { param($r) $r[0].baselineWorker.sourceRef = '2' * 40; $r }
Assert-IsolatedRejected 'renamed parser' { param($r) $r[0].baselineWorker.parserName = 'HeroParser.Baseline'; $r }
Assert-IsolatedRejected 'renamed models' { param($r) $r[0].baselineWorker.modelsName = 'CsvPipeABModels.Baseline'; $r }
Assert-IsolatedRejected 'missing binary hash' { param($r) $r[0].baselineWorker.parserHash = $null; $r }
Assert-IsolatedRejected 'different parser binaries' { param($r) $r[0].baselineWorker.parserHash = 'd' * 64; $r }
Assert-IsolatedRejected 'different model binaries' { param($r) $r[0].baselineWorker.modelsHash = 'd' * 64; $r }
Assert-IsolatedRejected 'different consumers' { param($r) $r[0].baselineWorker.consumerHash = 'd' * 64; $r }
Assert-IsolatedRejected 'different runtime' { param($r) $r[0].baselineWorker.runtime = 'other'; $r }
Assert-IsolatedRejected 'different GC' { param($r) $r[0].baselineWorker.serverGc = $true; $r }
Assert-IsolatedRejected 'different affinity' { param($r) $r[0].baselineWorker.affinity = '1'; $r }
Assert-IsolatedRejected 'instrumented worker' { param($r) $r[0].baselineWorker.jitDisasm = '*'; $r }
Assert-IsolatedRejected 'missing worker preflight' { param($r) $r | Where-Object kind -ne 'worker-verification' }
Assert-IsolatedRejected 'incomplete worker matrix' { param($r) $r[1].response.checks = @($r[1].response.checks | Select-Object -Skip 1); $r }
Assert-IsolatedRejected 'duplicate correctness case' { param($r) $r[1].response.checks[1].path = 'Scan'; $r }
Assert-IsolatedRejected 'fake split coverage' { param($r) ($r[1].response.checks | Where-Object { $_.transport -eq 'Segmented128' -and $_.path -eq 'Decode' } | Select-Object -First 1).SplitFields = 0; $r }
Assert-IsolatedRejected 'wrong response process' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baselineResponse.pid = 102; $r }
Assert-IsolatedRejected 'reused response' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -Skip 1 -First 1).baselineResponse.Id = 2; $r }
Assert-IsolatedRejected 'wrong response transport' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baselineResponse.transport = 'Stream4096'; $r }
Assert-IsolatedRejected 'wrong response repeats' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baselineResponse.repeats = 99; $r }
Assert-IsolatedRejected 'response time mismatch' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baselineResponse.measurement.Milliseconds = 2; $r }
Assert-IsolatedRejected 'response duration mismatch' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baselineResponse.batchMs = 126; $r }
Assert-IsolatedRejected 'response allocation mismatch' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baselineResponse.measurement.AllocatedBytes = 0; $r }
Assert-IsolatedRejected 'short warmup' { param($r) ($r | Where-Object kind -eq 'warmup' | Select-Object -First 1).elapsedSeconds = 9; $r }
Assert-IsolatedRejected 'short batch' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baselineBatchMs = 99; $r }
Assert-IsolatedRejected 'dishonest summary' { param($r) ($r | Where-Object kind -eq 'summary' | Select-Object -First 1).medianRatio = .99; $r }
Assert-IsolatedRejected 'missing completion' { param($r) $r | Where-Object kind -ne 'complete' }
Assert-IsolatedRejected 'legacy protocol' { param($r) $r[0].protocol = 'csv-pipe-v2-post-warmup-calibration'; $r }
$legacyRejected = $false
try { $null = Get-CsvPipeControlResult -Records (New-IsolatedRecords) } catch { $legacyRejected = $true }
if (!$legacyRejected) { throw 'Legacy checker accepted isolated evidence.' }
Write-Host 'PASS: legacy checker rejects isolated evidence'
$unstable = New-IsolatedRecords
foreach ($sample in @($unstable | Where-Object kind -eq 'pair')) {
    $sample.candidate.Milliseconds = 1.175; $sample.candidateBatchMs = 117.5; $sample.ratio = .94
    $sample.candidateResponse.measurement.Milliseconds = 1.175; $sample.candidateResponse.batchMs = 117.5
}
foreach ($summary in @($unstable | Where-Object kind -eq 'summary')) {
    $summary.medianRatio = .94; $summary.p10Ratio = .94; $summary.p90Ratio = .94; $summary.candidateMinBatchMs = 117.5
}
if ((Get-CsvPipeIsolatedControlResult -Records $unstable).Stable) { throw 'Isolated controls bypassed the original stability bounds.' }
Write-Host 'PASS: isolated controls preserve original stability bounds'

function New-PinnedRecords {
    $records = New-IsolatedRecords
    $records[0].protocol = 'csv-pipe-isolated-v4-same-cpu'
    foreach ($worker in @($records[0].baselineWorker, $records[0].candidateWorker)) {
        $worker.protocol = 'csv-pipe-isolated-v4-same-cpu'
        $worker | Add-Member -NotePropertyName pinnedCpu -NotePropertyValue '0'
        $worker | Add-Member -NotePropertyName allowedCpus -NotePropertyValue '0'
    }
    return $records
}
if (!(Get-CsvPipeIsolatedControlResult -Records (New-PinnedRecords)).Stable) { throw 'Valid pinned controls were rejected.' }
Write-Host 'PASS: pinned controls accept matching single-CPU workers'
foreach ($fault in @('DifferentCpu', 'MultipleAllowedCpus', 'MissingCpu', 'ProtocolMismatch')) {
    $records = New-PinnedRecords
    switch ($fault) {
        'DifferentCpu' { $records[0].candidateWorker.pinnedCpu = '1'; $records[0].candidateWorker.allowedCpus = '1' }
        'MultipleAllowedCpus' { $records[0].candidateWorker.allowedCpus = '0-3' }
        'MissingCpu' { $records[0].candidateWorker.pinnedCpu = $null }
        'ProtocolMismatch' { $records[0].candidateWorker.protocol = 'csv-pipe-isolated-v3' }
    }
    $rejected = $false
    try { $null = Get-CsvPipeIsolatedControlResult -Records $records } catch { $rejected = $true }
    if (!$rejected) { throw "Pinned controls accepted $fault." }
    Write-Host "PASS: pinned controls reject $fault"
}

$records = New-ControlRecords
foreach ($sample in @($records | Where-Object kind -eq 'pair')) {
    $sample.ratio = 1.2
    $sample.candidate.Milliseconds = 1.5
    $sample.candidateBatchMs = 150
}
foreach ($summary in @($records | Where-Object kind -eq 'summary')) {
    $summary.medianRatio = 1.2; $summary.p10Ratio = 1.2; $summary.p90Ratio = 1.2
    $summary.candidateMinBatchMs = 150
}
if ((Get-CsvPipeControlResult -Records $records).Stable) { throw 'Unstable controls passed the gate.' }
Write-Host 'PASS: complete but biased controls fail stability gate'

$records = New-ControlRecords
foreach ($sample in @($records | Where-Object kind -eq 'pair')) {
    $sample.ratio = if ($sample.pair -lt 4) { .8 } elseif ($sample.pair -ge 26) { 1.2 } else { 1.0 }
    $sample.candidate.Milliseconds = $sample.ratio * 1.25
    $sample.candidateBatchMs = $sample.ratio * 125
}
foreach ($summary in @($records | Where-Object kind -eq 'summary')) {
    $summary.p10Ratio = .8; $summary.p90Ratio = 1.2
    $summary.candidateMinBatchMs = 100
}
if ((Get-CsvPipeControlResult -Records $records).Stable) { throw 'A stable median hid noisy tails.' }
Write-Host 'PASS: stable medians with noisy tails fail stability gate'

function New-BiasRecords([string]$Mode) {
    $records = New-ControlRecords
    $records[0].protocol = 'csv-pipe-bias-v1-diagnostic-only'
    $records[0].BiasMode = $Mode
    $records[0].diagnosticOnly = $true
    $records[0].logicalBaselineModule = if ($Mode -in @('Swapped', 'CandidateSelf')) { 'Candidate' } else { 'Baseline' }
    $records[0].logicalCandidateModule = if ($Mode -in @('Swapped', 'BaselineSelf')) { 'Baseline' } else { 'Candidate' }
    return $records
}

foreach ($mode in @('Independent', 'Swapped', 'BaselineSelf', 'CandidateSelf')) {
    $records = New-BiasRecords $mode
    $result = Get-CsvPipeBiasResult -Records $records -Mode $mode
    if (!$result.Stable -or $result.Cases[0].CandidateFirstMedian -ne 1 -or
        $result.Cases[0].BaselineFirstMedian -ne 1 -or $result.Cases[0].FirstHalfMedian -ne 1 -or
        $result.Cases[0].SecondHalfMedian -ne 1) { throw 'Incorrect diagnostic cohorts.' }
    $rejected = $false
    try { $null = Get-CsvPipeControlResult -Records $records }
    catch { $rejected = $true }
    if (!$rejected) { throw 'Diagnostic evidence was accepted as performance controls.' }
    Write-Host "PASS: validates $mode diagnostics but rejects them for acceptance"
}
foreach ($mutation in @('routing', 'mode', 'source', 'protocol', 'duration', 'jit')) {
    $records = New-BiasRecords 'Swapped'
    switch ($mutation) {
        'routing' { $records[0].logicalBaselineModule = 'Baseline' }
        'mode' { $records[0].BiasMode = 'Independent' }
        'source' { $records[0].candidateRef = '2' * 40 }
        'protocol' { $records[0].protocol = 'csv-pipe-v2-post-warmup-calibration' }
        'duration' { ($records | Where-Object kind -eq 'pair' | Select-Object -First 1).baselineBatchMs = 99 }
        'jit' { $records[0].jitDisasm = '*' }
    }
    $rejected = $false
    try { $null = Get-CsvPipeBiasResult -Records $records -Mode 'Swapped' }
    catch { $rejected = $true }
    if (!$rejected) { throw "Malformed diagnostics accepted: $mutation." }
    Write-Host "PASS: rejects mismatched diagnostic $mutation"
}
$records = New-BiasRecords 'Independent'
$records[0].jitDisasm = 'HeroParser.Baseline!* CsvPipeABModels.Baseline!* CsvPipeABProbe!*'
$null = Get-CsvPipeBiasResult -Records $records -Mode 'Independent' -JitDiagnostic
Write-Host 'PASS: separately validates instrumented diagnostic evidence'
Assert-Rejected 'JIT-instrumented acceptance data' { param($r) $r[0].jitDisasm = '*'; $r }

$records = New-BiasRecords 'Independent'
foreach ($sample in @($records | Where-Object kind -eq 'pair')) {
    $sample.ratio = if ($sample.candidateFirst) { .98 } else { 1.02 }
    $sample.candidate.Milliseconds = $sample.ratio * 1.25
    $sample.candidateBatchMs = $sample.ratio * 125
}
foreach ($summary in @($records | Where-Object kind -eq 'summary')) {
    $summary.p10Ratio = .98; $summary.p90Ratio = 1.02; $summary.candidateMinBatchMs = 122.5
}
$result = Get-CsvPipeBiasResult -Records $records -Mode 'Independent'
foreach ($case in $result.Cases) {
    if ($case.CandidateFirstMedian -ne .98 -or $case.BaselineFirstMedian -ne 1.02 -or
        $case.FirstHalfMedian -ne .98 -or $case.SecondHalfMedian -ne 1.02) { throw 'Diagnostic cohort indexing is wrong.' }
}
Write-Host 'PASS: order/time cohorts preserve nonuniform raw-pair medians'

$records = New-BiasRecords 'Independent'
$records[0].jitDisasm = 'HeroParser!* CsvPipeABModels.Candidate!* CsvPipeABProbe!*'
$null = Get-CsvPipeBiasResult -Records $records -Mode 'Independent' -JitDiagnostic
Write-Host 'PASS: validates separate candidate-module JIT selector'
$records[0].jitDisasm = '*'
$records[0].jitDisasmAssemblies = 'HeroParser;HeroParser.Baseline'
$rejected = $false
try { $null = Get-CsvPipeBiasResult -Records $records -Mode 'Independent' -JitDiagnostic }
catch { $rejected = $true }
if (!$rejected) { throw 'Legacy ineffective assembly filtering was accepted.' }
Write-Host 'PASS: rejects ineffective mixed-module JIT selector'
