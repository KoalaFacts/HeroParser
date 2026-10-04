param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceSha,
    [ValidatePattern('^$|^[1-9]\d*$')][string]$EvidenceRun = '',
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-acceptance'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'acceptance-evidence.ps1')
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Do not overwrite acceptance validation.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$report = [ordered]@{ State = 'missing-current-acceptance-evidence'; SourceSha = $SourceSha
    EvidenceRun = $EvidenceRun; TimingAcceptancePassed = $false; MeasuredWorkersStarted = 0
    HistoricalRun = '36732093817'; HistoricalTimingAcceptancePassed = $false; Failure = $null; Validation = $null }
function Read-AcceptanceApi([string]$Endpoint, [string]$Name) {
    $json = & gh api $Endpoint
    if ($LASTEXITCODE -ne 0) { throw 'Cannot retrieve authenticated acceptance provenance.' }
    $json | Set-Content -LiteralPath (Join-Path $OutputDirectory "$Name.json")
    return ($json | ConvertFrom-Json)
}
try {
    if (!$EvidenceRun) { throw 'No current-source acceptance evidence supplied. Historical failure is retained; use pipe_acceptance_run only to validate an already authorized control artifact, not to start sampling.' }
    $report.State = 'rejected-current-acceptance-evidence'
    $run = Read-AcceptanceApi "repos/KoalaFacts/HeroParser/actions/runs/$EvidenceRun" 'origin-run'
    $jobs = Read-AcceptanceApi "repos/KoalaFacts/HeroParser/actions/runs/$EvidenceRun/attempts/1/jobs?per_page=100" 'origin-jobs'
    $artifacts = Read-AcceptanceApi "repos/KoalaFacts/HeroParser/actions/runs/$EvidenceRun/artifacts?per_page=100" 'origin-artifacts'
    if ($jobs.total_count -ne @($jobs.jobs).Count -or $artifacts.total_count -ne @($artifacts.artifacts).Count) {
        throw 'Incomplete provenance listing; refusing to select partial evidence.'
    }
    $artifact = @($artifacts.artifacts | Where-Object name -eq "csv-pipe-paired-$EvidenceRun-1")
    if ($artifact.Count -ne 1) { throw 'Exactly one original first-attempt control artifact is required.' }
    Assert-CsvPipeAcceptanceOrigin $run @($jobs.jobs) $artifact[0] $SourceSha $EvidenceRun
    $download = Join-Path $OutputDirectory 'original-evidence'
    & gh run download $EvidenceRun --repo KoalaFacts/HeroParser -n $artifact[0].name -D $download
    if ($LASTEXITCODE -ne 0) { throw 'Original acceptance artifact unavailable; no substitute trial.' }
    $report.Validation = Get-CsvPipeAcceptanceEvidence -EvidenceDirectory $download -SourceSha $SourceSha
    $report.State = 'current-source-controls-accepted-no-ab'
    $report.TimingAcceptancePassed = $true
}
catch { $report.Failure = $_.Exception.Message; throw }
finally {
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'acceptance-validation.json')
    if ($env:GITHUB_STEP_SUMMARY) {
        @(('## CSV Pipe Timing Acceptance: ' + $(if ($report.TimingAcceptancePassed) { 'PASSED' } else { 'FAILED' })),
            "Current source: $SourceSha; state: $($report.State); evidence run: $EvidenceRun.",
            'Historical run 36732093817 remains failed: 1.052845684 > unchanged limit 1.05.',
            'This job only validates existing evidence and starts zero measured workers. A/A acceptance is not A/B improvement or root-cause proof.',
            $report.Failure) >> $env:GITHUB_STEP_SUMMARY
    }
}
