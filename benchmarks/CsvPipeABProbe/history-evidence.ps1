function Get-CsvPipeHistoryPlan {
    param([Parameter(Mandatory = $true)][object[]]$Records,
        [Parameter(Mandatory = $true)][object[]]$CommandsA,
        [Parameter(Mandatory = $true)][object[]]$CommandsB)
    $null = Get-CsvPipeIsolatedControlResult -Records $Records
    if ($CommandsA.Count -ne $CommandsB.Count -or $CommandsA.Count -gt 2000 -or $CommandsA.Count -lt 100) {
        throw 'Historical command counts differ or exceed the replay budget.'
    }
    for ($i = 0; $i -lt $CommandsA.Count; $i++) {
        foreach ($command in @($CommandsA[$i], $CommandsB[$i])) {
            if ($command.Id -ne $i + 1 -or $command.Operation -notin @('verify', 'prepare', 'batch', 'stop') -or
                [string]$command.Repeats -notmatch '^\d+$' -or $command.Repeats -gt 65536) { throw 'Invalid historical command.' }
        }
        foreach ($field in @('Id', 'Operation', 'Transport', 'Repeats')) {
            if ($CommandsA[$i].$field -cne $CommandsB[$i].$field) { throw 'Historical worker commands differ.' }
        }
    }
    $requests = [Collections.Generic.List[object]]::new()
    $cursor = 0
    # Phase-local alternation resets during each calibration/warmup/measurement stage.
    function Add-HistoryRequest([string]$Operation, [string]$Transport, [int]$Repeats,
        [string]$Stage, [int]$Pair = -1, [bool]$CandidateFirst = $false) {
        $command = $CommandsA[$cursor]
        if ($null -eq $command -or $command.Operation -cne $Operation -or
            $command.Transport -cne $Transport -or $command.Repeats -ne $Repeats) {
            throw "Historical phase does not match command $($cursor + 1)."
        }
        $requests.Add([pscustomobject]@{ Id = $command.Id; Operation = $Operation; Transport = $Transport
            Repeats = $Repeats; Stage = $Stage; Pair = $Pair; CandidateFirst = $CandidateFirst })
        Set-Variable -Name cursor -Value ($cursor + 1) -Scope 1
    }
    Add-HistoryRequest 'verify' '' 0 'verification'
    foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
        Add-HistoryRequest 'prepare' $transport 0 'prepare'
        $pilot = @($Records | Where-Object { $_.kind -eq 'calibration' -and $_.transport -eq $transport -and $_.stage -eq 'pilot' })[0]
        $post = @($Records | Where-Object { $_.kind -eq 'calibration' -and $_.transport -eq $transport -and $_.stage -eq 'post-warmup' })[0]
        $warmup = @($Records | Where-Object { $_.kind -eq 'warmup' -and $_.transport -eq $transport })[0]
        foreach ($stage in @('pilot', 'warmup', 'post-warmup', 'measured')) {
            if ($stage -eq 'warmup') {
                for ($i = 0; $i -lt $warmup.pairs; $i++) {
                    Add-HistoryRequest 'batch' $transport $pilot.repeats $stage $i (($i % 2) -eq 0)
                }
            }
            elseif ($stage -eq 'measured') {
                $pairs = @($Records | Where-Object { $_.kind -eq 'pair' -and $_.transport -eq $transport } | Sort-Object pair)
                for ($i = 0; $i -lt 30; $i++) {
                    $pair = $pairs[$i]
                    if ($pair.baselineResponse.Id -ne $cursor + 1 -or $pair.candidateResponse.Id -ne $cursor + 1 -or
                        $pair.baselineResponse.repeats -ne $post.repeats -or $pair.candidateResponse.repeats -ne $post.repeats -or
                        $pair.candidateFirst -ne (($i % 2) -eq 0)) { throw 'Historical measured-response order/identity mismatch.' }
                    Add-HistoryRequest 'batch' $transport $post.repeats $stage $i $pair.candidateFirst
                }
            }
            else {
                $repeats = if ($stage -eq 'pilot') { 1 } else { [int]$pilot.repeats }
                $target = if ($stage -eq 'pilot') { $pilot.repeats } else { $post.repeats }
                for ($i = 0; ; $i++) {
                    Add-HistoryRequest 'batch' $transport $repeats $stage $i (($i % 2) -eq 0)
                    if ($repeats -eq $target) { break }
                    if ($repeats -ge $target -or $repeats -ge 65536) { throw 'Historical calibration is not bounded doubling.' }
                    $repeats *= 2
                }
            }
        }
    }
    Add-HistoryRequest 'stop' '' 0 'stop'
    if ($cursor -ne $CommandsA.Count) { throw 'Unconsumed historical commands.' }
    return $requests.ToArray()
}

