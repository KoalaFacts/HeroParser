$ErrorActionPreference = 'Stop'
# Reuse the existing synthetic records and numerical boundary tests; no parser runs.
. (Join-Path $PSScriptRoot 'test-control.ps1')
. (Join-Path $PSScriptRoot 'acceptance-evidence.ps1')
$sha = '1' * 40
$origin = [pscustomobject]@{ id = 42; repository = @{ full_name = 'KoalaFacts/HeroParser' }; head_sha = $sha
    event = 'workflow_dispatch'; path = '.github/workflows/benchmarks.yml'; run_attempt = 1; status = 'completed'; conclusion = 'success' }
$jobs = @([pscustomobject]@{ name = 'CSV Pipe Timing Acceptance'; run_id = 42; head_sha = $sha; run_attempt = 1
    status = 'completed'; conclusion = 'success'
    steps = @(@{ name = 'Run fixed isolated same-source controls'; status = 'completed'; conclusion = 'success' }) })
$artifact = [pscustomobject]@{ name = 'csv-pipe-paired-42-1'; expired = $false; workflow_run = @{ id = 42; head_sha = $sha } }
Assert-CsvPipeAcceptanceOrigin $origin $jobs $artifact $sha '42'
foreach ($fault in @('Source', 'Repository', 'Event', 'Workflow', 'Retry', 'Incomplete', 'Failed', 'Artifact', 'Expired', 'ArtifactSource', 'JobSource', 'JobRetry', 'Diagnostic', 'DuplicateJob')) {
    $r = $origin | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    $j = @($jobs | ConvertTo-Json -Depth 6 | ConvertFrom-Json)
    $a = $artifact | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    switch ($fault) {
        'Source' { $r.head_sha = '2' * 40 }
        'Repository' { $r.repository.full_name = 'other/repository' }
        'Event' { $r.event = 'pull_request' }
        'Workflow' { $r.path = '.github/workflows/csv-pipe-history.yml' }
        'Retry' { $r.run_attempt = 2 }
        'Incomplete' { $r.status = 'in_progress' }
        'Failed' { $r.conclusion = 'failure' }
        'Artifact' { $a.name = 'csv-pipe-history-42-1' }
        'Expired' { $a.expired = $true }
        'ArtifactSource' { $a.workflow_run.head_sha = '2' * 40 }
        'JobSource' { $j[0].head_sha = '2' * 40 }
        'JobRetry' { $j[0].run_attempt = 2 }
        'Diagnostic' { $j[0].steps[0].name = 'Collect fixed predeclared pairs, no acceptance or retries' }
        'DuplicateJob' { $j += $j[0] }
    }
    $rejected = $false
    try { Assert-CsvPipeAcceptanceOrigin $r $j $a $sha '42' } catch { $rejected = $true }
    if (!$rejected) { throw "Acceptance origin accepted $fault." }
    Write-Host "PASS: acceptance origin rejects $fault"
}

