using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PhotoManagementApp.Data;
using PhotoManagementApp.Models;

namespace PhotoManagementApp.Services
{
    public class ScanProgress
    {
        public string CurrentFolder { get; init; } = string.Empty;
        public int FilesSeen { get; init; }
        public int FilesIndexed { get; init; }
        public int FilesRemoved { get; init; }
        public bool IsComplete { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// Walks the configured library roots and keeps the index in step with disk.
    ///
    /// Scanning is incremental: a file whose path and last-write-time already match
    /// the index is skipped without opening it, so a rescan of an unchanged library
    /// costs one directory walk rather than a full metadata re-read.
    /// </summary>
    public class LibraryScanner
    {
        private const int BatchSize = 200;

        private readonly PhotoDatabase _database;
        private readonly ImageService _imageService;
        private readonly MetadataReader _metadataReader;

        public LibraryScanner(PhotoDatabase database)
            : this(database, new ImageService(), new MetadataReader())
        {
        }

        public LibraryScanner(PhotoDatabase database, ImageService imageService, MetadataReader metadataReader)
        {
            _database = database;
            _imageService = imageService;
            _metadataReader = metadataReader;
        }

        /// <summary>
        /// When true, a SHA-256 of each new file's contents is computed during the
        /// scan so duplicate detection can run without a second pass. Costs one
        /// full read per new file.
        /// </summary>
        public bool ComputeContentHashes { get; set; } = true;

        public Task<ScanProgress> ScanAllRootsAsync(
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var roots = _database.GetRoots();
            return ScanAsync(roots, progress, cancellationToken);
        }

        public async Task<ScanProgress> ScanAsync(
            IReadOnlyList<string> roots,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var seen = 0;
            var indexed = 0;
            var removed = 0;

            var result = await Task.Run(() =>
            {
                var livePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var batch = new List<Photo>(BatchSize);

                foreach (var root in roots)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!System.IO.Directory.Exists(root))
                        continue;

                    var known = _database.GetIndexedFiles(root);
                    var lastReportedFolder = string.Empty;

                    foreach (var file in _imageService.EnumerateImageFilesRecursive(
                                 root, includeHidden: false,
                                 shouldStop: () => cancellationToken.IsCancellationRequested))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        seen++;
                        livePaths.Add(file.FullName);

                        var folder = file.DirectoryName ?? string.Empty;
                        if (folder != lastReportedFolder)
                        {
                            lastReportedFolder = folder;
                            progress?.Report(new ScanProgress
                            {
                                CurrentFolder = folder,
                                FilesSeen = seen,
                                FilesIndexed = indexed,
                                Message = $"Scanning {Path.GetFileName(folder)}"
                            });
                        }

                        // Second-level truncation: the index stores whole seconds,
                        // so compare at that resolution or every file looks changed.
                        if (known.TryGetValue(file.FullName, out var knownModified) &&
                            Math.Abs((knownModified - Truncate(file.LastWriteTimeUtc)).TotalSeconds) < 1)
                        {
                            continue;
                        }

                        Photo photo;
                        try
                        {
                            photo = _metadataReader.Read(file);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        photo.DateModified = Truncate(photo.DateModified);
                        if (photo.DateTaken.HasValue)
                            photo.DateTaken = Truncate(photo.DateTaken.Value);

                        if (ComputeContentHashes)
                            photo.ContentHash = TryComputeHash(file.FullName);

                        batch.Add(photo);
                        indexed++;

                        if (batch.Count >= BatchSize)
                        {
                            _database.UpsertPhotos(batch);
                            batch.Clear();
                            progress?.Report(new ScanProgress
                            {
                                CurrentFolder = folder,
                                FilesSeen = seen,
                                FilesIndexed = indexed,
                                Message = $"Indexed {indexed} photos"
                            });
                        }
                    }
                }

                if (batch.Count > 0)
                {
                    _database.UpsertPhotos(batch);
                    batch.Clear();
                }

                // Drop index entries for files that have disappeared from disk.
                // Only rows under a root we actually walked are eligible, so an
                // unplugged external drive does not wipe its own photos.
                var stale = new List<string>();
                foreach (var root in roots)
                {
                    if (!System.IO.Directory.Exists(root))
                        continue;

                    foreach (var indexedPath in _database.GetIndexedFiles(root).Keys)
                    {
                        if (!livePaths.Contains(indexedPath) && !File.Exists(indexedPath))
                            stale.Add(indexedPath);
                    }
                }

                if (stale.Count > 0)
                {
                    _database.DeletePhotosByPath(stale);
                    removed = stale.Count;
                }

                return new ScanProgress
                {
                    FilesSeen = seen,
                    FilesIndexed = indexed,
                    FilesRemoved = removed,
                    IsComplete = true,
                    Message = BuildSummary(seen, indexed, removed)
                };
            }, cancellationToken).ConfigureAwait(false);

            progress?.Report(result);
            return result;
        }

        private static string BuildSummary(int seen, int indexed, int removed)
        {
            if (seen == 0)
                return "No photos found. Add a folder to get started.";

            var summary = indexed == 0
                ? $"Library up to date - {seen} photos"
                : $"Indexed {indexed} new or changed photos ({seen} total)";

            if (removed > 0)
                summary += $", removed {removed} missing";

            return summary;
        }

        private static DateTime Truncate(DateTime value) =>
            new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Kind);

        private static string? TryComputeHash(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                return Convert.ToHexString(sha.ComputeHash(stream));
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
