# Thumbnail Grid Studio CLI

The project ships with a native command-line tool: `thumbnail-grid-studio-cli`.

## Quick Start

```bash
./thumbnail-grid-studio-cli --h
```

```bash
./thumbnail-grid-studio-cli \
  --input /path/video1.mp4 \
  --input /path/video2.mkv \
  --output-dir /path/output
```

## Parameters

- `--h`, `-h`, `--help`  
  Show help output.

- `--input <file>`  
  Input video file. Can be used multiple times.

- `--output-dir <directory>`  
  Output directory for rendered images. Required.

- `--columns <int>`  
  Grid columns. Falls back to GUI setting.

- `--rows <int>`  
  Grid rows. Falls back to GUI setting.

- `--format <jpg|png>`  
  Export format. Falls back to GUI setting.

- `--width <px>`  
  Thumbnail width. Falls back to GUI setting / auto-size logic.

- `--height <px>`  
  Thumbnail height. Falls back to GUI setting / auto-size logic.

- `--spacing <px>`  
  Spacing between thumbnails. Falls back to GUI setting.

- `--background <RRGGBB>`  
  Contact-sheet background color. Falls back to GUI setting.

- `--metadata-color <RRGGBB>`  
  Metadata text color in header. Falls back to GUI setting.

- `--file-name-font-size <n>`  
  Header file name font size. Falls back to GUI setting.

- `--duration-font-size <n>`  
  Duration font size. Falls back to GUI setting.

- `--file-size-font-size <n>`  
  File size font size. Falls back to GUI setting.

- `--resolution-font-size <n>`  
  Resolution font size. Falls back to GUI setting.

- `--timestamp-font-size <n>`  
  Timestamp badge font size. Falls back to GUI setting.

## Fallback Behavior

If a style/layout parameter is not provided via CLI, the tool loads the value from the GUI app settings (`UserDefaults`) first.

This includes:

- colors
- font sizes
- export format
- grid size
- thumbnail size
- spacing

If no GUI value exists, built-in defaults are used.

## Examples

### 1) Use GUI settings except `rows`

```bash
./thumbnail-grid-studio-cli \
  --input /Users/you/Movies/demo.mov \
  --output-dir /Users/you/Exports \
  --rows 5
```

### 2) Force PNG with custom colors

```bash
./thumbnail-grid-studio-cli \
  --input /Users/you/Movies/demo.mov \
  --output-dir /Users/you/Exports \
  --format png \
  --background 2D3340 \
  --metadata-color FFFFFF
```

### 3) Fully custom typography

```bash
./thumbnail-grid-studio-cli \
  --input /Users/you/Movies/demo.mov \
  --output-dir /Users/you/Exports \
  --file-name-font-size 36 \
  --duration-font-size 22 \
  --file-size-font-size 22 \
  --resolution-font-size 22 \
  --timestamp-font-size 16
```
