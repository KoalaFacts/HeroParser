param(
    [Parameter(Mandatory = $true)][string]$BaselineSha,
    [Parameter(Mandatory = $true)][string]$Workspace,
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-profile',
    [string]$TraceTool = 'dotnet-trace'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Workspace = [IO.Path]::GetFullPath($Workspace)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($BaselineSha -notmatch '^[0-9a-f]{40}$') { throw 'A full baseline SHA is required.' }
if (Test-Path -LiteralPath $Workspace) { throw 'Profiling requires a fresh workspace.' }
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory).Count) {
    throw 'Profiling output must be empty.'
}
$candidateSha = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or @(& git -C $root status --porcelain).Count) { throw 'Candidate source must be committed and clean.' }
$resolvedBaseline = (& git -C $root rev-parse "$BaselineSha^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $resolvedBaseline -ne $BaselineSha) { throw 'Baseline commit is unavailable.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$baselineDirectory = Join-Path $Workspace 'baseline'
$probe = Join-Path $PSScriptRoot 'bin/Release/net10.0/CsvPipeABProbe.dll'
$baselineDll = Join-Path $baselineDirectory 'src/HeroParser/bin/Release/net10.0/HeroParser.Baseline.dll'
$worktreeAdded = $false
$runs = @()
$state = 'incomplete'
$traceVersion = (& $TraceTool --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Trace tool is unavailable.' }

function Invoke-Trace([string[]]$Arguments, [string]$Log) {
    & $TraceTool @Arguments 2>&1 | Tee-Object -FilePath $Log | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Trace collection or analysis failed.' }
}

function Invoke-Profile([string]$Side, [string]$Transport, [switch]$Jit) {
    $name = "$($Side.ToLowerInvariant())-$Transport-$(if ($Jit) { 'jit' } else { 'stacks' })"
    $ready = Join-Path $OutputDirectory "$name-ready.json"
    $seconds = if ($Jit) { 1 } else { 45 }
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in @($probe, '--rows', '2000', '--profile-side', $Side,
        '--profile-transport', $Transport, '--profile-ready-file', $ready, '--profile-seconds', "$seconds")) {
        $info.ArgumentList.Add($argument)
    }
    $info.Environment['HERO_PARSER_AB_BASELINE_REF'] = $BaselineSha
    $info.Environment['HERO_PARSER_AB_CANDIDATE_REF'] = $candidateSha
    $jitFile = Join-Path $OutputDirectory "$name.asm"
    if ($Jit) {
        $info.Environment['DOTNET_JitDisasm'] = '*TryBind* *GetValue*'
        $info.Environment['DOTNET_JitDisasmDiffable'] = '1'
        $info.Environment['DOTNET_JitStdOutFile'] = $jitFile
    }
    $child = [Diagnostics.Process]::Start($info)
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    try {
        if (!$Jit) {
            $deadline = [datetime]::UtcNow.AddSeconds(90)
            while (!(Test-Path -LiteralPath $ready)) {
                if ($child.HasExited -or [datetime]::UtcNow -ge $deadline) { throw 'Probe did not reach warmed readiness.' }
                Start-Sleep -Milliseconds 100
            }
            $metadata = Get-Content -LiteralPath $ready -Raw | ConvertFrom-Json
            if ($metadata.pid -ne $child.Id -or $metadata.side -ne $Side -or $metadata.transport -ne $Transport -or
                $metadata.warmupSeconds -lt 10 -or $metadata.warmedReads -lt 100) { throw 'Invalid warmed readiness metadata.' }
            $trace = Join-Path $OutputDirectory "$name.nettrace"
            Invoke-Trace @('collect', '--process-id', "$($child.Id)", '--duration', '00:00:00:30',
                '--profile', 'dotnet-sampled-thread-time,dotnet-common', '--output', $trace) (Join-Path $OutputDirectory "$name-collect.txt")
            if (!(Test-Path -LiteralPath $trace) -or (Get-Item -LiteralPath $trace).Length -eq 0) { throw 'No raw trace produced.' }
            Invoke-Trace @('report', $trace, 'topN', '-n', '40', '--verbose') (Join-Path $OutputDirectory "$name-exclusive.txt")
            Invoke-Trace @('report', $trace, 'topN', '-n', '40', '--verbose', '--inclusive') (Join-Path $OutputDirectory "$name-inclusive.txt")
            Invoke-Trace @('convert', $trace, '--format', 'Speedscope', '--output', (Join-Path $OutputDirectory $name)) (Join-Path $OutputDirectory "$name-convert.txt")
            $flame = Join-Path $OutputDirectory "$name.speedscope.json"
            $data = Get-Content -LiteralPath $flame -Raw | ConvertFrom-Json -Depth 100
            if (!$data.shared.frames.Count -or !$data.profiles.Count -or
                !@($data.profiles | Where-Object { $_.events.Count -gt 0 -or $_.samples.Count -gt 0 }).Count) {
                throw 'Converted trace has no sampled stacks.'
            }
        }
        if (!$child.WaitForExit(90000) -or $child.ExitCode -ne 0) { throw 'Diagnostic probe failed or timed out.' }
        $records = @($stdout.GetAwaiter().GetResult() -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
        $environment = @($records | Where-Object kind -eq 'environment')
        $complete = @($records | Where-Object kind -eq 'profile-complete')
        if ($environment.Count -ne 1 -or $complete.Count -ne 1 -or $complete[0].invalidCases -ne 0 -or
            $complete[0].reads -le 0 -or $complete[0].elapsedSeconds -lt $seconds -or
            $complete[0].side -ne $Side -or $complete[0].transport -ne $Transport -or
            $environment[0].candidateRef -ne $candidateSha -or $environment[0].baselineRef -ne $BaselineSha -or
            $environment[0].baselineMvid -eq $environment[0].candidateMvid -or
            $environment[0].baselineModelsMvid -eq $environment[0].candidateModelsMvid) { throw 'Incomplete diagnostic evidence.' }
        if ($Jit -and (!(Test-Path -LiteralPath $jitFile) -or
            !(Select-String -LiteralPath $jitFile -Pattern 'Assembly listing for method' -Quiet))) { throw 'No JIT disassembly produced.' }
        return [pscustomobject]@{ Name = $name; Side = $Side; Transport = $Transport; Jit = [bool]$Jit; Environment = $environment[0]; Complete = $complete[0] }
    }
    finally {
        if (!$child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $OutputDirectory "$name.ndjson")
        $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $OutputDirectory "$name-stderr.txt")
        $child.Dispose()
    }
}

try {
    & git -C $root worktree add --detach $baselineDirectory $BaselineSha
    if ($LASTEXITCODE -ne 0) { throw 'Baseline worktree creation failed.' }
    $worktreeAdded = $true
    & dotnet build (Join-Path $baselineDirectory 'src/HeroParser/HeroParser.csproj') -c Release -f net10.0 `
        '-p:AssemblyName=HeroParser.Baseline' '-t:Rebuild' --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Baseline build failed.' }
    & (Get-Process -Id $PID).Path -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -BaselineDll $baselineDll -BaselineRef $BaselineSha -CandidateRef $candidateSha `
        -Rows 2000 -Scenario Plain -VerifyOnly -DisableBuildServers -Output (Join-Path $OutputDirectory 'plain-verify.ndjson')
    if ($LASTEXITCODE -ne 0) { throw 'Plain correctness preflight failed.' }
    $verified = @(Get-Content -LiteralPath (Join-Path $OutputDirectory 'plain-verify.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
    if (@($verified | Where-Object kind -eq 'verified').Count -ne 9 -or
        @($verified | Where-Object kind -eq 'invalid').Count -ne 0 -or
        $verified[-1].kind -ne 'complete' -or $verified[-1].invalidCases -ne 0) { throw 'Incomplete correctness preflight.' }
    foreach ($arguments in @(@('--profile-seconds', '45'), @('--profile-side', 'Baseline'),
        @('--profile-side', 'Other', '--profile-transport', 'Contiguous', '--profile-ready-file', 'unused'),
        @('--profile-side', 'Candidate', '--profile-transport', 'Other', '--profile-ready-file', 'unused'),
        @('--profile-side', 'Candidate', '--profile-transport', 'Contiguous', '--profile-ready-file', 'unused', '--verify-only'),
        @('--profile-side', 'Candidate', '--profile-transport', 'Contiguous', '--profile-ready-file', 'unused', '--profile-seconds', '121'))) {
        $errorOutput = & dotnet $probe @arguments 2>&1 | Out-String
        $errorOutput | Add-Content -LiteralPath (Join-Path $OutputDirectory 'argument-validation.txt')
        if ($LASTEXITCODE -eq 0 -or $errorOutput -notmatch 'Profiling requires') { throw 'Invalid profiling arguments were not rejected.' }
    }
    foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
        foreach ($side in @('Baseline', 'Candidate')) { $runs += Invoke-Profile $side $transport }
    }
    foreach ($side in @('Baseline', 'Candidate')) { $runs += Invoke-Profile $side 'Contiguous' -Jit }
    $state = 'diagnostics-complete-not-performance-approval'
}
finally {
    [pscustomobject]@{ State = $state; BaselineSha = $BaselineSha; CandidateSha = $candidateSha; TraceVersion = $traceVersion;
        Sampling = 'Managed sampled thread time, not precise on-CPU time'; Rows = 2000; WarmupSeconds = 10;
        TraceSeconds = 30; Runs = $runs } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'profile-summary.json')
    if ($env:GITHUB_STEP_SUMMARY) {
        @('## CSV Pipe Diagnostics', "State: **$state**.", "Baseline: ``$BaselineSha``; candidate: ``$candidateSha``.",
            "Trace tool: ``$traceVersion``.", 'Managed sampled thread time is not precise on-CPU time.',
            'Plain Generated only. Trace attaches after warmup; JIT output uses separate processes.',
            'Diagnostics include the consumer checksum and must not be interpreted as A/B timing or merge approval.',
            'Raw traces, Speedscope files, inclusive/exclusive reports, JIT assembly and module IDs are in the artifact.') |
            Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
    if ($worktreeAdded) {
        $resolved = (Resolve-Path -LiteralPath $baselineDirectory).Path
        $dirty = @(& git -C $resolved status --porcelain)
        if ($resolved -ne $baselineDirectory -or $LASTEXITCODE -ne 0 -or $dirty.Count) { throw 'Retaining baseline: cleanup safety check failed.' }
        & git -C $root worktree remove $resolved
        if ($LASTEXITCODE -ne 0) { throw 'Baseline cleanup failed; no forced removal attempted.' }
    }
}
