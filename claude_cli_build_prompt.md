# Prompt for Claude CLI — Photo Management App: Next Steps (Phase 2)

You are working autonomously from a terminal via Claude Code CLI on the
`PhotoManagementApp` repository. Continue the phased build already in
progress. All non-trivial design decisions, architecture tradeoffs, and
debugging should be reasoned through by you in the terminal — do not defer
complex reasoning to `// TODO` comments for a human to resolve later.

## Verified current state (do not trust `development_log.md` blindly — re-verify with the compiler/test runner before writing new code)

- Framework: Avalonia UI (C#), `net9.0`, MVVM (`ReactiveObject`/`ReactiveCommand`
  via ReactiveUI).
- Structure: `PhotoManagementApp/` (app) + `PhotoManagementApp.Tests/` (xUnit
  tests, 6 tests, passing as of the last verified run).
- Implemented:
  - `MainWindow.axaml`: three-column layout (folder tree / thumbnail grid /
    preview pane).
  - `FolderItem`: folder tree population via `StorageProvider`.
  - `ImageItem`: lazy-loaded thumbnails, bound to `ObservableCollection<ImageItem>`.
  - `Services/ImageService.cs`: `GetImageFiles(folderPath)` (extension-filtered
    file enumeration, hardened against missing folders and
    `UnauthorizedAccessException`/`IOException` — returns empty list rather
    than throwing) and `FilterImages(files, searchText)` (case-insensitive
    filename substring filter). This logic was deliberately extracted out of
    `MainWindowViewModel` so it's unit-testable without the UI.
  - `MainWindowViewModel.LoadImages` delegates to `_imageService.GetImageFiles`.
  - Tests: filename filtering (3 cases), `GetImageFiles` happy path, and the
    new missing-folder regression test.
  - `.gitignore` added; `bin`/`obj` build artifacts untracked (205 files
    removed from git — do not re-add generated output).
- **Not yet wired up**: `ImageService.FilterImages` exists and is tested but
  is **not called from `MainWindowViewModel`** — there is no search box in
  the UI yet driving it. That's your first task below.
- **Known environment issue, not a code defect**: on at least one dev
  machine, Windows Smart App Control blocked execution of the freshly-built
  test DLL (unsigned local binary). This resolved itself in a later session
  run (tests executed fine, 6/6). Don't spend time working around this in
  code; if it recurs, note it in the log and treat CI/a clean runner as the
  pass/fail source of truth, per the existing log entry.
- No persistence layer yet (no SQLite), no EXIF/IPTC/XMP handling, no
  tagging/rating, no virtualized thumbnail grid, no thumbnail disk cache.

## Ground rules (carried over — still apply)

1. Verify before building: run `dotnet build` and `dotnet test` first, don't
   trust the log.
2. Work in the smallest safe increments: one coherent slice → build → test →
   commit → next slice. No giant multi-feature commits.
3. Resolve architecture/debugging judgment calls yourself, in-terminal;
   record non-obvious reasoning in `development_log.md` (continue the
   existing dated-entry convention).
4. No cloud dependencies — this app is offline-only.
5. Cross-platform correctness — avoid Windows-only APIs; prefer
   `SkiaSharp`/Avalonia-native imaging and `MetadataExtractor` for EXIF/
   IPTC/XMP when you get there.
6. Keep business logic out of code-behind and out of the ViewModel where
   it can instead live in a testable service (as `ImageService` already
   demonstrates) — write unit tests for all new logic, including edge
   cases, not just the happy path.
7. Prefer permissive OSS licenses (MIT/Apache/BSD) for new NuGet
   dependencies; flag anything GPL/LGPL before adding it rather than
   silently pulling it in.

## Immediate next slices (in order)

1. **Wire up search**: add a search `TextBox` bound to the thumbnail/image
   list in `MainWindow.axaml`; have `MainWindowViewModel` call
   `_imageService.FilterImages` reactively (e.g., on text change, debounced)
   against the currently loaded folder's images, updating `Images`. This
   closes the gap between the tested service method and actual UI behavior
   — don't let tested-but-unused code linger.
2. **Thumbnail grid virtualization**: confirm whether the current
   `ItemsControl`/`ListBox` backing the thumbnail grid virtualizes (check
   `MainWindow.axaml`). If not, switch to a virtualizing panel so a folder
   with thousands of images doesn't eagerly render every thumbnail control.
3. **On-disk thumbnail cache**: `ImageItem`'s lazy thumbnail loading
   currently (verify this) likely decodes the full image every time a
   thumbnail is requested. Add a cache (e.g., a `%LOCALAPPDATA%`-equivalent
   cross-platform cache dir, keyed by file path + last-write-time or content
   hash) so thumbnails aren't regenerated on every folder revisit.
4. **Sorting**: add sort-by (filename, date modified) to `MainWindowViewModel`,
   with unit tests on the sorting logic in isolation (extend `ImageService`
   or add a small `ImageSortService`, matching the existing pattern of
   keeping logic out of the ViewModel).
5. Once 1–4 are done and green, start **Phase 2 metadata work**: introduce
   SQLite (`Microsoft.Data.Sqlite` or `sqlite-net-pcl`) for tags/ratings,
   and EXIF read support via `MetadataExtractor`. Design the schema
   deliberately (file path + content hash as key, so renames/moves don't
   silently orphan metadata) and document the choice in
   `development_log.md`.

## Working style expectations

- Before each slice, state (in your own reasoning) what "done" looks like,
  so you can self-verify against it.
- Small, descriptive commits over batched ones.
- Make reasoned judgment calls on ambiguous design points and proceed;
  only stop for a genuine product-owner decision (e.g., a paid/licensed
  dependency, an irreversible data migration, dropping a previously-listed
  feature from scope).
- Run the full build + test suite at the end of each slice before moving
  on; don't leave the tree in a red state between commits.
- Keep `development_log.md` current with dated entries per meaningful
  change, matching the existing style already in the file.