function New-AcceptanceFixture {
    $cycles = foreach ($cycle in 1..3) {
        $records = New-PinnedRecords
        $records[0] | Add-Member -NotePropertyName cycle -NotePropertyValue $cycle
        $records[0] | Add-Member -NotePropertyName candidateLaunchedFirst -NotePropertyValue ($cycle -eq 2)
        foreach ($side in @('baseline', 'candidate')) {
            $worker = $records[0]."${side}Worker"
            $oldPid = $worker.pid
            $worker.pid = $cycle * 2 + $(if ($side -eq 'baseline') { 1000 } else { 1001 })
            $worker.parserMvid = [guid]'11111111-1111-1111-1111-111111111111'
            $worker.modelsMvid = [guid]'22222222-2222-2222-2222-222222222222'
            ($records | Where-Object { $_.kind -eq 'worker-verification' -and $_.response.pid -eq $oldPid }).response.pid = $worker.pid
            foreach ($record in $records | Where-Object kind -eq 'pair') { $record."${side}Response".pid = $worker.pid }
        }
        [pscustomobject]@{ Records = $records }
    }
    $controls = foreach ($cycle in 1..3) {
        [pscustomobject]@{ Run = $cycle; Valid = $true; Stable = $true
            Cases = (Get-CsvPipeIsolatedControlResult -Records $cycles[$cycle - 1].Records).Cases }
    }
    [pscustomobject]@{ Cycles = @($cycles)
        Summary = [pscustomobject]@{ Protocol = 'csv-pipe-isolated-v4-same-cpu'; SourceSha = $sha; PinnedCpu = '0'
            State = 'stable-isolated-controls-no-ab'; Controls = @($controls); Comparisons = @() } }
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $temporaryRoot ('csv-pipe-acceptance-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixtureRoot
try {
    foreach ($fault in @('Valid', 'MissingCycle', 'ExtraCycle', 'Source', 'Protocol', 'LaunchOrder', 'ReusedPid', 'Cpu',
        'BinaryDrift', 'MissingCompletion', 'MissingMatrix', 'SummaryOnly', 'MedianThreshold', 'TailThreshold', 'Partial', 'ReportedCase')) {
        $fixture = New-AcceptanceFixture
        $records = $fixture.Cycles[1].Records
        switch ($fault) {
            'Source' { $records[0].baselineRef = '2' * 40 }
            'Protocol' { $records[0].baselineWorker.protocol = 'csv-pipe-verification-boundary-v1' }
            'LaunchOrder' { $records[0].candidateLaunchedFirst = $false }
            'ReusedPid' { $records[0].baselineWorker.pid = $fixture.Cycles[0].Records[0].baselineWorker.pid }
            'Cpu' { $records[0].baselineWorker.allowedCpus = '0-1' }
            'BinaryDrift' { $records[0].baselineWorker.consumerHash = 'd' * 64; $records[0].candidateWorker.consumerHash = 'd' * 64 }
            'MissingCompletion' { $fixture.Cycles[1].Records = @($records | Where-Object kind -ne 'complete') }
            'MissingMatrix' { $fixture.Cycles[1].Records = @($records | Where-Object kind -ne 'worker-verification') }
            'SummaryOnly' { ($records | Where-Object kind -eq 'pair' | Select-Object -First 1).ratio = 1.06 }
            'Partial' { $fixture.Summary.Controls[1].Valid = $false }
            'ReportedCase' { $fixture.Summary.Controls[1].Cases[0].Median = .99 }
            { $_ -in @('MedianThreshold', 'TailThreshold') } {
                $samples = @($records | Where-Object { $_.kind -eq 'pair' -and $_.transport -eq 'Segmented128' })
                foreach ($sample in $samples) {
                    $ratio = if ($fault -eq 'MedianThreshold') { 1.05001 } elseif ($sample.pair -lt 6) { 1.11 } else { 1.0 }
                    $sample.candidate.Milliseconds = $sample.baseline.Milliseconds * $ratio
                    $sample.candidateBatchMs = $sample.candidate.Milliseconds * $sample.repeats
                    $sample.candidateResponse.measurement.Milliseconds = $sample.candidate.Milliseconds
                    $sample.candidateResponse.batchMs = $sample.candidateBatchMs
                    $sample.ratio = $ratio
                }
                $reported = $records | Where-Object { $_.kind -eq 'summary' -and $_.transport -eq 'Segmented128' }
                $sorted = @($samples.ratio | Sort-Object)
                foreach ($point in @(@('medianRatio', .5), @('p10Ratio', .1), @('p90Ratio', .9))) {
                    $index = 29 * $point[1]; $low = [int][Math]::Floor($index); $high = [int][Math]::Ceiling($index)
                    $reported.($point[0]) = $sorted[$low] + ($sorted[$high] - $sorted[$low]) * ($index - $low)
                }
                $reported.candidateMinBatchMs = ($samples.candidateBatchMs | Measure-Object -Minimum).Minimum
            }
        }
        $directory = Join-Path $fixtureRoot $fault
        $null = New-Item -ItemType Directory -Path $directory
        $fixture.Summary | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $directory 'series-summary.json')
        foreach ($cycle in 1..3) {
            if ($fault -eq 'MissingCycle' -and $cycle -eq 3) { continue }
            $fixture.Cycles[$cycle - 1].Records | ForEach-Object { $_ | ConvertTo-Json -Depth 12 -Compress } |
                Set-Content (Join-Path $directory "isolated-aa-$cycle.ndjson")
        }
        if ($fault -eq 'ExtraCycle') { Copy-Item (Join-Path $directory 'isolated-aa-3.ndjson') (Join-Path $directory 'isolated-aa-4.ndjson') }
        $rejected = $false
        try {
            $result = Get-CsvPipeAcceptanceEvidence -EvidenceDirectory $directory -SourceSha $sha
            if ($result.Workers -ne 6 -or $result.MeasuredPairs -ne 270 -or !$result.TimingAcceptancePassed) { throw 'Invalid recomputed acceptance result.' }
        }
        catch { if ($fault -eq 'Valid') { throw }; $rejected = $true }
        if ($fault -ne 'Valid' -and !$rejected) { throw "Acceptance evidence accepted $fault." }
        Write-Host "PASS: acceptance evidence $fault"
    }
}
finally {
    $resolved = (Resolve-Path -LiteralPath $fixtureRoot).ProviderPath
    if ($resolved -ne $fixtureRoot -or !$resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host 'PASS: synthetic evidence validation only; zero parser, benchmark or measured worker startups'
