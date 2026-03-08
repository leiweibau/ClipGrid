# Thumbnail Grid Studio (WinUI 3)

Diese Windows-App portiert die Kernidee der Swift-Version nach WinUI 3 und .NET:

- Mehrere Videos laden
- Metadaten über `ffprobe` lesen
- Frames über `ffmpeg` extrahieren
- Kontaktbögen als `JPG` oder `PNG` exportieren

## Voraussetzungen

- Windows 10/11
- .NET 8 SDK
- WinUI Workload (Visual Studio 2022 mit Windows App SDK)

## FFmpeg einbinden

Die App sucht in dieser Reihenfolge:

1. `THUMBNAIL_GRID_STUDIO_FFMPEG` / `THUMBNAIL_GRID_STUDIO_FFPROBE`
2. `Tools\win-x64\` oder `Tools\win-arm64\` (im App-Ausgabeverzeichnis)
3. System-`PATH`

Im Projekt sind dafür diese Ordner vorgesehen:

- `Tools/win-x64/ffmpeg.exe`
- `Tools/win-x64/ffprobe.exe`
- `Tools/win-arm64/ffmpeg.exe`
- `Tools/win-arm64/ffprobe.exe`

Optionales Download-Skript:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\download-ffmpeg.ps1 -Architecture win-x64
```

## Starten

```powershell
powershell -ExecutionPolicy Bypass -File ..\build-winui.ps1 -Configuration Release -Platform x64
dotnet run --project .\ThumbnailGridStudio.WinUI.csproj
```
