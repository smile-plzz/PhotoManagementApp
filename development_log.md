# Photo Management App Development Log

## 2025-07-12

### Major Framework Change: Switching to Avalonia UI

**Reasoning**: The user has requested a pivot to a more modern and efficient framework, citing the previous WPF approach as not efficient enough. Avalonia UI has been chosen for its cross-platform capabilities, modern architecture, efficiency, and familiarity for C# developers.

**Implication**: This is a complete rewrite. All previous WPF code and progress are being discarded.

### New High-Level Plan (Avalonia UI):

**Phase 1: Avalonia Project Setup & Basic UI (MVP)**
1.  **Project Scaffolding**: Create a new Avalonia UI project.
2.  **Basic UI Layout**: Design the main window with a clean, modern aesthetic, including a folder navigation pane, a main content area for displaying photo thumbnails, and a preview/detail pane.
3.  **File System Integration**: Implement functionality to allow users to select and browse local directories.
4.  **Thumbnail Generation & Display**: Efficiently generate and display thumbnails for images.
5.  **Basic Image Viewing**: Allow users to view full-size images.

**Phase 2: Core Features Implementation (Avalonia UI)**
-   **Metadata Support**: Implement viewing and editing of EXIF/IPTC/XMP metadata.
-   **Tagging and Rating**: Add custom tags, keywords, and star ratings.
-   **Search Functionality**: Implement search by filename.
-   **Sorting and Filtering**: Implement sorting by filename, date, and filtering by year.
-   **Error Logging**: Integrate a logging mechanism.

**Phase 3: Advanced Features & Optimization (Avalonia UI)**
-   **Batch Processing, Duplicate Detection, Import/Export, etc.**
-   **Performance Optimization**: Fine-tune image loading, rendering, and overall application responsiveness.
-   **Automated Testing**: Implement unit tests for core logic.

### Current Status:

-   **Framework Change Approved**: Initiating project setup for Avalonia UI.
-   **Avalonia Project Created**: New Avalonia UI project `PhotoManagementApp` successfully created.
-   **Basic UI Layout Implemented**: `MainWindow.axaml` updated with a three-column layout for folder navigation, photo thumbnails, and a preview/details pane.
-   **File System Integration (Initial)**: Implemented folder selection using `StorageProvider` and basic `TreeView` population with `FolderItem` ViewModel. All build warnings and errors resolved.
-   **Thumbnail Generation & Display**: Implemented `ImageItem` ViewModel for lazy loading of thumbnails. `MainWindowViewModel` now populates an `ObservableCollection<ImageItem>`.
-   **Basic Image Viewing**: `MainWindow.axaml` updated to bind `SelectedImage` to the preview pane.
-   **Build Status**: Project built successfully with 0 warnings and 0 errors.

## 2026-08-26

### Phase 1: Extract `ImageService`, unblock test compilation

**Baseline confirmed this session**: `dotnet build` on the app project succeeded
(0 warnings/errors). `dotnet test` failed to compile — `ImageFilteringTests.cs`
referenced a non-existent `ImageService` type (CS0246 x4), a forward-looking
stub the test project shipped with but the app never implemented.

**Change**: Added `PhotoManagementApp/Services/ImageService.cs` (namespace
`PhotoManagementApp`) with `GetImageFiles(string folderPath)` and
`FilterImages(IEnumerable<FileInfo>, string searchText)`, matching the
signatures the existing tests already assumed. Refactored
`MainWindowViewModel.LoadImages` to call `ImageService.GetImageFiles` instead
of duplicating the extension-filtering logic inline — this satisfies rule 6
(business logic must be testable/decoupled from UI code) ahead of building
more filtering/search features on top of it in Phase 2.

**Result**: `dotnet build` still green, 0 warnings/errors. Test project now
compiles (CS0246 resolved).

**Blocker found — environment, not code**: `dotnet test` compiles but the
test run fails at assembly-load time: *"Could not load file or assembly
'PhotoManagementApp.Tests.dll' ... An Application Control policy has blocked
this file. (0x800711C7)"*. Root-caused to Windows 11's **Smart App Control**
being enabled and enforced on this machine
(`HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy!VerifiedAndReputablePolicyState
= 1`), which blocks execution/loading of locally-built, unsigned binaries
that lack Microsoft reputation — including our own freshly-compiled test
DLL. This is an OS-level security policy, not a project defect, and turning
it off is a system security change (historically one-way without a Windows
reinstall on older builds) — flagged to the product owner rather than
changed unilaterally. Until resolved, test *compilation* can be verified
each phase, but test *execution* cannot be verified on this machine; CI (a
clean, non-locked-down runner) should be treated as the actual pass/fail
source of truth for the "working test suite" ship criterion.

**Update**: confirmed in a later session that `dotnet test` now executes
successfully (5 tests passing) — the Smart App Control block was transient
or has since been resolved. Test execution is verified going forward.

### Phase 1: Harden `ImageService.GetImageFiles` against missing/inaccessible folders

**Problem**: `GetImageFiles` called `new DirectoryInfo(folderPath).GetFiles()`
unguarded — a nonexistent folder path (e.g., a stale path, a removed drive,
a race between folder-picker and enumeration) threw an unhandled
`DirectoryNotFoundException`, and a permission-denied folder threw
`UnauthorizedAccessException`, either of which would crash `LoadImages` in
`MainWindowViewModel` and take down the UI thread's call chain.

