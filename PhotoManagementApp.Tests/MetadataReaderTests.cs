using System;
using System.IO;
using PhotoManagementApp.Services;

namespace PhotoManagementApp.Tests;

public class MetadataReaderTests : IDisposable
{
    private readonly TestLibrary _library = new();

    public void Dispose() => _library.Dispose();

    [Fact]
    public void Read_PopulatesFileSystemFactsForAnImageWithoutExif()
    {
        var path = _library.CreateImage("plain.png");

        var photo = new MetadataReader().Read(path);

        Assert.Equal("plain.png", photo.FileName);
        Assert.Equal(path, photo.FilePath);
        Assert.True(photo.FileSize > 0);
        Assert.Null(photo.DateTaken);
    }

    [Fact]
    public void Read_ExtractsDimensionsFromPngHeader()
    {
        var photo = new MetadataReader().Read(_library.CreateImage("plain.png"));

        Assert.Equal(1, photo.Width);
        Assert.Equal(1, photo.Height);
    }

    [Fact]
    public void Read_FallsBackToFileTimeWhenNoExifDateExists()
    {
        var photo = new MetadataReader().Read(_library.CreateImage("plain.png"));

        Assert.Null(photo.DateTaken);
        Assert.Equal(photo.DateModified, photo.EffectiveDate);
    }

    [Fact]
    public void Read_DoesNotThrowOnACorruptFile()
    {
        var path = _library.CreateFile("broken.jpg", new byte[] { 0xFF, 0xD8, 0x00, 0x01, 0x02 });

        var photo = new MetadataReader().Read(path);

        Assert.Equal("broken.jpg", photo.FileName);
    }

    [Fact]
    public void Read_DoesNotThrowOnAFileThatIsNotAnImage()
    {
        var photo = new MetadataReader().Read(_library.CreateNonImage("notes.txt"));

        Assert.Equal("notes.txt", photo.FileName);
    }
}
