function Get-CsvPipeControlResult {
    param(
        [Parameter(Mandatory = $true)][object[]]$Records,
        [int]$Rows = 2000,
        [int]$Pairs = 30,
        [int]$WarmupPairs = 10,
        [int]$MinSampleMs = 100
    )

    $environment = @($Records | Where-Object kind -eq 'environment')
    $complete = @($Records | Where-Object kind -eq 'complete')
    if ($environment.Count -ne 1 -or $complete.Count -ne 1 -or $complete[0].invalidCases -ne 0 -or
        @($Records | Where-Object kind -eq 'invalid').Count -ne 0) {
        throw 'Incomplete or invalid A/A evidence.'
    }
    $environment = $environment[0]
    if ($environment.baselineRef -notmatch '^[0-9a-f]{40}$' -or
        $environment.baselineRef -ne $environment.candidateRef -or
        $environment.Rows -ne $Rows -or $environment.Pairs -ne $Pairs -or
        $environment.WarmupPairs -ne $WarmupPairs -or $environment.MinSampleMs -ne $MinSampleMs -or
        $environment.Scenario -ne 'Plain' -or $environment.Path -ne 'Generated') {
        throw 'A/A source revisions or measurement settings do not match.'
    }
    foreach ($prefix in @('', 'Models')) {
        $a = [guid]$environment."baseline${prefix}Mvid"
        $b = [guid]$environment."candidate${prefix}Mvid"
        if ($a -eq [guid]::Empty -or $b -eq [guid]::Empty -or $a -eq $b) {
            throw 'A/A requires distinct independently built parser and model modules.'
        }
    }

    $verified = @($Records | Where-Object kind -eq 'verified')
    $summaries = @($Records | Where-Object kind -eq 'summary')
    $measurements = @($Records | Where-Object kind -eq 'pair')
    if ($verified.Count -ne 3 -or $summaries.Count -ne 3 -or $measurements.Count -ne 3 * $Pairs) {
        throw 'A/A requires all three transports and every measured pair.'
    }
    $results = foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
        $check = @($verified | Where-Object transport -eq $transport)
        $summary = @($summaries | Where-Object transport -eq $transport)
        $samples = @($measurements | Where-Object transport -eq $transport | Sort-Object pair)
        if ($check.Count -ne 1 -or $summary.Count -ne 1 -or $samples.Count -ne $Pairs -or
            $check[0].Rows -ne $Rows -or $summary[0].Rows -ne $Rows -or $summary[0].Pairs -ne $Pairs) {
            throw "Missing, duplicate or mismatched transport evidence: $transport."
        }
        foreach ($record in @($check) + @($summary) + @($samples)) {
            if ($record.scenario -ne 'Plain' -or $record.path -ne 'Generated') {
                throw 'Unexpected scenario or consumer in the A/A control.'
            }
        }
        for ($i = 0; $i -lt $Pairs; $i++) {
            $sample = $samples[$i]
            $a = [double]$sample.baseline.Milliseconds
            $b = [double]$sample.candidate.Milliseconds
            $ratio = [double]$sample.ratio
            if ($sample.pair -ne $i -or $sample.candidateFirst -ne (($i % 2) -eq 0) -or
                $sample.repeats -le 0 -or $sample.repeats -ne $summary[0].repeats -or
                ![double]::IsFinite($a) -or ![double]::IsFinite($b) -or ![double]::IsFinite($ratio) -or
                $a -le 0 -or $b -le 0 -or [Math]::Abs($ratio - $b / $a) -gt 1e-9) {
                throw "Malformed A/A measurement: $transport pair $i."
            }
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
                throw 'A/A summary does not agree with its raw measured pairs.'
            }
        }
        [pscustomobject]@{
            Transport = $transport
            Median = $percentiles[0]
            P10 = $percentiles[1]
            P90 = $percentiles[2]
            Stable = $percentiles[0] -ge .95 -and $percentiles[0] -le 1.05 -and
                $percentiles[1] -ge .90 -and $percentiles[2] -le 1.10
        }
    }
    [pscustomobject]@{ Stable = @($results | Where-Object { !$_.Stable }).Count -eq 0; Cases = @($results) }
}
