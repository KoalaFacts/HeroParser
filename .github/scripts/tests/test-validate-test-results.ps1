$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$validator = Join-Path $PSScriptRoot '..' 'validate-test-results.ps1'
$fixtures = Join-Path $PSScriptRoot 'fixtures'
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
[void](New-Item -ItemType Directory -Path $temp)
$passed = Get-Content (Join-Path $fixtures 'passed.trx') -Raw
$failures = [System.Collections.Generic.List[string]]::new()
$count = 0

function Assert-Report {
    param([string] $Name, [string[]] $Files, [bool] $Accept, [string] $Message = '')
    $script:count++
    $errorText = $null
    try { & $validator -Path $Files | Out-Null }
    catch { $errorText = $_.Exception.Message }
    if ($Accept -and $null -ne $errorText) {
        $script:failures.Add("${Name}: rejected valid report: $errorText")
    } elseif (-not $Accept -and $null -eq $errorText) {
        $script:failures.Add("${Name}: accepted invalid report")
    } elseif (-not $Accept -and $errorText -notlike "*$Message*") {
        $script:failures.Add("${Name}: expected '$Message', got '$errorText'")
    }
}

function Write-Report {
    param([string] $Name, [string] $Content)
    $file = Join-Path $temp "$Name.trx"
    [System.IO.File]::WriteAllText($file, $Content)
    return $file
}

