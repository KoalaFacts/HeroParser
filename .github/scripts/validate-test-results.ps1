<#
Validate the exact TRX files expected from the test commands before publishing.
A successful test process or a parseable report is not proof that results exist.
Failed tests remain publishable when their report is complete and consistent;
the test command and publisher retain responsibility for the test outcome.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string[]] $Path)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$namespace = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
$outcomeCounters = @{
    Passed = 'passed'; Failed = 'failed'; Error = 'error'; Timeout = 'timeout'
    Aborted = 'aborted'; Inconclusive = 'inconclusive'; PassedButRunAborted = 'passedButRunAborted'
    NotRunnable = 'notRunnable'; NotExecuted = 'notExecuted'; Disconnected = 'disconnected'
    Warning = 'warning'; Completed = 'completed'; InProgress = 'inProgress'; Pending = 'pending'
}

foreach ($file in $Path) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Missing expected TRX report: $file"
    }

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = $null
    try {
        $reader = [System.Xml.XmlReader]::Create((Resolve-Path -LiteralPath $file).Path, $settings)
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    } catch {
        throw "TRX report '$file' is malformed or unsafe XML: $($_.Exception.Message)"
    } finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }

    if ($document.DocumentElement.LocalName -ne 'TestRun' -or $document.DocumentElement.NamespaceURI -ne $namespace) {
        throw "TRX report '$file' must have a TestRun root in the expected TRX namespace."
    }
    $namespaces = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespaces.AddNamespace('t', $namespace)
    $counterNodes = $document.SelectNodes('/t:TestRun/t:ResultSummary/t:Counters', $namespaces)
    if ($counterNodes.Count -ne 1) {
        throw "TRX report '$file' must contain exactly one ResultSummary/Counters element."
    }
    $counters = @{}
    foreach ($name in @('total', 'executed') + @($outcomeCounters.Values)) {
        $value = 0L
        $text = $counterNodes[0].GetAttribute($name)
        if (-not [long]::TryParse($text, [ref] $value) -or $value -lt 0) {
            throw "TRX report '$file' has a missing or invalid nonnegative '$name' counter."
        }
        $counters[$name] = $value
    }
    if ($counters.total -eq 0 -or $counters.executed -eq 0) {
        throw "TRX report '$file' contains no executed tests. Check the test filter and TRX producer."
    }

    $results = $document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $namespaces)
    if ($results.Count -eq 0) {
        throw "TRX report '$file' contains no UnitTestResult records."
    }
    if ($counters.total -ne $results.Count) {
        throw "TRX report '$file' total counter ($($counters.total)) differs from UnitTestResult count ($($results.Count))."
    }
    $actual = @{}
    foreach ($name in $outcomeCounters.Values) { $actual[$name] = 0L }
    foreach ($result in $results) {
        $outcome = $result.GetAttribute('outcome')
        if ($outcome -cnotin $outcomeCounters.Keys) {
            throw "TRX report '$file' contains a missing or unsupported test outcome '$outcome'."
        }
        $actual[$outcomeCounters[$outcome]]++
    }
    foreach ($name in $outcomeCounters.Values) {
        if ($counters[$name] -ne $actual[$name]) {
            throw "TRX report '$file' '$name' counter ($($counters[$name])) differs from result outcomes ($($actual[$name]))."
        }
    }
    $executed = $results.Count - $actual.notExecuted
    if ($counters.executed -ne $executed) {
        throw "TRX report '$file' executed counter ($($counters.executed)) differs from executed result count ($executed)."
    }
    Write-Host "Validated ${file}: $($results.Count) results, $executed executed, $($actual.passed) passed, $($actual.failed) failed."
}
