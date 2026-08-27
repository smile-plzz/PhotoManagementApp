using System;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using PhotoManagementApp.Models;
using Directory = MetadataExtractor.Directory;

namespace PhotoManagementApp.Services
{
    /// <summary>
    /// Pulls EXIF/IPTC/XMP facts out of an image file. Every read is best-effort:
    /// a corrupt or metadata-free file yields a <see cref="Photo"/> populated from
    /// the file system alone rather than an exception, because a single bad file
    /// must never abort a library scan.
    /// </summary>
    public class MetadataReader
    {
        public Photo Read(FileInfo file)
        {
            var photo = Photo.FromFile(file);

            try
            {
                var directories = ImageMetadataReader.ReadMetadata(file.FullName);
                ApplyExifSubIfd(photo, directories);
                ApplyExifIfd0(photo, directories);
                ApplyDimensions(photo, directories);
                ApplyGps(photo, directories);
            }
            catch (Exception)
            {
                // Unreadable metadata is not a failure — the file-system facts stand.
            }

            return photo;
        }

        public Photo Read(string filePath) => Read(new FileInfo(filePath));

        private static void ApplyExifSubIfd(Photo photo, System.Collections.Generic.IReadOnlyList<Directory> directories)
        {
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd is null) return;

            if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken))
                photo.DateTaken = taken;
            else if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var digitized))
                photo.DateTaken = digitized;

            if (subIfd.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                photo.IsoSpeed = iso;

            if (subIfd.TryGetDouble(ExifDirectoryBase.TagFNumber, out var fNumber))
                photo.FNumber = fNumber;

            if (subIfd.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var focal))
                photo.FocalLength = focal;

            var exposure = subIfd.GetDescription(ExifDirectoryBase.TagExposureTime);
            if (!string.IsNullOrWhiteSpace(exposure))
                photo.ExposureTime = exposure;

            var lens = subIfd.GetDescription(ExifDirectoryBase.TagLensModel);
            if (!string.IsNullOrWhiteSpace(lens))
                photo.LensModel = lens;

            if (subIfd.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var w) && w > 0)
                photo.Width = w;
            if (subIfd.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var h) && h > 0)
                photo.Height = h;
        }

        private static void ApplyExifIfd0(Photo photo, System.Collections.Generic.IReadOnlyList<Directory> directories)
        {
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is null) return;

            var make = ifd0.GetDescription(ExifDirectoryBase.TagMake);
            if (!string.IsNullOrWhiteSpace(make))
                photo.CameraMake = make.Trim();

            var model = ifd0.GetDescription(ExifDirectoryBase.TagModel);
            if (!string.IsNullOrWhiteSpace(model))
                photo.CameraModel = model.Trim();

            if (ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation)
                && orientation >= 1 && orientation <= 8)
            {
                photo.Orientation = orientation;
            }

            // Fall back to IFD0's timestamp when the SubIFD had no capture time.
            if (photo.DateTaken is null &&
                ifd0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dateTime))
            {
                photo.DateTaken = dateTime;
            }
        }

        private static void ApplyDimensions(Photo photo, System.Collections.Generic.IReadOnlyList<Directory> directories)
        {
            if (photo.Width > 0 && photo.Height > 0) return;

            // JPEG/PNG/GIF/BMP each expose dimensions under their own directory type;
            // rather than special-case all of them, look for the conventional tag names.
            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    if (photo.Width == 0 &&
                        tag.Name.Contains("Image Width", StringComparison.OrdinalIgnoreCase) &&
                        directory.TryGetInt32(tag.Type, out var width) && width > 0)
                    {
                        photo.Width = width;
                    }

                    if (photo.Height == 0 &&
                        tag.Name.Contains("Image Height", StringComparison.OrdinalIgnoreCase) &&
                        directory.TryGetInt32(tag.Type, out var height) && height > 0)
                    {
                        photo.Height = height;
                    }
                }

                if (photo.Width > 0 && photo.Height > 0) return;
            }
        }

        private static void ApplyGps(Photo photo, System.Collections.Generic.IReadOnlyList<Directory> directories)
        {
            var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
            var location = gps?.GetGeoLocation();
            if (location is null || location.IsZero) return;

            photo.Latitude = location.Latitude;
            photo.Longitude = location.Longitude;
        }
    }
}