try {
    $unit = Write-Report 'unit' $passed
    $integration = Write-Report 'integration' $passed
    Assert-Report 'valid unit and integration' @($unit, $integration) $true
    Assert-Report 'missing run summary' @((Write-Report 'no-summary' ($passed -replace '(?s)<ResultSummary.*?</ResultSummary>', ''))) $false 'ResultSummary'
    Assert-Report 'duplicate summary without counters is ambiguous' @((Write-Report 'duplicate-summary' $passed.Replace('</TestRun>', '<ResultSummary outcome="Completed" /></TestRun>'))) $false 'exactly one ResultSummary'
    Assert-Report 'empty summary before populated summary is ambiguous' @((Write-Report 'duplicate-summary-first' $passed.Replace('<Results>', '<ResultSummary outcome="Completed" /><Results>'))) $false 'exactly one ResultSummary'
    foreach ($state in @('', ' ', 'Unknown', 'completed', 'Pending', 'InProgress', 'Disconnected', 'NotExecuted', 'NotRunnable')) {
        $summary = $passed.Replace('outcome="Completed"', "outcome=`"$state`"")
        Assert-Report "invalid run-level outcome '$state'" @((Write-Report "summary-$state" $summary)) $false 'run-level outcome'
    }
    Assert-Report 'missing run-level outcome' @((Write-Report 'summary-missing-outcome' $passed.Replace('outcome="Completed"', ''))) $false 'run-level outcome'
    foreach ($state in @('Completed', 'Passed', 'Failed', 'Error', 'Timeout', 'Aborted', 'Inconclusive', 'PassedButRunAborted', 'Warning')) {
        $summary = $passed.Replace('outcome="Completed"', "outcome=`"$state`"")
        Assert-Report "terminal run-level $state remains publishable" @((Write-Report "summary-terminal-$state" $summary)) $true
    }
    Assert-Report 'observed zero-result TRX' @((Join-Path $fixtures 'empty.trx')) $false 'no executed tests'
    Assert-Report 'one empty suite cannot hide behind healthy suite' @($unit, (Join-Path $fixtures 'empty.trx')) $false 'no executed tests'
    Assert-Report 'missing expected integration report' @($unit, (Join-Path $temp 'missing.trx')) $false 'missing.trx'
    Assert-Report 'malformed XML' @((Write-Report 'malformed' '<TestRun>')) $false 'malformed'
    Assert-Report 'DTD is prohibited' @((Write-Report 'dtd' ($passed.Replace('<TestRun', '<!DOCTYPE TestRun [<!ENTITY example "expanded">]><TestRun')))) $false 'unsafe XML'
    Assert-Report 'duplicate counters' @((Write-Report 'duplicate-counters' ($passed -replace '(<Counters[^>]*/>)', '$1$1'))) $false 'exactly one'
    Assert-Report 'wrong namespace' @((Write-Report 'namespace' $passed.Replace('http://microsoft.com/schemas/VisualStudio/TeamTest/2010', 'urn:wrong'))) $false 'namespace'
    Assert-Report 'wrong root' @((Write-Report 'root' $passed.Replace('TestRun', 'Other'))) $false 'TestRun'
    Assert-Report 'missing counters' @((Write-Report 'no-counters' ($passed -replace '<Counters[^>]*/>', ''))) $false 'Counters'
    Assert-Report 'missing results' @((Write-Report 'no-results' ($passed -replace '(?s)<Results>.*?</Results>', '<Results />'))) $false 'UnitTestResult'
    Assert-Report 'truncated result set' @((Write-Report 'truncated' ($passed -replace '    <UnitTestResult[^\n]+AlsoPasses[^\n]+\n', ''))) $false 'total'
    Assert-Report 'counter total mismatch' @((Write-Report 'total' $passed.Replace('total="2"', 'total="3"'))) $false 'total'
    Assert-Report 'counter executed mismatch' @((Write-Report 'executed' $passed.Replace('executed="2"', 'executed="1"'))) $false 'executed'
    Assert-Report 'counter passed mismatch' @((Write-Report 'passed' $passed.Replace('passed="2"', 'passed="1"'))) $false 'passed'
    Assert-Report 'counter failed mismatch' @((Write-Report 'failed' $passed.Replace('failed="0"', 'failed="1"'))) $false 'failed'
    Assert-Report 'negative counter' @((Write-Report 'negative' $passed.Replace('passed="2"', 'passed="-2"'))) $false 'passed'
    Assert-Report 'nonnumeric counter' @((Write-Report 'nonnumeric' $passed.Replace('total="2"', 'total="many"'))) $false 'total'
    Assert-Report 'missing outcome' @((Write-Report 'no-outcome' $passed.Replace('outcome="Passed"', ''))) $false 'outcome'
    Assert-Report 'outcome casing must match the TRX schema' @((Write-Report 'outcome-case' $passed.Replace('outcome="Passed"', 'outcome="passed"'))) $false 'outcome'
    Assert-Report 'unknown outcome' @((Write-Report 'unknown-outcome' $passed.Replace('outcome="Passed"', 'outcome="Unknown"'))) $false 'outcome'
    $skipped = $passed.Replace('testName="Example.AlsoPasses" outcome="Passed"', 'testName="Example.AlsoPasses" outcome="NotExecuted"').Replace('executed="2"', 'executed="1"').Replace('passed="2"', 'passed="1"').Replace('notExecuted="0"', 'notExecuted="1"')
    Assert-Report 'valid report containing a skipped test' @((Write-Report 'skipped' $skipped)) $true
    $failed = $passed.Replace('testName="Example.AlsoPasses" outcome="Passed"', 'testName="Example.AlsoPasses" outcome="Failed"').Replace('passed="2"', 'passed="1"').Replace('failed="0"', 'failed="1"')
    Assert-Report 'consistent failed test remains publishable' @((Write-Report 'valid-failed' $failed)) $true
    $allSkipped = $passed.Replace('outcome="Passed"', 'outcome="NotExecuted"').Replace('executed="2"', 'executed="0"').Replace('passed="2"', 'passed="0"').Replace('notExecuted="0"', 'notExecuted="2"')
    Assert-Report 'all skipped is not execution evidence' @((Write-Report 'all-skipped' $allSkipped)) $false 'no executed tests'
    $notRunnable = $passed.Replace('outcome="Passed"', 'outcome="NotRunnable"').Replace('passed="2"', 'passed="0"').Replace('notRunnable="0"', 'notRunnable="2"')
    Assert-Report 'NotRunnable cannot claim execution' @((Write-Report 'not-runnable-only' $notRunnable)) $false 'executed'
    Assert-Report 'all unrunnable is not execution evidence' @((Write-Report 'unrunnable-zero-executed' $notRunnable.Replace('executed="2"', 'executed="0"'))) $false 'no executed tests'
    $mixedNotRunnable = $skipped.Replace('NotExecuted', 'NotRunnable').Replace('notExecuted="1"', 'notExecuted="0"').Replace('notRunnable="0"', 'notRunnable="1"')
    Assert-Report 'unrunnable record is not counted as executed' @((Write-Report 'mixed-not-runnable' $mixedNotRunnable)) $true
    Assert-Report 'unrunnable record cannot inflate executed count' @((Write-Report 'inflated-not-runnable' $mixedNotRunnable.Replace('executed="1"', 'executed="2"'))) $false 'executed'
    foreach ($state in @('Pending', 'InProgress')) {
        $counter = $state.Substring(0, 1).ToLowerInvariant() + $state.Substring(1)
        $unfinished = $passed.Replace('outcome="Passed"', "outcome=`"$state`"").Replace('passed="2"', 'passed="0"').Replace("$counter=`"0`"", "$counter=`"2`"")
        Assert-Report "$state is not a final report" @((Write-Report "$state-only" $unfinished)) $false 'nonterminal'
        $mixed = $passed.Replace('testName="Example.AlsoPasses" outcome="Passed"', "testName=`"Example.AlsoPasses`" outcome=`"$state`"").Replace('passed="2"', 'passed="1"').Replace("$counter=`"0`"", "$counter=`"1`"")
        Assert-Report "passed test cannot hide $state record" @((Write-Report "mixed-$state" $mixed)) $false 'nonterminal'
    }
    $warning = $passed.Replace('outcome="Passed"', 'outcome="Warning"').Replace('passed="2"', 'passed="0"').Replace('warning="0"', 'warning="2"')
    Assert-Report 'run-level Warning is not a test outcome' @((Write-Report 'warning-only' $warning)) $false 'run-level'
    $mixedWarning = $passed.Replace('testName="Example.AlsoPasses" outcome="Passed"', 'testName="Example.AlsoPasses" outcome="Warning"').Replace('passed="2"', 'passed="1"').Replace('warning="0"', 'warning="1"')
    Assert-Report 'passed test cannot hide run-level Warning' @((Write-Report 'mixed-warning' $mixedWarning)) $false 'run-level'
    $disconnected = $passed.Replace('outcome="Passed"', 'outcome="Disconnected"').Replace('passed="2"', 'passed="0"').Replace('disconnected="0"', 'disconnected="2"')
    Assert-Report 'disconnected run is incomplete evidence' @((Write-Report 'disconnected-only' $disconnected)) $false 'incomplete'
    $mixedDisconnected = $passed.Replace('testName="Example.AlsoPasses" outcome="Passed"', 'testName="Example.AlsoPasses" outcome="Disconnected"').Replace('passed="2"', 'passed="1"').Replace('disconnected="0"', 'disconnected="1"')
    Assert-Report 'passed test cannot hide disconnected record' @((Write-Report 'mixed-disconnected' $mixedDisconnected)) $false 'incomplete'
    $failedAndSkipped = $skipped.Replace('outcome="Passed"', 'outcome="Failed"').Replace('passed="1"', 'passed="0"').Replace('failed="0"', 'failed="1"')
    Assert-Report 'failed terminal and skipped records remain publishable' @((Write-Report 'failed-and-skipped' $failedAndSkipped)) $true
    Assert-Report 'Failed summary preserves failed and skipped diagnostics' @((Write-Report 'failed-summary-and-skipped' $failedAndSkipped.Replace('outcome="Completed"', 'outcome="Failed"'))) $true
    foreach ($state in @('Failed', 'Error', 'Timeout', 'Aborted', 'Inconclusive', 'PassedButRunAborted', 'Completed')) {
        $counter = $state.Substring(0, 1).ToLowerInvariant() + $state.Substring(1)
        $terminal = $passed.Replace('outcome="Passed"', "outcome=`"$state`"").Replace('passed="2"', 'passed="0"').Replace("$counter=`"0`"", "$counter=`"2`"")
        Assert-Report "$state terminal results remain publishable" @((Write-Report "terminal-$state" $terminal)) $true
    }
    Assert-Report 'namespaced prefix is supported' @((Write-Report 'prefixed' ($passed.Replace('xmlns=', 'xmlns:t=') -replace '<(/?)(TestRun|Results|UnitTestResult|ResultSummary|Counters)', '<$1t:$2'))) $true
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL $_" }
    throw "$($failures.Count) of $count TRX validation tests failed."
}
Write-Host "Passed $count TRX validation tests."
