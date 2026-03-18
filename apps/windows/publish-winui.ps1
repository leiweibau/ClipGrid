param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Platform = "x64",
    [string]$OutputDir = "apps/windows/dist/Thumbnail Grid Studio",
    [bool]$CreateZip = $true,
    [bool]$IncludeCli = $true,
    [bool]$IncludeDotnetRuntimeInstaller = $true,
    [string]$DotnetRuntimeInstallerUrl = "https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.3/dotnet-runtime-10.0.3-win-x64.exe",
    [string]$DotnetRuntimeInstallerFileName = "dotnet-runtime-10.0.3-win-x64.exe"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
$project = Join-Path $scriptRoot "ThumbnailGridStudio.WinUI\ThumbnailGridStudio.WinUI.csproj"
$cliProject = Join-Path $scriptRoot "ThumbnailGridStudio.Cli\ThumbnailGridStudio.Cli.csproj"
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

function Publish-CliArtifacts(
    [string]$cliProjectPath,
    [string]$configuration,
    [string]$runtime,
    [string]$platform,
    [string]$publishDir,
    [string]$msbuildExtensionsPath)
{
    if (-not (Test-Path $cliProjectPath)) {
        Write-Warning "CLI project not found: $cliProjectPath"
        return
    }

    $tempCliOutput = Join-Path $scriptRoot ".cli-publish-temp"
    if (Test-Path $tempCliOutput) {
        Remove-Item -Path $tempCliOutput -Recurse -Force
    }
    New-Item -ItemType Directory -Path $tempCliOutput | Out-Null

    $publishArgs = @(
        "publish", $cliProjectPath,
        "-c", $configuration,
        "-r", $runtime,
        "--self-contained", "false",
        "-p:UseAppHost=true",
        "-p:PublishSingleFile=false",
        "-p:Platform=$platform",
        "-o", $tempCliOutput
    )

    if (-not [string]::IsNullOrWhiteSpace($msbuildExtensionsPath)) {
        $publishArgs += "-p:MSBuildExtensionsPath=$msbuildExtensionsPath"
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "CLI publish failed."
    }

    $cliOutputDir = Join-Path $publishDir "cli"
    if (-not (Test-Path $cliOutputDir)) {
        New-Item -ItemType Directory -Path $cliOutputDir | Out-Null
    }
    $staleCliTools = Join-Path $cliOutputDir "Tools"
    if (Test-Path $staleCliTools) {
        try {
            Remove-Item -Path $staleCliTools -Recurse -Force
        }
        catch {
            Write-Warning "Could not remove stale CLI tools folder '$staleCliTools': $($_.Exception.Message)"
        }
    }

    Get-ChildItem -Path $tempCliOutput -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($tempCliOutput.Length).TrimStart('\', '/')
        $destinationFile = Join-Path $cliOutputDir $relativePath
        $destinationDir = Split-Path -Parent $destinationFile
        if (-not (Test-Path $destinationDir)) {
            New-Item -ItemType Directory -Path $destinationDir | Out-Null
        }

        try {
            Copy-Item -Path $_.FullName -Destination $destinationFile -Force
        }
        catch {
            Write-Warning "Could not copy CLI artifact '$relativePath' (likely locked): $($_.Exception.Message)"
        }
    }

    Remove-Item -Path $tempCliOutput -Recurse -Force
    Write-Host "Published CLI artifacts to $cliOutputDir"
}

function Create-CliLauncher([string]$publishDir) {
    $launcherPath = Join-Path $publishDir "ThumbnailGridStudio-cli.cmd"
    $launcherContent = @"
@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "CLI_EXE=%SCRIPT_DIR%cli\ThumbnailGridStudio-cli.exe"
set "THUMBNAIL_GRID_STUDIO_FFMPEG=%SCRIPT_DIR%Tools\win-x64\ffmpeg.exe"
set "THUMBNAIL_GRID_STUDIO_FFPROBE=%SCRIPT_DIR%Tools\win-x64\ffprobe.exe"
if not exist "%CLI_EXE%" (
  echo Fehler: CLI nicht gefunden: "%CLI_EXE%"
  exit /b 1
)
"%CLI_EXE%" %*
exit /b %ERRORLEVEL%
"@

    Set-Content -Path $launcherPath -Value $launcherContent -Encoding Ascii
    Write-Host "Created CLI launcher: $launcherPath"
}

