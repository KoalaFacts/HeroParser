param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$BaselineSha,
    [Parameter(Mandatory = $true)][string]$Workspace,
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-series'
)

$ErrorActionPreference = 'Stop'
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
$resolvedBaseline = (& git -C $root rev-parse --verify "$BaselineSha^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $resolvedBaseline -ne $BaselineSha) { throw 'Baseline SHA is not an available commit.' }
$dirty = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Candidate tracked files must match the committed source.' }
$pwsh = (Get-Process -Id $PID).Path
$program = Join-Path $PSScriptRoot 'bin/Release/net10.0/CsvPipeABProbe.dll'
$baselineDll = Join-Path $baselineDirectory 'src/HeroParser/bin/Release/net10.0/HeroParser.Baseline.dll'
$controls = @()
$comparisons = @()
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
    for ($run = 1; $run -le 2; $run++) {
        $log = Join-Path $OutputDirectory "$Phase-$run.ndjson"
        & dotnet $program --rows 2000 --pairs 30 --warmup-pairs 10 --min-sample-ms 100 `
            --scenario Plain --path Generated | Tee-Object -FilePath $log | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Measurement failed: $Phase run $run." }
        $records = @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json })
        $environment = @($records | Where-Object kind -eq 'environment')
        if ($environment.Count -ne 1 -or $environment[0].candidateRef -ne $candidateSha) {
            throw 'Measurement environment or candidate source is missing.'
        }
        $fingerprint = $environment[0] | Select-Object runtime, os, processors, serverGc, affinity,
            candidateRef, candidateMvid, candidateModelsMvid | ConvertTo-Json -Compress
        if ($null -ne $script:fingerprint -and $script:fingerprint -ne $fingerprint) {
            throw 'Candidate modules or runtime settings changed during the paired series.'
        }
        $script:fingerprint = $fingerprint
        if ($Control) {
            $result = Get-CsvPipeControlResult -Records $records
            $runs += [pscustomobject]@{ Phase = $Phase; Run = $run; Stable = $result.Stable; Cases = $result.Cases }
        }
        else {
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

$oldBaseline = $env:HERO_PARSER_AB_BASELINE_REF
$oldCandidate = $env:HERO_PARSER_AB_CANDIDATE_REF
try {
    & git -C $root worktree add --detach $baselineDirectory $candidateSha
    if ($LASTEXITCODE -ne 0) { throw 'Could not create independent baseline worktree.' }
    $worktreeAdded = $true
    Prepare-Probe -Ref $candidateSha -Phase 'aa-before' -FullMatrix
    $controls += @(Measure-Series -Phase 'aa-before' -Control)
    if (@($controls | Where-Object { !$_.Stable }).Count -ne 0) {
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
finally {
    $env:HERO_PARSER_AB_BASELINE_REF = $oldBaseline
    $env:HERO_PARSER_AB_CANDIDATE_REF = $oldCandidate
    [pscustomobject]@{
        State = $state; BaselineSha = $BaselineSha; CandidateSha = $candidateSha
        Rows = 2000; Pairs = 30; WarmupPairs = 10; MinSampleMs = 100
        Affinity = $env:HERO_PARSER_AB_AFFINITY
        MedianBounds = @(.95, 1.05); P10Minimum = .90; P90Maximum = 1.10
        Controls = @($controls); Comparisons = @($comparisons)
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'series-summary.json')
    if ($env:GITHUB_STEP_SUMMARY) {
        "## CSV pipe paired experiment`nState: **$state**. Stable controls permit reviewing A/B; they do not prove no regression or authorize merging." |
            Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
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
