. (Join-Path $PSScriptRoot 'control.ps1')

function Assert-CsvPipeAcceptanceOrigin($Run, [object[]]$Jobs, $Artifact, [string]$SourceSha, [string]$RunId) {
    if ($RunId -cnotmatch '^[1-9]\d*$' -or [string]$Run.id -cne $RunId -or
        $Run.repository.full_name -cne 'KoalaFacts/HeroParser' -or $Run.head_sha -cne $SourceSha -or
        $Run.event -ne 'workflow_dispatch' -or $Run.path -cne '.github/workflows/benchmarks.yml' -or
        $Run.run_attempt -ne 1 -or $Run.status -ne 'completed' -or $Run.conclusion -ne 'success') {
        throw 'Acceptance requires a completed first-attempt control run for this exact source and repository.'
    }
    $producer = @($Jobs | Where-Object name -eq 'CSV Pipe Timing Acceptance')
    if ($producer.Count -ne 1 -or $producer[0].run_id -ne $Run.id -or $producer[0].head_sha -cne $SourceSha -or
        $producer[0].run_attempt -ne 1 -or $producer[0].status -ne 'completed' -or $producer[0].conclusion -ne 'success') {
        throw 'The original timing-control job did not complete successfully.'
    }
    $steps = @($producer[0].steps | Where-Object name -eq 'Run fixed isolated same-source controls')
    if ($steps.Count -ne 1 -or $steps[0].status -ne 'completed' -or $steps[0].conclusion -ne 'success') {
        throw 'Diagnostic or evidence-only jobs cannot authorize timing acceptance.'
    }
    if ($Artifact.name -cne "csv-pipe-paired-$RunId-1" -or $Artifact.expired -isnot [bool] -or $Artifact.expired -or
        $Artifact.workflow_run.id -ne $Run.id -or $Artifact.workflow_run.head_sha -cne $SourceSha) {
        throw 'Missing, expired or mismatched original control artifact.'
    }
}

function Get-CsvPipeAcceptanceEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
        [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceSha
    )
    $root = (Resolve-Path -LiteralPath $EvidenceDirectory).ProviderPath
    $summaries = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'series-summary.json')
    if ($summaries.Count -ne 1) { throw 'Acceptance requires exactly one complete control series.' }
    $directory = $summaries[0].Directory.FullName
    $summaryHash = (Get-FileHash -LiteralPath $summaries[0].FullName -Algorithm SHA256).Hash
    $summary = Get-Content -LiteralPath $summaries[0].FullName -Raw | ConvertFrom-Json
    $protocol = 'csv-pipe-isolated-v4-same-cpu'
    if ($summary.Protocol -cne $protocol -or $summary.SourceSha -cne $SourceSha -or
        $summary.State -cne 'stable-isolated-controls-no-ab' -or $summary.PinnedCpu -cnotmatch '^\d+$' -or
        $null -eq $summary.Comparisons -or @($summary.Comparisons).Count -ne 0 -or @($summary.Controls).Count -ne 3 -or
        @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'isolated-aa-*.ndjson').Count -ne 3) {
        throw 'Acceptance requires the complete three-pair, same-CPU v4 protocol, not a diagnostic summary.'
    }
    $owners = [Collections.Generic.HashSet[int]]::new()
    $fingerprint = $null
    $inputs = [Collections.Generic.List[object]]::new()
    $results = foreach ($cycle in 1..3) {
        $control = $summary.Controls[$cycle - 1]
        if ($control.Run -ne $cycle -or $control.Valid -isnot [bool] -or !$control.Valid -or
            $control.Stable -isnot [bool] -or !$control.Stable) { throw 'A control cycle failed or was omitted.' }
        $path = Join-Path $directory "isolated-aa-$cycle.ndjson"
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $records = @(Get-Content -LiteralPath $path | ConvertFrom-Json)
        $environment = $records[0]
        if ($environment.cycle -ne $cycle -or $environment.protocol -cne $protocol -or
            $environment.baselineRef -cne $SourceSha -or $environment.candidateRef -cne $SourceSha -or
            $environment.candidateLaunchedFirst -isnot [bool] -or $environment.candidateLaunchedFirst -ne ($cycle -eq 2)) {
            throw 'Control source, cycle or counterbalanced launch order does not match.'
        }
        foreach ($worker in @($environment.baselineWorker, $environment.candidateWorker)) {
            if (!$owners.Add([int]$worker.pid) -or $worker.protocol -cne $protocol -or
                $worker.pinnedCpu -cne $summary.PinnedCpu -or $worker.allowedCpus -cne $summary.PinnedCpu) {
                throw 'Controls require six fresh workers on the same requested CPU.'
            }
            $identity = $worker | Select-Object sourceRef, runtime, os, processors, serverGc, affinity,
                protocol, pinnedCpu, allowedCpus, parserName, modelsName, parserMvid, modelsMvid,
                parserHash, modelsHash, consumerHash | ConvertTo-Json -Compress
            if ($null -eq $fingerprint) { $fingerprint = $identity }
            if ($identity -cne $fingerprint) { throw 'Binary or runtime identity changed between control cycles.' }
        }
        # Recompute from every original measured pair using the existing, unchanged gate.
        $result = Get-CsvPipeIsolatedControlResult -Records $records
        if (!$result.Stable) { throw "Cycle $cycle exceeds the original median or percentile bounds." }
        if (@($control.Cases).Count -ne 3) { throw 'Missing reported transport results.' }
        foreach ($case in $result.Cases) {
            $reported = @($control.Cases | Where-Object Transport -eq $case.Transport)
            if ($reported.Count -ne 1) { throw 'Duplicate or missing reported transport.' }
            foreach ($field in @('Median', 'P10', 'P90', 'MinBaselineBatchMs', 'MinCandidateBatchMs', 'Stable')) {
                if ($reported[0].$field -ne $case.$field) { throw 'Reported control results disagree with original measured pairs.' }
            }
        }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $hash) { throw 'Evidence changed during validation.' }
        $inputs.Add([pscustomobject]@{ File = "isolated-aa-$cycle.ndjson"; Sha256 = $hash })
        [pscustomobject]@{ Cycle = $cycle; Cases = $result.Cases }
    }
    if ((Get-FileHash -LiteralPath $summaries[0].FullName -Algorithm SHA256).Hash -cne $summaryHash) { throw 'Control summary changed during validation.' }
    $inputs.Add([pscustomobject]@{ File = 'series-summary.json'; Sha256 = $summaryHash })
    [pscustomobject]@{ SourceSha = $SourceSha; Protocol = $protocol; Controls = @($results)
        ProcessPairs = 3; Workers = $owners.Count; MeasuredPairs = 270; TimingAcceptancePassed = $true
        InputFingerprints = $inputs.ToArray() }
}
