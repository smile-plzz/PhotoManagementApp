using Avalonia.Controls;
using PhotoManagementApp.ViewModels;

namespace PhotoManagementApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}