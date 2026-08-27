using System;
using System.IO;

namespace PhotoManagementApp.Tests;

/// <summary>
/// A temporary folder of real image files, cleaned up when the test finishes.
/// Tests that touch the file system or the metadata reader use this rather than
/// mocking, so the assertions cover the code paths that actually run in the app.
/// </summary>
public sealed class TestLibrary : IDisposable
{
    // A valid 1x1 PNG. Small enough to inline, real enough for the decoder and
    // MetadataExtractor to parse dimensions from.
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public string Root { get; }

    public TestLibrary()
    {
        Root = Path.Combine(Path.GetTempPath(), "pma-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string CreateImage(string relativePath, DateTime? lastWrite = null)
    {
        var fullPath = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, Convert.FromBase64String(OnePixelPngBase64));

        if (lastWrite.HasValue)
            File.SetLastWriteTimeUtc(fullPath, lastWrite.Value);

        return fullPath;
    }

    /// <summary>Creates a file with arbitrary bytes, for duplicate-hash scenarios.</summary>
    public string CreateFile(string relativePath, byte[] contents)
    {
        var fullPath = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, contents);
        return fullPath;
    }

    public string CreateNonImage(string relativePath)
    {
        var fullPath = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "not an image");
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A file left open by a failed test should not mask that failure.
        }
    }
}
