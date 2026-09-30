function Get-CsvPipeTimingResult {
    param(
        [Parameter(Mandatory = $true)][object[]]$Records,
        [int]$Rows = 2000,
        [int]$Pairs = 30,
        [int]$WarmupPairs = 10,
        [int]$MinSampleMs = 100,
        [ValidateSet('', 'Independent', 'Swapped', 'BaselineSelf', 'CandidateSelf')][string]$BiasMode = '',
        [switch]$JitDiagnostic
    )

    $environment = @($Records | Where-Object kind -eq 'environment')
    $complete = @($Records | Where-Object kind -eq 'complete')
    if ($environment.Count -ne 1 -or $complete.Count -ne 1 -or $complete[0].invalidCases -ne 0 -or
        @($Records | Where-Object kind -eq 'invalid').Count -ne 0) {
        throw 'Incomplete or invalid timing evidence.'
    }
    $environment = $environment[0]
    if ($environment.baselineRef -notmatch '^[0-9a-f]{40}$' -or
        $environment.candidateRef -notmatch '^[0-9a-f]{40}$' -or
        $environment.Rows -ne $Rows -or $environment.Pairs -ne $Pairs -or
        $environment.WarmupPairs -ne $WarmupPairs -or $environment.MinSampleMs -ne $MinSampleMs -or
        $environment.Scenario -ne 'Plain' -or $environment.Path -ne 'Generated') {
        throw 'Timing source revisions or measurement settings do not match.'
    }
    if ($BiasMode) {
        $aModule = if ($BiasMode -in @('Swapped', 'CandidateSelf')) { 'Candidate' } else { 'Baseline' }
        $bModule = if ($BiasMode -in @('Swapped', 'BaselineSelf')) { 'Baseline' } else { 'Candidate' }
        if ($environment.protocol -ne 'csv-pipe-bias-v1-diagnostic-only' -or
            $environment.BiasMode -ne $BiasMode -or $environment.diagnosticOnly -ne $true -or
            $environment.baselineRef -ne $environment.candidateRef -or
            $environment.logicalBaselineModule -ne $aModule -or $environment.logicalCandidateModule -ne $bModule) {
            throw 'Mismatched same-source bias diagnostic or module routing.'
        }
    }
    elseif ($environment.protocol -ne 'csv-pipe-v2-post-warmup-calibration' -or
        $environment.BiasMode -or $environment.diagnosticOnly -eq $true) {
        throw 'Diagnostic records cannot be used for timing acceptance.'
    }
    if ($JitDiagnostic) {
        if ($BiasMode -ne 'Independent' -or $environment.jitDisasmAssemblies -or
            $environment.jitDisasm -notin @('HeroParser.Baseline!* CsvPipeABModels.Baseline!* CsvPipeABProbe!*',
                'HeroParser!* CsvPipeABModels.Candidate!* CsvPipeABProbe!*')) {
            throw 'Missing separate JIT diagnostic instrumentation.'
        }
    }
    elseif ($environment.jitDisasm -or $environment.jitDisasmAssemblies) {
        throw 'JIT-instrumented records cannot be used as uninstrumented timing.'
    }
    if ($environment.minWarmupSeconds -ne 10 -or $environment.calibrationHeadroom -ne 1.25 -or
        $environment.maxCalibrationRepeats -ne 65536) {
        throw 'Unknown or mismatched timing protocol.'
    }
    foreach ($prefix in @('', 'Models')) {
        $a = [guid]$environment."baseline${prefix}Mvid"
        $b = [guid]$environment."candidate${prefix}Mvid"
        if ($a -eq [guid]::Empty -or $b -eq [guid]::Empty -or $a -eq $b) {
            throw 'Timing requires distinct independently built parser and model modules.'
        }
    }

    $verified = @($Records | Where-Object kind -eq 'verified')
    $summaries = @($Records | Where-Object kind -eq 'summary')
    $measurements = @($Records | Where-Object kind -eq 'pair')
    $calibrations = @($Records | Where-Object kind -eq 'calibration')
    $warmups = @($Records | Where-Object kind -eq 'warmup')
    if ($verified.Count -ne 3 -or $summaries.Count -ne 3 -or $measurements.Count -ne 3 * $Pairs -or
        $calibrations.Count -ne 6 -or $warmups.Count -ne 3 -or
        $Records[0].kind -ne 'environment' -or $Records[-1].kind -ne 'complete') {
        throw 'Timing requires all transports, warmups, calibrations and measured pairs.'
    }
    $results = foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
        $check = @($verified | Where-Object transport -eq $transport)
        $summary = @($summaries | Where-Object transport -eq $transport)
        $samples = @($measurements | Where-Object transport -eq $transport | Sort-Object pair)
        $pilot = @($calibrations | Where-Object { $_.transport -eq $transport -and $_.stage -eq 'pilot' })
        $final = @($calibrations | Where-Object { $_.transport -eq $transport -and $_.stage -eq 'post-warmup' })
        $warmup = @($warmups | Where-Object transport -eq $transport)
        if ($check.Count -ne 1 -or $summary.Count -ne 1 -or $samples.Count -ne $Pairs -or
            $check[0].Rows -ne $Rows -or $summary[0].Rows -ne $Rows -or $summary[0].Pairs -ne $Pairs) {
            throw "Missing, duplicate or mismatched transport evidence: $transport."
        }
        if ($pilot.Count -ne 1 -or $final.Count -ne 1 -or $warmup.Count -ne 1 -or
            ![double]::IsFinite([double]$warmup[0].elapsedSeconds) -or $warmup[0].elapsedSeconds -lt 10 -or
            ![double]::IsFinite([double]$warmup[0].pairs) -or $warmup[0].pairs -lt $WarmupPairs -or
            $warmup[0].pairs -ne [Math]::Truncate([double]$warmup[0].pairs) -or
            [Array]::IndexOf($Records, $pilot[0]) -ge [Array]::IndexOf($Records, $warmup[0]) -or
            [Array]::IndexOf($Records, $warmup[0]) -ge [Array]::IndexOf($Records, $final[0]) -or
            [Array]::IndexOf($Records, $final[0]) -ge [Array]::IndexOf($Records, $samples[0])) {
            throw 'Missing, short or incorrectly ordered warmup/calibration.'
        }
        foreach ($calibration in @($pilot[0], $final[0])) {
            $target = if ($calibration.stage -eq 'pilot') { $MinSampleMs } else { $MinSampleMs * 1.25 }
            if ($calibration.targetBatchMs -ne $target -or
                $calibration.repeats -le 0 -or $calibration.repeats -gt 65536 -or
                ![double]::IsFinite([double]$calibration.repeats) -or
                $calibration.repeats -ne [Math]::Truncate([double]$calibration.repeats) -or
                ![double]::IsFinite([double]$calibration.baselineBatchMs) -or
                ![double]::IsFinite([double]$calibration.candidateBatchMs) -or
                $calibration.baselineBatchMs -lt $target -or $calibration.candidateBatchMs -lt $target) {
                throw 'Invalid or undersized calibration.'
            }
        }
        if ($final[0].repeats -lt $pilot[0].repeats -or $final[0].repeats -ne $summary[0].repeats) {
            throw 'Final calibration does not match the measured repeat count.'
        }
        foreach ($record in @($check) + @($summary) + @($samples) + @($pilot) + @($final) + @($warmup)) {
            if ($record.scenario -ne 'Plain' -or $record.path -ne 'Generated') {
                throw 'Unexpected timing scenario or consumer.'
            }
        }
        for ($i = 0; $i -lt $Pairs; $i++) {
            $sample = $samples[$i]
            $a = [double]$sample.baseline.Milliseconds
            $b = [double]$sample.candidate.Milliseconds
            $ratio = [double]$sample.ratio
            if ($sample.pair -ne $i -or $sample.candidateFirst -ne (($i % 2) -eq 0) -or
                [Array]::IndexOf($Records, $sample) -le [Array]::IndexOf($Records, $final[0]) -or
                $sample.repeats -le 0 -or $sample.repeats -ne $summary[0].repeats -or
                ![double]::IsFinite($a) -or ![double]::IsFinite($b) -or ![double]::IsFinite($ratio) -or
                $a -le 0 -or $b -le 0 -or [Math]::Abs($ratio - $b / $a) -gt 1e-9) {
                throw "Malformed measurement: $transport pair $i."
            }
            $aBatch = [double]$sample.baselineBatchMs
            $bBatch = [double]$sample.candidateBatchMs
            if (![double]::IsFinite($aBatch) -or ![double]::IsFinite($bBatch) -or
                $aBatch -lt $MinSampleMs -or $bBatch -lt $MinSampleMs -or
                [Math]::Abs($aBatch - $a * $sample.repeats) -gt 1e-9 -or
                [Math]::Abs($bBatch - $b * $sample.repeats) -gt 1e-9) {
                throw "Short or inconsistent measured batch: $transport pair $i."
            }
        }
        $aMinimum = ($samples.baselineBatchMs | Measure-Object -Minimum).Minimum
        $bMinimum = ($samples.candidateBatchMs | Measure-Object -Minimum).Minimum
        if (![double]::IsFinite([double]$summary[0].baselineMinBatchMs) -or
            ![double]::IsFinite([double]$summary[0].candidateMinBatchMs) -or
            [Math]::Abs($summary[0].baselineMinBatchMs - $aMinimum) -gt 1e-9 -or
            [Math]::Abs($summary[0].candidateMinBatchMs - $bMinimum) -gt 1e-9) {
            throw 'Minimum batch summary does not agree with measured pairs.'
        }
        $sorted = @($samples.ratio | Sort-Object)
        $percentiles = foreach ($p in @(.5, .1, .9)) {
            $index = ($sorted.Count - 1) * $p
            $lower = [int][Math]::Floor($index)
            [double]$sorted[$lower] + ($sorted[[int][Math]::Ceiling($index)] - $sorted[$lower]) * ($index - $lower)
        }
        $reported = @($summary[0].medianRatio, $summary[0].p10Ratio, $summary[0].p90Ratio)
        for ($i = 0; $i -lt 3; $i++) {
            if (![double]::IsFinite([double]$reported[$i]) -or [Math]::Abs($reported[$i] - $percentiles[$i]) -gt 1e-9) {
                throw 'Timing summary does not agree with its raw measured pairs.'
            }
        }
        [pscustomobject]@{
            Transport = $transport
            Median = $percentiles[0]
            P10 = $percentiles[1]
            P90 = $percentiles[2]
            MinBaselineBatchMs = $aMinimum
            MinCandidateBatchMs = $bMinimum
            Stable = $percentiles[0] -ge .95 -and $percentiles[0] -le 1.05 -and
                $percentiles[1] -ge .90 -and $percentiles[2] -le 1.10
        }
    }
    [pscustomobject]@{ Stable = @($results | Where-Object { !$_.Stable }).Count -eq 0; Cases = @($results) }
}

