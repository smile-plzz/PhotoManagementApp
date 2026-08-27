using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PhotoManagementApp.Data;
using PhotoManagementApp.Models;
using PhotoManagementApp.Services;
using ReactiveUI;

namespace PhotoManagementApp.ViewModels
{
    /// <summary>
    /// The full-screen viewer. Holds one decoded image at a time and walks a
    /// snapshot of whatever the grid was showing, so navigation order matches
    /// what the user was just looking at.
    /// </summary>
    public class ViewerViewModel : ViewModelBase, IDisposable
    {
        private readonly IReadOnlyList<PhotoTileViewModel> _sequence;
        private readonly PhotoDatabase _database;
        private CancellationTokenSource? _loadCancellation;
        private Bitmap? _currentBitmap;
        private DispatcherTimer? _slideshowTimer;

        public ViewerViewModel(IReadOnlyList<PhotoTileViewModel> sequence, int startIndex, PhotoDatabase database)
        {
            _sequence = sequence.Count > 0 ? sequence : Array.Empty<PhotoTileViewModel>();
            _database = database;
            _index = Math.Clamp(startIndex, 0, Math.Max(0, _sequence.Count - 1));

            NextCommand = ReactiveCommand.Create(Next);
            PreviousCommand = ReactiveCommand.Create(Previous);
            ZoomInCommand = ReactiveCommand.Create(() => Zoom *= 1.25);
            ZoomOutCommand = ReactiveCommand.Create(() => Zoom /= 1.25);
            ResetZoomCommand = ReactiveCommand.Create(() => Zoom = 1.0);
            ToggleInfoCommand = ReactiveCommand.Create(() => ShowInfo = !ShowInfo);
            ToggleSlideshowCommand = ReactiveCommand.Create(ToggleSlideshow);
            RevealCommand = ReactiveCommand.Create(Reveal);
            SetRatingCommand = ReactiveCommand.Create<int>(SetRating);
            ToggleFavoriteCommand = ReactiveCommand.Create(ToggleFavorite);

            LoadCurrent();
        }

        public ReactiveCommand<Unit, Unit> NextCommand { get; }
        public ReactiveCommand<Unit, Unit> PreviousCommand { get; }
        public ReactiveCommand<Unit, double> ZoomInCommand { get; }
        public ReactiveCommand<Unit, double> ZoomOutCommand { get; }
        public ReactiveCommand<Unit, double> ResetZoomCommand { get; }
        public ReactiveCommand<Unit, bool> ToggleInfoCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleSlideshowCommand { get; }
        public ReactiveCommand<Unit, Unit> RevealCommand { get; }
        public ReactiveCommand<int, Unit> SetRatingCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleFavoriteCommand { get; }

        private int _index;
        public int Index
        {
            get => _index;
            private set
            {
                this.RaiseAndSetIfChanged(ref _index, value);
                this.RaisePropertyChanged(nameof(PositionDisplay));
            }
        }

        public PhotoTileViewModel? Current =>
            _sequence.Count == 0 ? null : _sequence[Math.Clamp(Index, 0, _sequence.Count - 1)];

        public Photo? CurrentPhoto => Current?.Photo;

        public string PositionDisplay => _sequence.Count == 0
            ? string.Empty
            : $"{Index + 1} / {_sequence.Count}";