function Get-CsvPipeGcPauses {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Events,
        [Parameter(Mandatory = $true)][int]$ExpectedPid)
    $open = @{}
    $pauses = [Collections.Generic.List[object]]::new()
    foreach ($event in $Events) {
        if ($event.Pid -ne $ExpectedPid -or [string]$event.UtcTicks -notmatch '^[1-9]\d*$') { throw 'GC event owner/clock mismatch.' }
        $instance = [string]$event.Payload.ClrInstanceID
        if ($event.Name -eq 'GC/SuspendEEStart') {
            if ($open.ContainsKey($instance)) { throw 'Nested or duplicate runtime suspension.' }
            $open[$instance] = $event
        }
        elseif ($event.Name -eq 'GC/RestartEEStop') {
            if (!$open.ContainsKey($instance) -or $event.UtcTicks -lt $open[$instance].UtcTicks) { throw 'Unmatched or regressed runtime restart.' }
            $start = $open[$instance]
            $pauses.Add([pscustomobject]@{ Pid = $ExpectedPid; StartUtcTicks = $start.UtcTicks
                EndUtcTicks = $event.UtcTicks; Reason = $start.Payload.Reason; ClrInstanceID = $instance })
            $open.Remove($instance)
        }
    }
    if ($open.Count) { throw 'Trace ended during runtime suspension; pause evidence incomplete.' }
    return $pauses.ToArray()
}

function Get-CsvPipePauseOverlap {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Pauses,
        [Parameter(Mandatory = $true)][long]$StartUtcTicks, [Parameter(Mandatory = $true)][long]$EndUtcTicks)
    if ($StartUtcTicks -le 0 -or $EndUtcTicks -le $StartUtcTicks) { throw 'Invalid request UTC window.' }
    $ticks = 0L; $count = 0
    foreach ($pause in $Pauses) {
        $overlap = [Math]::Min($EndUtcTicks, [long]$pause.EndUtcTicks) - [Math]::Max($StartUtcTicks, [long]$pause.StartUtcTicks)
        if ($overlap -gt 0) { $ticks += $overlap; $count++ }
    }
    return [pscustomobject]@{ SuspensionOverlapMs = $ticks / 10000.0; SuspensionsOverlapping = $count
        Clock = 'trace-UTC-to-coordinator-UTC-approximate'; Window = 'request-includes-IPC-not-exact-consumer-stopwatch' }
}

