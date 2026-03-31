# Thumbnail Grid Studio

Thumbnail Grid Studio is a native macOS app built with SwiftUI that imports multiple videos and exports a video preview image for each one as `JPG` or `PNG`.

![Thumbnail Grid Studio Screenshot](./screen.png)

## Features

The most important capabilities in the current version are:

- Multi-file import via drag and drop or file picker
- Contact sheet style video preview export with timestamps on every thumbnail
- Configurable grid, thumbnail size, spacing, colors, metadata visibility, and font sizes
- Built-in `ffmpeg` and `ffprobe` for broader format support such as `mkv`, `avi`, and `webm`
- Universal macOS app bundle for Apple Silicon and Intel
- Persistent settings across app restarts
- German and English localization
- One-click render and export for all loaded videos
- Light and dark placeholder preview artwork that follows the current macOS appearance
- Additional CLI tool for batch contact-sheet export

## Requirements

- macOS 13 or newer
- Xcode Command Line Tools or Xcode with Swift 6
- `nasm` available on `PATH` for `ffmpeg` builds

## Development

```bash
swift build
swift run
```

You can also open `Package.swift` directly in Xcode.

Detailed CLI documentation:

- `docs/CLI.md`

## Project Structure

- `Sources/ThumbnailGridStudio`: SwiftUI app, view models, renderer, and services
- `Sources/ThumbnailGridStudio/Resources`: localized `Localizable.strings` and preview placeholder artwork
- `Resources/Info.plist`: bundle metadata for the packaged app
- `Scripts/build-ffmpeg.sh`: builds bundled `ffmpeg` and `ffprobe` binaries from a pinned official FFmpeg revision
- `Scripts/package-app.sh`: builds the native `.app` bundle
- `icon.png`: source image for the app icon

## Build the App Bundle

```bash
bash Scripts/build-ffmpeg.sh
bash Scripts/package-app.sh
```

After that, the native macOS app will be available at `dist/Thumbnail Grid Studio.app`.

The packaging step also creates `dist/ThumbnailGridStudio-<version>-macOS-universal.zip`.

## Reproducible Build Notes

- `Scripts/build-ffmpeg.sh` is pinned to FFmpeg revision `30a811cc7d878184646e35fb94e72c5b964d575e`.
- The macOS shell scripts are tracked with LF line endings via Git attributes. Keep those files on LF to avoid `pipefail` shell errors.

## Running an Unnotarized Build

If macOS blocks the app because it is not notarized, use one of these methods:

1. Move `Thumbnail Grid Studio.app` to `Applications`.
2. In Finder, right-click the app and choose `Open`.
3. Confirm the security dialog with `Open`.

Alternatively:

1. Open `System Settings > Privacy & Security`.
2. Scroll to the security section after a blocked launch attempt.
3. Allow the app to open anyway, then launch it again.

In some cases with unsigned apps, you may also need:

```bash
xattr -r -d com.apple.quarantine /Applications/Thumbnail\ Grid\ Studio.app
```

## Notes

- The app icon is generated from `icon.png` during packaging.
- `ffmpeg` and `ffprobe` are bundled inside the app, so no separate installation is required.
- Exported images use the same base filename as the video, with the selected image extension.
