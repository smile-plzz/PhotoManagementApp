using System;
using Avalonia.Controls;
using Avalonia.Input;
using PhotoManagementApp.ViewModels;

namespace PhotoManagementApp.Views;

public partial class ViewerWindow : Window
{
    private ViewerViewModel? ViewModel => DataContext as ViewerViewModel;

    public ViewerWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        PointerWheelChanged += OnPointerWheelChanged;
        Closed += OnClosed;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        if (e.KeyModifiers == KeyModifiers.None && e.Key >= Key.D0 && e.Key <= Key.D5)
        {
            viewModel.SetRatingCommand.Execute((int)(e.Key - Key.D0)).Subscribe(_ => { });
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Right:
            case Key.Space:
            case Key.PageDown:
                viewModel.Next();
                e.Handled = true;
                break;

            case Key.Left:
            case Key.PageUp:
                viewModel.Previous();
                e.Handled = true;
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;

            case Key.F11:
            case Key.F:
                WindowState = WindowState == WindowState.FullScreen
                    ? WindowState.Normal
                    : WindowState.FullScreen;
                e.Handled = true;
                break;

            case Key.I:
                viewModel.ShowInfo = !viewModel.ShowInfo;
                e.Handled = true;
                break;

            case Key.O:
                viewModel.OpenExternally();
                e.Handled = true;
                break;

            case Key.OemPlus:
            case Key.Add:
                viewModel.Zoom *= 1.25;
                e.Handled = true;
                break;

            case Key.OemMinus:
            case Key.Subtract:
                viewModel.Zoom /= 1.25;
                e.Handled = true;
                break;

            case Key.D:
                viewModel.Zoom = 1.0;
                e.Handled = true;
                break;
        }
    }

    /// <summary>Ctrl+wheel zooms; a plain wheel scrolls the image as usual.</summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        ViewModel.Zoom *= e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        e.Handled = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel?.Dispose();
    }
}
