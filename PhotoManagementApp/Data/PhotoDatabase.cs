using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using PhotoManagementApp.Models;

namespace PhotoManagementApp.Data
{
    /// <summary>
    /// The library index. A single SQLite file holding one row per known photo
    /// plus tags, albums and the configured library roots.
    ///
    /// All public members are safe to call from any thread; a single connection
    /// is shared behind a lock, which is ample for a single-user desktop app and
    /// avoids the "database is locked" retries a connection pool would invite.
    /// </summary>
    public sealed class PhotoDatabase : IDisposable
    {
        private const int SchemaVersion = 1;

        private readonly SqliteConnection _connection;
        private readonly object _gate = new();
        private bool _disposed;

        public string DatabasePath { get; }

        public PhotoDatabase(string databasePath)
        {
            DatabasePath = databasePath;

            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            _connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
            }.ToString());

            _connection.Open();
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA synchronous=NORMAL;");
            Execute("PRAGMA foreign_keys=ON;");
            CreateSchema();
        }

        /// <summary>Opens the database at the app's standard location.</summary>
        public static PhotoDatabase OpenDefault() =>
            new(Services.AppPaths.DatabasePath);

        /// <summary>Opens a throwaway in-memory database. Used by tests.</summary>
        public static PhotoDatabase OpenInMemory()
        {
            var path = Path.Combine(Path.GetTempPath(),
                "pma-test-" + Guid.NewGuid().ToString("N") + ".db");
            return new PhotoDatabase(path);
        }

        // ---------------------------------------------------------------- schema

        private void CreateSchema()
        {
            Execute(@"
CREATE TABLE IF NOT EXISTS photos (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    file_path       TEXT NOT NULL UNIQUE,
    folder_path     TEXT NOT NULL,
    file_name       TEXT NOT NULL,
    file_size       INTEGER NOT NULL DEFAULT 0,
    date_modified   TEXT NOT NULL,
    date_taken      TEXT NULL,
    width           INTEGER NOT NULL DEFAULT 0,
    height          INTEGER NOT NULL DEFAULT 0,
    camera_make     TEXT NULL,
    camera_model    TEXT NULL,
    lens_model      TEXT NULL,
    iso_speed       INTEGER NULL,
    f_number        REAL NULL,
    exposure_time   TEXT NULL,
    focal_length    REAL NULL,
    latitude        REAL NULL,
    longitude       REAL NULL,
    place_name      TEXT NULL,
    orientation     INTEGER NOT NULL DEFAULT 1,
    rating          INTEGER NOT NULL DEFAULT 0,
    is_favorite     INTEGER NOT NULL DEFAULT 0,
    content_hash    TEXT NULL,
    date_indexed    TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_photos_folder      ON photos(folder_path);
CREATE INDEX IF NOT EXISTS idx_photos_date_taken  ON photos(date_taken);
CREATE INDEX IF NOT EXISTS idx_photos_rating      ON photos(rating);
CREATE INDEX IF NOT EXISTS idx_photos_hash        ON photos(content_hash);
CREATE INDEX IF NOT EXISTS idx_photos_camera      ON photos(camera_model);

CREATE TABLE IF NOT EXISTS tags (
    id   INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE COLLATE NOCASE
);

CREATE TABLE IF NOT EXISTS photo_tags (
    photo_id INTEGER NOT NULL REFERENCES photos(id) ON DELETE CASCADE,
    tag_id   INTEGER NOT NULL REFERENCES tags(id)   ON DELETE CASCADE,
    PRIMARY KEY (photo_id, tag_id)
);

CREATE INDEX IF NOT EXISTS idx_photo_tags_tag ON photo_tags(tag_id);

CREATE TABLE IF NOT EXISTS roots (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    path      TEXT NOT NULL UNIQUE,
    added_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS albums (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT NOT NULL UNIQUE COLLATE NOCASE,
    created_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS album_photos (
    album_id INTEGER NOT NULL REFERENCES albums(id) ON DELETE CASCADE,
    photo_id INTEGER NOT NULL REFERENCES photos(id) ON DELETE CASCADE,
    PRIMARY KEY (album_id, photo_id)
);
");
            Execute($"PRAGMA user_version={SchemaVersion};");
        }

        private void Execute(string sql)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        // ---------------------------------------------------------------- roots

        public void AddRoot(string path)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText =
                    "INSERT OR IGNORE INTO roots (path, added_utc) VALUES ($p, $t);";
                cmd.Parameters.AddWithValue("$p", path);
                cmd.Parameters.AddWithValue("$t", ToText(DateTime.UtcNow));
                cmd.ExecuteNonQuery();
            }
        }

        public void RemoveRoot(string path)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM roots WHERE path = $p;";
                cmd.Parameters.AddWithValue("$p", path);
                cmd.ExecuteNonQuery();
            }
        }

        public List<string> GetRoots()
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT path FROM roots ORDER BY path;";
                using var reader = cmd.ExecuteReader();
                var result = new List<string>();
                while (reader.Read())
                    result.Add(reader.GetString(0));
                return result;
            }
        }

        // ---------------------------------------------------------------- photos

        /// <summary>
        /// Inserts a photo, or updates the existing row for the same path.
        /// User-owned fields (rating, favourite, tags) are preserved across rescans.
        /// </summary>
        public long UpsertPhoto(Photo photo)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO photos (
    file_path, folder_path, file_name, file_size, date_modified, date_taken,
    width, height, camera_make, camera_model, lens_model, iso_speed, f_number,
    exposure_time, focal_length, latitude, longitude, place_name, orientation,
    rating, is_favorite, content_hash, date_indexed)
