using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotoManagementApp.Data;
using PhotoManagementApp.Models;
using PhotoManagementApp.Services;
using ReactiveUI;

namespace PhotoManagementApp.ViewModels
{
    public enum LibraryViewMode
    {
        Grid,
        Timeline,
        Duplicates
    }

    public class MainWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly PhotoDatabase _database;
        private readonly ThumbnailCache _thumbnails;
        private readonly LibraryScanner _scanner;

        private CancellationTokenSource? _scanCancellation;
        private CancellationTokenSource? _searchDebounce;
        private bool _suppressReload;

        public MainWindowViewModel()
            : this(PhotoDatabase.OpenDefault(), new ThumbnailCache())
        {
        }

        public MainWindowViewModel(PhotoDatabase database, ThumbnailCache thumbnails)
        {
            _database = database;
            _thumbnails = thumbnails;
            _scanner = new LibraryScanner(database);

            Photos = new ObservableCollection<PhotoTileViewModel>();
            Groups = new ObservableCollection<PhotoGroupViewModel>();
            DuplicateGroups = new ObservableCollection<PhotoGroupViewModel>();
            Folders = new ObservableCollection<FolderItem>();
            LibrarySections = new ObservableCollection<NavigationItem>();
            Years = new ObservableCollection<NavigationItem>();
            Tags = new ObservableCollection<NavigationItem>();
            Cameras = new ObservableCollection<NavigationItem>();
            SelectedPhotos = new ObservableCollection<PhotoTileViewModel>();
            SelectedPhotoTags = new ObservableCollection<string>();

            AddFolderCommand = ReactiveCommand.CreateFromTask(AddFolderAsync);
            RescanCommand = ReactiveCommand.CreateFromTask(RescanAsync);
            CancelScanCommand = ReactiveCommand.Create(CancelScan);
            ClearFiltersCommand = ReactiveCommand.Create(ClearFilters);
            RevealCommand = ReactiveCommand.Create(RevealSelected);
            OpenExternallyCommand = ReactiveCommand.Create(OpenSelectedExternally);
            CopyToFolderCommand = ReactiveCommand.CreateFromTask(CopySelectedToFolderAsync);
            ToggleFavoriteCommand = ReactiveCommand.Create(ToggleFavoriteOnSelection);
            AddTagCommand = ReactiveCommand.Create(AddTagToSelection);
            RemoveTagCommand = ReactiveCommand.Create<string>(RemoveTagFromSelection);
            SetRatingCommand = ReactiveCommand.Create<int>(SetRatingOnSelection);
            ShowDuplicatesCommand = ReactiveCommand.CreateFromTask(ShowDuplicatesAsync);
            ClearThumbnailCacheCommand = ReactiveCommand.CreateFromTask(ClearThumbnailCacheAsync);
            SelectAllCommand = ReactiveCommand.Create(SelectAll);

            BuildLibrarySections();
            LoadRoots();
        }

        // ------------------------------------------------------------ collections

        public ObservableCollection<PhotoTileViewModel> Photos { get; }
        public ObservableCollection<PhotoGroupViewModel> Groups { get; }
        public ObservableCollection<PhotoGroupViewModel> DuplicateGroups { get; }
        public ObservableCollection<FolderItem> Folders { get; }
        public ObservableCollection<NavigationItem> LibrarySections { get; }
        public ObservableCollection<NavigationItem> Years { get; }
        public ObservableCollection<NavigationItem> Tags { get; }
        public ObservableCollection<NavigationItem> Cameras { get; }
        public ObservableCollection<PhotoTileViewModel> SelectedPhotos { get; }
        public ObservableCollection<string> SelectedPhotoTags { get; }

        public string[] SortOptions { get; } =
        {
            "Date taken", "Date modified", "File name", "File size", "Rating", "Resolution"
        };

        public string[] ThumbnailSizeOptions { get; } = { "Small", "Medium", "Large" };

        // ------------------------------------------------------------ commands

