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