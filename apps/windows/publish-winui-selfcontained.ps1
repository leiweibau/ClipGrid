param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Platform = "x64",
    [string]$OutputDir = "apps/windows/dist/Thumbnail Grid Studio",
    [bool]$CreateZip = $true
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
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
$publishOutputDir = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
} else {
    Join-Path $repoRoot $OutputDir
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

function Resolve-TargetFramework([string]$projectPath) {
    $xml = [xml](Get-Content $projectPath -Raw)
    foreach ($pg in $xml.Project.PropertyGroup) {
        if ($pg.TargetFramework -and -not [string]::IsNullOrWhiteSpace($pg.TargetFramework)) {
            return $pg.TargetFramework
        }
    }
    throw "Could not resolve TargetFramework from $projectPath"
}

function Resolve-VersionTag([string]$projectPath) {
    $xml = [xml](Get-Content $projectPath -Raw)

    foreach ($pg in $xml.Project.PropertyGroup) {
        if ($pg.Version -and -not [string]::IsNullOrWhiteSpace($pg.Version)) {
            return $pg.Version.Trim()
        }
    }

    foreach ($pg in $xml.Project.PropertyGroup) {
        if ($pg.InformationalVersion -and -not [string]::IsNullOrWhiteSpace($pg.InformationalVersion)) {
            return $pg.InformationalVersion.Trim()
        }
    }

    return "0.0.0"
}

function Copy-WinUIXamlArtifacts([string]$projectPath, [string]$configuration, [string]$platform, [string]$runtime, [string]$publishDir) {
    $projectDir = Split-Path -Parent $projectPath
    $targetFramework = Resolve-TargetFramework $projectPath
    $buildOutDir = Join-Path $projectDir "bin\$platform\$configuration\$targetFramework\$runtime"

    if (-not (Test-Path $buildOutDir)) {
        Write-Warning "Build output folder not found for XAML artifacts: $buildOutDir"
        return
    }

    $artifacts = @()
    $artifacts += Get-ChildItem -Path $buildOutDir -Filter "*.xbf" -File -ErrorAction SilentlyContinue
    $artifacts += Get-ChildItem -Path $buildOutDir -Filter "*.pri" -File -ErrorAction SilentlyContinue

    if ($artifacts.Count -eq 0) {
        Write-Warning "No XAML artifacts (*.xbf / *.pri) found in $buildOutDir"
        return
    }

    foreach ($artifact in $artifacts) {
        Copy-Item -Path $artifact.FullName -Destination (Join-Path $publishDir $artifact.Name) -Force
    }

    Write-Host "Copied WinUI XAML artifacts from $buildOutDir to $publishDir"
}

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

$msbuildExtensionsPath = Get-MSBuildExtensionsPath

if ([string]::IsNullOrWhiteSpace($msbuildExtensionsPath) -or -not (Test-Path $msbuildExtensionsPath)) {
    Write-Warning "MSBuildExtensionsPath not found at '$msbuildExtensionsPath'. Falling back to default dotnet settings."
    dotnet publish $project `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:Platform=$Platform `
        -p:PublishSingleFile=false `
        -o $publishOutputDir
    if ($LASTEXITCODE -eq 0) {
        Copy-WinUIXamlArtifacts -projectPath $project -configuration $Configuration -platform $Platform -runtime $Runtime -publishDir $publishOutputDir
    }
    exit $LASTEXITCODE
}

Write-Host "Using MSBuildExtensionsPath: $msbuildExtensionsPath"
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:Platform=$Platform `
    -p:PublishSingleFile=false `
    -p:MSBuildExtensionsPath="$msbuildExtensionsPath" `
    -o $publishOutputDir

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Copy-WinUIXamlArtifacts -projectPath $project -configuration $Configuration -platform $Platform -runtime $Runtime -publishDir $publishOutputDir

Write-Host "Published to $publishOutputDir"

if ($CreateZip) {
    $versionTag = Resolve-VersionTag -projectPath $project
    $runtimeTag = $Runtime -replace "-", "_"
    $zipName = "ThumbnailGridStudio-v$versionTag-$runtimeTag.zip"
    $zipPath = Join-Path (Split-Path -Parent $publishOutputDir) $zipName

    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }

    Compress-Archive -Path $publishOutputDir -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Created ZIP: $zipPath"
}
