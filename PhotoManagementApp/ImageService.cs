using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoManagementApp
{
    public class ImageService
    {
        private readonly string[] _imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" };

        public List<FileInfo> GetImageFiles(string folderPath)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(folderPath);
            if (directoryInfo.Exists)
            {
                return directoryInfo.GetFiles()
                                    .Where(f => _imageExtensions.Contains(f.Extension.ToLower()))
                                    .ToList();
            }
            return new List<FileInfo>();
        }

        public List<FileInfo> FilterImages(List<FileInfo> imageFiles, string searchText)
        {
            if (string.IsNullOrEmpty(searchText))
            {
                return imageFiles;
            }
            return imageFiles.Where(file => file.Name.ToLower().Contains(searchText.ToLower())).ToList();
        }
    }
}