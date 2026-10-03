param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$Workspace,
    [string]$OutputDirectory = 'BenchmarkDotNet.Artifacts/pipe-build-provenance'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'history-evidence.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Workspace = [IO.Path]::GetFullPath($Workspace)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $Workspace) { throw 'Build regression requires a fresh workspace.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Do not overwrite build provenance.' }
$source = '89c06810e76c4623ebad3cfc89c4bcdef41acd59'
$input = Join-Path $InputDirectory 'isolated-aa-2.ndjson'
if ((Get-FileHash $input -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'edcda1bca129eeb6409bad58afc3661ddf30aecafe44115654174d670cb4d921') {
    throw 'Build regression requires authenticated historical fingerprints.'
}
$original = Get-Content $input | ConvertFrom-Json | Where-Object kind -eq 'environment' | Select-Object -First 1
if ($original.baselineRef -ne $source) { throw 'Unexpected frozen build source.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$records = [Collections.Generic.List[object]]::new()
$added = $false
$failure = $null
function Save-BuildFingerprint([string]$Phase, [string]$Label, [string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return }
    $sourceLink = $null
    $pdb = [IO.Path]::ChangeExtension($Path, '.pdb')
    if (Test-Path -LiteralPath $pdb) {
        $stream = [IO.File]::OpenRead($pdb)
        $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
        try {
            $reader = $provider.GetMetadataReader([Reflection.Metadata.MetadataReaderOptions]::Default, $null)
            foreach ($handle in $reader.CustomDebugInformation) {
                $entry = $reader.GetCustomDebugInformation($handle)
                if ($reader.GetGuid($entry.Kind) -eq [guid]'cc110556-a091-4d38-9fec-25ab9a351a6a') {
                    $sourceLink = [Text.Encoding]::UTF8.GetString($reader.GetBlobBytes($entry.Value))
                }
            }
        }
        finally { $provider.Dispose(); $stream.Dispose() }
    }
    $record = [pscustomobject]@{ Phase = $Phase; Label = $Label
        Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        Bytes = (Get-Item -LiteralPath $Path).Length
        Version = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path).ProductVersion; SourceLink = $sourceLink }
    $records.Add($record)
    $record | ConvertTo-Json -Compress | Add-Content (Join-Path $OutputDirectory 'fingerprints.ndjson')
    Write-Host "$Phase/$Label $($record.Hash) $($record.Version)"
    return $record
}
function Build-RegressionProject([string]$Phase, [string[]]$Arguments) {
    & dotnet build @Arguments --disable-build-servers -p:UseSharedCompilation=false "-bl:$(Join-Path $OutputDirectory "$Phase.binlog")" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build provenance phase failed: $Phase" }
}
try {
    & git -C $root worktree add --detach $Workspace $source
    if ($LASTEXITCODE -ne 0) { throw 'Frozen build regression checkout failed.' }
    $added = $true
    $probe = Join-Path $Workspace 'benchmarks/CsvPipeABProbe'
    $parser = Join-Path $Workspace 'src/HeroParser/bin/Release/net10.0/HeroParser.dll'
    $models = Join-Path $probe 'Models/bin/Release/isolated/CsvPipeABModels.dll'
    $null = Save-BuildFingerprint 'prior-head' 'parser' (Join-Path $root 'src/HeroParser/bin/Release/net10.0/HeroParser.dll')
    $null = Save-BuildFingerprint 'prior-head' 'models' (Join-Path $PSScriptRoot 'Models/bin/Release/boundary/CsvPipeABModels.dll')
    Build-RegressionProject 'frozen-parser' @((Join-Path $Workspace 'src/HeroParser/HeroParser.csproj'), '-c', 'Release', '-f', 'net10.0')
    $parserBefore = Save-BuildFingerprint 'frozen-parser' 'parser' $parser
    Build-RegressionProject 'frozen-models' @((Join-Path $probe 'Models/CsvPipeABModels.csproj'), '-c', 'Release', '-p:AssemblyName=CsvPipeABModels', "-p:ParserDll=$parser", '-p:BaseIntermediateOutputPath=obj/isolated/', '-p:OutputPath=bin/Release/isolated/')
    $modelsBefore = Save-BuildFingerprint 'frozen-models' 'models' $models
    $null = Save-BuildFingerprint 'frozen-models' 'parser' $parser
    Build-RegressionProject 'diagnostic-driver' @((Join-Path $PSScriptRoot 'CsvPipeABProbe.csproj'), '-c', 'Release', '-p:SingleModule=true', "-p:CandidateModelsDll=$models", '-p:BaseIntermediateOutputPath=obj/boundary/', '-p:OutputPath=bin/Release/boundary/', "-p:FrozenParserDll=$parser")
    $parserAfter = Save-BuildFingerprint 'after-driver' 'frozen-parser' $parser
    $modelsAfter = Save-BuildFingerprint 'after-driver' 'frozen-models' $models
    $parserCopy = Save-BuildFingerprint 'after-driver' 'copied-parser' (Join-Path $PSScriptRoot 'bin/Release/boundary/HeroParser.dll')
    $modelsCopy = Save-BuildFingerprint 'after-driver' 'copied-models' (Join-Path $PSScriptRoot 'bin/Release/boundary/CsvPipeABModels.dll')
    foreach ($pair in @(@($parserBefore, $parserAfter, $parserCopy, $original.baselineWorker.parserHash),
        @($modelsBefore, $modelsAfter, $modelsCopy, $original.baselineWorker.modelsHash))) {
        if (@($pair[0..2] | Where-Object { $null -eq $_ -or $_.Hash -ne $pair[3] }).Count) {
            throw 'Frozen build/copy provenance differs from authenticated history; see retained phase fingerprints and binlogs.'
        }
    }
    Write-Host 'PASS: frozen build/copy provenance; zero worker startups and zero batch commands'
}
catch { $failure = $_.Exception.Message; throw }
finally {
    [pscustomobject]@{ SourceSha = $source; MeasuredWorkersStarted = 0; BatchCommands = 0
        Failure = $failure; Fingerprints = @($records.ToArray()) } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'build-summary.json')
    if ($added) {
        if ((Resolve-Path -LiteralPath $Workspace).Path -ne $Workspace -or
            @(& git -C $Workspace status --porcelain).Count -or $LASTEXITCODE -ne 0) { throw 'Unsafe build regression cleanup; retaining checkout.' }
        & git -C $root worktree remove $Workspace
        if ($LASTEXITCODE -ne 0) { throw 'Build regression checkout cleanup failed.' }
    }
}