**Change**: `GetImageFiles` now checks `DirectoryInfo.Exists` up front and
returns an empty list instead of throwing for a missing folder, and wraps
`GetFiles()` in a try/catch for `UnauthorizedAccessException`/`IOException`,
also returning an empty list. This is a deliberate "fail soft, don't crash"
choice for v1 — a toast/notification surface for "folder inaccessible" is a
UI-layer concern to add later, not blocking this hardening slice.

**Tests**: added `GetImageFiles_ReturnsEmptyListWhenFolderDoesNotExist`.

**Result**: `dotnet build` green (0/0). `dotnet test` green, 6/6 passing.
## 2026-08-27

### Phase 2/3: library index, metadata, thumbnails, and a real gallery UI

**Starting point**: the app was a folder browser. It could open one folder at a
time, showed thumbnails by decoding each photo at full resolution, and its
details pane displayed the literal strings "Date Taken: N/A" / "Camera: N/A" —
no EXIF was ever read. `ImageService.FilterImages` was tested but never called
by any UI. Clicking a folder in the tree did nothing, because `LoadImages` was
only invoked once for the root the picker returned.

**Approach**: rather than adding features onto the folder-at-a-time shape, the
work introduced a library index. Every Picasa-style feature the product outline
asks for — date view, search, filters, smart albums, duplicate detection — is a
query against that index, so it had to come first.

#### Added

- **`Data/PhotoDatabase`** — SQLite index (Microsoft.Data.Sqlite) with tables for
  photos, tags, photo_tags, albums, album_photos and roots. WAL journaling, a
  shared connection behind a lock, and parameterised queries throughout.
  User-owned fields (rating, favourite, tags, place name) are deliberately left
  out of the upsert's `DO UPDATE SET` so a rescan cannot clobber them.
- **`Services/MetadataReader`** — EXIF/IPTC/XMP extraction via MetadataExtractor:
  date taken, camera make/model, lens, ISO, aperture, exposure, focal length,
  GPS and orientation. Every read is best-effort; a corrupt file yields a photo
  populated from file-system facts rather than aborting a scan.
- **`Services/LibraryScanner`** — incremental walk of the configured roots.
  Files whose path and second-truncated mtime already match the index are
  skipped without being opened, so rescanning an unchanged library is one
  directory walk. Computes a SHA-256 per new file for duplicate detection.
  Prunes index rows for deleted files, but only under roots that were actually
  walked — an unplugged external drive must not wipe its own photos.
- **`Services/ThumbnailCache`** — two-tier cache. `Bitmap.DecodeToWidth` streams
  at target size instead of materialising full-resolution bitmaps; results are
  written as small JPEGs under the app data folder, keyed by path + size + mtime.
  Concurrent decodes are capped at half the CPU count, and a bounded in-memory
  tier holds what is on screen.
- **`Services/PlatformService`** — reveal-in-file-manager (Explorer / Finder /
  freedesktop D-Bus with an `xdg-open` fallback), open-with-default-app, and a
  non-clobbering copy-to-folder.
- **New UI** — timeline view (photos banded by day/month/year, band width chosen
  from the range in view), virtualised flat grid, lazy-expanding folder tree,
  faceted sidebar (years, tags, cameras, favourites, untagged, duplicates),
  filter bar (search, rating, date range, has-GPS, untagged), six sort fields
  with direction toggle, details pane with real EXIF, and a full-screen viewer
  with zoom/pan, keyboard navigation, ratings, slideshow and an info overlay.

#### Fixed

- Thumbnails no longer decode at full resolution (`new Bitmap(stream)` on the
  original file). This was the single biggest obstacle to the "10,000+ images
  without slowdowns" requirement.
- Both photo views virtualise, and thumbnail decoding is triggered by a tile
  entering the visual tree, so scrolled-past photos are never decoded.
- Selecting a folder in the tree now filters the library to it.
- `ImageFilteringTests` used hardcoded `C:\test\...` paths, which parse as a
  single filename on non-Windows systems. Replaced with `Path.Combine`, so the
  suite is genuinely cross-platform.
- `ThumbnailCache` refuses to promote a zero-byte encode into the cache. Found
  during headless verification: a backend without a real encoder returns an
  empty file without throwing, and promoting it would poison that cache key
  permanently.

#### Test project

Retargeted from `net9.0-windows` to `net9.0` — nothing under test is
Windows-specific, and this lets the suite run on a Linux CI runner. Added
coverage for the database (filters, sorting, tags, ratings, duplicates, roots,
rescan field preservation), the scanner (incremental skip, re-index on change,
stale-row pruning, missing roots, hash-based duplicates), the metadata reader
(dimension extraction, graceful handling of corrupt and non-image files) and the
recursive file walker.

#### Verification

`dotnet build` green, 0 warnings / 0 errors. `dotnet test` green, **48/48
passing** (was 6). Beyond unit tests, the services were exercised end to end
under a headless Avalonia host against a generated 73-file library: scan indexed
73, year bucketing correct, search/sort/duplicate/rating/tag queries all correct,
25 thumbnails decoded with 0 failures and a populated disk cache, and a second
scan re-indexed 0 files, confirming the incremental path.

#### Known gaps

Editing (crop/rotate/colour), face recognition, and reverse geocoding of GPS
coordinates into place names are not implemented. The schema reserves an
`albums` table and a `place_name` column for the latter two. RAW and HEIC files
are indexed and searchable but render as placeholders, since the built-in
decoder cannot produce thumbnails for them.
