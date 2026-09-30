function Read-CsvPipeJitDump {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][int]$ExpectedPid)
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($Path))
    try {
        if ($reader.BaseStream.Length -lt 40 -or $reader.ReadUInt32() -ne 0x4A695444 -or $reader.ReadUInt32() -ne 1) {
            throw 'Unsupported or truncated jitdump header.'
        }
        $headerSize = $reader.ReadUInt32()
        $machine = $reader.ReadUInt32()
        $null = $reader.ReadUInt32()
        $owner = $reader.ReadUInt32()
        $created = $reader.ReadUInt64()
        $flags = $reader.ReadUInt64()
        if ($owner -ne $ExpectedPid -or $machine -ne 62 -or $headerSize -lt 40 -or $headerSize -gt $reader.BaseStream.Length) {
            throw 'Jitdump PID, architecture or size mismatch.'
        }
        $reader.BaseStream.Position = $headerSize
        $methods = [Collections.Generic.List[object]]::new()
        $indices = [Collections.Generic.HashSet[ulong]]::new()
        while ($reader.BaseStream.Position -lt $reader.BaseStream.Length) {
            $start = $reader.BaseStream.Position
            if ($reader.BaseStream.Length - $start -lt 16) { throw 'Truncated jitdump record header.' }
            $id = $reader.ReadUInt32()
            $size = $reader.ReadUInt32()
            $timestamp = $reader.ReadUInt64()
            if ($size -lt 16 -or $size -gt 16777216 -or $start + $size -gt $reader.BaseStream.Length) { throw 'Invalid jitdump record bounds.' }
            if ($id -eq 0) {
                if ($size -lt 57) { throw 'Truncated code-load record.' }
                $processId = $reader.ReadUInt32()
                $threadId = $reader.ReadUInt32()
                $vma = $reader.ReadUInt64()
                $address = $reader.ReadUInt64()
                $codeSize = $reader.ReadUInt64()
                $index = $reader.ReadUInt64()
                if ($processId -ne $ExpectedPid -or !$address -or $codeSize -gt $size - 57 -or !$indices.Add($index)) {
                    throw 'Invalid code-load owner, address, size or index.'
                }
                $nameSize = [int]($size - 56 - $codeSize)
                $name = $reader.ReadBytes($nameSize)
                if ($name.Length -ne $nameSize -or $name[-1] -ne 0 -or [Array]::IndexOf($name, [byte]0) -ne $nameSize - 1) {
                    throw 'Invalid jitdump function name.'
                }
                $code = $reader.ReadBytes([int]$codeSize)
                if ($code.Length -ne $codeSize) { throw 'Truncated native code.' }
                $methods.Add([pscustomobject]@{ Pid = $processId; Tid = $threadId; Index = $index; Timestamp = $timestamp
                    Address = ('0x{0:x}' -f $address); Vma = ('0x{0:x}' -f $vma); Size = $codeSize
                    Name = [Text.UTF8Encoding]::new($false, $true).GetString($name, 0, $nameSize - 1)
                    CodeHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($code)).ToLowerInvariant() })
            }
            elseif ($id -notin @(1, 2, 3, 4)) { throw 'Unknown jitdump record type.' }
            $reader.BaseStream.Position = $start + $size
        }
        if (!$methods.Count) { throw 'Jitdump has no native code-load records.' }
        return [pscustomobject]@{ Pid = $owner; Machine = $machine; Created = $created; Flags = $flags; Methods = $methods.ToArray() }
    }
    finally { $reader.Dispose() }
}

