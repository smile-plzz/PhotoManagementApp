using System;
using System.Collections.Generic;

namespace PhotoManagementApp.Models
{
    public enum PhotoSortField
    {
        DateTaken,
        DateModified,
        FileName,
        FileSize,
        Rating,
        Resolution
    }

    /// <summary>
    /// A saved or ad-hoc set of criteria used to query the library. Everything is
    /// optional; an empty query matches the whole library.
    /// </summary>
    public class PhotoQuery
    {
        /// <summary>Free text matched against file name, folder, camera, tags and place.</summary>
        public string? SearchText { get; set; }

        /// <summary>Restrict to one folder. When <see cref="IncludeSubfolders"/> is set, its descendants too.</summary>
        public string? FolderPath { get; set; }

        public bool IncludeSubfolders { get; set; } = true;

        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }

        public int? MinRating { get; set; }
        public bool FavoritesOnly { get; set; }

        public string? CameraModel { get; set; }

        public bool RequireLocation { get; set; }
        public bool UntaggedOnly { get; set; }

        public List<string> Tags { get; } = new();

        public PhotoSortField SortField { get; set; } = PhotoSortField.DateTaken;
        public bool SortDescending { get; set; } = true;

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(SearchText) &&
            FolderPath is null &&
            DateFrom is null &&
            DateTo is null &&
            MinRating is null &&
            !FavoritesOnly &&
            CameraModel is null &&
            !RequireLocation &&
            !UntaggedOnly &&
            Tags.Count == 0;

        public PhotoQuery Clone()
        {
            var copy = new PhotoQuery
            {
                SearchText = SearchText,
                FolderPath = FolderPath,
                IncludeSubfolders = IncludeSubfolders,
                DateFrom = DateFrom,
                DateTo = DateTo,
                MinRating = MinRating,
                FavoritesOnly = FavoritesOnly,
                CameraModel = CameraModel,
                RequireLocation = RequireLocation,
                UntaggedOnly = UntaggedOnly,
                SortField = SortField,
                SortDescending = SortDescending,
            };
            copy.Tags.AddRange(Tags);
            return copy;
        }
    }
}
