# Only the retained September 30 diagnostic uses this Linux x64 toolchain.
# Package identity: https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
# SDK 10.0.401 archive SHA512:
# 51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b
# The frozen models inherited this URL from the original checkout. A clone's
# optional .git remote suffix changes AssemblyMetadata and deterministic bytes.
$csvPipeHistoryModelRepositoryUrl = 'https://github.com/KoalaFacts/HeroParser'

function Get-CsvPipeHistoryBuildArguments([string[]]$Arguments) {
    if ([IO.Path]::GetFileName($Arguments[0]) -ceq 'CsvPipeABModels.csproj') {
        return @($Arguments) + @("-p:RepositoryUrl=$csvPipeHistoryModelRepositoryUrl")
    }
    return $Arguments
}

function Invoke-CsvPipeHistoryProcess([string]$Executable, [string[]]$Arguments, [long]$DeadlineTicks) {
    function Get-RemainingMilliseconds {
        $remaining = ($DeadlineTicks - [Diagnostics.Stopwatch]::GetTimestamp()) * 1000.0 / [Diagnostics.Stopwatch]::Frequency
        if ($remaining -le 0) { return 0 }
        return [int][Math]::Min([int]::MaxValue, [Math]::Ceiling($remaining))
    }
    # GNU timeout owns a separate process group, so cleanup can reach descendants
    # even if their direct parent already exited while they retain an output pipe.
    $timeout = (Get-Command timeout -CommandType Application | Select-Object -First 1).Source
    $kill = (Get-Command kill -CommandType Application | Select-Object -First 1).Source
    $remaining = Get-RemainingMilliseconds
    if ($remaining -eq 0) { throw 'Historical build deadline expired; no subprocess started.' }
    $duration = ($remaining / 1000.0).ToString('0.000', [Globalization.CultureInfo]::InvariantCulture) + 's'
    $info = [Diagnostics.ProcessStartInfo]::new($timeout)
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in (@('--signal=KILL', '--', $duration, $Executable) + $Arguments)) { $info.ArgumentList.Add($argument) }
    if ((Get-RemainingMilliseconds) -eq 0) { throw 'Historical build deadline expired; no subprocess started.' }
    $process = [Diagnostics.Process]::Start($info)
    $output = $process.StandardOutput.ReadToEndAsync()
    $errors = $process.StandardError.ReadToEndAsync()
    $streams = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($output, $errors))
    $timedOut = $false
    $exitCode = -1
    try {
        if (!$process.WaitForExit((Get-RemainingMilliseconds))) {
            $timedOut = $true
            $process.Kill($true)
        }
        # Pipes can outlive the direct process; never wait for them beyond the same deadline.
        if (!$streams.Wait((Get-RemainingMilliseconds))) { $timedOut = $true }
        if ($process.HasExited -and $process.ExitCode -in @(124, 137)) { $timedOut = $true }
    }
    finally {
        # A successful parent can leave children that closed their output pipes.
        # End the owned process group on every completion, not just a timeout.
        $cleanupInfo = [Diagnostics.ProcessStartInfo]::new($kill)
        $cleanupInfo.UseShellExecute = $false
        $cleanupInfo.RedirectStandardError = $true
        foreach ($argument in @('-KILL', '--', "-$($process.Id)")) { $cleanupInfo.ArgumentList.Add($argument) }
        $cleanup = [Diagnostics.Process]::Start($cleanupInfo)
        try { if (!$cleanup.WaitForExit(1000)) { $cleanup.Kill() } }
        finally { $cleanup.Dispose() }
        if (!$process.HasExited) { $process.Kill($true) }
        # Cleanup/draining has a bounded grace period after the work deadline.
        # Compose the result afterward, retaining text emitted before timeout.
        $null = $process.WaitForExit(1000)
        if (!$streams.IsCompleted) { $null = $streams.Wait(1000) }
        if ($process.HasExited) { $exitCode = $process.ExitCode }
        $process.Dispose()
    }
    return [pscustomobject]@{
        TimedOut = $timedOut
        ExitCode = $exitCode
        StandardOutput = if ($output.IsCompletedSuccessfully) { $output.GetAwaiter().GetResult() } else { '' }
        StandardError = if ($errors.IsCompletedSuccessfully) { $errors.GetAwaiter().GetResult() } else { 'Output incomplete after historical deadline cleanup.' }
    }
}

