using Avalonia.Media.Imaging;
using ReactiveUI;
using System.IO;
using System.Threading.Tasks;
using System; // Added for Exception and Console

namespace PhotoManagementApp.ViewModels
{
    public class ImageItem : ViewModelBase
    {
        private Bitmap? _thumbnail;
        public string FullPath { get; }

        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            private set => this.RaiseAndSetIfChanged(ref _thumbnail, value);
        }

        public ImageItem(string fullPath)
        {
            FullPath = fullPath;
            // LoadThumbnailAsync is now awaited in the constructor
            Task.Run(LoadThumbnailAsync); // Run in background, not awaited here to avoid blocking constructor
        }

        private async Task LoadThumbnailAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    using (var stream = File.OpenRead(FullPath))
                    {
                        Thumbnail = new Bitmap(stream);
                    }
                }
                catch (Exception ex)
                {
                    // Log error
                    Console.WriteLine($"Error loading thumbnail for {FullPath}: {ex.Message}");
                }
            });
        }
    }
}
