using ReactiveUI;

namespace PhotoManagementApp.ViewModels
{
    public enum NavigationKind
    {
        AllPhotos,
        Favorites,
        RecentlyAdded,
        Untagged,
        Year,
        Tag,
        Camera,
        Place,
        Duplicates
    }

    /// <summary>An entry in the left sidebar.</summary>
    public class NavigationItem : ViewModelBase
    {
        public NavigationKind Kind { get; }
        public string Label { get; }
        public string Glyph { get; }

        /// <summary>Kind-specific payload: the year, tag name, camera model or place key.</summary>
        public object? Payload { get; }

        private int _count;
        public int Count
        {
            get => _count;
            set => this.RaiseAndSetIfChanged(ref _count, value);
        }

        public NavigationItem(NavigationKind kind, string label, string glyph, object? payload = null, int count = 0)
        {
            Kind = kind;
            Label = label;
            Glyph = glyph;
            Payload = payload;
            _count = count;
        }

        public string CountDisplay => Count > 0 ? Count.ToString() : string.Empty;
    }
}