function Read-CsvPipePerfReport {
    param([Parameter(Mandatory = $true)][string[]]$Lines, [Parameter(Mandatory = $true)][int]$ExpectedPid,
        [int[]]$ExpectedTids = @($ExpectedPid))
    $rows = foreach ($line in $Lines) {
        if ($line.TrimStart().StartsWith('#') -or [string]::IsNullOrWhiteSpace($line)) { continue }
        $fields = $line.Split('|').Trim()
        if ($fields.Count -ne 6 -or $fields[0] -notmatch '^\d+(\.\d+)?%$' -or $fields[1] -notmatch '^\d+$' -or
            $fields[2] -notmatch '^\d+$' -or $fields[3] -notmatch '^(\d+):') {
            throw "Unexpected native perf report row: $line"
        }
        $threadId = [int]$Matches[1]
        if ($threadId -notin $ExpectedTids) { throw 'Native sample thread does not belong to the captured worker.' }
        [pscustomobject]@{ Percent = [double]::Parse($fields[0].TrimEnd('%'), [Globalization.CultureInfo]::InvariantCulture)
            Samples = [long]$fields[1]; Period = [long]$fields[2]; Pid = $ExpectedPid; Tid = $threadId
            Symbol = ($fields[4] -replace '^\[.\]\s*', ''); Dso = $fields[5] }
    }
    $samples = ($rows.Samples | Measure-Object -Sum).Sum
    $period = ($rows.Period | Measure-Object -Sum).Sum
    if ($samples -lt 500 -or $period -le 0) { throw 'Insufficient native on-CPU samples.' }
    $unknownPeriod = ($rows | Where-Object { $_.Symbol -match '^\[unknown\]$|^0x[0-9a-f]+$' } | Measure-Object -Property Period -Sum).Sum
    $workload = @($rows | Where-Object { $_.Symbol -match 'HeroParser|CsvPipeABModels|Case.*CandidateAsync' })
    if (!$workload.Count -or $unknownPeriod / $period -gt .20) { throw 'Native symbol resolution is insufficient for hotspot attribution.' }
    return [pscustomobject]@{ Samples = $samples; Period = $period; UnknownFraction = $unknownPeriod / $period
        Hotspots = @($rows | Sort-Object Period -Descending); Workload = $workload }
}

function Assert-CsvPipeNativeManifest($Manifest) {
    if ($Manifest.Protocol -ne 'csv-pipe-nativecpu-v1-diagnostic-only' -or $Manifest.DiagnosticOnly -ne $true -or
        $Manifest.WorkloadSha -notmatch '^[0-9a-f]{40}$' -or $Manifest.DiagnosticSha -notmatch '^[0-9a-f]{40}$' -or
        $Manifest.Event -ne 'cpu-clock:u' -or $Manifest.Clock -ne 'mono' -or $Manifest.Frequency -ne 199 -or $Manifest.DurationSeconds -ne 30 -or
        $Manifest.PerfMapEnabled -ne 1 -or $Manifest.PerfMapStubGranularity -ne 2 -or @($Manifest.Runs).Count -ne 2) { throw 'Incomplete or mismatched native diagnostic manifest.' }
    $a = $Manifest.Runs[0].Environment
    $b = $Manifest.Runs[1].Environment
    Assert-CsvPipeWorkerEnvironment $a $b $Manifest.WorkloadSha $Manifest.WorkloadSha
    foreach ($run in $Manifest.Runs) {
        foreach ($number in @('Samples', 'UnknownFraction', 'WarmupSeconds', 'JitMethodCount', 'NativeAssemblyCount', 'LostSamples')) {
            if ($null -eq $run.$number -or ![double]::IsFinite([double]$run.$number) -or $run.$number -lt 0) {
                throw 'Native evidence contains missing, negative or nonfinite counters.'
            }
        }
        if ($run.Environment.protocol -ne 'csv-pipe-isolated-v4-same-cpu' -or $run.JitPid -ne $run.Environment.pid -or
            $run.Samples -lt 500 -or $run.UnknownFraction -gt .20 -or @($run.Hotspots).Count -eq 0 -or
            $run.Batch.pid -ne $run.Environment.pid -or $run.Batch.transport -ne 'Segmented128' -or $run.Batch.repeats -ne 65536 -or
            ![double]::IsFinite([double]$run.Batch.batchMs) -or $run.Batch.batchMs -lt 32000 -or $run.LostSamples -ne 0 -or $run.WarmupSeconds -lt 10 -or
            !$run.JitMethodCount -or !$run.NativeAssemblyCount) { throw 'Native diagnostic evidence is missing, short, lost or mismatched.' }
    }
}
