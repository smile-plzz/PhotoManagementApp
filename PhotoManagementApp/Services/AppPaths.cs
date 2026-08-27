using System;
using System.IO;

namespace PhotoManagementApp.Services
{
    /// <summary>
    /// Resolves where the app keeps its library index and thumbnail cache.
    /// Everything lives under the per-user application data folder so the app
    /// never writes into the user's photo folders.
    /// </summary>
    public static class AppPaths
    {
        private const string AppFolderName = "PhotoManagementApp";

        private static string? _overrideRoot;

        /// <summary>Redirects all app data to a specific directory. Used by tests.</summary>
        public static void OverrideDataRoot(string path)
        {
            _overrideRoot = path;
            Directory.CreateDirectory(path);
        }

        public static string DataRoot
        {
            get
            {
                if (_overrideRoot is not null)
                    return _overrideRoot;

                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

                var root = Path.Combine(baseDir, AppFolderName);
                Directory.CreateDirectory(root);
                return root;
            }
        }

        public static string DatabasePath => Path.Combine(DataRoot, "library.db");

        public static string ThumbnailCacheDirectory
        {
            get
            {
                var dir = Path.Combine(DataRoot, "thumbnails");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
