param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][ValidateSet('36814110508')][string]$OriginRun,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$ObjDump = 'objdump',
    [switch]$UseWsl
)

$ErrorActionPreference = 'Stop'
$scriptHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
$helperPath = Join-Path $PSScriptRoot 'native-evidence.ps1'
$helperHash = (Get-FileHash -LiteralPath $helperPath -Algorithm SHA256).Hash.ToLowerInvariant()
. (Join-Path $PSScriptRoot 'native-evidence.ps1')
$InputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Do not overwrite offline evidence.' }
$inputs = [Collections.Generic.List[object]]::new()
function Add-InputFingerprint([string]$Path) {
    $inputs.Add([pscustomobject]@{
        Path = [IO.Path]::GetRelativePath($InputDirectory, $Path).Replace('\', '/')
        Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}
Add-InputFingerprint (Join-Path $InputDirectory 'history-summary.json')
$summary = Get-Content (Join-Path $InputDirectory 'history-summary.json') -Raw | ConvertFrom-Json
if ($summary.SourceSha -ne '89c06810e76c4623ebad3cfc89c4bcdef41acd59' -or
    $summary.DiagnosticSha -ne '4bb4f48dd535bf1a501998ec5c41a810c4746347' -or
    $summary.StopwatchFrequency -ne 1000000000 -or
    $summary.Protocol -ne 'csv-pipe-history-v1-diagnostic-only' -or !$summary.DiagnosticOnly) { throw 'Unsupported historical evidence.' }
$toolVersion = if ($UseWsl) { @(& wsl --exec $ObjDump --version) } else { @(& $ObjDump --version) }
if ($LASTEXITCODE -ne 0) { throw 'Offline disassembler unavailable.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$results = [Collections.Generic.List[object]]::new()
$conditions = @($summary.Results | Where-Object { $_.Condition.Observed })
if ($conditions.Count -ne 4) { throw 'All four retained observed pairs are required.' }
foreach ($condition in $conditions) {
    if (!$condition.Complete) { throw 'Only complete retained conditions can be disassembled.' }
    $directory = Join-Path $InputDirectory $condition.Condition.Name
    foreach ($name in @('cpu-events-stdout.txt', 'native-request-windows.ndjson')) {
        Add-InputFingerprint (Join-Path $directory $name)
    }
    $windows = @{}
    $sampleCounts = @{}
    foreach ($side in @('a', 'b')) {
        $worker = Join-Path $directory $side
        $environment = Get-Content (Join-Path $worker 'worker.ndjson') -TotalCount 1 | ConvertFrom-Json
        $owner = [int]$environment.pid
        $raw = Join-Path $worker "jit-$owner.dump"
        foreach ($name in @('worker.ndjson', 'jit-methods.json', 'request-windows.ndjson', "jit-$owner.dump")) {
            Add-InputFingerprint (Join-Path $worker $name)
        }
        $rawHash = (Get-FileHash $raw -Algorithm SHA256).Hash.ToLowerInvariant()
        $dump = Read-CsvPipeJitDump $raw $owner
        if ($dump.Flags -ne 0) { throw 'Native evidence requires monotonic JIT load times.' }
        $retained = Get-Content (Join-Path $worker 'jit-methods.json') -Raw | ConvertFrom-Json
        $requests = @(Get-Content (Join-Path $worker 'request-windows.ndjson') | ConvertFrom-Json)
        $measured = @($requests | Where-Object { $_.Stage -eq 'measured' -and $_.Transport -eq 'Segmented128' })
        if ($measured.Count -ne 30 -or @($requests | Where-Object Pid -ne $owner).Count) { throw 'Measured request ownership incomplete.' }
        $selected = @($dump.Methods | Where-Object {
            $_.Name -match 'CsvPipeSequenceReader\+<MoveNextSlowAsync>.*::MoveNext\(\)\[OptimizedTier1\]$|Csv::TryReadRow\(.*\[OptimizedTier1\]$'
        })
        if (!$selected.Count) { throw 'Native target methods unavailable.' }
        $output = Join-Path $OutputDirectory "$($condition.Condition.Name)/$side"
        $null = New-Item -ItemType Directory -Path $output -Force
        $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($raw))
        $bodies = @{}
        try {
            $reader.BaseStream.Position = 8
            $reader.BaseStream.Position = $reader.ReadUInt32()
            while ($reader.BaseStream.Position -lt $reader.BaseStream.Length) {
                $start = $reader.BaseStream.Position
                $id = $reader.ReadUInt32(); $length = $reader.ReadUInt32(); $null = $reader.ReadUInt64()
                if ($id -eq 0) {
                    $reader.BaseStream.Position = $start + 40
                    $size = $reader.ReadUInt64(); $index = $reader.ReadUInt64()
                    if ($index -in $selected.Index) {
                        $reader.BaseStream.Position = $start + $length - $size
                        $bodies[[string]$index] = $reader.ReadBytes([int]$size)
                    }
                }
                $reader.BaseStream.Position = $start + $length
            }
        }
        finally { $reader.Dispose() }
        foreach ($method in $selected) {
            $body = $bodies[[string]$method.Index]
            $reference = @($retained.Methods | Where-Object Index -eq $method.Index)
            if ($body.Length -ne $method.Size -or
                [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($body)).ToLowerInvariant() -ne $method.CodeHash -or
                $reference.Count -ne 1 -or $reference[0].CodeHash -ne $method.CodeHash -or
                $reference[0].Address -ne $method.Address -or $reference[0].Pid -ne $owner) { throw 'Extracted code differs from retained owner/version.' }
            $binary = Join-Path $output "$($method.Index).bin"
            [IO.File]::WriteAllBytes($binary, $body)
            $path = $binary
            if ($UseWsl) {
                $path = (& wsl --exec wslpath -a $binary | Out-String).Trim()
                if ($LASTEXITCODE -ne 0 -or !$path) { throw 'Offline path translation failed.' }
            }
            $arguments = @('-D', '-w', '-z', '-b', 'binary', '-m', 'i386:x86-64', '-M', 'intel', "--adjust-vma=$($method.Address)", $path)
            $assembly = if ($UseWsl) { @(& wsl --exec $ObjDump @arguments) } else { @(& $ObjDump @arguments) }
            if ($LASTEXITCODE -ne 0) { throw 'Offline disassembler failed.' }
            $assembly | Set-Content (Join-Path $output "$($method.Index).asm.txt")
            $address = [Convert]::ToUInt64($method.Address.Substring(2), 16)
            $instructions = [Collections.Generic.List[object]]::new()
            $covered = 0
            foreach ($line in $assembly) {
                if ($line -notmatch '^\s*([0-9a-f]+):\s+((?:[0-9a-f]{2}\s+)+)\s*(\S.*)$') { continue }
                $ip = [Convert]::ToUInt64($Matches[1], 16)
                $bytes = @($Matches[2].Trim() -split '\s+')
                if ($ip -ne $address + $covered) { throw 'Disassembly instruction coverage discontinuity.' }
                $instructions.Add([pscustomobject]@{ Offset = $covered; Size = $bytes.Count; Instruction = $Matches[3]; Samples = 0 })
                $covered += $bytes.Count
            }
            if ($covered -ne $method.Size -or @($instructions | Where-Object Instruction -match '\(bad\)').Count) { throw 'Incomplete or invalid instruction decoding.' }
            $results.Add([pscustomobject]@{ Condition = $condition.Condition.Name; Side = $side; Pid = $owner
                RawHash = $rawHash; Method = $method; Instructions = $instructions; MeasuredSamples = 0 })
        }
        $pages = @{}
        foreach ($code in $dump.Methods) {
            if (!$code.Size) { continue }
            $start = [Convert]::ToUInt64($code.Address.Substring(2), 16)
            for ($page = $start -shr 12; $page -le (($start + $code.Size - 1) -shr 12); $page++) {
                $key = [string]$page
                if (!$pages.ContainsKey($key)) { $pages[$key] = [Collections.Generic.List[object]]::new() }
                $pages[$key].Add([pscustomobject]@{ Start = $start; End = $start + $code.Size; Method = $code })
            }
        }
        $windows[[string]$owner] = @{ Pages = $pages; Windows = $measured }
        if ((Get-FileHash $raw -Algorithm SHA256).Hash.ToLowerInvariant() -ne $rawHash) { throw 'Original jitdump changed during offline analysis.' }
    }
    $samples = @(Get-Content (Join-Path $directory 'cpu-events-stdout.txt'))
    foreach ($line in $samples) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^\s*(\d+)(?:/|\s+)(\d+)\s+(\d+\.\d+):\s+cpu-clock:u:\s+([0-9a-f]+)\s*$') { throw 'Unrecognized retained CPU sample.' }
        $owner = $Matches[1]
        if (!$windows.ContainsKey($owner)) { throw 'CPU sample owner differs from paired workers.' }
        $seconds = [decimal]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture)
        $ns = [ulong]($seconds * 1000000000)
        $ticks = [long]($seconds * $summary.StopwatchFrequency)
        $ip = [Convert]::ToUInt64($Matches[4], 16)
        $request = @($windows[$owner].Windows | Where-Object { $_.StartMonotonicTicks -le $ticks -and $_.EndMonotonicTicks -ge $ticks })
        if (!$request.Count) { continue }
        if ($request.Count -ne 1) { throw 'Sample belongs to overlapping measured requests.' }
        $method = @($windows[$owner].Pages[[string]($ip -shr 12)] | Where-Object {
            $_.Method.Timestamp -le $ns -and $_.Start -le $ip -and $ip -lt $_.End
        } | Sort-Object { $_.Method.Timestamp } -Descending | Select-Object -First 1 -ExpandProperty Method)
        if (!$method.Count) { continue }
        $target = @($results | Where-Object { $_.Condition -eq $condition.Condition.Name -and $_.Pid -eq [int]$owner -and $_.Method.Index -eq $method[0].Index })
        if (!$target.Count) { continue }
        if ($target.Count -ne 1) { throw 'Ambiguous native target.' }
        $offset = $ip - [Convert]::ToUInt64($target[0].Method.Address.Substring(2), 16)
        $instruction = @($target[0].Instructions | Where-Object { $_.Offset -le $offset -and $offset -lt $_.Offset + $_.Size })
        if ($instruction.Count -ne 1) { throw 'Sample has no decoded instruction.' }
        $instruction[0].Samples++
        $target[0].MeasuredSamples++
        $key = "$owner/$($request[0].Id)/$($method[0].Index)"
        $sampleCounts[$key] = 1 + $sampleCounts[$key]
    }
    $correlation = @(Get-Content (Join-Path $directory 'native-request-windows.ndjson') | ConvertFrom-Json)
    foreach ($target in @($results | Where-Object Condition -eq $condition.Condition.Name)) {
        $matchedRequests = 0
        foreach ($request in $windows[[string]$target.Pid].Windows) {
            $rows = @($correlation | Where-Object {
                $_.Pid -eq $target.Pid -and $_.Id -eq $request.Id -and $_.CodeIndex -eq $target.Method.Index
            })
            $key = "$($target.Pid)/$($request.Id)/$($target.Method.Index)"
            $count = [int]$sampleCounts[$key]
            if ($rows.Count -gt 1 -or ($rows.Count -eq 1 -and
                ($rows[0].Stage -ne 'measured' -or $rows[0].Transport -ne 'Segmented128' -or
                $rows[0].Samples -ne $count -or $rows[0].CodeHash -ne $target.Method.CodeHash)) -or
                ($rows.Count -eq 0 -and $count)) { throw 'Instruction samples differ from retained per-request correlation.' }
            if ($count) { $matchedRequests++ }
        }
        $target | Add-Member -NotePropertyName MatchedRequests -NotePropertyValue $matchedRequests
    }
}
foreach ($input in $inputs) {
    if ((Get-FileHash -LiteralPath (Join-Path $InputDirectory $input.Path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $input.Hash) {
        throw 'Retained analysis input changed.'
    }
}
if ((Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $scriptHash -or
    (Get-FileHash -LiteralPath $helperPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $helperHash) {
    throw 'Analysis code changed during decoding.'
}
$analysisSha = (& git -C $PSScriptRoot rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Analysis repository revision unavailable.' }
$inputs | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'input-fingerprints.json')
foreach ($result in $results) {
    $result.Instructions | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory "$($result.Condition)/$($result.Side)/$($result.Method.Index)-instructions.json")
}
$results | Select-Object Condition, Side, Pid, RawHash, Method, MeasuredSamples, MatchedRequests |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDirectory 'methods.json')
[pscustomobject]@{ DiagnosticOnly = $true; OriginRun = $OriginRun; CaptureSha = $summary.DiagnosticSha
    SourceSha = $summary.SourceSha; AnalysisSha = $analysisSha; MeasuredWorkersStarted = 0
    ScriptHash = $scriptHash; HelperHash = $helperHash
    DisassemblerVersion = $toolVersion[0]; AnalysisComplete = $true
    Scope = 'retained-segmented-request-IP-samples-not-exact-instruction-latency-or-inline-source-map' } |
    ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'analysis.json')
Write-Host "Decoded $($results.Count) retained native bodies; no workload or new sampling."
