using System;
using System.IO;

namespace PhotoManagementApp.Models
{
    /// <summary>
    /// One indexed photo. Mirrors a row of the <c>photos</c> table.
    /// </summary>
    public class Photo
    {
        public long Id { get; set; }

        public string FilePath { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }

        /// <summary>Last write time of the file, used to detect changes on rescan.</summary>
        public DateTime DateModified { get; set; }

        /// <summary>EXIF capture time when available, otherwise null.</summary>
        public DateTime? DateTaken { get; set; }

        /// <summary>Best available date for grouping: EXIF date if known, else file mtime.</summary>
        public DateTime EffectiveDate => DateTaken ?? DateModified;

        public int Width { get; set; }
        public int Height { get; set; }

        public string? CameraMake { get; set; }
        public string? CameraModel { get; set; }
        public string? LensModel { get; set; }
        public int? IsoSpeed { get; set; }
        public double? FNumber { get; set; }
        public string? ExposureTime { get; set; }
        public double? FocalLength { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        /// <summary>Human-readable place label, reverse-geocoded offline or set manually.</summary>
        public string? PlaceName { get; set; }

        /// <summary>EXIF orientation tag (1-8). 1 means no rotation.</summary>
        public int Orientation { get; set; } = 1;

        /// <summary>0 = unrated, 1-5 stars.</summary>
        public int Rating { get; set; }

        public bool IsFavorite { get; set; }

        /// <summary>Hash of file content, used for duplicate detection. Null until computed.</summary>
        public string? ContentHash { get; set; }

        public DateTime DateIndexed { get; set; }

        public string CameraDisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CameraMake) && string.IsNullOrWhiteSpace(CameraModel))
                    return string.Empty;
                if (string.IsNullOrWhiteSpace(CameraMake))
                    return CameraModel!.Trim();
                if (string.IsNullOrWhiteSpace(CameraModel))
                    return CameraMake.Trim();

                // Many cameras repeat the make inside the model ("Canon" / "Canon EOS 5D").
                var make = CameraMake.Trim();
                var model = CameraModel.Trim();
                return model.StartsWith(make, StringComparison.OrdinalIgnoreCase)
                    ? model
                    : $"{make} {model}";
            }
        }

        public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

        public string DimensionsDisplay => Width > 0 && Height > 0 ? $"{Width} x {Height}" : "Unknown";

        public string FileSizeDisplay => FormatBytes(FileSize);

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
        }

        public static Photo FromFile(FileInfo file) => new()
        {
            FilePath = file.FullName,
            FolderPath = file.DirectoryName ?? string.Empty,
            FileName = file.Name,
            FileSize = file.Length,
            DateModified = file.LastWriteTimeUtc,
        };
    }
}
