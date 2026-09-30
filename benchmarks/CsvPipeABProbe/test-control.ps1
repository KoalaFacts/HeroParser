$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'control.ps1')

function New-ControlRecords {
    $records = @(
        [pscustomobject]@{
            kind = 'environment'; baselineRef = '1' * 40; candidateRef = '1' * 40
            baselineMvid = [guid]::NewGuid(); candidateMvid = [guid]::NewGuid()
            baselineModelsMvid = [guid]::NewGuid(); candidateModelsMvid = [guid]::NewGuid()
            Rows = 2000; Pairs = 30; WarmupPairs = 10; MinSampleMs = 100
            Scenario = 'Plain'; Path = 'Generated'
        }
    )
    foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
        $records += [pscustomobject]@{ kind = 'verified'; scenario = 'Plain'; transport = $transport; path = 'Generated'; Rows = 2000 }
        for ($i = 0; $i -lt 30; $i++) {
            $records += [pscustomobject]@{
                kind = 'pair'; scenario = 'Plain'; transport = $transport; path = 'Generated'
                pair = $i; candidateFirst = ($i % 2) -eq 0; repeats = 100; ratio = 1.0
                baseline = [pscustomobject]@{ Milliseconds = 1.0 }
                candidate = [pscustomobject]@{ Milliseconds = 1.0 }
            }
        }
        $records += [pscustomobject]@{
            kind = 'summary'; scenario = 'Plain'; transport = $transport; path = 'Generated'
            Rows = 2000; Pairs = 30; repeats = 100; medianRatio = 1.0; p10Ratio = 1.0; p90Ratio = 1.0
        }
    }
    $records += [pscustomobject]@{ kind = 'complete'; invalidCases = 0 }
    return $records
}

function Assert-Rejected([string]$Name, [scriptblock]$Mutate) {
    $records = New-ControlRecords
    $records = & $Mutate $records
    $rejected = $false
    try { $null = Get-CsvPipeControlResult -Records $records }
    catch { $rejected = $true }
    if (!$rejected) { throw "Gate accepted malformed evidence: $Name." }
    Write-Host "PASS: rejects $Name"
}

if (!(Get-CsvPipeControlResult -Records (New-ControlRecords)).Stable) { throw 'Valid controls were rejected.' }
Write-Host 'PASS: accepts stable complete controls'
Assert-Rejected 'missing completion' { param($r) $r | Where-Object kind -ne 'complete' }
Assert-Rejected 'invalid case count' { param($r) $r[-1].invalidCases = 1; $r }
Assert-Rejected 'different source revision' { param($r) $r[0].candidateRef = '2' * 40; $r }
Assert-Rejected 'same parser module' { param($r) $r[0].candidateMvid = $r[0].baselineMvid; $r }
Assert-Rejected 'same model module' { param($r) $r[0].candidateModelsMvid = $r[0].baselineModelsMvid; $r }
Assert-Rejected 'different settings' { param($r) $r[0].MinSampleMs = 50; $r }
Assert-Rejected 'missing transport' { param($r) $r | Where-Object transport -ne 'Stream4096' }
Assert-Rejected 'duplicate transport' { param($r) ($r | Where-Object transport -eq 'Stream4096') | ForEach-Object { $_.transport = 'Contiguous' }; $r }
Assert-Rejected 'missing measured pair' { param($r) $r | Where-Object { $_.kind -ne 'pair' -or $_.pair -ne 29 } }
Assert-Rejected 'duplicate pair index' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).pair = 1; $r }
Assert-Rejected 'nonfinite time' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).baseline.Milliseconds = [double]::NaN; $r }
Assert-Rejected 'wrong ratio' { param($r) ($r | Where-Object kind -eq 'pair' | Select-Object -First 1).ratio = 2.0; $r }
Assert-Rejected 'dishonest summary' { param($r) ($r | Where-Object kind -eq 'summary' | Select-Object -First 1).medianRatio = 0.5; $r }
Assert-Rejected 'wrong consumer' { param($r) ($r | Where-Object kind -eq 'verified' | Select-Object -First 1).path = 'Scan'; $r }

$records = New-ControlRecords
foreach ($sample in @($records | Where-Object kind -eq 'pair')) {
    $sample.ratio = 1.2
    $sample.candidate.Milliseconds = 1.2
}
foreach ($summary in @($records | Where-Object kind -eq 'summary')) {
    $summary.medianRatio = 1.2; $summary.p10Ratio = 1.2; $summary.p90Ratio = 1.2
}
if ((Get-CsvPipeControlResult -Records $records).Stable) { throw 'Unstable controls passed the gate.' }
Write-Host 'PASS: complete but biased controls fail stability gate'

$records = New-ControlRecords
foreach ($sample in @($records | Where-Object kind -eq 'pair')) {
    $sample.ratio = if ($sample.pair -lt 4) { .8 } elseif ($sample.pair -ge 26) { 1.2 } else { 1.0 }
    $sample.candidate.Milliseconds = $sample.ratio
}
foreach ($summary in @($records | Where-Object kind -eq 'summary')) {
    $summary.p10Ratio = .8; $summary.p90Ratio = 1.2
}
if ((Get-CsvPipeControlResult -Records $records).Stable) { throw 'A stable median hid noisy tails.' }
Write-Host 'PASS: stable medians with noisy tails fail stability gate'
