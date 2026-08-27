using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;

namespace PhotoManagementApp.ViewModels
{
    /// <summary>
    /// A folder in the navigation tree. Children are enumerated the first time the
    /// node is expanded, so adding a drive root does not walk the whole disk.
    /// </summary>
    public class FolderItem : ViewModelBase
    {
        private static readonly FolderItem PlaceholderChild = new("Loading...", isPlaceholder: true);

        private bool _childrenLoaded;

        public string Name { get; }
        public string FullPath { get; }
        public bool IsPlaceholder { get; }
        public ObservableCollection<FolderItem> Subfolders { get; }

        public FolderItem(DirectoryInfo directoryInfo)
        {
            Name = string.IsNullOrEmpty(directoryInfo.Name) ? directoryInfo.FullName : directoryInfo.Name;
            FullPath = directoryInfo.FullName;
            IsPlaceholder = false;
            Subfolders = new ObservableCollection<FolderItem> { PlaceholderChild };
        }

        public FolderItem(string path) : this(new DirectoryInfo(path))
        {
        }

        private FolderItem(string name, bool isPlaceholder)
        {
            Name = name;
            FullPath = string.Empty;
            IsPlaceholder = isPlaceholder;
            Subfolders = new ObservableCollection<FolderItem>();
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                this.RaiseAndSetIfChanged(ref _isExpanded, value);
                if (value)
                    _ = LoadSubfoldersAsync();
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        /// <summary>
        /// Populates the child collection from disk. Repeated calls are no-ops,
        /// and inaccessible folders simply end up with no children rather than
        /// surfacing an error.
        /// </summary>
        public async Task LoadSubfoldersAsync()
        {
            if (_childrenLoaded || IsPlaceholder)
                return;
            _childrenLoaded = true;

            var children = await Task.Run(() =>
            {
                try
                {
                    return new DirectoryInfo(FullPath)
                        .GetDirectories()
                        .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                        .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                        .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                        .Select(d => new FolderItem(d))
                        .ToList();
                }
                catch (UnauthorizedAccessException)
                {
                    return new System.Collections.Generic.List<FolderItem>();
                }
                catch (IOException)
                {
                    return new System.Collections.Generic.List<FolderItem>();
                }
            }).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Subfolders.Clear();
                foreach (var child in children)
                    Subfolders.Add(child);
            });
        }
    }
}