        public ReactiveCommand<Unit, Unit> AddFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> RescanCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelScanCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearFiltersCommand { get; }
        public ReactiveCommand<Unit, Unit> RevealCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenExternallyCommand { get; }
        public ReactiveCommand<Unit, Unit> CopyToFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleFavoriteCommand { get; }
        public ReactiveCommand<Unit, Unit> AddTagCommand { get; }
        public ReactiveCommand<string, Unit> RemoveTagCommand { get; }
        public ReactiveCommand<int, Unit> SetRatingCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDuplicatesCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearThumbnailCacheCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }

        // ------------------------------------------------------------ view state

        private LibraryViewMode _viewMode = LibraryViewMode.Timeline;
        public LibraryViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _viewMode, value);
                this.RaisePropertyChanged(nameof(IsGridView));
                this.RaisePropertyChanged(nameof(IsTimelineView));
                this.RaisePropertyChanged(nameof(IsDuplicatesView));
            }
        }

        public bool IsGridView => ViewMode == LibraryViewMode.Grid;
        public bool IsTimelineView => ViewMode == LibraryViewMode.Timeline;
        public bool IsDuplicatesView => ViewMode == LibraryViewMode.Duplicates;

        public bool ShowGrid
        {
            get => ViewMode == LibraryViewMode.Grid;
            set { if (value) ViewMode = LibraryViewMode.Grid; }
        }

        public bool ShowTimeline
        {
            get => ViewMode == LibraryViewMode.Timeline;
            set { if (value) ViewMode = LibraryViewMode.Timeline; }
        }

        private int _thumbnailSizeIndex = 1;
        public int ThumbnailSizeIndex
        {
            get => _thumbnailSizeIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _thumbnailSizeIndex, value);
                this.RaisePropertyChanged(nameof(TileSize));
            }
        }

        public double TileSize => ThumbnailSizeIndex switch
        {
            0 => 110,
            2 => 260,
            _ => 170
        };

        private string _statusMessage = "Add a folder to build your library.";
        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            set => this.RaiseAndSetIfChanged(ref _isScanning, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        private string _resultSummary = string.Empty;
        public string ResultSummary
        {
            get => _resultSummary;
            set => this.RaiseAndSetIfChanged(ref _resultSummary, value);
        }

        public bool HasNoPhotos => Photos.Count == 0 && Groups.Count == 0 && !IsScanning;

        // ------------------------------------------------------------ filters

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                this.RaiseAndSetIfChanged(ref _searchText, value);
                DebouncedReload();
            }
        }

        private int _minRating;
        public int MinRating
        {
            get => _minRating;
            set
            {
                this.RaiseAndSetIfChanged(ref _minRating, value);
                ReloadPhotos();
            }
        }

        private bool _favoritesOnly;
        public bool FavoritesOnly
        {
            get => _favoritesOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _favoritesOnly, value);
                ReloadPhotos();
            }
        }

        private bool _withLocationOnly;
        public bool WithLocationOnly
        {
            get => _withLocationOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _withLocationOnly, value);
                ReloadPhotos();
            }
        }

        private bool _untaggedOnly;
        public bool UntaggedOnly
        {
            get => _untaggedOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _untaggedOnly, value);
                ReloadPhotos();
            }
        }

        private int _sortIndex;
        public int SortIndex
        {
            get => _sortIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _sortIndex, value);
                ReloadPhotos();
            }
        }

        private bool _sortDescending = true;
        public bool SortDescending
        {
            get => _sortDescending;
            set
            {
                this.RaiseAndSetIfChanged(ref _sortDescending, value);
                ReloadPhotos();
            }
        }

        private DateTimeOffset? _dateFrom;
        public DateTimeOffset? DateFrom
        {
            get => _dateFrom;
            set
            {
                this.RaiseAndSetIfChanged(ref _dateFrom, value);
                ReloadPhotos();
            }
        }

        private DateTimeOffset? _dateTo;
        public DateTimeOffset? DateTo
        {
            get => _dateTo;
            set
            {
                this.RaiseAndSetIfChanged(ref _dateTo, value);
                ReloadPhotos();
            }
        }

        private string? _activeFolderPath;
        public string? ActiveFolderPath
        {
            get => _activeFolderPath;
            set
            {
                this.RaiseAndSetIfChanged(ref _activeFolderPath, value);
                this.RaisePropertyChanged(nameof(ScopeLabel));
                ReloadPhotos();
            }
        }

        private string? _activeTag;
        private string? _activeCamera;
        private int? _activeYear;

        private NavigationItem? _selectedNavigationItem;
        public NavigationItem? SelectedNavigationItem
        {
            get => _selectedNavigationItem;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedNavigationItem, value);
                if (value is not null)
                    ApplyNavigation(value);
            }
        }

        public string ScopeLabel
        {
            get
            {
                if (_activeFolderPath is not null)
                    return Path.GetFileName(_activeFolderPath.TrimEnd(Path.DirectorySeparatorChar));
                if (_activeYear is not null)
                    return _activeYear.Value.ToString();
                if (_activeTag is not null)
                    return "#" + _activeTag;
                if (_activeCamera is not null)
                    return _activeCamera;
                return "All photos";
            }
        }

        // ------------------------------------------------------------ selection

        private PhotoTileViewModel? _selectedPhoto;
        public PhotoTileViewModel? SelectedPhoto
        {
            get => _selectedPhoto;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedPhoto, value);
                this.RaisePropertyChanged(nameof(HasSelection));
                this.RaisePropertyChanged(nameof(SelectedDetails));
                RefreshSelectedTags();
            }
        }

        public bool HasSelection => SelectedPhoto is not null;

        public IReadOnlyList<DetailRow> SelectedDetails
        {
            get
            {
                var photo = SelectedPhoto?.Photo;
                if (photo is null)
                    return Array.Empty<DetailRow>();

                var rows = new List<DetailRow>
                {
                    new("File", photo.FileName),
                    new("Folder", photo.FolderPath),
                    new("Date taken", photo.DateTaken?.ToString("dddd, d MMMM yyyy 'at' HH:mm") ?? "Not recorded"),
                    new("Modified", photo.DateModified.ToLocalTime().ToString("d MMM yyyy HH:mm")),
                    new("Dimensions", photo.DimensionsDisplay),
                    new("Size", photo.FileSizeDisplay),
                };

                if (!string.IsNullOrEmpty(photo.CameraDisplayName))
                    rows.Add(new DetailRow("Camera", photo.CameraDisplayName));
                if (!string.IsNullOrEmpty(photo.LensModel))
                    rows.Add(new DetailRow("Lens", photo.LensModel!));
                if (photo.FNumber is not null)
                    rows.Add(new DetailRow("Aperture", $"f/{photo.FNumber:0.#}"));
                if (!string.IsNullOrEmpty(photo.ExposureTime))
                    rows.Add(new DetailRow("Exposure", photo.ExposureTime!));
                if (photo.IsoSpeed is not null)
                    rows.Add(new DetailRow("ISO", photo.IsoSpeed.Value.ToString()));
                if (photo.FocalLength is not null)
                    rows.Add(new DetailRow("Focal length", $"{photo.FocalLength:0.#} mm"));
                if (photo.HasLocation)
                    rows.Add(new DetailRow("Location", $"{photo.Latitude:0.#####}, {photo.Longitude:0.#####}"));

                return rows;
            }
        }

        private string _newTagText = string.Empty;
        public string NewTagText
        {
            get => _newTagText;
            set => this.RaiseAndSetIfChanged(ref _newTagText, value);
        }

        // ------------------------------------------------------------ navigation

        private void BuildLibrarySections()
        {
            LibrarySections.Clear();
            LibrarySections.Add(new NavigationItem(NavigationKind.AllPhotos, "All photos", "\U0001F5BC"));
            LibrarySections.Add(new NavigationItem(NavigationKind.Favorites, "Favourites", "♥"));
            LibrarySections.Add(new NavigationItem(NavigationKind.RecentlyAdded, "Recently added", "➕"));
            LibrarySections.Add(new NavigationItem(NavigationKind.Untagged, "Untagged", "○"));
            LibrarySections.Add(new NavigationItem(NavigationKind.Duplicates, "Duplicates", "⧉"));
        }

        private void ApplyNavigation(NavigationItem item)
        {
            _suppressReload = true;
            _activeFolderPath = null;
            _activeTag = null;
            _activeCamera = null;
            _activeYear = null;
            _favoritesOnly = false;
            _untaggedOnly = false;
            _dateFrom = null;
            _dateTo = null;

            switch (item.Kind)
            {
                case NavigationKind.Favorites:
                    _favoritesOnly = true;
                    break;
                case NavigationKind.Untagged:
                    _untaggedOnly = true;
                    break;
                case NavigationKind.RecentlyAdded:
                    _dateFrom = DateTimeOffset.Now.AddDays(-30);
                    break;
                case NavigationKind.Year when item.Payload is int year:
                    _activeYear = year;
                    _dateFrom = new DateTimeOffset(new DateTime(year, 1, 1));
                    _dateTo = new DateTimeOffset(new DateTime(year, 12, 31, 23, 59, 59));
                    break;
                case NavigationKind.Tag when item.Payload is string tag:
                    _activeTag = tag;
                    break;
                case NavigationKind.Camera when item.Payload is string camera:
                    _activeCamera = camera;
                    break;
            }

            _suppressReload = false;

            this.RaisePropertyChanged(nameof(FavoritesOnly));
            this.RaisePropertyChanged(nameof(UntaggedOnly));
            this.RaisePropertyChanged(nameof(DateFrom));
            this.RaisePropertyChanged(nameof(DateTo));
            this.RaisePropertyChanged(nameof(ActiveFolderPath));
            this.RaisePropertyChanged(nameof(ScopeLabel));

            if (item.Kind == NavigationKind.Duplicates)
            {
                _ = ShowDuplicatesAsync();
                return;
            }

            if (ViewMode == LibraryViewMode.Duplicates)
                ViewMode = LibraryViewMode.Timeline;

            ReloadPhotos();
        }

        /// <summary>Called by the view when a folder in the tree is clicked.</summary>
        public void SelectFolder(FolderItem folder)
        {
            if (folder.IsPlaceholder)
                return;

            _activeTag = null;
            _activeCamera = null;
            _activeYear = null;
            ActiveFolderPath = folder.FullPath;
        }

        // ------------------------------------------------------------ loading

        private PhotoQuery BuildQuery()
        {
            var query = new PhotoQuery
            {
                SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                FolderPath = _activeFolderPath,
                IncludeSubfolders = true,
                MinRating = MinRating > 0 ? MinRating : null,
                FavoritesOnly = FavoritesOnly,
                CameraModel = _activeCamera,
                RequireLocation = WithLocationOnly,
                UntaggedOnly = UntaggedOnly,
                DateFrom = DateFrom?.DateTime,
                DateTo = DateTo?.DateTime,
                SortField = SortIndex switch
                {
                    1 => PhotoSortField.DateModified,
                    2 => PhotoSortField.FileName,
                    3 => PhotoSortField.FileSize,
                    4 => PhotoSortField.Rating,
                    5 => PhotoSortField.Resolution,
                    _ => PhotoSortField.DateTaken
                },
                SortDescending = SortDescending,
            };

            if (_activeTag is not null)
                query.Tags.Add(_activeTag);

            return query;
        }

        private void DebouncedReload()
        {
            _searchDebounce?.Cancel();
            _searchDebounce = new CancellationTokenSource();
            var token = _searchDebounce.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (!token.IsCancellationRequested)
                    await Dispatcher.UIThread.InvokeAsync(ReloadPhotos);
            }, token);
        }

        public void ReloadPhotos()
        {
            if (_suppressReload)
                return;

            _ = ReloadPhotosAsync();
        }

        private async Task ReloadPhotosAsync()
        {
            IsBusy = true;
            try
            {
                var query = BuildQuery();
                var photos = await Task.Run(() => _database.Query(query)).ConfigureAwait(true);

                foreach (var tile in Photos)
                    tile.CancelLoad();

                Photos.Clear();
                Groups.Clear();
                SelectedPhotos.Clear();

                var tiles = photos.Select(p => new PhotoTileViewModel(p, _thumbnails)).ToList();
                foreach (var tile in tiles)
                    Photos.Add(tile);

                foreach (var group in BuildTimelineGroups(tiles))
                    Groups.Add(group);

                ResultSummary = tiles.Count switch
                {
                    0 => "No photos match",
                    1 => "1 photo",
                    _ => $"{tiles.Count:N0} photos"
                };

                if (SelectedPhoto is not null && tiles.All(t => t.FilePath != SelectedPhoto.FilePath))
                    SelectedPhoto = null;

                this.RaisePropertyChanged(nameof(HasNoPhotos));
                RefreshFacets();
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Buckets photos into timeline bands. The bucket width adapts to how much
        /// is on screen: a single day's shoot groups by day, a decade by year, so
        /// the header count stays readable either way.
        /// </summary>
        private static IEnumerable<PhotoGroupViewModel> BuildTimelineGroups(IReadOnlyList<PhotoTileViewModel> tiles)
        {
            if (tiles.Count == 0)
                yield break;

            var dates = tiles.Select(t => t.Photo.EffectiveDate).ToList();
            var span = dates.Max() - dates.Min();

            Func<DateTime, DateTime> bucket;
            Func<DateTime, string> header;

            if (span.TotalDays <= 92)
            {
                bucket = d => d.Date;
                header = d => d.ToString("dddd, d MMMM yyyy");
            }
            else if (span.TotalDays <= 366 * 4)
            {
                bucket = d => new DateTime(d.Year, d.Month, 1);
                header = d => d.ToString("MMMM yyyy");
            }
            else
            {
                bucket = d => new DateTime(d.Year, 1, 1);
                header = d => d.Year.ToString();
            }

            // Preserve the query's ordering within and between groups.
            var groups = new List<(DateTime Key, List<PhotoTileViewModel> Items)>();
            var index = new Dictionary<DateTime, int>();

            foreach (var tile in tiles)
            {
                var key = bucket(tile.Photo.EffectiveDate);
                if (!index.TryGetValue(key, out var position))
                {
                    position = groups.Count;
                    index[key] = position;
                    groups.Add((key, new List<PhotoTileViewModel>()));
                }
                groups[position].Items.Add(tile);
            }

            foreach (var (key, items) in groups)
            {
                var places = items.Select(i => i.Photo.PlaceName)
                                  .Where(p => !string.IsNullOrWhiteSpace(p))
                                  .Distinct()
                                  .Take(2)
                                  .ToList();
                var sub = places.Count > 0
                    ? string.Join(", ", places)
                    : items.Select(i => Path.GetFileName(i.FolderPath)).Distinct().FirstOrDefault() ?? string.Empty;

                yield return new PhotoGroupViewModel(header(key), sub, items);
            }
        }

        private void RefreshFacets()
        {
            var years = _database.GetYearCounts();
            Years.Clear();
            foreach (var (year, count) in years)
                Years.Add(new NavigationItem(NavigationKind.Year, year.ToString(), "\U0001F4C5", year, count));

            var tags = _database.GetAllTags();
            Tags.Clear();
            foreach (var (name, count) in tags)
                Tags.Add(new NavigationItem(NavigationKind.Tag, name, "\U0001F3F7", name, count));

            var cameras = _database.GetDistinctCameras();
            Cameras.Clear();
            foreach (var camera in cameras)
                Cameras.Add(new NavigationItem(NavigationKind.Camera, camera, "\U0001F4F7", camera));
        }

        private void LoadRoots()
        {
            Folders.Clear();
            foreach (var root in _database.GetRoots())
            {
                if (System.IO.Directory.Exists(root))
                    Folders.Add(new FolderItem(root));
            }

            var count = _database.GetPhotoCount();
            if (count > 0)
            {
                StatusMessage = $"{count:N0} photos in library";
                ReloadPhotos();
            }
        }

        // ------------------------------------------------------------ scanning

        private async Task AddFolderAsync()
        {
            var window = (Avalonia.Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (window?.StorageProvider is not { } storageProvider)
                return;

            var picked = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose a folder of photos",
                AllowMultiple = true
            });

            if (picked.Count == 0)
                return;

            var added = new List<string>();
            foreach (var folder in picked)
            {
                var path = folder.Path.LocalPath;
                if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path))
                    continue;

                _database.AddRoot(path);
                added.Add(path);

                if (Folders.All(f => !string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                    Folders.Add(new FolderItem(path));
            }

            if (added.Count > 0)
                await RunScanAsync(added);
        }

        private Task RescanAsync() => RunScanAsync(_database.GetRoots());

        private async Task RunScanAsync(IReadOnlyList<string> roots)
        {
            if (roots.Count == 0)
            {
                StatusMessage = "No folders added yet.";
                return;
            }

            CancelScan();
            _scanCancellation = new CancellationTokenSource();
            IsScanning = true;

            var progress = new Progress<ScanProgress>(p =>
            {
                StatusMessage = p.Message;
            });

            try
            {
                var result = await _scanner.ScanAsync(roots, progress, _scanCancellation.Token);
                StatusMessage = result.Message;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Scan failed: " + ex.Message;
            }
            finally
            {
                IsScanning = false;
                await ReloadPhotosAsync();
            }
        }

        private void CancelScan()
        {
            _scanCancellation?.Cancel();
            _scanCancellation = null;
        }

        // ------------------------------------------------------------ actions

        private void ClearFilters()
        {
            _suppressReload = true;
            SearchText = string.Empty;
            MinRating = 0;
            FavoritesOnly = false;
            WithLocationOnly = false;
            UntaggedOnly = false;
            DateFrom = null;
            DateTo = null;
            _activeFolderPath = null;
            _activeTag = null;
            _activeCamera = null;
            _activeYear = null;
            _suppressReload = false;

            this.RaisePropertyChanged(nameof(ActiveFolderPath));
            this.RaisePropertyChanged(nameof(ScopeLabel));
            ReloadPhotos();
        }

        private void RevealSelected()
        {
            if (SelectedPhoto is null) return;
            if (!PlatformService.RevealInFileManager(SelectedPhoto.FilePath))
                StatusMessage = "Could not open the file manager.";
        }

        private void OpenSelectedExternally()
        {
            if (SelectedPhoto is null) return;
            if (!PlatformService.OpenWithDefaultApplication(SelectedPhoto.FilePath))
                StatusMessage = "Could not open the file.";
        }

        private async Task CopySelectedToFolderAsync()
        {
            var targets = EffectiveSelection();
            if (targets.Count == 0)
                return;

            var window = (Avalonia.Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (window?.StorageProvider is not { } storageProvider)
                return;

            var picked = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = targets.Count == 1 ? "Copy photo to..." : $"Copy {targets.Count} photos to...",
                AllowMultiple = false
            });

            if (picked.Count == 0)
                return;

            var destination = picked[0].Path.LocalPath;
            var copied = 0;
            var failed = 0;

            await Task.Run(() =>
            {
                foreach (var tile in targets)
                {
                    try
                    {
                        PlatformService.CopyToFolder(tile.FilePath, destination);
                        copied++;
                    }
                    catch (Exception)
                    {
                        failed++;
                    }
                }
            });

            StatusMessage = failed == 0
                ? $"Copied {copied} photo(s) to {Path.GetFileName(destination)}"
                : $"Copied {copied}, failed {failed}";
        }

        private void ToggleFavoriteOnSelection()
        {
            var targets = EffectiveSelection();
            if (targets.Count == 0) return;

            // One click sets a consistent state across a mixed selection rather
            // than flipping each photo independently.
            var makeFavorite = targets.Any(t => !t.IsFavorite);
            foreach (var tile in targets)
            {
                tile.IsFavorite = makeFavorite;
                _database.SetFavorite(tile.Id, makeFavorite);
            }

            StatusMessage = makeFavorite
                ? $"Added {targets.Count} to favourites"
                : $"Removed {targets.Count} from favourites";
        }

        private void SetRatingOnSelection(int rating)
        {
            var targets = EffectiveSelection();
            if (targets.Count == 0) return;

            foreach (var tile in targets)
            {
                tile.Rating = rating;
                _database.SetRating(tile.Id, rating);
            }

            StatusMessage = rating == 0
                ? $"Cleared rating on {targets.Count} photo(s)"
                : $"Rated {targets.Count} photo(s) {rating} star(s)";
        }

        private void AddTagToSelection()
        {
            var tag = NewTagText.Trim();
            if (string.IsNullOrEmpty(tag))
                return;

            var targets = EffectiveSelection();
            if (targets.Count == 0)
                return;

            foreach (var tile in targets)
                _database.AddTagToPhoto(tile.Id, tag);

            NewTagText = string.Empty;
            RefreshSelectedTags();
            RefreshFacets();
            StatusMessage = $"Tagged {targets.Count} photo(s) with '{tag}'";
        }

        private void RemoveTagFromSelection(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return;

            foreach (var tile in EffectiveSelection())
                _database.RemoveTagFromPhoto(tile.Id, tag);

            RefreshSelectedTags();
            RefreshFacets();
        }

        private void RefreshSelectedTags()
        {
            SelectedPhotoTags.Clear();
            if (SelectedPhoto is null)
                return;

            foreach (var tag in _database.GetTagsForPhoto(SelectedPhoto.Id))
                SelectedPhotoTags.Add(tag);
        }

        private async Task ShowDuplicatesAsync()
        {
            IsBusy = true;
            try
            {
                var groups = await Task.Run(() => _database.GetDuplicateGroups());

                DuplicateGroups.Clear();
                var wastedBytes = 0L;

                foreach (var group in groups)
                {
                    var tiles = group.Select(p => new PhotoTileViewModel(p, _thumbnails)).ToList();
                    // Every copy beyond the first is redundant.
                    wastedBytes += group.Skip(1).Sum(p => p.FileSize);

                    DuplicateGroups.Add(new PhotoGroupViewModel(
                        group[0].FileName,
                        $"{group.Count} copies · {Photo.FormatBytes(group[0].FileSize)} each",
                        tiles));
                }

                ViewMode = LibraryViewMode.Duplicates;
                ResultSummary = groups.Count == 0
                    ? "No duplicates found"
                    : $"{groups.Count} duplicate group(s) · {Photo.FormatBytes(wastedBytes)} recoverable";
                StatusMessage = ResultSummary;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ClearThumbnailCacheAsync()
        {
            await Task.Run(() => _thumbnails.Clear());
            StatusMessage = "Thumbnail cache cleared.";
            await ReloadPhotosAsync();
        }

        private void SelectAll()
        {
            SelectedPhotos.Clear();
            foreach (var tile in Photos)
                SelectedPhotos.Add(tile);
            StatusMessage = $"{SelectedPhotos.Count} selected";
        }

        /// <summary>
        /// The photos an action applies to: the multi-selection when there is one,
        /// otherwise just the focused photo.
        /// </summary>
        private List<PhotoTileViewModel> EffectiveSelection()
        {
            if (SelectedPhotos.Count > 0)
                return SelectedPhotos.ToList();
            return SelectedPhoto is null
                ? new List<PhotoTileViewModel>()
                : new List<PhotoTileViewModel> { SelectedPhoto };
        }

        /// <summary>Handles a click on a tile, honouring Ctrl for multi-select.</summary>
        public void HandleTileClick(PhotoTileViewModel tile, bool isCtrlPressed)
        {
            if (isCtrlPressed)
            {
                if (SelectedPhotos.Remove(tile))
                    tile.IsSelected = false;
                else
                {
                    SelectedPhotos.Add(tile);
                    tile.IsSelected = true;
                }
                StatusMessage = $"{SelectedPhotos.Count} selected";
            }
            else
            {
                foreach (var previous in SelectedPhotos)
                    previous.IsSelected = false;
                SelectedPhotos.Clear();
            }

            if (SelectedPhoto is not null && SelectedPhoto != tile && !SelectedPhotos.Contains(SelectedPhoto))
                SelectedPhoto.IsSelected = false;

            tile.IsSelected = true;
            SelectedPhoto = tile;
        }

        /// <summary>Photos in current display order — the viewer navigates through these.</summary>
        public IReadOnlyList<PhotoTileViewModel> CurrentSequence =>
            ViewMode == LibraryViewMode.Duplicates
                ? DuplicateGroups.SelectMany(g => g.Photos).ToList()
                : Photos.ToList();

        internal PhotoDatabase Database => _database;
        internal ThumbnailCache Thumbnails => _thumbnails;

        public void Dispose()
        {
            CancelScan();
            _searchDebounce?.Cancel();
            _thumbnails.Dispose();
            _database.Dispose();
        }
    }

    public record DetailRow(string Label, string Value);
}