function Get-CsvPipeControlResult {
    param(
        [Parameter(Mandatory = $true)][object[]]$Records,
        [int]$Rows = 2000,
        [int]$Pairs = 30,
        [int]$WarmupPairs = 10,
        [int]$MinSampleMs = 100
    )
    $environment = @($Records | Where-Object kind -eq 'environment')
    if ($environment.Count -ne 1 -or $environment[0].baselineRef -ne $environment[0].candidateRef) {
        throw 'A/A requires identical source revisions.'
    }
    Get-CsvPipeTimingResult -Records $Records -Rows $Rows -Pairs $Pairs -WarmupPairs $WarmupPairs -MinSampleMs $MinSampleMs
}

function Get-CsvPipeBiasResult {
    param(
        [Parameter(Mandatory = $true)][object[]]$Records,
        [Parameter(Mandatory = $true)][ValidateSet('Independent', 'Swapped', 'BaselineSelf', 'CandidateSelf')][string]$Mode,
        [switch]$JitDiagnostic
    )
    $result = Get-CsvPipeTimingResult -Records $Records -BiasMode $Mode -JitDiagnostic:$JitDiagnostic
    foreach ($case in $result.Cases) {
        $samples = @($Records | Where-Object { $_.kind -eq 'pair' -and $_.transport -eq $case.Transport })
        foreach ($group in @('CandidateFirst', 'BaselineFirst', 'FirstHalf', 'SecondHalf')) {
            $subset = @($samples | Where-Object {
                # switch rebinds $_; preserve the pair before selecting its cohort.
                $sample = $_
                switch ($group) {
                    'CandidateFirst' { $sample.candidateFirst -eq $true }
                    'BaselineFirst' { $sample.candidateFirst -eq $false }
                    'FirstHalf' { $sample.pair -lt 15 }
                    'SecondHalf' { $sample.pair -ge 15 }
                }
            } | Sort-Object ratio)
            if ($subset.Count -ne 15) { throw 'Incomplete order/time diagnostic cohort.' }
            $case | Add-Member -NotePropertyName "${group}Median" -NotePropertyValue $subset[7].ratio
        }
    }
    return $result
}