function Get-CsvPipeSchedulingDelta {
    param([Parameter(Mandatory = $true)]$Before, [Parameter(Mandatory = $true)]$After)
    $matched = 0; $run = 0L; $wait = 0L; $slices = 0L
    foreach ($task in $After.Tasks) {
        $old = @($Before.Tasks | Where-Object { $_.Tid -eq $task.Tid -and $_.StartTicks -eq $task.StartTicks })
        if (!$old.Count) { continue }
        if ($old.Count -ne 1) { throw 'Duplicate scheduler thread identity.' }
        $previous = $old[0].SchedStat.Trim() -split '\s+'
        $current = $task.SchedStat.Trim() -split '\s+'
        if ($previous.Count -ne 3 -or $current.Count -ne 3) { throw 'Malformed task scheduling counters.' }
        $delta = for ($i = 0; $i -lt 3; $i++) {
            if ($previous[$i] -notmatch '^\d+$' -or $current[$i] -notmatch '^\d+$' -or [long]$current[$i] -lt [long]$previous[$i]) {
                throw 'Scheduler counters missing or regressed.'
            }
            [long]$current[$i] - [long]$previous[$i]
        }
        $matched++; $run += $delta[0]; $wait += $delta[1]; $slices += $delta[2]
    }
    return [pscustomobject]@{ MatchedLifetimeTasks = $matched; BeforeTasks = @($Before.Tasks).Count; AfterTasks = @($After.Tasks).Count
        MatchedTaskRunMs = $run / 1000000.0; MatchedTaskRunQueueMs = $wait / 1000000.0; MatchedTaskSlices = $slices
        Scope = 'surviving-same-start-time-threads-not-complete-process-or-consumer-window' }
}

function Assert-CsvPipeParentRuntimeEnvironment {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Variables)
    $unknown = @()
    foreach ($variable in $Variables) {
        # setup-dotnet sets this SDK flag; it does not apply to the frozen net10.0 worker.
        if ($variable.Name -eq 'DOTNET_MULTILEVEL_LOOKUP' -and $variable.Value -eq '0') { continue }
        if ($variable.Name -eq 'DOTNET_NOLOGO' -and $variable.Value -in @('true', '1', 'yes', 'false', '0', 'no')) { continue }
        if ($variable.Name -match '^(DOTNET_|COMPlus_)' -and $variable.Name -notin @('DOTNET_ROOT', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE')) {
            $unknown += $variable.Name
        }
    }
    if ($unknown.Count) { throw "Uncontrolled parent runtime overrides: $($unknown -join ', ')" }
}

