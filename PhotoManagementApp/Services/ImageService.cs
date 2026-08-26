using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoManagementApp
{
    public class ImageService
    {
        private static readonly string[] SupportedExtensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"
        };

        public List<FileInfo> GetImageFiles(string folderPath)
        {
            var directoryInfo = new DirectoryInfo(folderPath);
            if (!directoryInfo.Exists)
            {
                return new List<FileInfo>();
            }

            try
            {
                return directoryInfo
                    .GetFiles()
                    .Where(f => SupportedExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (UnauthorizedAccessException)
            {
                return new List<FileInfo>();
            }
            catch (IOException)
            {
                return new List<FileInfo>();
            }
        }

        public List<FileInfo> FilterImages(IEnumerable<FileInfo> imageFiles, string searchText)
        {
            if (string.IsNullOrEmpty(searchText))
            {
                return imageFiles.ToList();
            }

            return imageFiles
                .Where(f => f.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
