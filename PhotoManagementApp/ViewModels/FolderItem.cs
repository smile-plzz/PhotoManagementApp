using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System; // Added for Exception and UnauthorizedAccessException

namespace PhotoManagementApp.ViewModels
{
    public class FolderItem : ViewModelBase
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public ObservableCollection<FolderItem> Subfolders { get; set; }

        public FolderItem(DirectoryInfo directoryInfo)
        {
            Name = directoryInfo.Name;
            FullPath = directoryInfo.FullName;
            Subfolders = new ObservableCollection<FolderItem>();
            // Add a dummy item to allow expansion
            Subfolders.Add(new FolderItem("Loading..."));
        }

        // Constructor for dummy item
        private FolderItem(string name)
        {
            Name = name;
            FullPath = string.Empty;
            Subfolders = new ObservableCollection<FolderItem>();
        }

        public async Task LoadSubfolders()
        {
            Subfolders.Clear();
            try
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(FullPath);
                await Task.Run(() =>
                {
                    foreach (var subDir in directoryInfo.GetDirectories())
                    {
                        if ((subDir.Attributes & FileAttributes.Hidden) == 0) // Exclude hidden directories
                        {
                            Subfolders.Add(new FolderItem(subDir));
                        }
                    }
                });
            }
            catch (UnauthorizedAccessException)
            {
                // Log or handle access denied
            }
            catch (Exception)
            {
                // Log other exceptions
            }
        }
    }
}
