param([Parameter(Mandatory = $true)][string]$InputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'control.ps1')
. (Join-Path $PSScriptRoot 'history-evidence.ps1')
$checks = 0
function Assert-HistoryReject([scriptblock]$Action) {
    $rejected = $false
    try { $null = & $Action } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid historical correlation was accepted.' }
    $script:checks++
}
$records = @(Get-Content (Join-Path $InputDirectory 'isolated-aa-2.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
$a = @(Get-Content (Join-Path $InputDirectory '2-a-commands.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
$b = @(Get-Content (Join-Path $InputDirectory '2-b-commands.ndjson') | ForEach-Object { $_ | ConvertFrom-Json })
$plan = @(Get-CsvPipeHistoryPlan $records $a $b)
$prelude = @($plan | Where-Object { $_.Operation -eq 'batch' -and $_.Transport -eq 'Contiguous' })
if ($prelude.Count -ne 135 -or ($prelude.Repeats | Measure-Object -Sum).Sum -ne 22463 -or
    @($plan | Where-Object Stage -eq 'measured').Count -ne 90 -or $plan.Count -ne $a.Count) { throw 'Historical request plan lost batches.' }
$checks++
Assert-HistoryReject { Get-CsvPipeHistoryPlan $records $a $b[0..($b.Count - 2)] }
foreach ($field in @('Id', 'Operation', 'Transport', 'Repeats')) {
    $copy = $b | ConvertTo-Json | ConvertFrom-Json
    $copy[2].$field = if ($field -in @('Id', 'Repeats')) { 65536 } else { 'invalid' }
    Assert-HistoryReject { Get-CsvPipeHistoryPlan $records $a $copy }
}
$copy = $a | ConvertTo-Json | ConvertFrom-Json
$copy[2].Repeats = 2
Assert-HistoryReject { Get-CsvPipeHistoryPlan $records $copy $copy }
$changed = $records | ConvertTo-Json -Depth 15 | ConvertFrom-Json
(@($changed | Where-Object kind -eq 'pair')[0]).baselineResponse.Id++
Assert-HistoryReject { Get-CsvPipeHistoryPlan $changed $a $b }
$events = @(
    [pscustomobject]@{ Pid = 42; UtcTicks = 10000L; Name = 'GC/SuspendEEStart'; Payload = @{ ClrInstanceID = 1; Reason = 'SuspendForGC' } },
    [pscustomobject]@{ Pid = 42; UtcTicks = 50000L; Name = 'GC/RestartEEStop'; Payload = @{ ClrInstanceID = 1 } }
)
$pauses = @(Get-CsvPipeGcPauses $events 42)
$overlap = Get-CsvPipePauseOverlap $pauses 20000 40000
if ($pauses.Count -ne 1 -or $overlap.SuspensionOverlapMs -ne 2 -or $overlap.SuspensionsOverlapping -ne 1) { throw 'Pause clipping failed.' }
$checks++
if ((Get-CsvPipePauseOverlap $pauses 60000 70000).SuspensionOverlapMs -ne 0) { throw 'Nonoverlap was charged as pause time.' }
$checks++
Assert-HistoryReject { Get-CsvPipeGcPauses $events 99 }
Assert-HistoryReject { Get-CsvPipeGcPauses @($events[0]) 42 }
Assert-HistoryReject { Get-CsvPipeGcPauses @($events[1]) 42 }
Assert-HistoryReject { Get-CsvPipeGcPauses @($events[0], $events[0], $events[1]) 42 }
Assert-HistoryReject { Get-CsvPipePauseOverlap $pauses 40000 20000 }
$info = [Diagnostics.ProcessStartInfo]::new('unused')
$info.Environment['GH_TOKEN'] = 'test-sentinel'
$info.Environment['COMPlus_TieredPGO'] = '0'
Set-CsvPipeWorkerEnvironment $info
if ($info.Environment.ContainsKey('GH_TOKEN') -or $info.Environment.ContainsKey('COMPlus_TieredPGO')) { throw 'Worker inherited a secret or runtime override.' }
$checks++
$method = [pscustomobject]@{ Pid = 42; Index = 1; Address = '0x1000'; Size = 256; Timestamp = 1000000000UL
    Name = 'HeroParser.Test'; CodeHash = 'a' * 64 }
$window = [pscustomobject]@{ Id = 1; Stage = 'measured'; Transport = 'Segmented128'; Pair = 0
    StartMonotonicTicks = 1000; EndMonotonicTicks = 3000 }
$workers = @{ '42' = @{ Methods = @($method); Windows = @($window) } }
$correlated = Get-CsvPipeCpuCorrelation @('42 42 2.000000000: cpu-clock:u: 1001') $workers 1000
if ($correlated.Quality[0].Mapped -ne 1 -or $correlated.Windows[0].CodeIndex -ne 1) { throw 'Native address/window correlation failed.' }
$checks++
$combined = Get-CsvPipeCpuCorrelation @('42/42 2.000000000: cpu-clock:u: 1001', '') $workers 1000
if ($combined.Quality[0].Mapped -ne 1 -or $combined.Windows[0].CodeIndex -ne 1) { throw 'Combined perf PID/TID or empty separator parsing failed.' }
$checks++
Assert-HistoryReject { Get-CsvPipeCpuCorrelation @('99 99 2.000000000: cpu-clock:u: 1001') $workers 1000 }
Assert-HistoryReject { Get-CsvPipeCpuCorrelation @('unexpected sample') $workers 1000 }
Assert-HistoryReject { Get-CsvPipeCpuCorrelation @('42 42 2.000000000: cpu-clock:u: 1001') $workers 0 }
$beforeLoad = Get-CsvPipeCpuCorrelation @('42 42 0.500000000: cpu-clock:u: 1001') $workers 1000
if ($beforeLoad.Quality[0].Mapped -ne 0) { throw 'A future code version was assigned to an earlier sample.' }
$checks++
$before = @{ Tasks = @(@{ Tid = 42; StartTicks = 10; SchedStat = '1000000 2000000 3' }) }
$after = @{ Tasks = @(@{ Tid = 42; StartTicks = 10; SchedStat = '4000000 3000000 5' }, @{ Tid = 43; StartTicks = 20; SchedStat = '7 8 9' }) }
$scheduler = Get-CsvPipeSchedulingDelta $before $after
if ($scheduler.MatchedLifetimeTasks -ne 1 -or $scheduler.MatchedTaskRunMs -ne 3 -or
    $scheduler.MatchedTaskRunQueueMs -ne 1 -or $scheduler.MatchedTaskSlices -ne 2) { throw 'Wrong scheduler counter units/scope.' }
$checks++
Assert-HistoryReject { Get-CsvPipeSchedulingDelta $after $before }
$reused = @{ Tasks = @(@{ Tid = 42; StartTicks = 99; SchedStat = '0 0 0' }) }
if ((Get-CsvPipeSchedulingDelta $before $reused).MatchedLifetimeTasks -ne 0) { throw 'Reused TID was counted as a surviving thread.' }
$checks++
Assert-CsvPipeParentRuntimeEnvironment @([pscustomobject]@{ Name = 'DOTNET_MULTILEVEL_LOOKUP'; Value = '0' })
$checks++
Assert-HistoryReject { Assert-CsvPipeParentRuntimeEnvironment @([pscustomobject]@{ Name = 'DOTNET_MULTILEVEL_LOOKUP'; Value = '1' }) }
Assert-HistoryReject { Assert-CsvPipeParentRuntimeEnvironment @([pscustomobject]@{ Name = 'DOTNET_TieredPGO'; Value = '0' }) }
Assert-CsvPipeParentRuntimeEnvironment @([pscustomobject]@{ Name = 'DOTNET_NOLOGO'; Value = 'true' })
$checks++
Assert-HistoryReject { Assert-CsvPipeParentRuntimeEnvironment @([pscustomobject]@{ Name = 'DOTNET_NOLOGO'; Value = 'unknown' }) }
$full = [Diagnostics.ProcessStartInfo]::new('unused')
$gcOnly = [Diagnostics.ProcessStartInfo]::new('unused')
Set-CsvPipeWorkerEnvironment $full
Set-CsvPipeWorkerEnvironment $gcOnly
Set-CsvPipeHistoryTraceEnvironment $full 'runtime.nettrace' $true
Set-CsvPipeHistoryTraceEnvironment $gcOnly 'runtime.nettrace' $false
$different = @($full.Environment.Keys | Where-Object { $full.Environment[$_] -ne $gcOnly.Environment[$_] })
if ($full.Environment.Count -ne $gcOnly.Environment.Count -or $different.Count -ne 1 -or
    $different[0] -ne 'DOTNET_EventPipeConfig' -or $full.Environment[$different[0]] -ne 'Microsoft-Windows-DotNETRuntime:11:5' -or
    $gcOnly.Environment[$different[0]] -ne 'Microsoft-Windows-DotNETRuntime:1:5') { throw 'JIT event intervention changed more than one runtime setting.' }
$checks++
Write-Host "PASS: $checks historical correlation checks; no parser or profiler executed"
