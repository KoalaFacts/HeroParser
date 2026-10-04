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
Assert-WorkflowBoundary ($infrastructure -match 'run: ./benchmarks/CsvPipeABProbe/test-acceptance.ps1') 'read-only acceptance is regression tested'
Assert-WorkflowBoundary ($workflow -match 'pipe_acceptance_run:') 'evidence-only dispatch has an explicit input'
foreach ($job in @('pipe-profile', 'pipe-bias', 'pipe-history', 'pipe-native-profile')) {
    Assert-WorkflowBoundary ((Get-WorkflowJob $job) -match 'if:.*!inputs.pipe_acceptance_run') "$job cannot collect during evidence-only dispatch"
}
Assert-WorkflowBoundary ((Get-WorkflowJob 'setup') -match '!inputs.pipe_acceptance_run') 'evidence-only dispatch never starts ordinary benchmarks'
Assert-WorkflowBoundary ((Get-WorkflowJob 'benchmark') -match "if: needs.setup.outputs.run-benchmarks == 'true'") 'ordinary benchmarks require a measured-code change'
Assert-WorkflowBoundary ((Get-WorkflowJob 'setup') -match '\$measure = \$true') 'unknown change scope does not waive benchmark collection'
Assert-WorkflowBoundary ((Get-WorkflowJob 'setup') -match '\$oldJob.Value.Replace' -and
    (Get-WorkflowJob 'setup') -match '\$envPattern') 'benchmark definitions and global runtime settings are not infrastructure-only waivers'

$retained = [regex]::Match($acceptance,
    '(?ms)^      - name: Retain failed historical timing acceptance without sampling\r?\n(?<settings>.*?)^        run: \|\r?\n(?<code>.*?)(?=^      - |\z)')
Assert-WorkflowBoundary $retained.Success 'historical failure remains a separate immutable record'
Assert-WorkflowBoundary ($retained.Groups['settings'].Value -match "inputs.pipe_acceptance_run != ''") 'evidence-only events also retain historical failure'
$code = $retained.Groups['code'].Value -replace '(?m)^          ', ''
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
Assert-WorkflowBoundary ($errors.Count -eq 0) 'retained-failure PowerShell parses'
$commands = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true))
$allowed = @('New-Item', 'ConvertTo-Json', 'Set-Content', 'Join-Path')
Assert-WorkflowBoundary (@($commands | Where-Object { $_.GetCommandName() -notin $allowed }).Count -eq 0) 'retained-failure reporting invokes only data-output commands'
Assert-WorkflowBoundary (@($ast.FindAll({ param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] }, $true)).Count -eq 0) 'retained-failure reporting has no hidden member invocation'
Assert-WorkflowBoundary ($code -notmatch '\bthrow\b') 'historical reporting no longer hardcodes the current verdict'
$validation = [regex]::Match($acceptance,
    '(?ms)^      - name: Validate current-source evidence without sampling\r?\n(?<settings>.*?)^        run: \|\r?\n(?<code>.*?)(?=^      - |\z)')
Assert-WorkflowBoundary ($validation.Success -and $validation.Groups['settings'].Value -match "inputs.pipe_acceptance_run != ''") 'current evidence has a non-sampling validation path'
Assert-WorkflowBoundary ($validation.Groups['code'].Value -match 'validate-acceptance.ps1 -SourceSha \$env:SOURCE_SHA -EvidenceRun \$env:EVIDENCE_RUN') 'current verdict binds to exact source and original evidence run'
Assert-WorkflowBoundary ($validation.Groups['code'].Value -notmatch 'run-isolated|dotnet|Start-Process') 'current validation cannot start measured workers'
Assert-WorkflowBoundary ($validation.Groups['code'].Value -match 'CONTROL_MODE' -and $validation.Groups['code'].Value -match 'OTHER_MODES') 'evidence validation rejects mixed experiment modes'

function Test-EvidenceDispatchRoute([string]$Job, [hashtable]$Modes) {
    $condition = [regex]::Match((Get-WorkflowJob $Job), '(?m)^    if: (.+)$').Groups[1].Value.Trim()
    if (!$condition) { throw 'Missing job route.' }
    $event = 'workflow_dispatch'
    $expression = $condition.Replace('always()', '$true').Replace('github.event_name', '$event').Replace('inputs.', '$Modes.')
    $expression = $expression.Replace('!=', ' -ne ').Replace('==', ' -eq ').Replace('&&', ' -and ').Replace('||', ' -or ')
    $expression = $expression -replace '!(?!=)', ' -not '
    return [bool](& ([scriptblock]::Create($expression)))
}
$otherInputs = @('pipe_ab', 'pipe_controls', 'pipe_history', 'pipe_history_replay_run', 'pipe_history_jit_control',
    'pipe_history_same_run', 'pipe_bias', 'pipe_bias_jit_only', 'pipe_profile', 'pipe_native_profile',
    'pipe_native_replay_run', 'pipe_native_history_probe')
foreach ($extra in @('none') + $otherInputs) {
    $modes = @{ pipe_acceptance_run = '42' }
    foreach ($inputName in $otherInputs) { $modes[$inputName] = $false }
    if ($extra -ne 'none') { $modes[$extra] = if ($extra -like '*_run') { '123' } else { $true } }
    foreach ($job in @('pipe-ab', 'pipe-timing-acceptance')) {
        Assert-WorkflowBoundary (Test-EvidenceDispatchRoute $job $modes) "$job evaluates evidence or explicitly rejects mixed $extra input"
    }
    foreach ($job in @('setup', 'pipe-profile', 'pipe-bias', 'pipe-history', 'pipe-native-profile')) {
        Assert-WorkflowBoundary (!(Test-EvidenceDispatchRoute $job $modes)) "$job stays excluded for evidence dispatch with $extra input"
    }
}

$timing = [regex]::Match($acceptance,
    '(?ms)^      - name: Run fixed isolated same-source controls\r?\n(?<settings>.*?)^        run: \|\r?\n(?<code>.*?)(?=^      - |\z)')
Assert-WorkflowBoundary $timing.Success 'explicit timing step remains present'
Assert-WorkflowBoundary ($timing.Groups['settings'].Value -match "if: github.event_name == 'workflow_dispatch'") 'only explicit dispatch can invoke timing controls'
Assert-WorkflowBoundary ($timing.Groups['settings'].Value -match "inputs.pipe_acceptance_run == ''") 'evidence-only dispatch excludes control sampling'
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
    & ([scriptblock]::Create($code))
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
    Assert-WorkflowBoundary ($summary -match 'Historical CSV Pipe Timing Acceptance: FAILED' -and
        $summary -match '1.052845684 > unchanged limit 1.05') 'CI summary exposes the historical failure and unchanged threshold'
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'validate-acceptance.ps1') -SourceSha ('1' * 40) }
    catch {
        if ($_.Exception.Message -notmatch '^No current-source acceptance evidence supplied\.') { throw }
        $rejected = $true
    }
    Assert-WorkflowBoundary $rejected 'missing current evidence still fails instead of manufacturing approval'
    $current = Get-Content -LiteralPath 'BenchmarkDotNet.Artifacts/pipe-acceptance/acceptance-validation.json' -Raw | ConvertFrom-Json
    Assert-WorkflowBoundary ($current.State -eq 'missing-current-acceptance-evidence' -and !$current.TimingAcceptancePassed -and
        !$current.HistoricalTimingAcceptancePassed -and $current.MeasuredWorkersStarted -eq 0) 'current missing evidence and historical failure remain distinguishable'
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
Write-Host 'PASS: evidence lifecycle preserves original gates; zero measured workers'