function Set-CsvPipeWorkerEnvironment {
    param([Parameter(Mandatory = $true)][Diagnostics.ProcessStartInfo]$Info)
    $parent = @{}
    foreach ($key in @('PATH', 'DOTNET_ROOT', 'LANG', 'LC_ALL', 'TZ', 'TMPDIR')) {
        $value = [Environment]::GetEnvironmentVariable($key)
        if ($value) { $parent[$key] = $value }
    }
    $Info.Environment.Clear()
    foreach ($key in $parent.Keys) { $Info.Environment[$key] = $parent[$key] }
    $Info.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $Info.Environment['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
}

function Set-CsvPipeHistoryTraceEnvironment {
    param([Parameter(Mandatory = $true)][Diagnostics.ProcessStartInfo]$Info,
        [Parameter(Mandatory = $true)][string]$TracePath, [Parameter(Mandatory = $true)][bool]$JitEvents)
    $Info.Environment['DOTNET_PerfMapEnabled'] = '1'
    $Info.Environment['DOTNET_PerfMapShowOptimizationTiers'] = '1'
    $Info.Environment['DOTNET_PerfMapStubGranularity'] = '2'
    $Info.Environment['DOTNET_EnableEventPipe'] = '1'
    $Info.Environment['DOTNET_EventPipeConfig'] = if ($JitEvents) { 'Microsoft-Windows-DotNETRuntime:11:5' } else { 'Microsoft-Windows-DotNETRuntime:1:5' }
    $Info.Environment['DOTNET_EventPipeOutputPath'] = $TracePath
    $Info.Environment['DOTNET_EventPipeCircularMB'] = '40'
}

function Get-CsvPipeSameRunConditions {
    return @(
        @{ Name = 'reference-b-first'; Observed = $false; BFirst = $true; JitEvents = $true },
        @{ Name = 'jit-events-a-first'; Observed = $true; BFirst = $false; JitEvents = $true },
        @{ Name = 'gc-only-b-first'; Observed = $true; BFirst = $true; JitEvents = $false },
        @{ Name = 'gc-only-a-first'; Observed = $true; BFirst = $false; JitEvents = $false },
        @{ Name = 'jit-events-b-first'; Observed = $true; BFirst = $true; JitEvents = $true },
        @{ Name = 'reference-a-first'; Observed = $false; BFirst = $false; JitEvents = $true }
    )
}

function Get-CsvPipeVerificationBoundaryConditions {
    return @(
        @{ Name = 'matrix-b-first'; Observed = $true; BFirst = $true; VerificationMode = 'matrix' },
        @{ Name = 'external-a-first'; Observed = $true; BFirst = $false; VerificationMode = 'external' },
        @{ Name = 'external-b-first'; Observed = $true; BFirst = $true; VerificationMode = 'external' },
        @{ Name = 'matrix-a-first'; Observed = $true; BFirst = $false; VerificationMode = 'matrix' }
    )
}

function Assert-CsvPipeBoundaryEnvironment($A, $B, [string]$Source, [string]$Mode) {
    foreach ($worker in @($A, $B)) {
        if ($worker.protocol -ne 'csv-pipe-verification-boundary-v1' -or $worker.verificationMode -ne $Mode) {
            throw 'Verification-boundary treatment or protocol mismatch.'
        }
    }
    # Reuse identity/affinity validation on copies, never relabel recorded diagnostic evidence as acceptance.
    $copies = @($A, $B) | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    foreach ($copy in $copies) { $copy.protocol = 'csv-pipe-isolated-v4-same-cpu' }
    Assert-CsvPipeWorkerEnvironment $copies[0] $copies[1] $Source $Source
}

function Assert-CsvPipeExternalVerification($Response, [int]$WorkerPid, [int]$VerifierPid, [bool]$VerifierComplete) {
    if (!$VerifierComplete -or $VerifierPid -le 0 -or $VerifierPid -eq $WorkerPid -or
        $Response.pid -ne $WorkerPid -or $Response.kind -ne 'worker-verified' -or $Response.Id -ne 1 -or
        $Response.verificationMode -ne 'external' -or $null -eq $Response.checks -or @($Response.checks).Count) {
        throw 'External verification has no separate completed full-matrix owner.'
    }
}

function Get-CsvPipeVerificationBoundaryDecision([object[]]$Results, [bool]$HardwareVerified) {
    $expected = @(Get-CsvPipeVerificationBoundaryConditions)
    $incomplete = [pscustomobject]@{ State = 'incomplete-verification-boundary'; ReproducingControls = 0; CausalComparisonQualified = $false }
    if (!$HardwareVerified -or $Results.Count -ne 4) { return $incomplete }
    $cases = @{}
    for ($i = 0; $i -lt 4; $i++) {
        $result = $Results[$i]; $condition = $expected[$i]
        if ($result.Complete -isnot [bool] -or !$result.Complete -or $result.Condition.Name -ne $condition.Name) { return $incomplete }
        foreach ($field in @('Observed', 'BFirst', 'VerificationMode')) {
            if ($result.Condition.$field -cne $condition.$field -or $result.Condition.$field.GetType() -ne $condition.$field.GetType()) {
                throw 'Verification-boundary conditions changed.'
            }
        }
        foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
            $rows = @($result.Summaries | Where-Object Transport -eq $transport)
            if ($rows.Count -ne 1 -or $rows[0].Pairs -ne 30) { return $incomplete }
            foreach ($field in @('MedianRatio', 'P10Ratio', 'P90Ratio')) {
                if ($null -eq $rows[0].$field -or ![double]::IsFinite([double]$rows[0].$field) -or $rows[0].$field -le 0) {
                    throw 'Invalid verification-boundary distribution.'
                }
            }
            if ($rows[0].P10Ratio -gt $rows[0].MedianRatio -or $rows[0].P90Ratio -lt $rows[0].MedianRatio) {
                throw 'Verification-boundary percentiles inconsistent.'
            }
            $cases["$($condition.Name)/$transport"] = $rows[0]
        }
    }
    $directions = foreach ($name in @('matrix-b-first', 'matrix-a-first')) {
        $case = $cases["$name/Segmented128"]
        if ($case.MedianRatio -gt 1.05 -and $case.P10Ratio -gt 1) { 1 }
        elseif ($case.MedianRatio -lt .95 -and $case.P90Ratio -lt 1) { -1 }
        else { 0 }
    }
    $qualified = $directions[0] -ne 0 -and $directions[0] -eq $directions[1]
    $stable = $true
    foreach ($name in @('external-a-first', 'external-b-first')) {
        foreach ($transport in @('Contiguous', 'Segmented128', 'Stream4096')) {
            $case = $cases["$name/$transport"]
            if ($case.MedianRatio -lt .98 -or $case.MedianRatio -gt 1.02 -or $case.P10Ratio -lt .95 -or $case.P90Ratio -gt 1.05) {
                $stable = $false
            }
        }
    }
    return [pscustomobject]@{
        State = if (!$qualified) { 'no-reproducing-verification-control' }
            elseif ($stable) { 'verification-isolation-supported-needs-confirmation' }
            else { 'verification-isolation-insufficient' }
        ReproducingControls = @($directions | Where-Object { $_ -ne 0 }).Count
        CausalComparisonQualified = $qualified; ExternalDistributionsStable = $stable
        TimingAcceptancePassed = $false
    }
}

