using System;
using System.IO;
using System.Linq;
using PhotoManagementApp.Data;
using PhotoManagementApp.Models;

namespace PhotoManagementApp.Tests;

public class PhotoDatabaseTests : IDisposable
{
    private readonly PhotoDatabase _database = PhotoDatabase.OpenInMemory();

    public void Dispose()
    {
        var path = _database.DatabasePath;
        _database.Dispose();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(path + suffix); } catch (IOException) { }
        }
    }

    private Photo MakePhoto(
        string path,
        DateTime? taken = null,
        int rating = 0,
        string? camera = null,
        double? latitude = null,
        long size = 1000)
    {
        return new Photo
        {
            FilePath = path,
            FolderPath = Path.GetDirectoryName(path) ?? "/",
            FileName = Path.GetFileName(path),
            FileSize = size,
            DateModified = new DateTime(2024, 1, 1, 12, 0, 0),
            DateTaken = taken,
            Rating = rating,
            CameraModel = camera,
            Latitude = latitude,
            Longitude = latitude.HasValue ? 5.0 : null,
            Width = 100,
            Height = 80,
        };
    }

    [Fact]
    public void UpsertPhoto_InsertsAndReturnsStableId()
    {
        var id = _database.UpsertPhoto(MakePhoto("/photos/a.jpg"));
        Assert.True(id > 0);

        var again = _database.UpsertPhoto(MakePhoto("/photos/a.jpg"));
        Assert.Equal(id, again);
        Assert.Equal(1, _database.GetPhotoCount());
    }

    [Fact]
    public void UpsertPhoto_PreservesRatingAndFavoriteAcrossRescan()
    {
        var id = _database.UpsertPhoto(MakePhoto("/photos/a.jpg"));
        _database.SetRating(id, 4);
        _database.SetFavorite(id, true);

        // A rescan re-reads the file and upserts it again with rating 0.
        _database.UpsertPhoto(MakePhoto("/photos/a.jpg"));

        var stored = _database.GetPhotoByPath("/photos/a.jpg");
        Assert.NotNull(stored);
        Assert.Equal(4, stored!.Rating);
        Assert.True(stored.IsFavorite);
    }

    [Fact]
    public void Query_WithNoCriteria_ReturnsEverything()
    {
        _database.UpsertPhoto(MakePhoto("/photos/a.jpg"));
        _database.UpsertPhoto(MakePhoto("/photos/b.jpg"));

        Assert.Equal(2, _database.Query(new PhotoQuery()).Count);
    }

    [Fact]
    public void Query_FiltersByFolderIncludingSubfolders()
    {
        _database.UpsertPhoto(MakePhoto(Path.Combine("/photos", "a.jpg")));
        _database.UpsertPhoto(MakePhoto(Path.Combine("/photos", "2024", "b.jpg")));
        _database.UpsertPhoto(MakePhoto(Path.Combine("/other", "c.jpg")));

        var withSubfolders = _database.Query(new PhotoQuery
        {
            FolderPath = "/photos",
            IncludeSubfolders = true
        });
        Assert.Equal(2, withSubfolders.Count);

        var withoutSubfolders = _database.Query(new PhotoQuery
        {
            FolderPath = "/photos",
            IncludeSubfolders = false
        });
        Assert.Single(withoutSubfolders);
    }

    [Fact]
    public void Query_FiltersByDateRangeUsingDateTakenWhenPresent()
    {
        _database.UpsertPhoto(MakePhoto("/photos/2023.jpg", new DateTime(2023, 6, 1)));
        _database.UpsertPhoto(MakePhoto("/photos/2024.jpg", new DateTime(2024, 6, 1)));

        var results = _database.Query(new PhotoQuery
        {
            DateFrom = new DateTime(2024, 1, 1),
            DateTo = new DateTime(2024, 12, 31)
        });

        Assert.Single(results);
        Assert.Equal("2024.jpg", results[0].FileName);
    }

    [Fact]
    public void Query_FiltersByMinimumRating()
    {
        _database.UpsertPhoto(MakePhoto("/photos/low.jpg"));
        var goodId = _database.UpsertPhoto(MakePhoto("/photos/high.jpg"));
        _database.SetRating(goodId, 5);

        var results = _database.Query(new PhotoQuery { MinRating = 4 });

        Assert.Single(results);
        Assert.Equal("high.jpg", results[0].FileName);
    }

    [Fact]
    public void Query_RequireLocation_ExcludesPhotosWithoutGps()
    {
        _database.UpsertPhoto(MakePhoto("/photos/nogps.jpg"));
        _database.UpsertPhoto(MakePhoto("/photos/gps.jpg", latitude: 51.5));

        var results = _database.Query(new PhotoQuery { RequireLocation = true });

        Assert.Single(results);
        Assert.Equal("gps.jpg", results[0].FileName);
    }

    [Fact]
    public void Query_SearchTermsAreCombinedWithAnd()
    {
        _database.UpsertPhoto(MakePhoto("/photos/beach-canon.jpg", camera: "Canon EOS R5"));
        _database.UpsertPhoto(MakePhoto("/photos/beach-nikon.jpg", camera: "Nikon Z6"));

        var bothTerms = _database.Query(new PhotoQuery { SearchText = "beach Canon" });
        Assert.Single(bothTerms);
        Assert.Equal("beach-canon.jpg", bothTerms[0].FileName);

        var oneTerm = _database.Query(new PhotoQuery { SearchText = "beach" });
        Assert.Equal(2, oneTerm.Count);
    }

    [Fact]
    public void Query_SearchMatchesTags()
    {
        var id = _database.UpsertPhoto(MakePhoto("/photos/IMG_1.jpg"));
        _database.AddTagToPhoto(id, "holiday");

        var results = _database.Query(new PhotoQuery { SearchText = "holiday" });

        Assert.Single(results);
    }

    [Fact]
    public void Query_SortsByFileSizeInBothDirections()
    {
        _database.UpsertPhoto(MakePhoto("/photos/small.jpg", size: 100));
        _database.UpsertPhoto(MakePhoto("/photos/large.jpg", size: 900));

        var descending = _database.Query(new PhotoQuery
        {
            SortField = PhotoSortField.FileSize,
            SortDescending = true
        });
        Assert.Equal("large.jpg", descending[0].FileName);

        var ascending = _database.Query(new PhotoQuery
        {
            SortField = PhotoSortField.FileSize,
            SortDescending = false
        });
        Assert.Equal("small.jpg", ascending[0].FileName);
    }

    [Fact]
    public void Tags_AddRemoveAndCount()
    {
        var id = _database.UpsertPhoto(MakePhoto("/photos/a.jpg"));

        _database.AddTagToPhoto(id, "family");
        _database.AddTagToPhoto(id, "family");   // duplicate must not double up
        _database.AddTagToPhoto(id, "2024");

        Assert.Equal(new[] { "2024", "family" }, _database.GetTagsForPhoto(id).OrderBy(t => t).ToArray());
        Assert.Contains(_database.GetAllTags(), t => t.Name == "family" && t.Count == 1);

        _database.RemoveTagFromPhoto(id, "family");
        Assert.DoesNotContain("family", _database.GetTagsForPhoto(id));
    }

    [Fact]
    public void Query_UntaggedOnly_ReturnsPhotosWithNoTags()
    {
        var tagged = _database.UpsertPhoto(MakePhoto("/photos/tagged.jpg"));
        _database.UpsertPhoto(MakePhoto("/photos/untagged.jpg"));
        _database.AddTagToPhoto(tagged, "family");

        var results = _database.Query(new PhotoQuery { UntaggedOnly = true });

        Assert.Single(results);
        Assert.Equal("untagged.jpg", results[0].FileName);
    }

    [Fact]
    public void GetDuplicateGroups_GroupsBySharedContentHash()
    {
        var a = MakePhoto("/photos/a.jpg");
        a.ContentHash = "HASH1";
        var b = MakePhoto("/backup/a.jpg");
        b.ContentHash = "HASH1";
        var c = MakePhoto("/photos/c.jpg");
        c.ContentHash = "HASH2";

        _database.UpsertPhoto(a);
        _database.UpsertPhoto(b);
        _database.UpsertPhoto(c);

        var groups = _database.GetDuplicateGroups();

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
    }

    [Fact]
    public void GetYearCounts_BucketsByEffectiveDate()
    {
        _database.UpsertPhoto(MakePhoto("/photos/a.jpg", new DateTime(2024, 3, 1)));
        _database.UpsertPhoto(MakePhoto("/photos/b.jpg", new DateTime(2024, 9, 1)));
        _database.UpsertPhoto(MakePhoto("/photos/c.jpg", new DateTime(2023, 9, 1)));

        var counts = _database.GetYearCounts();

        Assert.Equal((2024, 2), counts[0]);
        Assert.Equal((2023, 1), counts[1]);
    }

    [Fact]
    public void Roots_AreStoredWithoutDuplicates()
    {
        _database.AddRoot("/photos");
        _database.AddRoot("/photos");
        _database.AddRoot("/more");

        Assert.Equal(2, _database.GetRoots().Count);

        _database.RemoveRoot("/more");
        Assert.Single(_database.GetRoots());
    }

    [Fact]
    public void DeletePhotosByPath_RemovesRowsAndTheirTags()
    {
        var id = _database.UpsertPhoto(MakePhoto("/photos/a.jpg"));
        _database.AddTagToPhoto(id, "family");

        _database.DeletePhotosByPath(new[] { "/photos/a.jpg" });

        Assert.Equal(0, _database.GetPhotoCount());
        // The tag itself survives, but its association is gone.
        Assert.Contains(_database.GetAllTags(), t => t.Name == "family" && t.Count == 0);
    }

    [Fact]
    public void SetRating_ClampsOutOfRangeValues()
    {
        var id = _database.UpsertPhoto(MakePhoto("/photos/a.jpg"));

        _database.SetRating(id, 99);
        Assert.Equal(5, _database.GetPhotoByPath("/photos/a.jpg")!.Rating);

        _database.SetRating(id, -3);
        Assert.Equal(0, _database.GetPhotoByPath("/photos/a.jpg")!.Rating);
    }
}
