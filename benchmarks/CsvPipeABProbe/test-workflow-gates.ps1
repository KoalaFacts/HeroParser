$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$workflow = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/benchmarks.yml'))

function Assert-WorkflowBoundary([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    Write-Host "PASS: $Message"
}

function Get-WorkflowJob([string]$Name) {
    $pattern = '(?ms)^  ' + [regex]::Escape($Name) + ':\r?\n(?<job>.*?)(?=^  [a-z][a-z0-9-]*:|\z)'
    $matches = [regex]::Matches($workflow, $pattern)
    if ($matches.Count -ne 1) { throw "Expected exactly one $Name job." }
    return $matches[0].Groups['job'].Value
}

$infrastructure = Get-WorkflowJob 'pipe-ab'
$acceptance = Get-WorkflowJob 'pipe-timing-acceptance'
Assert-WorkflowBoundary ($infrastructure -match 'name: CSV Pipe Infrastructure Validation') 'infrastructure has its own check name'
Assert-WorkflowBoundary ($acceptance -match 'name: CSV Pipe Timing Acceptance') 'timing acceptance has its own check name'
Assert-WorkflowBoundary ($infrastructure -match 'run: ./benchmarks/CsvPipeABProbe/test-control.ps1') 'infrastructure validates the unchanged numerical gate'
Assert-WorkflowBoundary ($infrastructure -notmatch 'run-isolated\.ps1|run-series\.ps1|dotnet (run|exec)|throw.*Timing acceptance') 'infrastructure does not sample or report timing acceptance'
Assert-WorkflowBoundary ($acceptance -match 'needs: pipe-ab') 'explicit timing depends on infrastructure validation'
Assert-WorkflowBoundary ($acceptance -match 'if: always\(\) &&') 'retained failure remains visible even if infrastructure fails'
Assert-WorkflowBoundary (($infrastructure + $acceptance) -notmatch 'continue-on-error') 'neither check suppresses failures'

$retained = [regex]::Match($acceptance,
    '(?ms)^      - name: Retain failed historical timing acceptance without sampling\r?\n(?<settings>.*?)^        run: \|\r?\n(?<code>.*?)(?=^      - |\z)')
Assert-WorkflowBoundary $retained.Success 'automatic acceptance has an explicit retained-failure step'
Assert-WorkflowBoundary ($retained.Groups['settings'].Value -match "if: github.event_name != 'workflow_dispatch'") 'automatic events retain failure instead of sampling'
$code = $retained.Groups['code'].Value -replace '(?m)^          ', ''
$tokens = $null; $errors = $null
$null = [Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
Assert-WorkflowBoundary ($errors.Count -eq 0) 'retained-failure PowerShell parses'
Assert-WorkflowBoundary ($code -notmatch 'run-isolated|run-series|dotnet|Start-Process') 'retained-failure reporting contains no timing invocation'

$timing = [regex]::Match($acceptance,
    '(?ms)^      - name: Run fixed isolated same-source controls\r?\n(?<settings>.*?)^        run: \|\r?\n(?<code>.*?)(?=^      - |\z)')
Assert-WorkflowBoundary $timing.Success 'explicit timing step remains present'
Assert-WorkflowBoundary ($timing.Groups['settings'].Value -match "if: github.event_name == 'workflow_dispatch'") 'only explicit dispatch can invoke timing controls'
$timingCode = $timing.Groups['code'].Value
Assert-WorkflowBoundary ($timingCode -match "INFRASTRUCTURE_RESULT -ne 'success'") 'failed infrastructure blocks explicit timing'
Assert-WorkflowBoundary ($timingCode -match "RUN_ATTEMPT -ne '1'") 'timing-control retries are rejected'
Assert-WorkflowBoundary ($timingCode -match "LEGACY_AB -eq 'true'") 'legacy A/B remains suspended'
Assert-WorkflowBoundary ($timingCode -match 'Unchanged workload: no A/A retrial') 'unchanged failed workloads remain blocked'
Assert-WorkflowBoundary ($timingCode -match 'Native diagnostic success cannot waive this failure') 'native diagnostics cannot waive failed controls'
Assert-WorkflowBoundary ($timingCode -match 'run-isolated\.ps1.*-PinSameCpu') 'explicit controls retain same-CPU isolation'

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $temporaryRoot ('csv-pipe-workflow-gate-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
$originalSummary = $env:GITHUB_STEP_SUMMARY
Push-Location $fixture
try {
    $env:GITHUB_STEP_SUMMARY = Join-Path $fixture 'summary.md'
    $rejected = $false
    try { & ([scriptblock]::Create($code)) }
    catch {
        if ($_.Exception.Message -notmatch '^CSV Pipe timing acceptance remains FAILED at run 36732093817\.') { throw }
        $rejected = $true
    }
    Assert-WorkflowBoundary $rejected 'automatic timing acceptance still fails'
    $record = Get-Content -LiteralPath 'BenchmarkDotNet.Artifacts/pipe-series/retained-acceptance.json' -Raw | ConvertFrom-Json
    Assert-WorkflowBoundary ($record.State -eq 'retained-timing-acceptance-failed' -and
        $record.HistoricalRun -eq '36732093817' -and
        $record.SourceSha -eq '89c06810e76c4623ebad3cfc89c4bcdef41acd59') 'retained record identifies the original failed source and run'
    Assert-WorkflowBoundary ($record.HistoricalMedianRatio -eq 1.052845684 -and $record.MedianUpperLimit -eq 1.05 -and
        $record.HistoricalMedianRatio -gt $record.MedianUpperLimit) 'original failing observation and upper threshold are unchanged'
    Assert-WorkflowBoundary ($record.TimingAcceptancePassed -is [bool] -and !$record.TimingAcceptancePassed -and
        $record.ControlsAccepted -is [bool] -and !$record.ControlsAccepted) 'machine-readable performance approval stays false'
    Assert-WorkflowBoundary ($record.MeasuredWorkersStarted -eq 0) 'automatic reporting starts zero measured workers'
    $summary = Get-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Raw
    Assert-WorkflowBoundary ($summary -match 'Timing Acceptance: FAILED' -and
        $summary -match '1.052845684 > unchanged limit 1.05') 'CI summary exposes the historical failure and unchanged threshold'
}
finally {
    Pop-Location
    $env:GITHUB_STEP_SUMMARY = $originalSummary
    $resolved = (Resolve-Path -LiteralPath $fixture).ProviderPath
    if ($resolved -ne $fixture -or !$resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unexpected workflow-gate fixture cleanup target.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host 'PASS: workflow separation preserves failed acceptance; zero measured workers'