function Get-CsvPipeHistoryCommandOutput([string]$Dotnet, [string[]]$Arguments, [long]$DeadlineTicks) {
    $result = Invoke-CsvPipeHistoryProcess $Dotnet $Arguments $DeadlineTicks
    if ($result.TimedOut) { throw 'Historical build deadline expired during toolchain evaluation.' }
    if ($result.ExitCode -ne 0) { throw "Historical toolchain evaluation failed: $($result.StandardError)" }
    return $result.StandardOutput
}

function Get-CsvPipeHistoryDirectoryHash([string]$Directory) {
    $root = (Resolve-Path -LiteralPath $Directory).Path
    [string[]]$files = [IO.Directory]::GetFiles($root, '*', [IO.SearchOption]::AllDirectories)
    [Array]::Sort($files, [StringComparer]::Ordinal)
    $manifest = [Text.StringBuilder]::new()
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($root, $file).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $null = $manifest.Append($relative).Append([char]0).Append($hash).Append("`n")
    }
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($manifest.ToString()))).ToLowerInvariant()
}

function Assert-CsvPipeHistoryToolchainIdentity($Identity) {
    $expected = @{
        Sdk = '10.0.401'
        Compiler = '5.9.0-1.26423.113 (e34a38d2ae1fc26406a317517196e55c68ff83ab)'
        CompilerHash = 'dd115f0fb8248a4ef5652d9d39e65c7cbef3b45c3e9ccee8542f7fa4600af433'
        ReferencePack = '10.0.12'
        ReferencePackHash = '4f365796a02170a3f7703cc6b3a753460c17d473391f01f2dffc7f7deba5a64b'
        Runtime = '10.0.12'
        RuntimeHash = 'a94a0a855dda23066c16683163baea26e91ea0d3eba371d8b4d5fb848bcea636'
    }
    foreach ($field in $expected.Keys) {
        if ($Identity.$field -cne $expected[$field]) {
            throw "Historical toolchain mismatch: $field. Expected $($expected[$field]); found $($Identity.$field). Do not rebuild or replace historical hashes."
        }
    }
}

function Get-CsvPipeHistoryToolchain {
    param([long]$DeadlineTicks = ([Diagnostics.Stopwatch]::GetTimestamp() + [Diagnostics.Stopwatch]::Frequency * 900))
    if (!$IsLinux -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
        throw 'Historical build provenance requires the original Linux x64 toolchain.'
    }
    if (!$env:DOTNET_ROOT) { throw 'Set DOTNET_ROOT to an isolated SDK 10.0.401 installation.' }
    $root = (Resolve-Path -LiteralPath $env:DOTNET_ROOT).Path.TrimEnd('/')
    $dotnet = Join-Path $root 'dotnet'
    if ((Get-Command dotnet -CommandType Application | Select-Object -First 1).Source -cne $dotnet) {
        throw 'PATH must select the isolated historical DOTNET_ROOT/dotnet host.'
    }
    foreach ($name in @('MSBuildSDKsPath', 'MSBUILD_EXE_PATH', 'MSBUILDADDITIONALSDKRESOLVERSFOLDER',
        'DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR', 'DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR',
        'DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER', 'RoslynTargetsPath', 'CscToolPath', 'CscToolExe')) {
        if ([Environment]::GetEnvironmentVariable($name)) { throw "Uncontrolled historical toolchain override: $name" }
    }
    $sdks = @((Get-CsvPipeHistoryCommandOutput $dotnet @('--list-sdks') $DeadlineTicks) -split '\r?\n' | Where-Object { $_ })
    if ( $sdks.Count -ne 1 -or $sdks[0] -cne "10.0.401 [$root/sdk]") {
        throw 'Historical DOTNET_ROOT must contain only SDK 10.0.401.'
    }
    $runtimes = @((Get-CsvPipeHistoryCommandOutput $dotnet @('--list-runtimes') $DeadlineTicks) -split '\r?\n' | Where-Object { $_ })
    if ( @($runtimes | Where-Object { $_ -notmatch '^Microsoft\.(NETCore|AspNetCore)\.App 10\.0\.12 \[' }).Count -or
        @($runtimes | Where-Object { $_ -ceq "Microsoft.NETCore.App 10.0.12 [$root/shared/Microsoft.NETCore.App]" }).Count -ne 1) {
        throw 'Historical DOTNET_ROOT must contain only runtime 10.0.12.'
    }
    $packs = @(Get-ChildItem -LiteralPath (Join-Path $root 'packs/Microsoft.NETCore.App.Ref') -Directory)
    if ($packs.Count -ne 1 -or $packs[0].Name -cne '10.0.12') { throw 'Historical reference pack must be exactly 10.0.12.' }
    $compilerPath = Join-Path $root 'sdk/10.0.401/Roslyn/bincore/csc.dll'
    $sdk = (Get-CsvPipeHistoryCommandOutput $dotnet @('--version') $DeadlineTicks).Trim()
    $compiler = (Get-CsvPipeHistoryCommandOutput $dotnet @($compilerPath, '-version') $DeadlineTicks).Trim()
    $identity = [pscustomobject]@{
        Dotnet = $dotnet; Root = $root; Sdk = $sdk; Compiler = $compiler
        CompilerHash = Get-CsvPipeHistoryDirectoryHash (Split-Path -Parent $compilerPath)
        ReferencePack = $packs[0].Name
        ReferencePackHash = Get-CsvPipeHistoryDirectoryHash $packs[0].FullName
        Runtime = '10.0.12'
        RuntimeHash = Get-CsvPipeHistoryDirectoryHash (Join-Path $root 'shared/Microsoft.NETCore.App/10.0.12')
    }
    Assert-CsvPipeHistoryToolchainIdentity $identity
    return $identity
}

