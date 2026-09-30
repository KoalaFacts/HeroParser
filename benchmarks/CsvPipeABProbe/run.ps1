param(
    [Parameter(Mandatory = $true)][string]$BaselineDll,
    [string]$BaselineGeneratorProject = '',
    [string]$BaselineRef = '',
    [string]$CandidateRef = '',
    [int]$Rows = 2000,
    [int]$Pairs = 20,
    [int]$WarmupPairs = 6,
    [int]$MinSampleMs = 30,
    [switch]$VerifyOnly,
    [switch]$DisableBuildServers,
    [string]$Scenario = '',
    [string]$Path = '',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$BaselineDll = (Resolve-Path -LiteralPath $BaselineDll).Path
if (!$BaselineGeneratorProject) {
    $BaselineGeneratorProject = Join-Path (Split-Path -Parent $BaselineDll) '../../../../HeroParser.Generators/HeroParser.Generators.csproj'
}
$BaselineGeneratorProject = (Resolve-Path -LiteralPath $BaselineGeneratorProject).Path
$parserProject = Join-Path $root 'src/HeroParser/HeroParser.csproj'
$candidateDll = Join-Path $root 'src/HeroParser/bin/Release/net10.0/HeroParser.dll'
$modelProject = Join-Path $PSScriptRoot 'Models/CsvPipeABModels.csproj'
$baselineModels = Join-Path $PSScriptRoot 'Models/bin/Release/baseline/CsvPipeABModels.Baseline.dll'
$candidateModels = Join-Path $PSScriptRoot 'Models/bin/Release/candidate/CsvPipeABModels.Candidate.dll'

function Invoke-Build([string[]]$BuildArgs) {
    if ($DisableBuildServers) { $BuildArgs += @('--disable-build-servers', '-p:UseSharedCompilation=false') }
    & dotnet build @BuildArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
}

Invoke-Build @($parserProject, '-c', 'Release', '-f', 'net10.0')
foreach ($side in @('Baseline', 'Candidate')) {
    $parser = if ($side -eq 'Baseline') { $BaselineDll } else { $candidateDll }
    $directory = $side.ToLowerInvariant()
    $generator = if ($side -eq 'Baseline') { $BaselineGeneratorProject } else { Join-Path $root 'src/HeroParser.Generators/HeroParser.Generators.csproj' }
    Invoke-Build @($modelProject, '-c', 'Release', "-p:AssemblyName=CsvPipeABModels.$side",
        "-p:ParserDll=$parser", "-p:GeneratorProject=$generator", "-p:BaseIntermediateOutputPath=obj/$directory/",
        "-p:OutputPath=bin/Release/$directory/")
}
Invoke-Build @((Join-Path $PSScriptRoot 'CsvPipeABProbe.csproj'), '-c', 'Release',
    "-p:BaselineDll=$BaselineDll", "-p:BaselineModelsDll=$baselineModels", "-p:CandidateModelsDll=$candidateModels")

$probeArgs = @('--rows', $Rows, '--pairs', $Pairs, '--warmup-pairs', $WarmupPairs, '--min-sample-ms', $MinSampleMs)
if ($VerifyOnly) { $probeArgs += '--verify-only' }
if ($Scenario) { $probeArgs += @('--scenario', $Scenario) }
if ($Path) { $probeArgs += @('--path', $Path) }
$oldBaseline = $env:HERO_PARSER_AB_BASELINE_REF
$oldCandidate = $env:HERO_PARSER_AB_CANDIDATE_REF
try {
    $env:HERO_PARSER_AB_BASELINE_REF = $BaselineRef
    $env:HERO_PARSER_AB_CANDIDATE_REF = $CandidateRef
    $program = Join-Path $PSScriptRoot 'bin/Release/net10.0/CsvPipeABProbe.dll'
    if ($Output) {
        $Output = [IO.Path]::GetFullPath($Output)
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $Output) -Force
        & dotnet $program @probeArgs | Tee-Object -FilePath $Output
    }
    else {
        & dotnet $program @probeArgs
    }
    $exitCode = $LASTEXITCODE
}
finally {
    $env:HERO_PARSER_AB_BASELINE_REF = $oldBaseline
    $env:HERO_PARSER_AB_CANDIDATE_REF = $oldCandidate
}
exit $exitCode
