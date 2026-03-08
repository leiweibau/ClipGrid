# Thumbnail Grid Studio

This repository is organized by platform.

## Layout

- `apps/macos`: Native macOS implementation (Swift/SwiftUI + CLI)
- `apps/windows`: Native Windows implementation (WinUI 3 + .NET)

## Current Status

- macOS app is fully implemented in `apps/macos`.
- Windows app is implemented in `apps/windows`.

## Build macOS App

```bash
cd apps/macos
bash Scripts/package-app.sh
```

## Build Windows App

```powershell
powershell -ExecutionPolicy Bypass -File .\apps\windows\publish-winui-selfcontained.ps1 -Configuration Release -Runtime win-x64 -Platform x64
```

See platform-specific details in:

- `apps/macos/README.md`
- `apps/macos/docs/CLI.md`
- `apps/windows/README.md`
