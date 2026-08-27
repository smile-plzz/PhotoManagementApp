using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PhotoManagementApp.Models;
using PhotoManagementApp.Services;
using ReactiveUI;

namespace PhotoManagementApp.ViewModels
{
    /// <summary>
    /// One photo as shown in the grid. The thumbnail is not loaded until
    /// <see cref="BeginLoadThumbnail"/> is called, which the grid does when a tile
    /// scrolls into view — so opening a 50,000-photo library decodes nothing.
    /// </summary>
    public class PhotoTileViewModel : ViewModelBase
    {
        private readonly ThumbnailCache _thumbnails;
        private CancellationTokenSource? _loadCancellation;
        private int _loadStarted;

        public Photo Photo { get; }

        public PhotoTileViewModel(Photo photo, ThumbnailCache thumbnails)
        {
            Photo = photo;
            _thumbnails = thumbnails;
            _rating = photo.Rating;
            _isFavorite = photo.IsFavorite;
        }

        public string FilePath => Photo.FilePath;
        public string FileName => Photo.FileName;
        public string FolderPath => Photo.FolderPath;
        public long Id => Photo.Id;

        private Bitmap? _thumbnail;
        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            private set => this.RaiseAndSetIfChanged(ref _thumbnail, value);
        }

        private bool _isThumbnailLoaded;
        public bool IsThumbnailLoaded
        {
            get => _isThumbnailLoaded;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isThumbnailLoaded, value);
                this.RaisePropertyChanged(nameof(ShowPlaceholder));
            }
        }

        /// <summary>True while the thumbnail is still pending, so the grid can show a stand-in.</summary>
        public bool ShowPlaceholder => !_isThumbnailLoaded;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        private bool _thumbnailFailed;
        /// <summary>True when no thumbnail could be produced (e.g. an unsupported RAW).</summary>
        public bool ThumbnailFailed
        {
            get => _thumbnailFailed;
            private set
            {
                this.RaiseAndSetIfChanged(ref _thumbnailFailed, value);
                this.RaisePropertyChanged(nameof(ShowPlaceholder));
            }
        }

        private int _rating;
        public int Rating
        {
            get => _rating;
            set
            {
                this.RaiseAndSetIfChanged(ref _rating, value);
                Photo.Rating = value;
                this.RaisePropertyChanged(nameof(HasRating));
                this.RaisePropertyChanged(nameof(RatingDisplay));
            }
        }

        public bool HasRating => Rating > 0;
        public string RatingDisplay => Rating > 0 ? new string('*', Rating) : string.Empty;

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                this.RaiseAndSetIfChanged(ref _isFavorite, value);
                Photo.IsFavorite = value;
            }
        }

        public string DateDisplay => Photo.EffectiveDate.ToString("d MMM yyyy");
        public string TimeDisplay => Photo.EffectiveDate.ToString("HH:mm");
        public string SizeDisplay => Photo.FileSizeDisplay;
        public string DimensionsDisplay => Photo.DimensionsDisplay;
        public string CameraDisplay => Photo.CameraDisplayName;
        public bool HasLocation => Photo.HasLocation;

        public string Tooltip =>
            $"{FileName}\n{DateDisplay} {TimeDisplay}\n{DimensionsDisplay}  ·  {SizeDisplay}" +
            (string.IsNullOrEmpty(CameraDisplay) ? string.Empty : $"\n{CameraDisplay}");

        /// <summary>
        /// Starts loading the thumbnail if it has not been started already.
        /// Idempotent and safe to call repeatedly as tiles scroll in and out.
        /// </summary>
        public void BeginLoadThumbnail()
        {
            if (Interlocked.Exchange(ref _loadStarted, 1) == 1)
                return;

            _loadCancellation = new CancellationTokenSource();
            var token = _loadCancellation.Token;

            _ = Task.Run(async () =>
            {
                var bitmap = await _thumbnails.GetThumbnailAsync(FilePath, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                    return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (bitmap is null)
                    {
                        ThumbnailFailed = true;
                    }
                    else
                    {
                        Thumbnail = bitmap;
                        IsThumbnailLoaded = true;
                    }
                });
            }, token);
        }

        /// <summary>Abandons an in-flight thumbnail load for a tile scrolled out of view.</summary>
        public void CancelLoad()
        {
            _loadCancellation?.Cancel();
        }
    }
}