function Get-CsvPipeHardwareIdentity {
    param([Parameter(Mandatory = $true)][string]$Cpu)
    if (!$IsLinux -or $Cpu -notmatch '^\d+$') { throw 'Linux CPU identity required.' }
    $info = [IO.File]::ReadAllText('/proc/cpuinfo')
    $identity = [ordered]@{ PinnedCpu = $Cpu }
    foreach ($field in @('vendor_id', 'cpu family', 'model', 'model name', 'stepping', 'flags')) {
        $values = @([regex]::Matches($info, "(?m)^$([regex]::Escape($field))\s*:\s*(.+)$") |
            ForEach-Object { $_.Groups[1].Value.Trim() } | Sort-Object -Unique)
        if ($values.Count -ne 1 -or !$values[0]) { throw "Missing or heterogeneous CPU identity: $field" }
        $identity[$field] = $values[0]
    }
    $kernel = (& uname -r | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or !$kernel) { throw 'Kernel identity unavailable.' }
    $boot = [IO.File]::ReadAllText('/proc/sys/kernel/random/boot_id').Trim()
    if ($boot -notmatch '^[0-9a-f-]{36}$' -or [guid]$boot -eq [guid]::Empty) { throw 'Boot identity unavailable.' }
    $allowed = [regex]::Match([IO.File]::ReadAllText('/proc/self/status'), '(?m)^Cpus_allowed_list:\s*([0-9,-]+)$')
    if (!$allowed.Success) { throw 'Coordinator affinity unavailable.' }
    $identity['Kernel'] = $kernel
    $identity['BootIdHash'] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($boot))).ToLowerInvariant()
    $identity['AllowedCpus'] = $allowed.Groups[1].Value
    return [pscustomobject]$identity
}

function Assert-CsvPipeHardwareMatch {
    param([Parameter(Mandatory = $true)]$Expected, [Parameter(Mandatory = $true)]$Actual)
    foreach ($field in @('PinnedCpu', 'vendor_id', 'cpu family', 'model', 'model name', 'stepping', 'flags', 'Kernel', 'BootIdHash', 'AllowedCpus')) {
        if ([string]::IsNullOrWhiteSpace([string]$Expected.$field) -or [string]::IsNullOrWhiteSpace([string]$Actual.$field) -or
            $Expected.$field -cne $Actual.$field) { throw "Runner identity changed or missing: $field" }
    }
}