VALUES (
    $file_path, $folder_path, $file_name, $file_size, $date_modified, $date_taken,
    $width, $height, $camera_make, $camera_model, $lens_model, $iso_speed, $f_number,
    $exposure_time, $focal_length, $latitude, $longitude, $place_name, $orientation,
    $rating, $is_favorite, $content_hash, $date_indexed)
ON CONFLICT(file_path) DO UPDATE SET
    folder_path   = excluded.folder_path,
    file_name     = excluded.file_name,
    file_size     = excluded.file_size,
    date_modified = excluded.date_modified,
    date_taken    = excluded.date_taken,
    width         = excluded.width,
    height        = excluded.height,
    camera_make   = excluded.camera_make,
    camera_model  = excluded.camera_model,
    lens_model    = excluded.lens_model,
    iso_speed     = excluded.iso_speed,
    f_number      = excluded.f_number,
    exposure_time = excluded.exposure_time,
    focal_length  = excluded.focal_length,
    latitude      = excluded.latitude,
    longitude     = excluded.longitude,
    orientation   = excluded.orientation,
    content_hash  = excluded.content_hash,
    date_indexed  = excluded.date_indexed;

SELECT id FROM photos WHERE file_path = $file_path;";

                BindPhoto(cmd, photo);
                var id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                photo.Id = id;
                return id;
            }
        }

        /// <summary>Bulk upsert inside one transaction. Much faster during a scan.</summary>
        public void UpsertPhotos(IEnumerable<Photo> photos)
        {
            lock (_gate)
            {
                using var transaction = _connection.BeginTransaction();
                foreach (var photo in photos)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
INSERT INTO photos (
    file_path, folder_path, file_name, file_size, date_modified, date_taken,
    width, height, camera_make, camera_model, lens_model, iso_speed, f_number,
    exposure_time, focal_length, latitude, longitude, place_name, orientation,
    rating, is_favorite, content_hash, date_indexed)
VALUES (
    $file_path, $folder_path, $file_name, $file_size, $date_modified, $date_taken,
    $width, $height, $camera_make, $camera_model, $lens_model, $iso_speed, $f_number,
    $exposure_time, $focal_length, $latitude, $longitude, $place_name, $orientation,
    $rating, $is_favorite, $content_hash, $date_indexed)
ON CONFLICT(file_path) DO UPDATE SET
    folder_path   = excluded.folder_path,
    file_name     = excluded.file_name,
    file_size     = excluded.file_size,
    date_modified = excluded.date_modified,
    date_taken    = excluded.date_taken,
    width         = excluded.width,
    height        = excluded.height,
    camera_make   = excluded.camera_make,
    camera_model  = excluded.camera_model,
    lens_model    = excluded.lens_model,
    iso_speed     = excluded.iso_speed,
    f_number      = excluded.f_number,
    exposure_time = excluded.exposure_time,
    focal_length  = excluded.focal_length,
    latitude      = excluded.latitude,
    longitude     = excluded.longitude,
    orientation   = excluded.orientation,
    content_hash  = excluded.content_hash,
    date_indexed  = excluded.date_indexed;";
                    BindPhoto(cmd, photo);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }

        private static void BindPhoto(SqliteCommand cmd, Photo p)
        {
            cmd.Parameters.AddWithValue("$file_path", p.FilePath);
            cmd.Parameters.AddWithValue("$folder_path", p.FolderPath);
            cmd.Parameters.AddWithValue("$file_name", p.FileName);
            cmd.Parameters.AddWithValue("$file_size", p.FileSize);
            cmd.Parameters.AddWithValue("$date_modified", ToText(p.DateModified));
            cmd.Parameters.AddWithValue("$date_taken", (object?)ToTextOrNull(p.DateTaken) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$width", p.Width);
            cmd.Parameters.AddWithValue("$height", p.Height);
            cmd.Parameters.AddWithValue("$camera_make", (object?)p.CameraMake ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$camera_model", (object?)p.CameraModel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lens_model", (object?)p.LensModel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$iso_speed", (object?)p.IsoSpeed ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$f_number", (object?)p.FNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$exposure_time", (object?)p.ExposureTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$focal_length", (object?)p.FocalLength ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$latitude", (object?)p.Latitude ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$longitude", (object?)p.Longitude ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$place_name", (object?)p.PlaceName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$orientation", p.Orientation);
            cmd.Parameters.AddWithValue("$rating", p.Rating);
            cmd.Parameters.AddWithValue("$is_favorite", p.IsFavorite ? 1 : 0);
            cmd.Parameters.AddWithValue("$content_hash", (object?)p.ContentHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$date_indexed", ToText(DateTime.UtcNow));
        }

        public Photo? GetPhotoByPath(string filePath)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = SelectColumns + " FROM photos WHERE file_path = $p;";
                cmd.Parameters.AddWithValue("$p", filePath);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadPhoto(reader) : null;
            }
        }

        public int GetPhotoCount()
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM photos;";
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Returns path -> last-write-time for every indexed file under a root.
        /// The scanner uses this to skip files that have not changed.
        /// </summary>
        public Dictionary<string, DateTime> GetIndexedFiles(string? underFolder = null)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                if (underFolder is null)
                {
                    cmd.CommandText = "SELECT file_path, date_modified FROM photos;";
                }
                else
                {
                    cmd.CommandText =
                        "SELECT file_path, date_modified FROM photos WHERE folder_path = $f OR folder_path LIKE $like;";
                    cmd.Parameters.AddWithValue("$f", underFolder);
                    cmd.Parameters.AddWithValue("$like", underFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "%");
                }

                using var reader = cmd.ExecuteReader();
                var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
                while (reader.Read())
                    result[reader.GetString(0)] = FromText(reader.GetString(1));
                return result;
            }
        }

        public void DeletePhotosByPath(IEnumerable<string> filePaths)
        {
            lock (_gate)
            {
                using var transaction = _connection.BeginTransaction();
                foreach (var path in filePaths)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM photos WHERE file_path = $p;";
                    cmd.Parameters.AddWithValue("$p", path);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }

        // ---------------------------------------------------------------- querying

        private const string SelectColumns = @"
SELECT id, file_path, folder_path, file_name, file_size, date_modified, date_taken,
       width, height, camera_make, camera_model, lens_model, iso_speed, f_number,
       exposure_time, focal_length, latitude, longitude, place_name, orientation,
       rating, is_favorite, content_hash, date_indexed";

        public List<Photo> Query(PhotoQuery query, int limit = 0, int offset = 0)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                var where = new List<string>();

                if (!string.IsNullOrWhiteSpace(query.SearchText))
                {
                    // Each whitespace-separated term must match somewhere, so
                    // "canon 2024" narrows rather than widens.
                    var terms = query.SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < terms.Length; i++)
                    {
                        var param = "$search" + i;
                        where.Add($@"(
                            file_name   LIKE {param} OR
                            folder_path LIKE {param} OR
                            camera_make LIKE {param} OR
                            camera_model LIKE {param} OR
                            place_name  LIKE {param} OR
                            EXISTS (SELECT 1 FROM photo_tags pt JOIN tags t ON t.id = pt.tag_id
                                    WHERE pt.photo_id = photos.id AND t.name LIKE {param})
                        )");
                        cmd.Parameters.AddWithValue(param, "%" + terms[i] + "%");
                    }
                }

                if (query.FolderPath is not null)
                {
                    if (query.IncludeSubfolders)
                    {
                        where.Add("(folder_path = $folder OR folder_path LIKE $folderLike)");
                        cmd.Parameters.AddWithValue("$folder", query.FolderPath);
                        cmd.Parameters.AddWithValue("$folderLike",
                            query.FolderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "%");
                    }
                    else
                    {
                        where.Add("folder_path = $folder");
                        cmd.Parameters.AddWithValue("$folder", query.FolderPath);
                    }
                }

                if (query.DateFrom is not null)
                {
                    where.Add("COALESCE(date_taken, date_modified) >= $dateFrom");
                    cmd.Parameters.AddWithValue("$dateFrom", ToText(query.DateFrom.Value));
                }

                if (query.DateTo is not null)
                {
                    where.Add("COALESCE(date_taken, date_modified) <= $dateTo");
                    cmd.Parameters.AddWithValue("$dateTo", ToText(query.DateTo.Value));
                }

                if (query.MinRating is > 0)
                {
                    where.Add("rating >= $minRating");
                    cmd.Parameters.AddWithValue("$minRating", query.MinRating.Value);
                }

                if (query.FavoritesOnly)
                    where.Add("is_favorite = 1");

                if (!string.IsNullOrWhiteSpace(query.CameraModel))
                {
                    where.Add("camera_model = $camera");
                    cmd.Parameters.AddWithValue("$camera", query.CameraModel);
                }

                if (query.RequireLocation)
                    where.Add("latitude IS NOT NULL AND longitude IS NOT NULL");

                if (query.UntaggedOnly)
                    where.Add("NOT EXISTS (SELECT 1 FROM photo_tags pt WHERE pt.photo_id = photos.id)");

                for (int i = 0; i < query.Tags.Count; i++)
                {
                    var param = "$tag" + i;
                    where.Add($@"EXISTS (SELECT 1 FROM photo_tags pt JOIN tags t ON t.id = pt.tag_id
                                         WHERE pt.photo_id = photos.id AND t.name = {param})");
                    cmd.Parameters.AddWithValue(param, query.Tags[i]);
                }

                var sql = SelectColumns + " FROM photos";
                if (where.Count > 0)
                    sql += " WHERE " + string.Join(" AND ", where);

                sql += " ORDER BY " + OrderByClause(query);

                if (limit > 0)
                {
                    sql += " LIMIT $limit OFFSET $offset";
                    cmd.Parameters.AddWithValue("$limit", limit);
                    cmd.Parameters.AddWithValue("$offset", offset);
                }

                cmd.CommandText = sql + ";";

                using var reader = cmd.ExecuteReader();
                var results = new List<Photo>();
                while (reader.Read())
                    results.Add(ReadPhoto(reader));
                return results;
            }
        }

        private static string OrderByClause(PhotoQuery query)
        {
            var direction = query.SortDescending ? "DESC" : "ASC";
            var column = query.SortField switch
            {
                PhotoSortField.DateTaken => "COALESCE(date_taken, date_modified)",
                PhotoSortField.DateModified => "date_modified",
                PhotoSortField.FileName => "file_name COLLATE NOCASE",
                PhotoSortField.FileSize => "file_size",
                PhotoSortField.Rating => "rating",
                PhotoSortField.Resolution => "(width * height)",
                _ => "COALESCE(date_taken, date_modified)"
            };
            // file_path breaks ties so paging and grouping stay stable.
            return $"{column} {direction}, file_path ASC";
        }

        private static Photo ReadPhoto(SqliteDataReader r) => new()
        {
            Id = r.GetInt64(0),
            FilePath = r.GetString(1),
            FolderPath = r.GetString(2),
            FileName = r.GetString(3),
            FileSize = r.GetInt64(4),
            DateModified = FromText(r.GetString(5)),
            DateTaken = r.IsDBNull(6) ? null : FromText(r.GetString(6)),
            Width = r.GetInt32(7),
            Height = r.GetInt32(8),
            CameraMake = r.IsDBNull(9) ? null : r.GetString(9),
            CameraModel = r.IsDBNull(10) ? null : r.GetString(10),
            LensModel = r.IsDBNull(11) ? null : r.GetString(11),
            IsoSpeed = r.IsDBNull(12) ? null : r.GetInt32(12),
            FNumber = r.IsDBNull(13) ? null : r.GetDouble(13),
            ExposureTime = r.IsDBNull(14) ? null : r.GetString(14),
            FocalLength = r.IsDBNull(15) ? null : r.GetDouble(15),
            Latitude = r.IsDBNull(16) ? null : r.GetDouble(16),
            Longitude = r.IsDBNull(17) ? null : r.GetDouble(17),
            PlaceName = r.IsDBNull(18) ? null : r.GetString(18),
            Orientation = r.GetInt32(19),
            Rating = r.GetInt32(20),
            IsFavorite = r.GetInt32(21) != 0,
            ContentHash = r.IsDBNull(22) ? null : r.GetString(22),
            DateIndexed = FromText(r.GetString(23)),
        };

        // ---------------------------------------------------------------- facets

        public List<string> GetDistinctCameras()
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT DISTINCT camera_model FROM photos
                                    WHERE camera_model IS NOT NULL AND TRIM(camera_model) <> ''
                                    ORDER BY camera_model COLLATE NOCASE;";
                using var reader = cmd.ExecuteReader();
                var result = new List<string>();
                while (reader.Read())
                    result.Add(reader.GetString(0));
                return result;
            }
        }

        public List<string> GetDistinctFolders()
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT folder_path FROM photos ORDER BY folder_path COLLATE NOCASE;";
                using var reader = cmd.ExecuteReader();
                var result = new List<string>();
                while (reader.Read())
                    result.Add(reader.GetString(0));
                return result;
            }
        }

        /// <summary>Year -> photo count, newest year first. Drives the timeline sidebar.</summary>
        public List<(int Year, int Count)> GetYearCounts()
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT CAST(substr(COALESCE(date_taken, date_modified), 1, 4) AS INTEGER) AS y,
                                           COUNT(*)
                                    FROM photos GROUP BY y ORDER BY y DESC;";
                using var reader = cmd.ExecuteReader();
                var result = new List<(int, int)>();
                while (reader.Read())
                    result.Add((reader.GetInt32(0), reader.GetInt32(1)));
                return result;
            }
        }

        // ---------------------------------------------------------------- ratings & tags

        public void SetRating(long photoId, int rating)
        {
            rating = Math.Clamp(rating, 0, 5);
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE photos SET rating = $r WHERE id = $id;";
                cmd.Parameters.AddWithValue("$r", rating);
                cmd.Parameters.AddWithValue("$id", photoId);
                cmd.ExecuteNonQuery();
            }
        }

        public void SetFavorite(long photoId, bool isFavorite)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE photos SET is_favorite = $f WHERE id = $id;";
                cmd.Parameters.AddWithValue("$f", isFavorite ? 1 : 0);
                cmd.Parameters.AddWithValue("$id", photoId);
                cmd.ExecuteNonQuery();
            }
        }

        public long GetOrCreateTag(string name)
        {
            name = name.Trim();
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"INSERT INTO tags (name) VALUES ($n) ON CONFLICT(name) DO NOTHING;
                                    SELECT id FROM tags WHERE name = $n;";
                cmd.Parameters.AddWithValue("$n", name);
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        public void AddTagToPhoto(long photoId, string tagName)
        {
            var tagId = GetOrCreateTag(tagName);
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO photo_tags (photo_id, tag_id) VALUES ($p, $t);";
                cmd.Parameters.AddWithValue("$p", photoId);
                cmd.Parameters.AddWithValue("$t", tagId);
                cmd.ExecuteNonQuery();
            }
        }

        public void RemoveTagFromPhoto(long photoId, string tagName)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"DELETE FROM photo_tags
                                    WHERE photo_id = $p
                                      AND tag_id IN (SELECT id FROM tags WHERE name = $n);";
                cmd.Parameters.AddWithValue("$p", photoId);
                cmd.Parameters.AddWithValue("$n", tagName.Trim());
                cmd.ExecuteNonQuery();
            }
        }

        public List<string> GetTagsForPhoto(long photoId)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT t.name FROM tags t
                                    JOIN photo_tags pt ON pt.tag_id = t.id
                                    WHERE pt.photo_id = $p ORDER BY t.name COLLATE NOCASE;";
                cmd.Parameters.AddWithValue("$p", photoId);
                using var reader = cmd.ExecuteReader();
                var result = new List<string>();
                while (reader.Read())
                    result.Add(reader.GetString(0));
                return result;
            }
        }

        /// <summary>All tags in the library with how many photos carry each.</summary>
        public List<(string Name, int Count)> GetAllTags()
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT t.name, COUNT(pt.photo_id)
                                    FROM tags t LEFT JOIN photo_tags pt ON pt.tag_id = t.id
                                    GROUP BY t.id ORDER BY t.name COLLATE NOCASE;";
                using var reader = cmd.ExecuteReader();
                var result = new List<(string, int)>();
                while (reader.Read())
                    result.Add((reader.GetString(0), reader.GetInt32(1)));
                return result;
            }
        }

        // ---------------------------------------------------------------- duplicates

        /// <summary>
        /// Groups of photos sharing a content hash, largest group first.
        /// Only files whose hash has been computed take part.
        /// </summary>
        public List<List<Photo>> GetDuplicateGroups()
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = SelectColumns + @"
FROM photos
WHERE content_hash IS NOT NULL
  AND content_hash IN (SELECT content_hash FROM photos
                       WHERE content_hash IS NOT NULL
                       GROUP BY content_hash HAVING COUNT(*) > 1)
ORDER BY content_hash, file_path;";

                using var reader = cmd.ExecuteReader();
                var all = new List<Photo>();
                while (reader.Read())
                    all.Add(ReadPhoto(reader));

                return all.GroupBy(p => p.ContentHash!)
                          .Select(g => g.ToList())
                          .OrderByDescending(g => g.Count)
                          .ToList();
            }
        }

        public void SetContentHash(long photoId, string hash)
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE photos SET content_hash = $h WHERE id = $id;";
                cmd.Parameters.AddWithValue("$h", hash);
                cmd.Parameters.AddWithValue("$id", photoId);
                cmd.ExecuteNonQuery();
            }
        }

        // ---------------------------------------------------------------- helpers

        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        private static string ToText(DateTime value) =>
            value.ToString(DateFormat, CultureInfo.InvariantCulture);

        private static string? ToTextOrNull(DateTime? value) =>
            value.HasValue ? ToText(value.Value) : null;

        private static DateTime FromText(string text) =>
            DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)
                ? parsed
                : DateTime.MinValue;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_gate)
            {
                _connection.Close();
                _connection.Dispose();
            }
            SqliteConnection.ClearAllPools();
        }
    }
}
