using System.IO;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using PhotoManagementApp;

namespace PhotoManagementApp.Tests
{
    public class ImageFilteringTests
    {
        [Fact]
        public void FilterImages_FiltersByFileNameCorrectly()
        {
            // Arrange
            var imageService = new ImageService();
            var imageFiles = new List<FileInfo>
            {
                new FileInfo("C:\\test\\image1.jpg"),
                new FileInfo("C:\\test\\another_image.png"),
                new FileInfo("C:\\test\\my_photo.jpeg"),
                new FileInfo("C:\\test\\image_final.gif")
            };

            string searchText = "image";

            // Act
            var filteredImages = imageService.FilterImages(imageFiles, searchText);

            // Assert
            Assert.Equal(3, filteredImages.Count);
            Assert.Contains(filteredImages, f => f.Name == "image1.jpg");
            Assert.Contains(filteredImages, f => f.Name == "another_image.png");
            Assert.Contains(filteredImages, f => f.Name == "image_final.gif");
            Assert.DoesNotContain(filteredImages, f => f.Name == "my_photo.jpeg");
        }

        [Fact]
        public void FilterImages_ReturnsAllImagesWhenSearchTextIsEmpty()
        {
            // Arrange
            var imageService = new ImageService();
            var imageFiles = new List<FileInfo>
            {
                new FileInfo("C:\\test\\image1.jpg"),
                new FileInfo("C:\\test\\another_image.png")
            };

            string searchText = "";

            // Act
            var filteredImages = imageService.FilterImages(imageFiles, searchText);

            // Assert
            Assert.Equal(2, filteredImages.Count);
            Assert.Contains(filteredImages, f => f.Name == "image1.jpg");
            Assert.Contains(filteredImages, f => f.Name == "another_image.png");
        }

        [Fact]
        public void FilterImages_ReturnsNoImagesWhenNoMatch()
        {
            // Arrange
            var imageService = new ImageService();
            var imageFiles = new List<FileInfo>
            {
                new FileInfo("C:\\test\\image1.jpg"),
                new FileInfo("C:\\test\\another_image.png")
            };

            string searchText = "xyz";

            // Act
            var filteredImages = imageService.FilterImages(imageFiles, searchText);

            // Assert
            Assert.Empty(filteredImages);
        }

        [Fact]
        public void GetImageFiles_ReturnsCorrectImageFiles()
        {
            // Arrange
            var imageService = new ImageService();
            string testFolderPath = Path.Combine(Path.GetTempPath(), "TestImages");
            Directory.CreateDirectory(testFolderPath);

            File.WriteAllBytes(Path.Combine(testFolderPath, "test1.jpg"), new byte[10]);
            File.WriteAllBytes(Path.Combine(testFolderPath, "test2.png"), new byte[10]);
            File.WriteAllText(Path.Combine(testFolderPath, "test.txt"), "This is a text file.");

            // Act
            var imageFiles = imageService.GetImageFiles(testFolderPath);

            // Assert
            Assert.Equal(2, imageFiles.Count);
            Assert.Contains(imageFiles, f => f.Name == "test1.jpg");
            Assert.Contains(imageFiles, f => f.Name == "test2.png");
            Assert.DoesNotContain(imageFiles, f => f.Name == "test.txt");

            // Clean up
            Directory.Delete(testFolderPath, true);
        }
    }
}