function Get-CsvPipeSameRunDecision {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Results,
        [Parameter(Mandatory = $true)][bool]$HardwareVerified)
    $expected = @(Get-CsvPipeSameRunConditions)
    if (!$HardwareVerified -or $Results.Count -ne 6 -or @($Results | Where-Object { !$_.Complete }).Count) {
        return [pscustomobject]@{ State = 'incomplete-same-run-evidence'; ReproducingControls = 0; CausalComparisonQualified = $false }
    }
    $cases = @{}
    if (($Results.Condition.Name -join ',') -cne ($expected.Name -join ',')) { throw 'Same-run condition order changed.' }
    foreach ($condition in $expected) {
        $result = @($Results | Where-Object { $_.Condition.Name -eq $condition.Name })
        if ($result.Count -ne 1) { throw 'Missing or duplicate same-run condition.' }
        if ($result[0].Complete -isnot [bool]) { throw 'Invalid same-run completion evidence.' }
        foreach ($field in @('Observed', 'BFirst', 'JitEvents')) {
            if ($result[0].Condition.$field -isnot [bool] -or $result[0].Condition.$field -ne $condition.$field) { throw 'Same-run treatment changed.' }
        }
        $summary = @($result[0].Summaries | Where-Object Transport -eq 'Segmented128')
        if ($summary.Count -ne 1 -or $summary[0].Pairs -ne 30) { throw 'Same-run segmented distribution incomplete.' }
        foreach ($field in @('MedianRatio', 'P10Ratio', 'P90Ratio')) {
            if ($null -eq $summary[0].$field -or ![double]::IsFinite([double]$summary[0].$field) -or $summary[0].$field -le 0) { throw 'Invalid same-run ratio.' }
        }
        if ($summary[0].P10Ratio -gt $summary[0].MedianRatio -or $summary[0].MedianRatio -gt $summary[0].P90Ratio) { throw 'Same-run percentiles inconsistent.' }
        $cases[$condition.Name] = $summary[0]
    }
    $reproduced = @(@('jit-events-a-first', 'jit-events-b-first') | Where-Object {
        $cases[$_].MedianRatio -gt 1.05 -and $cases[$_].P10Ratio -gt 1.0
    }).Count
    $state = 'no-reproducing-same-run-control'
    if ($reproduced -eq 2) {
        $referenceBias = @(@('reference-a-first', 'reference-b-first') | Where-Object {
            $cases[$_].MedianRatio -gt 1.05 -and $cases[$_].P10Ratio -gt 1.0
        }).Count
        $gcBias = @(@('gc-only-a-first', 'gc-only-b-first') | Where-Object {
            $cases[$_].MedianRatio -gt 1.05 -and $cases[$_].P10Ratio -gt 1.0
        }).Count
        $referencesStable = @(@('reference-a-first', 'reference-b-first') | Where-Object {
            $cases[$_].MedianRatio -ge .98 -and $cases[$_].MedianRatio -le 1.02
        }).Count -eq 2
        $gcStable = @(@('gc-only-a-first', 'gc-only-b-first') | Where-Object {
            $cases[$_].MedianRatio -ge .98 -and $cases[$_].MedianRatio -le 1.02
        }).Count -eq 2
        $state = if ($referenceBias -or $gcBias) { 'jit-keyword-not-necessary-for-observed-bias' }
            elseif ($referencesStable -and $gcStable) { 'keyword-effect-supported-needs-independent-confirmation' }
            else { 'reproducing-control-with-inconclusive-intervention' }
    }
    return [pscustomobject]@{ State = $state; ReproducingControls = $reproduced; CausalComparisonQualified = ($reproduced -eq 2) }
}

