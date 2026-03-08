param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot "ThumbnailGridStudio.WinUI\ThumbnailGridStudio.WinUI.csproj"
$dotnetHome = Join-Path $scriptRoot ".dotnet"
$nugetRoot = Join-Path $scriptRoot ".nuget"
$nugetPackages = Join-Path $nugetRoot "packages"
$nugetHttpCache = Join-Path $nugetRoot "http-cache"
$nugetPluginsCache = Join-Path $nugetRoot "plugins-cache"
$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:NUGET_PACKAGES = $nugetPackages
$env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache
$env:NUGET_PLUGINS_CACHE_PATH = $nugetPluginsCache
foreach ($path in @($dotnetHome, $nugetRoot, $nugetPackages, $nugetHttpCache, $nugetPluginsCache)) {
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
    }
}

function Get-MSBuildExtensionsPath {
    $known = @(
        "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild",
        "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild",
        "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild",
        "C:\Program Files\Microsoft Visual Studio\17\Community\MSBuild",
        "C:\Program Files\Microsoft Visual Studio\17\Professional\MSBuild",
        "C:\Program Files\Microsoft Visual Studio\17\Enterprise\MSBuild",
        "C:\Program Files (x86)\Microsoft Visual Studio\17\BuildTools\MSBuild"
    )

    foreach ($path in $known) {
        if (Test-Path (Join-Path $path "Microsoft\VisualStudio")) {
            return $path
        }
    }

    return $null
}

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

$msbuildExtensionsPath = Get-MSBuildExtensionsPath
if ([string]::IsNullOrWhiteSpace($msbuildExtensionsPath)) {
    Write-Warning "MSBuildExtensionsPath not found. Using default dotnet build settings."
    dotnet build $project -c $Configuration -p:Platform=$Platform
    exit $LASTEXITCODE
}

Write-Host "Using MSBuildExtensionsPath: $msbuildExtensionsPath"
dotnet build $project `
    -c $Configuration `
    -p:Platform=$Platform `
    -p:MSBuildExtensionsPath="$msbuildExtensionsPath"

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Build succeeded."
