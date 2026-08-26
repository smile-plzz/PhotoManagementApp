# Prompt for Claude CLI — Complete & Ship Photo Management App (Avalonia UI)

You are working autonomously from a terminal via Claude Code CLI on the
`PhotoManagementApp` repository (branch: current feature branch). Treat this
as an extended, multi-session engineering engagement: plan deeply, implement
incrementally, verify each phase before moving to the next, and commit as you
go. All non-trivial design decisions, architecture tradeoffs, and debugging
should be reasoned through by you in the terminal — do not hand off complex
reasoning to inline comments or TODOs for a human to resolve later.

## Context: current state of the repo

- Framework: Avalonia UI (C#), targeting `net9.0`, MVVM pattern (`CommunityToolkit.Mvvm`-style `ViewModelBase`/`ObservableObject` conventions already implied by `ViewModels/`).
- Project layout: `PhotoManagementApp/` (app) and `PhotoManagementApp.Tests/` (test project) already scaffolded.
- Implemented so far (Phase 1, partial):
  - `MainWindow.axaml` with a three-column layout: folder tree (left), thumbnail grid (center), preview/detail pane (right).
  - Folder browsing via `StorageProvider`, populated into a `TreeView` backed by `FolderItem`.
  - Lazy-loaded thumbnails via `ImageItem`, bound to an `ObservableCollection<ImageItem>` in `MainWindowViewModel`.
  - Basic full image preview bound to `SelectedImage`.
  - Project builds with 0 warnings/errors as of the last recorded log entry.
- Not yet implemented: everything in Phase 2 and Phase 3 below, and there is **no persistence layer, no metadata/EXIF handling, and no automated tests written yet** (test project exists but is empty/unverified — confirm this first).
- Source of truth for full feature scope: `PhotoManagementAppFeatures.markdown` in repo root. Treat it as the product backlog, not a literal spec — use judgment on scope/sequencing for an offline desktop MVP-to-v1 product.
- Progress log convention: `development_log.md` is updated with dated entries after each meaningful milestone. Continue this convention.

## Your mandate

Take this project from "Phase 1 partial MVP" to a **shippable, cross-platform
desktop v1 release**, working phase by phase, committing working increments,
and never leaving the build in a broken state between commits.

## Ground rules

1. **Verify before building.** Before writing new code, run `dotnet build`
   and `dotnet test` to confirm the actual current state matches the logs.
   Do not trust `development_log.md` blindly — treat it as a hint, confirm
   with the compiler and test runner.
2. **Work in phases, smallest safe increments.** Within each phase, pick one
   coherent slice (e.g., "EXIF read support" before "EXIF editing"), implement
   it, build, test, commit, then move on. Do not attempt to implement an
   entire phase in one giant commit.
3. **All complex reasoning happens in-terminal, by you.** This includes:
   architecture decisions (e.g., which SQLite ORM/wrapper, how to structure
   the metadata cache vs. source-of-truth files, how to avoid UI-thread
   blocking on large libraries), debugging build/runtime failures, and
   performance tradeoffs (e.g., thumbnail caching strategy for 10,000+ image
   libraries). Do not defer these to comments like `// TODO: figure out
   later` — resolve them now, and record *why* you chose an approach in
   `development_log.md` when the reasoning is non-obvious.
4. **No cloud dependencies.** This is an explicitly offline app. Do not
   introduce any network calls, telemetry, or cloud SDKs. Face
   recognition, if implemented, must run via a local/offline model.
5. **Cross-platform correctness.** Avalonia targets Windows/Linux/macOS.
   Avoid Windows-only APIs (e.g., raw `System.Drawing`) — use
   Avalonia-compatible or cross-platform image libraries (e.g.,
   `ImageSharp`, `SkiaSharp` via Avalonia's own rendering, `MetadataExtractor`
   for EXIF/IPTC/XMP).
6. **Testability.** Business logic (metadata parsing, filtering/sorting,
   duplicate detection, search indexing) must live in testable,
   ViewModel/service classes decoupled from Avalonia UI code, with unit
   tests in `PhotoManagementApp.Tests`. UI-only glue code doesn't need
   coverage, but don't let logic leak into code-behind where it can't be
   tested.
7. **No secrets, no telemetry, no license surprises.** Prefer permissive
   open-source NuGet packages (MIT/Apache/BSD). Flag any GPL/LGPL dependency
   before adding it rather than silently pulling it in.
8. **Definition of "shipped."** A "shipped final product" means: the app
   builds and runs on at least Windows and Linux, has no known crashing
   bugs in the core golden path (open folder → browse → view → tag/rate →
   search/filter), has a packaged/published build (`dotnet publish`,
   self-contained, per-OS), a working test suite that passes in CI-like
   conditions locally, and an up-to-date README with actual setup/usage
   instructions (replacing today's placeholder "Instructions will be
   provided once the basic project structure is in place").

## Build plan

### Phase 1 — Finish the MVP shell
- Confirm current build/test status (see rule 1).
- Harden folder browsing: handle permission errors, empty folders, symlinks,
  very large directories (virtualize the TreeView/grid — don't eagerly
  enumerate everything).
- Thumbnail grid: switch to a virtualized `ItemsRepeater`/`ListBox` if not
  already, with an on-disk thumbnail cache (don't regenerate thumbnails every
  launch).
- Full image viewer: add zoom/pan, and support at least JPEG/PNG/RAW/HEIC
  (evaluate library support; document what's out of scope if RAW/HEIC prove
  too heavy for v1 and note it explicitly rather than silently dropping it).
- Commit: "Phase 1 complete: robust folder browsing, thumbnail caching,
  full image viewer."

### Phase 2 — Core features
- **Local metadata store**: introduce SQLite (e.g., `Microsoft.Data.Sqlite`
  or `sqlite-net-pcl`) for tags, ratings, and cached EXIF — keyed by file
  path + content hash so it survives file moves/renames reasonably. Design
  the schema now; don't leave it implicit.
- **EXIF/IPTC/XMP read + edit** via `MetadataExtractor` (read) and an
  appropriate write-capable library (evaluate `ExifLibNet` or writing IPTC/
  XMP via a maintained library) — non-destructive by default per the feature
  doc (Phase 3 calls for non-destructive editing generally; apply that
  principle to metadata edits too, or clearly document if v1 writes
  in-place).
- **Tagging & star ratings**: UI + persistence.
- **Search by filename**, then **indexed search by metadata/tags/rating** as
  the schema supports it.
- **Sorting/filtering**: by date taken, date modified, filename, size,
  rating; combinable filters as described in the feature doc.
- **Logging**: introduce structured logging (e.g., `Microsoft.Extensions.
  Logging` + a rolling file sink) for diagnosing crashes/perf issues in the
  field, since this is an offline app with no telemetry.
- Unit tests for all new business logic (metadata service, search/filter
  logic, tag/rating persistence) — not just happy path; include edge cases
  (corrupt EXIF, missing files, duplicate tags).
- Commit incrementally per sub-feature, not as one Phase 2 commit.

### Phase 3 — Advanced features & polish
- Duplicate detection (content hash first; perceptual hash as a stretch
  goal — call out the tradeoff in the log if you scope it down).
- Batch operations (rename/tag/rate/move).
- Import from external drive/SD card with auto-organize.
- Basic non-destructive editing (crop/rotate/resize/brightness-contrast).
- Slideshow / lightbox / full-screen mode.
- Performance pass: profile against a synthetic library of 10,000+ images;
  fix any UI-thread blocking, unbounded memory growth from thumbnail/EXIF
  caches, or slow startup.
- Decide and document what's explicitly out of scope for v1 (e.g., face
  recognition, cross-device sync, plugin support are reasonable to defer —
  say so in README/roadmap rather than half-implementing them).

### Phase 4 — Ship
- `dotnet publish` self-contained builds for win-x64 and linux-x64 (add
  osx-x64/arm64 if feasible).
- Smoke-test each published build actually launches and can browse/view a
  real folder of images.
- Rewrite `README.md`: real setup instructions, screenshots if feasible,
  supported platforms, known limitations, how to run tests.
- Final `development_log.md` entry summarizing what shipped vs. what was
  deferred.
- Tag a release commit (and a git tag, e.g. `v1.0.0`, if the workflow
  calls for it).

## Working style expectations

- Before each phase, restate (briefly, in your own reasoning) what "done"
  looks like for that phase, so you can self-verify against it.
- Prefer small, reviewable commits with descriptive messages over large
  batched ones.
- When you hit a genuine judgment call with no clearly-correct answer
  (e.g., RAW format support, face recognition scope, GPL dependency),
  make a reasoned decision, document the reasoning, and proceed — don't
  stall waiting for input unless it's truly a decision only the product
  owner can make (e.g., paid/licensed component, irreversible data
  migration).
- At the end of each phase, run the full build + test suite and confirm
  green before moving on.
