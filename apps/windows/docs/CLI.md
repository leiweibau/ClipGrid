# Windows CLI Documentation

`ThumbnailGridStudio-cli` renders thumbnail grids from video files using the same settings model as the WinUI app.

## Location

- Published executable: `apps/windows/dist/Thumbnail Grid Studio/cli/ThumbnailGridStudio-cli.exe`
- Optional launcher in app root: `apps/windows/dist/Thumbnail Grid Studio/ThumbnailGridStudio-cli.cmd`

## Behavior

- The CLI first loads GUI settings from:
  - `%LOCALAPPDATA%\ThumbnailGridStudio\settings.json`
- Every CLI parameter overrides the loaded setting value.
- Input can be a file or a directory.
- Directories are scanned recursively by default.

## Usage

```powershell
ThumbnailGridStudio-cli --input <file-or-dir> [--input <file-or-dir> ...] [options]
```

Short forms:

- `-i` for `--input`
- `-o` for `--output-dir`
- `-h` for `--help`

## Options

| Option | Value | Description |
|---|---|---|
| `-i`, `--input` | `<path>` | Input video file or directory. Can be repeated. |
| `-o`, `--output-dir` | `<dir>` | Output directory. Default: `%USERPROFILE%\Pictures\ThumbnailGridStudio\Exports` |
| `--recursive` | `<bool>` | Recursively scan input directories. Default: `true` |
| `--columns` | `<int>` | Number of grid columns |
| `--rows` | `<int>` | Number of grid rows |
| `--width` | `<int\|auto>` | Thumbnail width. `auto` allowed |
| `--height` | `<int\|auto>` | Thumbnail height. `auto` allowed |
| `--spacing` | `<int>` | Spacing between thumbnails |
| `--format` | `jpg\|png` | Export image format |
| `--background` | `<HEX>` | Background color (HEX, e.g. `1F2126`) |
| `--metadata-color` | `<HEX>` | Metadata text color (HEX, e.g. `FFFFFF`) |
| `--show-title` | `<bool>` | Show video title |
| `--show-duration` | `<bool>` | Show duration |
| `--show-file-size` | `<bool>` | Show file size |
| `--show-resolution` | `<bool>` | Show resolution |
| `--show-timestamp` | `<bool>` | Show timestamp inside each thumbnail |
| `--show-bitrate` | `<bool>` | Show video bitrate |
| `--show-video-codec` | `<bool>` | Show video codec |
| `--show-audio-codec` | `<bool>` | Show audio codec (repeated per audio track) |
| `--title-font` | `<px>` | Title font size |
| `--duration-font` | `<px>` | Duration font size |
| `--file-size-font` | `<px>` | File size font size |
| `--resolution-font` | `<px>` | Resolution font size |
| `--timestamp-font` | `<px>` | Timestamp font size |
| `--bitrate-font` | `<px>` | Bitrate font size |
| `--video-codec-font` | `<px>` | Video codec font size |
| `--audio-codec-font` | `<px>` | Audio codec font size |
| `--export-separate` | `<bool>` | Export additional single thumbnail images |
| `--concurrency` | `<1-8>` | Parallel render concurrency |
| `-h`, `--help` | (none) | Show help |

Legacy aliases (still supported): `--output`, `--thumb-width`, `--thumb-height`, `--text-color`.

## Boolean Values

Accepted boolean values:

- `true`, `false`
- `1`, `0`
- `yes`, `no`
- `on`, `off`

## Examples

Render one file:

```powershell
ThumbnailGridStudio-cli `
  -i "M:\Videos\input.mp4" `
  --output-dir "C:\Users\<User>\Desktop"
```

Render a directory with overrides:

```powershell
ThumbnailGridStudio-cli `
  -i "D:\VideoBatch" `
  --recursive true `
  --columns 5 `
  --rows 4 `
  --format png `
  --show-title true `
  --show-timestamp true `
  --timestamp-font 16 `
  --output-dir "D:\Exports"
```

Use one dimension with auto aspect handling:

```powershell
ThumbnailGridStudio-cli `
  -i "D:\Videos\clip.mkv" `
  --width 480 `
  --height auto
```
