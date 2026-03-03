# ClipGrid

This is a vibe-coding project. I do not plan to review pull requests or handle issues for this repository.

ClipGrid is a native macOS app built with SwiftUI that imports multiple videos and exports a video preview image for each one as `JPG` or `PNG`.

![ClipGrid Screenshot](screen.png)

## Features

- Import multiple videos at once
- Drag and drop plus file picker support for video import
- Export video preview sheets with a configurable grid and timestamps on every thumbnail
- Toggle metadata in the preview header on or off
- Configure columns, rows, thumbnail size, and spacing
- Customize the background color
- Persist settings across app restarts
- German and English localization
- Export all loaded videos in one run

## Requirements

- macOS 13 or newer
- Xcode Command Line Tools or Xcode with Swift 6

## Development

```bash
swift build
swift run
```

You can also open `Package.swift` directly in Xcode.

## Project Structure

- `Sources/ClipGrid`: SwiftUI app, view models, renderer, and services
- `Sources/ClipGrid/Resources`: localized `Localizable.strings`
- `Resources/Info.plist`: bundle metadata for the packaged app
- `Scripts/package-app.sh`: builds the native `.app` bundle
- `icon.png`: source image for the app icon

## Build the App Bundle

```bash
bash Scripts/package-app.sh
```

After that, the native macOS app will be available at `dist/ClipGrid.app`.

## Running an Unnotarized Build

If macOS blocks the app because it is not notarized, use one of these methods:

1. Move `ClipGrid.app` to `Applications`.
2. In Finder, right-click the app and choose `Open`.
3. Confirm the security dialog with `Open`.

Alternatively:

1. Open `System Settings > Privacy & Security`.
2. Scroll to the security section after a blocked launch attempt.
3. Allow the app to open anyway, then launch it again.

## Notes

- The app icon is generated from `icon.png` during packaging.
- Exported images use the same base filename as the video, with the selected image extension.
