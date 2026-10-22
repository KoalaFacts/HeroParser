$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'history-toolchain.ps1')
$expectedUrl = 'https://github.com/KoalaFacts/HeroParser'
$modelArguments = @('/synthetic/Models/CsvPipeABModels.csproj', '-c', 'Release', '-p:AssemblyName=CsvPipeABModels', '-p:ParserDll=/synthetic/HeroParser.dll')
$prepared = $modelArguments
if (Get-Command Get-CsvPipeHistoryBuildArguments -ErrorAction SilentlyContinue) {
    $prepared = @(Get-CsvPipeHistoryBuildArguments $modelArguments)
}
if ($prepared[-1] -cne "-p:RepositoryUrl=$expectedUrl" -or
    ($prepared[0..($modelArguments.Count - 1)] -join "`0") -cne ($modelArguments -join "`0")) {
    throw 'Historical model build did not preserve its arguments and pin the original RepositoryUrl.'
}
$checks = 1
foreach ($project in @('/synthetic/HeroParser.csproj', '/synthetic/CsvPipeABProbe.csproj')) {
    $arguments = @($project, '-c', 'Release')
    if ((@(Get-CsvPipeHistoryBuildArguments $arguments) -join "`0") -cne ($arguments -join "`0")) {
        throw 'Historical model RepositoryUrl changed a parser or driver build.'
    }
    $checks++
}

# Replace only the external MSBuild evaluation boundary. Its synthetic clone
# has a .git remote suffix; only the real command property can override it.
$toolchain = [pscustomobject]@{ Dotnet = '/unused/dotnet'; Root = '/synthetic/sdk'; Sdk = '10.0.401'; ReferencePack = '10.0.12' }
$script:evaluatedArguments = @()
$script:ignoreRepositoryOverride = $false
function Get-CsvPipeHistoryCommandOutput([string]$Dotnet, [string[]]$Arguments, [long]$DeadlineTicks) {
    $script:evaluatedArguments = $Arguments
    $repositoryUrl = "$expectedUrl.git"
    if (!$script:ignoreRepositoryOverride) {
        foreach ($argument in $Arguments) {
            if ($argument.StartsWith('-p:RepositoryUrl=', [StringComparison]::Ordinal)) {
                $repositoryUrl = $argument.Substring('-p:RepositoryUrl='.Length)
            }
        }
    }
    return (@{
        Properties = @{
            NETCoreSdkVersion = '10.0.401'; MSBuildToolsPath = '/synthetic/sdk/sdk/10.0.401'
            RoslynTargetsPath = '/synthetic/sdk/sdk/10.0.401/Roslyn'; NetCoreTargetingPackRoot = '/synthetic/sdk/packs'
            CscToolPath = ''; CscToolExe = ''; RepositoryUrl = $repositoryUrl
            Configuration = if ('-p:Configuration=Release' -cin $Arguments) { 'Release' } else { 'Debug' }
            UseSharedCompilation = if ('-p:UseSharedCompilation=false' -cin $Arguments) { 'false' } else { 'true' }
        }
        Items = @{ KnownFrameworkReference = @(@{ Identity = 'Microsoft.NETCore.App'; TargetFramework = 'net10.0'; TargetingPackVersion = '10.0.12' }) }
    } | ConvertTo-Json -Depth 6)
}
$selection = Assert-CsvPipeHistoryProjectToolchain $toolchain $prepared
if ($selection.Properties.RepositoryUrl -cne $expectedUrl -or
    $selection.Properties.Configuration -cne 'Release' -or
    "-p:RepositoryUrl=$expectedUrl" -cnotin $script:evaluatedArguments -or
    '-p:ParserDll=/synthetic/HeroParser.dll' -cnotin $script:evaluatedArguments -or
    @($script:evaluatedArguments | Where-Object { $_ -like '-getProperty:*RepositoryUrl*' }).Count -ne 1) {
    throw 'Historical model preflight did not evaluate and retain the exact build properties.'
}
$checks++
$script:ignoreRepositoryOverride = $true
$rejected = $false
try { $null = Assert-CsvPipeHistoryProjectToolchain $toolchain $prepared } catch {
    $rejected = $_.Exception.Message -like '*RepositoryUrl*'
}
if (!$rejected) { throw 'Historical model preflight accepted a .git-suffixed RepositoryUrl.' }
$checks++
$selection = Assert-CsvPipeHistoryProjectToolchain $toolchain @('/synthetic/CsvPipeABProbe.csproj')
if ($selection.Properties.RepositoryUrl -cne "$expectedUrl.git") { throw 'Model URL policy leaked into a driver evaluation.' }
$checks++

# Exercise the real build-only coordinator. Only its external process boundary
# is synthetic, so neither MSBuild nor the parser is ever started.
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
try {
    $null = New-Item -ItemType Directory -Path $testDirectory
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'history-test-frozen-build.ps1'), [ref]$null, [ref]$null)
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Build-RegressionProject' }, $true)
    Invoke-Expression $function.Extent.Text
    $script:projectBuilds = 0
    $deadlineTicks = [Diagnostics.Stopwatch]::GetTimestamp() + [Diagnostics.Stopwatch]::Frequency * 5
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $OutputDirectory = $testDirectory
    $script:ignoreRepositoryOverride = $false
    $script:builtArguments = @()
    function Invoke-CsvPipeHistoryProcess([string]$Executable, [string[]]$Arguments, [long]$DeadlineTicks) {
        $script:builtArguments = $Arguments
        return [pscustomobject]@{ TimedOut = $false; ExitCode = 0; StandardOutput = 'synthetic'; StandardError = '' }
    }
    Build-RegressionProject 'synthetic-models' $modelArguments
    $retained = Get-Content (Join-Path $testDirectory 'synthetic-models-toolchain.json') -Raw | ConvertFrom-Json
    if ($script:projectBuilds -ne 1 -or $retained.Properties.RepositoryUrl -cne $expectedUrl -or
        $retained.Properties.Configuration -cne 'Release' -or $retained.Properties.UseSharedCompilation -cne 'false' -or
        "-p:RepositoryUrl=$expectedUrl" -cnotin $script:builtArguments -or
        '-p:UseSharedCompilation=false' -cnotin $script:builtArguments -or
        '-p:ParserDll=/synthetic/HeroParser.dll' -cnotin $script:builtArguments) {
        throw 'Build coordinator lost the original model URL or its evaluated provenance.'
    }
    $checks++
}
finally { if (Test-Path -LiteralPath $testDirectory) { Remove-Item -LiteralPath $testDirectory -Recurse -Force } }
Write-Host "PASS: $checks historical RepositoryUrl checks; synthetic evaluator only, no builds or parser workers"
