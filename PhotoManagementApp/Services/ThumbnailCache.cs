using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace PhotoManagementApp.Services
{
    /// <summary>
    /// Produces grid thumbnails without ever holding a full-resolution bitmap.
    ///
    /// Two tiers back every request: a bounded in-memory tier for what is on
    /// screen, and a JPEG-on-disk tier under the app data folder so a second
    /// visit to a folder costs a small decode instead of re-reading the original.
    /// Decoding is capped by a semaphore, otherwise scrolling a large folder
    /// spawns hundreds of concurrent decodes and starves the UI thread.
    /// </summary>
    public sealed class ThumbnailCache : IDisposable
    {
        public const int DefaultThumbnailSize = 256;

        private readonly string _cacheDirectory;
        private readonly int _thumbnailSize;
        private readonly SemaphoreSlim _decodeLimit;
        private readonly ConcurrentDictionary<string, Bitmap> _memoryCache = new();
        private readonly ConcurrentQueue<string> _memoryOrder = new();
        private readonly int _memoryCapacity;
        private bool _disposed;

        public ThumbnailCache(
            string? cacheDirectory = null,
            int thumbnailSize = DefaultThumbnailSize,
            int memoryCapacity = 600,
            int maxConcurrentDecodes = 0)
        {
            _cacheDirectory = cacheDirectory ?? AppPaths.ThumbnailCacheDirectory;
            System.IO.Directory.CreateDirectory(_cacheDirectory);
            _thumbnailSize = thumbnailSize;
            _memoryCapacity = Math.Max(32, memoryCapacity);
            _decodeLimit = new SemaphoreSlim(
                maxConcurrentDecodes > 0 ? maxConcurrentDecodes : Math.Max(2, Environment.ProcessorCount / 2));
        }

        /// <summary>
        /// Returns a thumbnail for a file, or null if it could not be decoded.
        /// Safe to call from any thread.
        /// </summary>
        public async Task<Bitmap?> GetThumbnailAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return null;

            string key;
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists)
                    return null;
                key = BuildKey(info);
            }
            catch (Exception)
            {
                return null;
            }

            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            await _decodeLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Another caller may have populated it while we waited for a slot.
                if (_memoryCache.TryGetValue(key, out cached))
                    return cached;

                var bitmap = await Task.Run(() => LoadOrCreate(filePath, key), cancellationToken)
                                       .ConfigureAwait(false);

                if (bitmap is not null)
                    StoreInMemory(key, bitmap);

                return bitmap;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                _decodeLimit.Release();
            }
        }

        private Bitmap? LoadOrCreate(string filePath, string key)
        {
            var cachePath = Path.Combine(_cacheDirectory, key + ".jpg");

            if (File.Exists(cachePath))
            {
                try
                {
                    using var cacheStream = File.OpenRead(cachePath);
                    return new Bitmap(cacheStream);
                }
                catch (Exception)
                {
                    // A truncated cache entry (e.g. from a crash mid-write) is
                    // discarded and regenerated below.
                    TryDelete(cachePath);
                }
            }

            if (!ImageService.IsDecodable(filePath))
                return null;

            try
            {
                Bitmap scaled;
                using (var source = File.OpenRead(filePath))
                {
                    // DecodeToWidth streams the image at the target size, so a 40MP
                    // original never materialises as a full bitmap in memory.
                    scaled = Bitmap.DecodeToWidth(source, _thumbnailSize, BitmapInterpolationMode.MediumQuality);
                }

                TrySaveToDisk(scaled, cachePath);
                return scaled;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TrySaveToDisk(Bitmap bitmap, string cachePath)
        {
            // Write to a temp name then move, so a partially written file is never
            // visible under the real cache key.
            var tempPath = cachePath + ".tmp";
            try
            {
                using (var output = File.Create(tempPath))
                {
                    bitmap.Save(output);
                }

                // A backend without a real encoder can produce an empty file
                // without throwing. Promoting that would poison the cache with an
                // entry that fails to load forever, so drop it instead.
                if (new FileInfo(tempPath).Length == 0)
                {
                    TryDelete(tempPath);
                    return;
                }

                File.Move(tempPath, cachePath, overwrite: true);
            }
            catch (Exception)
            {
                TryDelete(tempPath);
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (Exception) { /* nothing useful to do */ }
        }

        private void StoreInMemory(string key, Bitmap bitmap)
        {
            if (!_memoryCache.TryAdd(key, bitmap))
                return;

            _memoryOrder.Enqueue(key);

            while (_memoryOrder.Count > _memoryCapacity && _memoryOrder.TryDequeue(out var oldest))
            {
                if (_memoryCache.TryRemove(oldest, out var evicted))
                    evicted.Dispose();
            }
        }

        /// <summary>
        /// Cache key combining path, size and last-write-time, so editing a photo
        /// in place produces a new key rather than serving a stale thumbnail.
        /// </summary>
        private string BuildKey(FileInfo info)
        {
            var raw = string.Create(CultureInfo.InvariantCulture,
                $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{_thumbnailSize}");
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>Deletes every cached thumbnail from disk and memory.</summary>
        public void Clear()
        {
            foreach (var key in _memoryCache.Keys)
            {
                if (_memoryCache.TryRemove(key, out var bitmap))
                    bitmap.Dispose();
            }
            while (_memoryOrder.TryDequeue(out _)) { }

            try
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(_cacheDirectory, "*.jpg"))
                    TryDelete(file);
            }
            catch (Exception) { /* cache dir may be gone; nothing to clear */ }
        }

        public long GetCacheSizeBytes()
        {
            try
            {
                long total = 0;
                foreach (var file in System.IO.Directory.EnumerateFiles(_cacheDirectory, "*.jpg"))
                {
                    try { total += new FileInfo(file).Length; }
                    catch (IOException) { }
                }
                return total;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var key in _memoryCache.Keys)
            {
                if (_memoryCache.TryRemove(key, out var bitmap))
                    bitmap.Dispose();
            }
            _decodeLimit.Dispose();
        }
    }
}