function Trim-LanguageResources([string]$publishDir) {
    # Match macOS supported languages:
    # ar, bn, de, en, es, fr, hi, ja, ko, pt, ru, tr, zh-Hans
    # Windows culture folder equivalent for zh-Hans is zh-CN.
    $allowedPrefixes = @("ar", "bn", "de", "en", "es", "fr", "hi", "ja", "ko", "pt", "ru", "tr")
    $allowedSpecificCultures = @("zh-CN")
    $protectedFolders = @("cli", "Microsoft.UI.Xaml", "Tools")

    $dirs = Get-ChildItem -Path $publishDir -Directory -ErrorAction SilentlyContinue
    foreach ($dir in $dirs) {
        $name = $dir.Name

        if ($protectedFolders -contains $name) {
            continue
        }

        # Only touch culture-style folders like de-DE, zh-CN, sr-Cyrl-RS.
        if ($name -notmatch '^[A-Za-z]{2,3}(-[A-Za-z0-9]+)+$') {
            continue
        }

        $isSpecificAllowed = $allowedSpecificCultures | Where-Object { $_.Equals($name, [System.StringComparison]::OrdinalIgnoreCase) }
        if ($isSpecificAllowed) {
            continue
        }

        $prefix = $name.Split('-')[0].ToLowerInvariant()
        if ($allowedPrefixes -contains $prefix) {
            continue
        }

        Remove-Item -Path $dir.FullName -Recurse -Force
    }

    Write-Host "Trimmed language resource folders to macOS-equivalent language set."
}

function Download-DotnetRuntimeInstaller(
    [string]$url,
    [string]$targetDirectory,
    [string]$fileName)
{
    if ([string]::IsNullOrWhiteSpace($url)) {
        Write-Warning "Dotnet runtime installer URL is empty; skipping download."
        return
    }

    if ([string]::IsNullOrWhiteSpace($fileName)) {
        Write-Warning "Dotnet runtime installer filename is empty; skipping download."
        return
    }

    $targetPath = Join-Path $targetDirectory $fileName
    if (Test-Path $targetPath) {
        Remove-Item -Path $targetPath -Force
    }

    Write-Host "Downloading .NET runtime installer from $url"
    Invoke-WebRequest -Uri $url -OutFile $targetPath
    $header = Get-Content -Path $targetPath -Encoding Byte -TotalCount 2
    if ($header.Length -lt 2 -or $header[0] -ne 0x4D -or $header[1] -ne 0x5A) {
        throw "Downloaded runtime installer is not a valid Windows executable (expected MZ header)."
    }
    Write-Host "Downloaded .NET runtime installer to $targetPath"
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
        --self-contained false `
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
    --self-contained false `
    -p:Platform=$Platform `
    -p:PublishSingleFile=false `
    -p:MSBuildExtensionsPath="$msbuildExtensionsPath" `
    -o $publishOutputDir

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Copy-WinUIXamlArtifacts -projectPath $project -configuration $Configuration -platform $Platform -runtime $Runtime -publishDir $publishOutputDir

if ($IncludeCli) {
    Publish-CliArtifacts `
        -cliProjectPath $cliProject `
        -configuration $Configuration `
        -runtime $Runtime `
        -platform $Platform `
        -publishDir $publishOutputDir `
        -msbuildExtensionsPath $msbuildExtensionsPath
    Create-CliLauncher -publishDir $publishOutputDir
}

if ($IncludeDotnetRuntimeInstaller) {
    Download-DotnetRuntimeInstaller `
        -url $DotnetRuntimeInstallerUrl `
        -targetDirectory $publishOutputDir `
        -fileName $DotnetRuntimeInstallerFileName
}

Trim-LanguageResources -publishDir $publishOutputDir

Write-Host "Published to $publishOutputDir"

if ($CreateZip) {
    $versionTag = Resolve-VersionTag -projectPath $project
    $runtimeTag = $Runtime -replace "-", "_"
    $zipName = "ThumbnailGridStudio-$versionTag-$runtimeTag.zip"
    $zipPath = Join-Path (Split-Path -Parent $publishOutputDir) $zipName

    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }

    Compress-Archive -Path $publishOutputDir -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Created ZIP: $zipPath"
}
