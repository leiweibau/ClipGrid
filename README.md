# Thumbnail Grid Studio

Thumbnail Grid Studio is a desktop app for creating contact-sheet style preview images from video files (`JPG` or `PNG`).

The repository contains two native implementations:

| Platform | Stack | Status | Release Package | Details |
|---|---|---|---|---|
| macOS | SwiftUI + Swift CLI | Implemented | [GitHub Releases](https://github.com/leiweibau/ThumbnailGridStudio/releases) | [macOS README](./apps/macos/README.md) |
| Windows | WinUI 3 + .NET 8 | Implemented | [v1.3.1 - ThumbnailGridStudio-win-x64-fdd.zip](https://github.com/leiweibau/ThumbnailGridStudio/releases/download/v1.3.1/ThumbnailGridStudio-win-x64-fdd.zip) | [Windows README](./apps/windows/README.md) |

## Application Overview

- Import multiple videos via drag and drop or file picker
- Generate thumbnail grids/contact sheets with configurable layout
- Customize spacing, colors, metadata visibility, and font sizes
- Export as `JPG` or `PNG`
- Bundled `ffmpeg`/`ffprobe` for broad format support (`mp4`, `mov`, `avi`, `mkv`, `webm`, ...)

## Screenshots

<table>
  <tr>
    <th width="50%">macOS</th>
    <th width="50%">Windows</th>
  </tr>
  <tr>
    <td width="50%"><img src="./apps/macos/screen.png" alt="Thumbnail Grid Studio macOS" /></td>
    <td width="50%"><img src="./apps/windows/screen.png" alt="Thumbnail Grid Studio Windows" /></td>
  </tr>
</table>

## Repository Layout

- `apps/macos`: macOS app source, packaging scripts, CLI
- `apps/windows`: Windows app source, build/publish scripts

## Platform Documentation

- [macOS README](./apps/macos/README.md)
- [Windows README](./apps/windows/README.md)
- [macOS CLI Documentation](./apps/macos/docs/CLI.md)
