param([Parameter(Mandatory = $true)][string]$WorkerDll)
$ErrorActionPreference = 'Stop'
if (!$IsLinux) { throw 'Pinned verification tests require Linux.' }
. (Join-Path $PSScriptRoot 'control.ps1')
. (Join-Path $PSScriptRoot 'history-evidence.ps1')
$WorkerDll = (Resolve-Path -LiteralPath $WorkerDll).Path
$status = Get-Content /proc/self/status | Select-String '^Cpus_allowed_list:\s*([0-9,-]+)$'
$cpu = (($status.Matches[0].Groups[1].Value -split ',')[0] -split '-')[0]
if ($cpu -notmatch '^\d+$') { throw 'Test CPU unavailable.' }
function Test-VerificationWorker([string]$Protocol, [string]$Mode, [bool]$Reject, [switch]$PrepareBeforeVerify) {
    $info = [Diagnostics.ProcessStartInfo]::new('taskset')
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    Set-CsvPipeWorkerEnvironment $info
    $info.Environment['HERO_PARSER_WORKER_PROTOCOL'] = $Protocol
    $info.Environment['HERO_PARSER_WORKER_VERIFICATION_MODE'] = $Mode
    $info.Environment['HERO_PARSER_WORKER_CPU'] = $cpu
    foreach ($argument in @('--cpu-list', $cpu, 'dotnet', $WorkerDll, '--rows', '2000')) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    $errors = $process.StandardError.ReadToEndAsync()
    try {
        if ($Reject -and !$PrepareBeforeVerify) {
            if (!$process.WaitForExit(10000) -or !$process.ExitCode) { throw 'Invalid worker protocol/treatment was accepted.' }
            return
        }
        $line = $process.StandardOutput.ReadLineAsync()
        if (!$line.Wait(30000)) { throw 'Verification test startup timed out.' }
        $environment = $line.Result | ConvertFrom-Json
        if ($environment.pid -ne $process.Id -or $environment.protocol -ne $Protocol -or
            $environment.verificationMode -ne $Mode -or $environment.allowedCpus -ne $cpu) { throw 'Test worker identity mismatch.' }
        if ($PrepareBeforeVerify) {
            $process.StandardInput.WriteLine('{"Id":1,"Operation":"prepare","Transport":"Segmented128","Repeats":0}')
            $process.StandardInput.Flush()
            if (!$process.WaitForExit(10000) -or !$process.ExitCode) { throw 'An unverified worker prepared parser work.' }
            return
        }
        $process.StandardInput.WriteLine('{"Id":1,"Operation":"verify","Transport":null,"Repeats":0}')
        $process.StandardInput.Flush()
        $line = $process.StandardOutput.ReadLineAsync()
        if (!$line.Wait(60000)) { throw 'Verification response timed out.' }
        $response = $line.Result | ConvertFrom-Json
        if ($response.pid -ne $process.Id) { throw 'Verification response ownership mismatch.' }
        if ($Mode -eq 'matrix') { Assert-CsvPipeWorkerVerification $response }
        elseif ($response.verificationMode -ne 'external' -or @($response.checks).Count) { throw 'External verification executed the matrix.' }
        $process.StandardInput.WriteLine('{"Id":2,"Operation":"stop","Transport":null,"Repeats":0}')
        $process.StandardInput.Flush()
        $line = $process.StandardOutput.ReadLineAsync()
        if (!$line.Wait(30000)) { throw 'Test worker stop timed out.' }
        $response = $line.Result | ConvertFrom-Json
        if ($response.kind -ne 'worker-stopped' -or $response.pid -ne $process.Id -or $response.Id -ne 2 -or
            !$process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'Test worker stop failed.' }
    }
    finally {
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $stderr = $errors.GetAwaiter().GetResult()
        $process.Dispose()
        if (!$Reject -and $stderr) { throw "Verification test emitted stderr: $stderr" }
    }
}
Test-VerificationWorker 'csv-pipe-verification-boundary-v1' 'matrix' $false
Test-VerificationWorker 'csv-pipe-verification-boundary-v1' 'external' $false
Test-VerificationWorker 'csv-pipe-isolated-v4-same-cpu' 'external' $true
Test-VerificationWorker 'csv-pipe-verification-boundary-v1' 'invalid' $true
Test-VerificationWorker 'csv-pipe-verification-boundary-v1' 'external' $true -PrepareBeforeVerify
Write-Host 'PASS: five verification protocol tests; no batch commands, benchmarks or sampling'
