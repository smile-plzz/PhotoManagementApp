using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhotoManagementApp.ViewModels;
using PhotoManagementApp.Views;

namespace PhotoManagementApp;

public partial class MainWindow : Window
{
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        KeyDown += OnWindowKeyDown;
    }

    /// <summary>
    /// A tile has entered the visual tree, meaning it is on (or near) screen.
    /// This is where thumbnail decoding starts, so scrolled-past photos are
    /// never decoded at all.
    /// </summary>
    private void OnTileAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { DataContext: PhotoTileViewModel tile })
            tile.BeginLoadThumbnail();
    }

    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PhotoTileViewModel tile } || ViewModel is null)
            return;

        var isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        ViewModel.HandleTileClick(tile, isCtrl);
    }

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PhotoTileViewModel tile })
            OpenViewer(tile);
    }

    private void OnFolderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null)
            return;

        if (e.AddedItems.OfType<FolderItem>().FirstOrDefault() is { } folder)
            ViewModel.SelectFolder(folder);
    }

    private void OpenViewer(PhotoTileViewModel tile)
    {
        if (ViewModel is null)
            return;

        var sequence = ViewModel.CurrentSequence;
        var startIndex = 0;
        for (var i = 0; i < sequence.Count; i++)
        {
            if (sequence[i].FilePath == tile.FilePath)
            {
                startIndex = i;
                break;
            }
        }

        var viewer = new ViewerWindow
        {
            DataContext = new ViewerViewModel(sequence, startIndex, ViewModel.Database)
        };
        viewer.Show(this);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is null)
            return;

        // Digits set a rating on the selection, matching the muscle memory of
        // most photo managers.
        if (e.KeyModifiers == KeyModifiers.None && e.Key >= Key.D0 && e.Key <= Key.D5)
        {
            ViewModel.SetRatingCommand.Execute((int)(e.Key - Key.D0)).Subscribe(_ => { });
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when ViewModel.SelectedPhoto is not null:
                OpenViewer(ViewModel.SelectedPhoto);
                e.Handled = true;
                break;

            case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                ViewModel.SelectAllCommand.Execute().Subscribe(_ => { });
                e.Handled = true;
                break;

            case Key.F when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                this.FindControl<TextBox>("SearchBox")?.Focus();
                e.Handled = true;
                break;
        }
    }
}
