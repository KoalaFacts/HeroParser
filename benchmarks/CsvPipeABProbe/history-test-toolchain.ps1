$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot 'history-toolchain.ps1'
if (Test-Path -LiteralPath $helper) { . $helper }
$expected = [pscustomobject]@{
    Sdk = '10.0.401'
    Compiler = '5.9.0-1.26423.113 (e34a38d2ae1fc26406a317517196e55c68ff83ab)'
    CompilerHash = 'dd115f0fb8248a4ef5652d9d39e65c7cbef3b45c3e9ccee8542f7fa4600af433'
    ReferencePack = '10.0.12'
    ReferencePackHash = '4f365796a02170a3f7703cc6b3a753460c17d473391f01f2dffc7f7deba5a64b'
    Runtime = '10.0.12'
    RuntimeHash = 'a94a0a855dda23066c16683163baea26e91ea0d3eba371d8b4d5fb848bcea636'
}
Assert-CsvPipeHistoryToolchainIdentity $expected
$checks = 1
foreach ($field in @('Sdk', 'Compiler', 'CompilerHash', 'ReferencePack', 'ReferencePackHash', 'Runtime', 'RuntimeHash')) {
    $changed = $expected | ConvertTo-Json | ConvertFrom-Json
    $changed.$field = 'unexpected'
    $rejected = $false
    try { Assert-CsvPipeHistoryToolchainIdentity $changed } catch { $rejected = $true }
    if (!$rejected) { throw "Historical toolchain drift was accepted: $field" }
    $checks++
}
Write-Host "PASS: $checks historical toolchain identity checks; no builds or parser worker startups"

