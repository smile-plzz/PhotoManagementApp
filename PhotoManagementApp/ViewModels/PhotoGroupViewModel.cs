using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PhotoManagementApp.ViewModels
{
    /// <summary>
    /// A dated band of photos in the timeline, e.g. all shots from one day or month.
    /// </summary>
    public class PhotoGroupViewModel : ViewModelBase
    {
        public string Header { get; }
        public string SubHeader { get; }
        public ObservableCollection<PhotoTileViewModel> Photos { get; }

        public PhotoGroupViewModel(string header, string subHeader, IEnumerable<PhotoTileViewModel> photos)
        {
            Header = header;
            SubHeader = subHeader;
            Photos = new ObservableCollection<PhotoTileViewModel>(photos);
        }

        public int Count => Photos.Count;
        public string CountDisplay => Count == 1 ? "1 photo" : $"{Count} photos";
    }
}
