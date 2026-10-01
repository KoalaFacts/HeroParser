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
