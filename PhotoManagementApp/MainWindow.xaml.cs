// MainWindow.xaml.cs
using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;

namespace PhotoManagementApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<FileInfo> _currentImageFiles = new List<FileInfo>();
        private ImageService _imageService = new ImageService();
        private string _currentSortOrder = "Filename"; // Default sort order

        public MainWindow()
        {
            InitializeComponent();
            // Further initialization or data loading can go here
        }

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    string selectedPath = dialog.SelectedPath;
                    LoadFolders(selectedPath);
                }
            }
        }

        private void LoadFolders(string path)
        {
            FolderTreeView.Items.Clear();
            try
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(path);
                if (directoryInfo.Exists)
                {
                    TreeViewItem rootItem = CreateTreeViewItem(directoryInfo);
                    FolderTreeView.Items.Add(rootItem);
                    rootItem.IsExpanded = true; // Expand the root folder
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Windows.MessageBox.Show("Access to the path is denied.", "Permission Denied", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                Logger.LogError($"Access denied to folder: {path}", ex);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"An error occurred: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Logger.LogError($"Error loading folders from {path}", ex);
            }
        }

        private TreeViewItem CreateTreeViewItem(DirectoryInfo directoryInfo)
        {
            TreeViewItem item = new TreeViewItem();
            item.Header = directoryInfo.Name;
            item.Tag = directoryInfo;
            item.Expanded += Folder_Expanded;

            // Add a dummy item to allow expansion
            item.Items.Add(new TreeViewItem());

            return item;
        }

        private void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            TreeViewItem item = (TreeViewItem)sender;
            if (item.Items.Count == 1 && item.Items[0] is TreeViewItem dummyItem && dummyItem.Header == null)
            {
                item.Items.Clear();
                DirectoryInfo directoryInfo = (DirectoryInfo)item.Tag;

                try
                {
                    foreach (DirectoryInfo subDir in directoryInfo.GetDirectories())
                    {
                        if ((subDir.Attributes & FileAttributes.Hidden) == 0) // Exclude hidden directories
                        {
                            item.Items.Add(CreateTreeViewItem(subDir));
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Handle access denied for subdirectories silently or log it
                    Logger.LogError($"Access denied to subfolder: {directoryInfo.FullName}", ex);
                }
                catch (Exception ex)
                {
                    // Handle other exceptions
                    System.Windows.MessageBox.Show($"Error expanding folder: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    Logger.LogError($"Error expanding folder: {directoryInfo.FullName}", ex);
                }
            }
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (FolderTreeView.SelectedItem is TreeViewItem selectedItem)
            {
                if (selectedItem.Tag is DirectoryInfo directoryInfo)
                {
                    LoadImages(directoryInfo.FullName);
                }
            }
        }

        private void LoadImages(string folderPath)
        {
            PhotoItemsControl.ItemsSource = null; // Clear previous items
            _currentImageFiles.Clear();
            try
            {
                _currentImageFiles = _imageService.GetImageFiles(folderPath);
                FilterAndDisplayImages();
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Windows.MessageBox.Show("Access to the path is denied.", "Permission Denied", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                Logger.LogError($"Access denied to folder: {folderPath}", ex);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"An error occurred: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Logger.LogError($"An error occurred while loading images from {folderPath}", ex);
            }
        }

        private async void FilterAndDisplayImages()
        {
            if (PhotoItemsControl == null || SearchTextBox == null || SortComboBox == null)
            {
                return; 
            }

            PhotoItemsControl.ItemsSource = null; // Clear previous items
            string searchText = SearchTextBox.Text.ToLower();

            List<FileInfo> imagesToDisplay = _imageService.FilterImages(_currentImageFiles, searchText);

            // Apply sorting
            switch (_currentSortOrder)
            {
                case "Filename":
                    imagesToDisplay = imagesToDisplay.OrderBy(f => f.Name).ToList();
                    break;
                case "Date Taken":
                    imagesToDisplay = imagesToDisplay.OrderBy(f => GetDateTaken(f.FullName)).ToList();
                    break;
            }

            List<ImageData> imageDataList = new List<ImageData>();

            foreach (FileInfo file in imagesToDisplay)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        // Use a FileStream to prevent file locking issues
                        using (FileStream stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read))
                        {
                            MemoryStream memoryStream = new MemoryStream();
                            stream.CopyTo(memoryStream);
                            memoryStream.Position = 0; // Reset stream position
                            bitmap.StreamSource = memoryStream;
                            bitmap.DecodePixelWidth = 100; // Set decode width for thumbnail
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze(); // Freeze the bitmap for better performance
                        }

                        ImageData imageData = new ImageData
                        {
                            ThumbnailSource = bitmap,
                            FullPath = file.FullName
                        };

                        Dispatcher.Invoke(() =>
                        {
                            imageDataList.Add(imageData);
                        });
                    }
                    catch (Exception ex)
                    {
                        // Log or handle specific image loading errors
                        Logger.LogError($"Error loading image {file.Name}", ex);
                    }
                });
            }
            PhotoItemsControl.ItemsSource = imageDataList; // Set ItemsSource after all images are processed
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            FilterAndDisplayImages();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAndDisplayImages();
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SortComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                _currentSortOrder = selectedItem.Content?.ToString() ?? "Filename"; // Handle potential null content
                FilterAndDisplayImages(); // Re-filter and display with new sort order
            }
        }

        private DateTime GetDateTaken(string imagePath)
        {
            try
            {
                IEnumerable<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(imagePath);
                var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (subIfdDirectory != null && subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dateTaken))
                {
                    return dateTaken;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting date taken for {Path.GetFileName(imagePath)}", ex);
            }
            return DateTime.MinValue; // Return a default value if date taken is not found or an error occurs
        }

        private void ThumbnailImage_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Image clickedImage && clickedImage.Tag is string imagePath)
            {
                // Display full image
                BitmapImage fullImage = new BitmapImage();
                fullImage.BeginInit();
                using (FileStream stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
                {
                    MemoryStream memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    memoryStream.Position = 0; // Reset stream position
                    fullImage.StreamSource = memoryStream;
                    fullImage.CacheOption = BitmapCacheOption.OnLoad;
                    fullImage.EndInit();
                    fullImage.Freeze();
                }
                PreviewImage.Source = fullImage;

                // Display basic metadata
                FileNameTextBlock.Text = $"Filename: {Path.GetFileName(imagePath)}";

                try
                {
                    IEnumerable<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(imagePath);

                    // Date Taken
                    var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                    if (subIfdDirectory != null && subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dateTaken))
                    {
                        DateTakenTextBlock.Text = $"Date Taken: {dateTaken.ToShortDateString()}";
                    }
                    else
                    {
                        DateTakenTextBlock.Text = "Date Taken: N/A";
                    }

                    // Camera Model
                    var exifIfd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                    if (exifIfd0Directory != null && exifIfd0Directory.ContainsTag(ExifIfd0Directory.TagModel))
                    {
                        CameraTextBlock.Text = $"Camera: {exifIfd0Directory.GetString(ExifIfd0Directory.TagModel)}";
                    }
                    else
                    {
                        CameraTextBlock.Text = "Camera: N/A";
                    }

                    // Tags (Keywords) - often found in IPTC or XMP
                    var iptcDirectory = directories.OfType<IptcDirectory>().FirstOrDefault();
                    if (iptcDirectory != null && iptcDirectory.ContainsTag(IptcDirectory.TagKeywords))
                    {
                        string[]? keywords = iptcDirectory.GetStringArray(IptcDirectory.TagKeywords);
                        if (keywords != null && keywords.Length > 0)
                        {
                            TagsTextBlock.Text = $"Tags: {string.Join(", ", keywords)}";
                        }
                        else
                        {
                            TagsTextBlock.Text = "Tags: N/A";
                        }
                    }
                    else
                    {
                        TagsTextBlock.Text = "Tags: N/A";
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error extracting metadata for {Path.GetFileName(imagePath)}", ex);
                    DateTakenTextBlock.Text = "Date Taken: Error";
                    CameraTextBlock.Text = "Camera: Error";
                    TagsTextBlock.Text = "Tags: Error";
                }
            }
        }
    }
}