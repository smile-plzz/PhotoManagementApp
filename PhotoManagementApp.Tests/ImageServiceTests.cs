using System;
using System.IO;
using System.Linq;
using PhotoManagementApp;
using PhotoManagementApp.Models;

namespace PhotoManagementApp.Tests;

public class ImageServiceTests : IDisposable
{
    private readonly TestLibrary _library = new();
    private readonly ImageService _imageService = new();

    public void Dispose() => _library.Dispose();

    [Fact]
    public void EnumerateImageFilesRecursive_FindsImagesInNestedFolders()
    {
        _library.CreateImage("top.png");
        _library.CreateImage(Path.Combine("2024", "trip", "deep.png"));
        _library.CreateNonImage(Path.Combine("2024", "readme.txt"));

        var files = _imageService.EnumerateImageFilesRecursive(_library.Root).ToList();

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Name == "deep.png");
    }

    [Fact]
    public void EnumerateImageFilesRecursive_ReturnsNothingForMissingFolder()
    {
        var files = _imageService
            .EnumerateImageFilesRecursive(Path.Combine(_library.Root, "nope"))
            .ToList();

        Assert.Empty(files);
    }

    [Fact]
    public void EnumerateImageFilesRecursive_StopsWhenAskedTo()
    {
        for (var i = 0; i < 20; i++)
            _library.CreateImage($"img{i}.png");

        var files = _imageService
            .EnumerateImageFilesRecursive(_library.Root, shouldStop: () => true)
            .ToList();

        Assert.Empty(files);
    }

    [Fact]
    public void IsSupported_AcceptsRawAndCommonFormatsCaseInsensitively()
    {
        Assert.True(ImageService.IsSupported("holiday.JPG"));
        Assert.True(ImageService.IsSupported("holiday.cr2"));
        Assert.True(ImageService.IsSupported("holiday.HEIC"));
        Assert.False(ImageService.IsSupported("holiday.txt"));
    }

    [Fact]
    public void IsDecodable_ExcludesRawFormats()
    {
        Assert.True(ImageService.IsDecodable("a.jpg"));
        Assert.False(ImageService.IsDecodable("a.cr2"));
    }
}

public class PhotoModelTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5242880, "5 MB")]
    public void FormatBytes_ProducesReadableSizes(long bytes, string expected)
    {
        Assert.Equal(expected, Photo.FormatBytes(bytes));
    }

    [Fact]
    public void CameraDisplayName_DoesNotRepeatTheMakeInsideTheModel()
    {
        var photo = new Photo { CameraMake = "Canon", CameraModel = "Canon EOS R5" };
        Assert.Equal("Canon EOS R5", photo.CameraDisplayName);
    }

    [Fact]
    public void CameraDisplayName_JoinsMakeAndModelWhenDistinct()
    {
        var photo = new Photo { CameraMake = "NIKON CORPORATION", CameraModel = "Z 6" };
        Assert.Equal("NIKON CORPORATION Z 6", photo.CameraDisplayName);
    }

    [Fact]
    public void EffectiveDate_PrefersDateTakenOverFileTime()
    {
        var photo = new Photo
        {
            DateModified = new DateTime(2025, 1, 1),
            DateTaken = new DateTime(2020, 5, 5)
        };

        Assert.Equal(new DateTime(2020, 5, 5), photo.EffectiveDate);
    }

    [Fact]
    public void PhotoQuery_CloneCopiesTagsIndependently()
    {
        var original = new PhotoQuery { SearchText = "beach", MinRating = 3 };
        original.Tags.Add("holiday");

        var copy = original.Clone();
        copy.Tags.Add("family");

        Assert.Equal("beach", copy.SearchText);
        Assert.Equal(3, copy.MinRating);
        Assert.Single(original.Tags);
        Assert.Equal(2, copy.Tags.Count);
    }

    [Fact]
    public void PhotoQuery_IsEmptyOnlyWhenNothingIsSet()
    {
        Assert.True(new PhotoQuery().IsEmpty);
        Assert.False(new PhotoQuery { FavoritesOnly = true }.IsEmpty);
    }
}
