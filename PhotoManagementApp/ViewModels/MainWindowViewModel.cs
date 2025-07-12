using Avalonia.Controls;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Media.Imaging;
using System.Linq;
using System;
using Avalonia.Controls.ApplicationLifetimes; // Added for IClassicDesktopStyleApplicationLifetime

namespace PhotoManagementApp.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<FolderItem> Folders { get; }
        public ObservableCollection<ImageItem> Images { get; }
        public ReactiveCommand<Unit, Unit> SelectFolderCommand { get; }

        private ImageItem? _selectedImage;
        public ImageItem? SelectedImage
        {
            get => _selectedImage;
            set => this.RaiseAndSetIfChanged(ref _selectedImage, value);
        }

        public MainWindowViewModel()
        {
            Folders = new ObservableCollection<FolderItem>();
            Images = new ObservableCollection<ImageItem>();
            SelectFolderCommand = ReactiveCommand.CreateFromTask(SelectFolder, outputScheduler: RxApp.MainThreadScheduler);
        }

        private async Task SelectFolder()
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;

            if (topLevel?.StorageProvider is { } storageProvider)
            {
                var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
                if (folder != null && folder.Count > 0)
                {
                    Folders.Clear();
                    var rootFolder = new FolderItem(new DirectoryInfo(folder[0].Path.LocalPath));
                    Folders.Add(rootFolder);
                    await rootFolder.LoadSubfolders();
                    LoadImages(rootFolder.FullPath);
                }
            }
        }

        private void LoadImages(string folderPath)
        {
            Images.Clear();
            var imageFiles = Directory.GetFiles(folderPath)
                                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                                                f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                                f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) ||
                                                f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

            foreach (var imageFile in imageFiles)
            {
                Images.Add(new ImageItem(imageFile));
            }
        }
    }
}
