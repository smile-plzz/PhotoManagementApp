# Photo Library

An offline photo manager for your own machine. Point it at the folders where your
photos already live and it builds a searchable, date-organised library on top of
them — Picasa-style — without moving, renaming or uploading a single file.

Built with [Avalonia UI](https://avaloniaui.net/) on .NET 9, so the same codebase
runs on Windows, macOS and Linux.

---

## Install and run

### 1. Install the .NET 9 SDK

Download from <https://dotnet.microsoft.com/download/dotnet/9.0>, or:

```bash
winget install Microsoft.DotNet.SDK.9     # Windows
brew install --cask dotnet-sdk            # macOS
sudo apt install dotnet-sdk-9.0           # Ubuntu / Debian
```

Check it with `dotnet --version`.

### 2. Get the code and run it

```bash
git clone https://github.com/smile-plzz/PhotoManagementApp
cd PhotoManagementApp
dotnet run --project PhotoManagementApp
```

### 3. (Recommended) Build a double-clickable app

So you don't need a terminal every time:

```bash
# Windows
dotnet publish PhotoManagementApp -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# macOS (Apple Silicon)
dotnet publish PhotoManagementApp -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true

# Linux
dotnet publish PhotoManagementApp -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

The executable appears under
`PhotoManagementApp/bin/Release/net9.0/<runtime>/publish/`.
Make a desktop shortcut to it. A self-contained build bundles the runtime, so the
machine you copy it to does not need .NET installed.

---

## First run

1. Click **Add folder** and choose where your photos live (your Pictures folder,
   an external drive, an SD card — you can add several).
2. The library scans in the background. The status bar shows progress; you can
   keep browsing while it works.
3. Photos appear grouped by date, newest first.

Scanning reads each photo once to pull out its EXIF data, then remembers it.
Later scans only look at files that are new or changed, so a rescan of an
unchanged library takes seconds.

**Your photo files are never modified.** The index and thumbnail cache live
separately, under:

| OS | Location |
|---|---|
| Windows | `%APPDATA%\PhotoManagementApp\` |
| macOS | `~/Library/Application Support/PhotoManagementApp/` |
| Linux | `~/.config/PhotoManagementApp/` |

Deleting that folder resets the library. It does not touch your photos.

---

## What it does

### Browsing
- **Timeline** — photos grouped by day, month or year, with the band width chosen
  automatically to suit the range in view.
- **Grid** — a flat, virtualised wall of thumbnails, in three sizes.
- **Folders** — the original directory tree, expanded on demand.
- **Sidebar shortcuts** — All photos, Favourites, Recently added, Untagged,
  Duplicates, plus one entry per year, tag and camera found in your library.

### Finding things
- Search across file names, folders, camera makes and models, and tags. Multiple
  words narrow the result rather than widening it, so `beach canon` means both.
- Filter by minimum star rating, favourites, date range, whether a photo has GPS
  data, and whether it has been tagged.
- Sort by date taken, date modified, file name, file size, rating or resolution,
  ascending or descending.

### The viewer
Double-click any photo (or press <kbd>Enter</kbd>) for the full-screen viewer:

| Key | Action |
|---|---|
| <kbd>←</kbd> <kbd>→</kbd> <kbd>Space</kbd> | Previous / next photo |
| <kbd>0</kbd>–<kbd>5</kbd> | Set star rating |
| <kbd>+</kbd> <kbd>−</kbd> | Zoom in / out |
| <kbd>D</kbd> | Reset zoom to fit |
| <kbd>Ctrl</kbd> + scroll | Zoom to pointer |
| <kbd>I</kbd> | Toggle the EXIF info panel |
| <kbd>F</kbd> / <kbd>F11</kbd> | Full screen |
| <kbd>O</kbd> | Open in your default image app |
| <kbd>Esc</kbd> | Close |

There is also a slideshow, and **Show in folder**, which opens your system file
manager with the photo selected.

### Organising
- **Star ratings** (0–5) and **favourites**, applied to one photo or a whole
  selection at once.
- **Tags**, typed freely; every tag becomes a sidebar shortcut.
- **Copy to…** copies the selected photos into a folder you pick, renaming rather
  than overwriting if a name is already taken.
- **Duplicate detection** groups files by a SHA-256 of their contents (computed
  during the scan) and reports how much space the redundant copies occupy.

Ratings, favourites and tags are stored in the index, not written into your image
files, and they survive rescans.

### Main window shortcuts

| Key | Action |
|---|---|
| <kbd>0</kbd>–<kbd>5</kbd> | Rate the selection |
| <kbd>Ctrl</kbd>+<kbd>A</kbd> | Select all |
| <kbd>Ctrl</kbd>+click | Add or remove a photo from the selection |
| <kbd>Enter</kbd> | Open the viewer |

---

## Formats

Indexed, with thumbnails and full-size viewing:
`.jpg` `.jpeg` `.png` `.gif` `.bmp` `.tif` `.tiff` `.webp`

Indexed for metadata, search and organisation, but shown as a placeholder because
the built-in decoder cannot render them — use **Open** to view them in another
app: `.heic` `.heif` `.cr2` `.cr3` `.nef` `.arw` `.dng` `.orf` `.rw2` `.raf` `.pef`

---

## Performance notes

Two things keep a large library responsive:

- **Thumbnails are decoded at thumbnail size**, never at full resolution, and are
  cached as small JPEGs on disk. A 40-megapixel original costs about as much to
  show as a small one, and only once.
- **Both photo views are virtualised.** Thumbnail decoding starts when a tile
  scrolls into view, so opening a library of any size decodes only what you can
  actually see. Decodes are capped at half your CPU count so scrolling never
  starves the interface.

If thumbnails ever look stale or the cache grows too large, there is a
**Clear thumbnail cache** command; they regenerate on demand.

---

## Development

```bash
dotnet build                 # build everything
dotnet test                  # run the test suite
dotnet run --project PhotoManagementApp
```

### Layout

```
PhotoManagementApp/
├── Models/          Photo, PhotoQuery — plain data, no UI or DB dependencies
├── Data/            PhotoDatabase — the SQLite index (photos, tags, albums, roots)
├── Services/        MetadataReader, LibraryScanner, ThumbnailCache,
│                    ImageService, PlatformService, AppPaths
├── ViewModels/      MainWindowViewModel, ViewerViewModel, PhotoTileViewModel, ...
├── Views/           ViewerWindow
└── MainWindow.axaml The main three-pane window

PhotoManagementApp.Tests/   xUnit suite, cross-platform (net9.0)
```

Business logic is kept out of the view models so it can be tested without a UI:
the database, scanner, metadata reader and file walker all have direct coverage.

### Not built yet

Photo editing (crop, rotate, colour), face recognition, reverse geocoding of GPS
coordinates into place names, and album management beyond tags. The database
schema has tables for albums and a `place_name` column reserved for the last two.

---

## Development log

See `development_log.md`.