# These are shell-only subprocesses: no SDK builds, parser workers or samples.
$expired = [Diagnostics.Stopwatch]::GetTimestamp() - 1
$rejected = $false
try { Invoke-CsvPipeHistoryProcess '/not-an-executable' @() $expired } catch {
    $rejected = $_.Exception.Message -like '*deadline*'
}
if (!$rejected) { throw 'An expired deadline was not rejected before subprocess startup.' }
$deadline = [Diagnostics.Stopwatch]::GetTimestamp() + [Diagnostics.Stopwatch]::Frequency * 5
$result = Invoke-CsvPipeHistoryProcess '/bin/sh' @('-c', 'printf output; printf error >&2; exit 7') $deadline
if ($result.TimedOut -or $result.ExitCode -ne 7 -or $result.StandardOutput -cne 'output' -or $result.StandardError -cne 'error') {
    throw 'Bounded subprocess lost exit code or captured output.'
}
$deadline = [Diagnostics.Stopwatch]::GetTimestamp() + [long]([Diagnostics.Stopwatch]::Frequency / 5)
$watch = [Diagnostics.Stopwatch]::StartNew()
$result = Invoke-CsvPipeHistoryProcess '/bin/sh' @('-c', 'sleep 5') $deadline
if (!$result.TimedOut -or $watch.Elapsed.TotalSeconds -gt 3) { throw 'Subprocess ignored the shared deadline.' }
Write-Host 'PASS: 3 bounded subprocess checks; no builds or parser worker startups'
$directory = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
try {
    $null = New-Item -ItemType Directory -Path $directory
    [IO.File]::WriteAllText((Join-Path $directory 'entry.dll'), 'entry')
    [IO.File]::WriteAllText((Join-Path $directory 'implementation.dll'), 'first')
    $before = Get-CsvPipeHistoryDirectoryHash $directory
    [IO.File]::WriteAllText((Join-Path $directory 'implementation.dll'), 'changed')
    if ((Get-CsvPipeHistoryDirectoryHash $directory) -ceq $before) { throw 'Component fingerprint ignored a non-entrypoint implementation binary.' }
    [IO.File]::WriteAllText((Join-Path $directory 'implementation.dll'), 'first')
    if ((Get-CsvPipeHistoryDirectoryHash $directory) -cne $before) { throw 'Component fingerprint is not deterministic.' }
    Write-Host 'PASS: complete component fingerprints detect implementation changes'
}
finally { if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force } }
# Exercise the real build coordinator with a synthetic evaluation that consumes
# its deadline. The invalid executable proves the subsequent build never starts.
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
try {
    $null = New-Item -ItemType Directory -Path $testDirectory
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'history-test-frozen-build.ps1'), [ref]$null, [ref]$null)
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Build-RegressionProject' }, $true)
    Invoke-Expression $function.Extent.Text
    $script:projectBuilds = 0
    $script:deadlineTicks = [Diagnostics.Stopwatch]::GetTimestamp() + [Diagnostics.Stopwatch]::Frequency * 5
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $toolchain = [pscustomobject]@{ Dotnet = '/not-an-executable' }
    $OutputDirectory = $testDirectory
    function Assert-CsvPipeHistoryProjectToolchain {
        $script:deadlineTicks = [Diagnostics.Stopwatch]::GetTimestamp() - 1
        return [pscustomobject]@{ Synthetic = $true }
    }
    $rejected = $false
    try { Build-RegressionProject 'synthetic-expired' @('unused.csproj') } catch { $rejected = $_.Exception.Message -like '*deadline*' }
    if (!$rejected -or $script:projectBuilds -ne 0) { throw 'Build coordinator started a build after evaluation exhausted the deadline.' }
    Write-Host 'PASS: exhausted evaluation cannot start another build'
}
finally { if (Test-Path -LiteralPath $testDirectory) { Remove-Item -LiteralPath $testDirectory -Recurse -Force } }
$pidFile = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
try {
    $deadline = [Diagnostics.Stopwatch]::GetTimestamp() + [long]([Diagnostics.Stopwatch]::Frequency / 5)
    $result = Invoke-CsvPipeHistoryProcess '/bin/sh' @('-c', 'printf retained-output-marker; printf retained-error-marker >&2; sleep 5 & echo $! > "$1"; exit 0', 'deadline-test', $pidFile) $deadline
    if (!$result.TimedOut) { throw 'A descendant retaining the output pipe escaped the deadline.' }
    if ($result.StandardOutput -cne 'retained-output-marker' -or $result.StandardError -cne 'retained-error-marker') {
        throw 'Timeout cleanup lost output already emitted before a descendant retained the pipes.'
    }
    $childId = [int](Get-Content -LiteralPath $pidFile)
    $stat = "/proc/$childId/stat"
    if (Test-Path -LiteralPath $stat) {
        $state = ([IO.File]::ReadAllText($stat) -replace '^.*\) ', '').Split(' ')[0]
        if ($state -ne 'Z') { throw 'A descendant survived after its direct parent exited at the deadline.' }
    }
    Write-Host 'PASS: deadline cleans up descendants whose direct parent exited'
}
finally {
    if (Test-Path -LiteralPath $pidFile) {
        $childId = [int](Get-Content -LiteralPath $pidFile)
        try { $child = [Diagnostics.Process]::GetProcessById($childId); if (!$child.HasExited) { $child.Kill() }; $child.Dispose() } catch [ArgumentException] { }
        Remove-Item -LiteralPath $pidFile -Force
    }
}
$pidFile = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
try {
    $deadline = [Diagnostics.Stopwatch]::GetTimestamp() + [Diagnostics.Stopwatch]::Frequency * 1
    $result = Invoke-CsvPipeHistoryProcess '/bin/sh' @('-c', 'sleep 5 >/dev/null 2>&1 </dev/null & echo $! > "$1"; exit 0', 'closed-pipe-test', $pidFile) $deadline
    if ($result.TimedOut -or $result.ExitCode -ne 0) { throw 'Closed-pipe cleanup lost the successful parent result.' }
    $childId = [int](Get-Content -LiteralPath $pidFile)
    $stat = "/proc/$childId/stat"
    $cleanupWatch = [Diagnostics.Stopwatch]::StartNew()
    while (Test-Path -LiteralPath $stat) {
        $state = ([IO.File]::ReadAllText($stat) -replace '^.*\) ', '').Split(' ')[0]
        if ($state -eq 'Z') { break }
        if ($cleanupWatch.Elapsed.TotalMilliseconds -gt 500) { throw 'A detached child with closed output pipes survived normal subprocess completion.' }
        Start-Sleep -Milliseconds 10
    }
    Write-Host 'PASS: normal completion cleans up descendants with closed output pipes'
}
finally {
    if (Test-Path -LiteralPath $pidFile) {
        $childId = [int](Get-Content -LiteralPath $pidFile)
        try { $child = [Diagnostics.Process]::GetProcessById($childId); if (!$child.HasExited) { $child.Kill() }; $child.Dispose() } catch [ArgumentException] { }
        Remove-Item -LiteralPath $pidFile -Force
    }
}
