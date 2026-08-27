using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoManagementApp
{
    /// <summary>
    /// File-system level helpers for locating image files. Deliberately free of
    /// UI and database concerns so it stays unit-testable.
    /// </summary>
    public class ImageService
    {
        /// <summary>
        /// Formats the app will index. RAW formats are indexed for metadata and
        /// listing; whether a thumbnail can be decoded for them depends on the
        /// platform's codecs, and the grid falls back to a placeholder if not.
        /// </summary>
        public static readonly string[] SupportedExtensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp",
            ".heic", ".heif",
            ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".raf", ".pef"
        };

        /// <summary>Extensions whose thumbnails the built-in decoder can produce.</summary>
        public static readonly string[] DecodableExtensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp"
        };

        public static bool IsSupported(string path) =>
            SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        public static bool IsDecodable(string path) =>
            DecodableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        /// <summary>Image files directly inside a folder. Returns empty for a missing or unreadable folder.</summary>
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

        /// <summary>
        /// Walks a folder tree yielding image files. Unreadable subfolders are
        /// skipped rather than aborting the walk, and the traversal is iterative
        /// so a deep tree cannot blow the stack.
        /// </summary>
        public IEnumerable<FileInfo> EnumerateImageFilesRecursive(
            string folderPath,
            bool includeHidden = false,
            Func<bool>? shouldStop = null)
        {
            var root = new DirectoryInfo(folderPath);
            if (!root.Exists)
                yield break;

            var pending = new Stack<DirectoryInfo>();
            pending.Push(root);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (pending.Count > 0)
            {
                if (shouldStop?.Invoke() == true)
                    yield break;

                var current = pending.Pop();

                // Guards against symlink loops pointing back up the tree.
                if (!visited.Add(current.FullName))
                    continue;

                FileInfo[] files;
                try
                {
                    files = current.GetFiles();
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }

                foreach (var file in files)
                {
                    if (!includeHidden && (file.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    if (SupportedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                        yield return file;
                }

                DirectoryInfo[] subdirectories;
                try
                {
                    subdirectories = current.GetDirectories();
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }

                foreach (var subdirectory in subdirectories)
                {
                    if (!includeHidden && (subdirectory.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    if ((subdirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    pending.Push(subdirectory);
                }
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