        private Bitmap? _image;
        public Bitmap? Image
        {
            get => _image;
            private set => this.RaiseAndSetIfChanged(ref _image, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        private string _loadError = string.Empty;
        public string LoadError
        {
            get => _loadError;
            private set
            {
                this.RaiseAndSetIfChanged(ref _loadError, value);
                this.RaisePropertyChanged(nameof(HasLoadError));
            }
        }

        public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

        private double _zoom = 1.0;
        public double Zoom
        {
            get => _zoom;
            set
            {
                var clamped = Math.Clamp(value, 0.1, 12.0);
                this.RaiseAndSetIfChanged(ref _zoom, clamped);
                this.RaisePropertyChanged(nameof(ZoomDisplay));
                this.RaisePropertyChanged(nameof(IsZoomed));
            }
        }

        public string ZoomDisplay => $"{Zoom * 100:0}%";
        public bool IsZoomed => Math.Abs(Zoom - 1.0) > 0.01;

        private bool _showInfo = true;
        public bool ShowInfo
        {
            get => _showInfo;
            set => this.RaiseAndSetIfChanged(ref _showInfo, value);
        }

        private bool _isSlideshowRunning;
        public bool IsSlideshowRunning
        {
            get => _isSlideshowRunning;
            private set => this.RaiseAndSetIfChanged(ref _isSlideshowRunning, value);
        }

        public string Title => Current?.FileName ?? "Viewer";

        public IReadOnlyList<DetailRow> Details
        {
            get
            {
                var photo = CurrentPhoto;
                if (photo is null)
                    return Array.Empty<DetailRow>();

                var rows = new List<DetailRow>
                {
                    new("Taken", photo.DateTaken?.ToString("dddd, d MMMM yyyy 'at' HH:mm") ?? "Not recorded"),
                    new("Folder", photo.FolderPath),
                    new("Size", $"{photo.DimensionsDisplay}  ·  {photo.FileSizeDisplay}"),
                };

                if (!string.IsNullOrEmpty(photo.CameraDisplayName))
                    rows.Add(new DetailRow("Camera", photo.CameraDisplayName));

                var exposure = new List<string>();
                if (photo.FNumber is not null) exposure.Add($"f/{photo.FNumber:0.#}");
                if (!string.IsNullOrEmpty(photo.ExposureTime)) exposure.Add(photo.ExposureTime!);
                if (photo.IsoSpeed is not null) exposure.Add($"ISO {photo.IsoSpeed}");
                if (photo.FocalLength is not null) exposure.Add($"{photo.FocalLength:0.#}mm");
                if (exposure.Count > 0)
                    rows.Add(new DetailRow("Exposure", string.Join("  ·  ", exposure)));

                if (photo.HasLocation)
                    rows.Add(new DetailRow("Location", $"{photo.Latitude:0.#####}, {photo.Longitude:0.#####}"));

                return rows;
            }
        }

        public int Rating => CurrentPhoto?.Rating ?? 0;
        public bool IsFavorite => CurrentPhoto?.IsFavorite ?? false;

        // ------------------------------------------------------------ navigation

        public void Next()
        {
            if (_sequence.Count == 0) return;
            Index = (Index + 1) % _sequence.Count;
            LoadCurrent();
        }

        public void Previous()
        {
            if (_sequence.Count == 0) return;
            Index = (Index - 1 + _sequence.Count) % _sequence.Count;
            LoadCurrent();
        }

        private void LoadCurrent()
        {
            _loadCancellation?.Cancel();
            _loadCancellation = new CancellationTokenSource();
            var token = _loadCancellation.Token;

            Zoom = 1.0;
            LoadError = string.Empty;
            RaiseCurrentChanged();

            var photo = CurrentPhoto;
            if (photo is null)
                return;

            var path = photo.FilePath;
            IsLoading = true;

            _ = Task.Run(async () =>
            {
                Bitmap? bitmap = null;
                string? error = null;

                try
                {
                    if (!File.Exists(path))
                    {
                        error = "File no longer exists on disk.";
                    }
                    else if (!ImageService.IsDecodable(path))
                    {
                        error = "This format cannot be displayed. Use 'Open' to view it in another app.";
                    }
                    else
                    {
                        using var stream = File.OpenRead(path);
                        bitmap = new Bitmap(stream);
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                if (token.IsCancellationRequested)
                {
                    bitmap?.Dispose();
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        bitmap?.Dispose();
                        return;
                    }

                    // Free the previous full-resolution decode before adopting the
                    // new one, so paging through a folder holds one image, not all.
                    _currentBitmap?.Dispose();
                    _currentBitmap = bitmap;
                    Image = bitmap;
                    LoadError = error ?? string.Empty;
                    IsLoading = false;
                });
            }, token);
        }

        private void RaiseCurrentChanged()
        {
            this.RaisePropertyChanged(nameof(Current));
            this.RaisePropertyChanged(nameof(CurrentPhoto));
            this.RaisePropertyChanged(nameof(Details));
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(Rating));
            this.RaisePropertyChanged(nameof(IsFavorite));
        }

        // ------------------------------------------------------------ actions

        private void SetRating(int rating)
        {
            if (Current is null) return;
            Current.Rating = rating;
            _database.SetRating(Current.Id, rating);
            this.RaisePropertyChanged(nameof(Rating));
        }

        private void ToggleFavorite()
        {
            if (Current is null) return;
            Current.IsFavorite = !Current.IsFavorite;
            _database.SetFavorite(Current.Id, Current.IsFavorite);
            this.RaisePropertyChanged(nameof(IsFavorite));
        }

        private void Reveal()
        {
            if (Current is not null)
                PlatformService.RevealInFileManager(Current.FilePath);
        }

        public void OpenExternally()
        {
            if (Current is not null)
                PlatformService.OpenWithDefaultApplication(Current.FilePath);
        }

        private void ToggleSlideshow()
        {
            if (IsSlideshowRunning)
            {
                StopSlideshow();
                return;
            }

            _slideshowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _slideshowTimer.Tick += (_, _) => Next();
            _slideshowTimer.Start();
            IsSlideshowRunning = true;
        }

        private void StopSlideshow()
        {
            _slideshowTimer?.Stop();
            _slideshowTimer = null;
            IsSlideshowRunning = false;
        }

        public void Dispose()
        {
            StopSlideshow();
            _loadCancellation?.Cancel();
            _currentBitmap?.Dispose();
            _currentBitmap = null;
            Image = null;
        }
    }
}