function Get-CsvPipeCpuCorrelation {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][hashtable]$Workers,
        [Parameter(Mandatory = $true)][long]$StopwatchFrequency)
    if ($StopwatchFrequency -le 0) { throw 'CPU correlation clock unavailable.' }
    $pages = @{}; $counts = @{}; $quality = @{}
    foreach ($owner in $Workers.Keys) {
        $pages[$owner] = @{}
        $quality[$owner] = [pscustomobject]@{ Pid = [int]$owner; Samples = 0; Mapped = 0; InRequest = 0; WorkloadAssigned = 0 }
        foreach ($method in $Workers[$owner].Methods) {
            $address = [Convert]::ToUInt64($method.Address.Substring(2), 16)
            if ($method.Pid -ne [int]$owner -or !$method.Size -or !$method.Timestamp) { throw 'Native method owner/extent/clock mismatch.' }
            $entry = [pscustomobject]@{ Start = $address; End = $address + $method.Size; Method = $method }
            for ($page = $address -shr 12; $page -le (($entry.End - 1) -shr 12); $page++) {
                $key = [string]$page
                if (!$pages[$owner].ContainsKey($key)) { $pages[$owner][$key] = [Collections.Generic.List[object]]::new() }
                $pages[$owner][$key].Add($entry)
            }
        }
    }
    foreach ($line in $Lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^\s*(?<owner>\d+)(?:/|\s+)(?<thread>\d+)\s+(?<time>\d+\.\d+):\s+cpu-clock:u:\s+(?<ip>[0-9a-f]+)\s*$') {
            throw 'Unexpected CPU sample format; cannot silently discard evidence.'
        }
        $owner = $Matches.owner
        if (!$Workers.ContainsKey($owner)) { throw 'CPU sample belongs to a different process.' }
        $seconds = [decimal]::Parse($Matches.time, [Globalization.CultureInfo]::InvariantCulture)
        $nanoseconds = [ulong]($seconds * 1000000000)
        $ticks = [long]($seconds * $StopwatchFrequency)
        $ip = [Convert]::ToUInt64($Matches.ip, 16)
        $quality[$owner].Samples++
        $candidates = @($pages[$owner][[string]($ip -shr 12)] | Where-Object {
            $_.Start -le $ip -and $_.End -gt $ip -and $_.Method.Timestamp -le $nanoseconds
        } | Sort-Object { $_.Method.Timestamp } -Descending)
        if (!$candidates.Count) { continue }
        $quality[$owner].Mapped++
        $windows = @($Workers[$owner].Windows | Where-Object { $_.StartMonotonicTicks -le $ticks -and $_.EndMonotonicTicks -ge $ticks })
        if (!$windows.Count) { continue }
        if ($windows.Count -ne 1) { throw 'CPU sample matches overlapping request windows.' }
        $quality[$owner].InRequest++
        $method = $candidates[0].Method
        if ($method.Name -notmatch 'HeroParser|CsvPipeABModels|Case.*CandidateAsync') { continue }
        $quality[$owner].WorkloadAssigned++
        $window = $windows[0]
        $key = "$owner/$($window.Id)/$($method.Index)"
        if (!$counts.ContainsKey($key)) {
            $counts[$key] = [pscustomobject]@{ Pid = [int]$owner; Id = $window.Id; Stage = $window.Stage
                Transport = $window.Transport; Pair = $window.Pair; CodeIndex = $method.Index
                Address = $method.Address; NativeBytes = $method.Size; CodeHash = $method.CodeHash
                Name = $method.Name; Samples = 0; Clock = 'perf-mono-nanoseconds-to-coordinator-monotonic'
                Match = 'latest-preceding-jit-load-covering-IP-not-an-inline-source-map' }
        }
        $counts[$key].Samples++
    }
    return [pscustomobject]@{ Quality = @($quality.Values); Windows = @($counts.Values | Sort-Object Pid, Id, CodeIndex) }
}
