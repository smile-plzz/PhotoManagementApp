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
            return new DirectoryInfo(folderPath)
                .GetFiles()
                .Where(f => SupportedExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .ToList();
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