function Assert-CsvPipeHistoryProjectToolchain($Toolchain, [string[]]$BuildArguments,
    [long]$DeadlineTicks = ([Diagnostics.Stopwatch]::GetTimestamp() + [Diagnostics.Stopwatch]::Frequency * 900)) {
    $project = $BuildArguments[0]
    $buildProperties = @($BuildArguments | Where-Object { $_.StartsWith('-p:', [StringComparison]::Ordinal) })
    for ($index = 1; $index -lt $BuildArguments.Count; $index++) {
        $property = switch -CaseSensitive ($BuildArguments[$index]) {
            '-c' { 'Configuration' }
            '--configuration' { 'Configuration' }
            '-f' { 'TargetFramework' }
            '--framework' { 'TargetFramework' }
        }
        if ($property) {
            if (++$index -ge $BuildArguments.Count) { throw "Missing historical build value: $property" }
            $buildProperties += "-p:$property=$($BuildArguments[$index])"
        }
    }
    $arguments = @('msbuild', $project, '-p:TargetFramework=net10.0', '-getProperty:NETCoreSdkVersion,MSBuildToolsPath,RoslynTargetsPath,NetCoreTargetingPackRoot,CscToolPath,CscToolExe,RepositoryUrl,Configuration,UseSharedCompilation', '-getItem:KnownFrameworkReference') + $buildProperties
    $json = Get-CsvPipeHistoryCommandOutput $Toolchain.Dotnet $arguments $DeadlineTicks
    $selection = ($json | Out-String) | ConvertFrom-Json
    $properties = $selection.Properties
    $framework = @($selection.Items.KnownFrameworkReference | Where-Object { $_.Identity -eq 'Microsoft.NETCore.App' -and $_.TargetFramework -eq 'net10.0' })
    if ($properties.NETCoreSdkVersion -cne $Toolchain.Sdk -or
        [IO.Path]::GetFullPath($properties.MSBuildToolsPath).TrimEnd('/') -cne "$($Toolchain.Root)/sdk/10.0.401" -or
        [IO.Path]::GetFullPath($properties.RoslynTargetsPath).TrimEnd('/') -cne "$($Toolchain.Root)/sdk/10.0.401/Roslyn" -or
        [IO.Path]::GetFullPath($properties.NetCoreTargetingPackRoot).TrimEnd('/') -cne "$($Toolchain.Root)/packs" -or
        $properties.CscToolPath -or $properties.CscToolExe -or
        $framework.Count -ne 1 -or $framework[0].TargetingPackVersion -cne $Toolchain.ReferencePack) {
        throw 'Project selected a different historical SDK, compiler or reference pack.'
    }
    if ([IO.Path]::GetFileName($project) -ceq 'CsvPipeABModels.csproj' -and
        $properties.RepositoryUrl -cne $csvPipeHistoryModelRepositoryUrl) {
        throw 'Historical model RepositoryUrl differs from the original checkout; do not replace historical hashes.'
    }
    return $selection
}
