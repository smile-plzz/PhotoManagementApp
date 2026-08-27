using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoManagementApp;
using PhotoManagementApp.Data;
using PhotoManagementApp.Models;
using PhotoManagementApp.Services;

namespace PhotoManagementApp.Tests;

public class LibraryScannerTests : IDisposable
{
    private readonly TestLibrary _library = new();
    private readonly PhotoDatabase _database = PhotoDatabase.OpenInMemory();

    public void Dispose()
    {
        var path = _database.DatabasePath;
        _database.Dispose();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(path + suffix); } catch (IOException) { }
        }
        _library.Dispose();
    }

    [Fact]
    public async Task ScanAsync_IndexesImagesAndIgnoresOtherFiles()
    {
        _library.CreateImage("a.png");
        _library.CreateImage(Path.Combine("2024", "b.png"));
        _library.CreateNonImage("notes.txt");

        var result = await new LibraryScanner(_database).ScanAsync(new[] { _library.Root });

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.FilesIndexed);
        Assert.Equal(2, _database.GetPhotoCount());
    }

    [Fact]
    public async Task ScanAsync_SecondRunSkipsUnchangedFiles()
    {
        _library.CreateImage("a.png");
        var scanner = new LibraryScanner(_database);

        await scanner.ScanAsync(new[] { _library.Root });
        var second = await scanner.ScanAsync(new[] { _library.Root });

        Assert.Equal(1, second.FilesSeen);
        Assert.Equal(0, second.FilesIndexed);
    }

    [Fact]
    public async Task ScanAsync_ReindexesAFileWhoseTimestampChanged()
    {
        var path = _library.CreateImage("a.png");
        var scanner = new LibraryScanner(_database);
        await scanner.ScanAsync(new[] { _library.Root });

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(1));
        var second = await scanner.ScanAsync(new[] { _library.Root });

        Assert.Equal(1, second.FilesIndexed);
    }

    [Fact]
    public async Task ScanAsync_RemovesIndexEntriesForDeletedFiles()
    {
        var path = _library.CreateImage("a.png");
        _library.CreateImage("b.png");
        var scanner = new LibraryScanner(_database);
        await scanner.ScanAsync(new[] { _library.Root });

        File.Delete(path);
        var second = await scanner.ScanAsync(new[] { _library.Root });

        Assert.Equal(1, second.FilesRemoved);
        Assert.Equal(1, _database.GetPhotoCount());
    }

    [Fact]
    public async Task ScanAsync_MissingRootIsIgnoredRatherThanThrowing()
    {
        var result = await new LibraryScanner(_database)
            .ScanAsync(new[] { Path.Combine(_library.Root, "does-not-exist") });

        Assert.True(result.IsComplete);
        Assert.Equal(0, result.FilesIndexed);
    }

    [Fact]
    public async Task ScanAsync_ComputesHashesThatIdentifyDuplicates()
    {
        var bytes = File.ReadAllBytes(_library.CreateImage("original.png"));
        _library.CreateFile(Path.Combine("backup", "copy.png"), bytes);

        await new LibraryScanner(_database) { ComputeContentHashes = true }
            .ScanAsync(new[] { _library.Root });

        var groups = _database.GetDuplicateGroups();

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
    }

    [Fact]
    public async Task ScanAsync_ReportsProgress()
    {
        _library.CreateImage("a.png");
        var reports = new System.Collections.Generic.List<ScanProgress>();
        var progress = new Progress<ScanProgress>(reports.Add);

        await new LibraryScanner(_database).ScanAsync(new[] { _library.Root }, progress);

        // Progress is marshalled to the captured context, so give it a moment to drain.
        await Task.Delay(100);
        Assert.NotEmpty(reports);
    }
}
