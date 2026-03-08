param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Architecture = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$targetDir = Join-Path $root ("Tools\" + $Architecture)
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

if ($Architecture -eq "win-arm64") {
    $url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials-arm64.zip"
}
else {
    $url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
}

$archivePath = Join-Path $env:TEMP ("ffmpeg-" + $Architecture + "-" + [Guid]::NewGuid().ToString("N") + ".zip")
Write-Host "Lade herunter: $url"
Invoke-WebRequest -Uri $url -OutFile $archivePath

$extractDir = Join-Path $env:TEMP ("ffmpeg-extract-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
Expand-Archive -Path $archivePath -DestinationPath $extractDir -Force

$ffmpeg = Get-ChildItem -Path $extractDir -Recurse -Filter ffmpeg.exe | Select-Object -First 1
$ffprobe = Get-ChildItem -Path $extractDir -Recurse -Filter ffprobe.exe | Select-Object -First 1
if (-not $ffmpeg -or -not $ffprobe) {
    throw "ffmpeg.exe oder ffprobe.exe nicht im Archiv gefunden."
}

Copy-Item $ffmpeg.FullName (Join-Path $targetDir "ffmpeg.exe") -Force
Copy-Item $ffprobe.FullName (Join-Path $targetDir "ffprobe.exe") -Force

Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Fertig. Tools liegen in: $targetDir"
