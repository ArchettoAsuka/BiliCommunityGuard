using System.Windows;
using BiliCommunityGuard.App.ViewModels;

namespace BiliCommunityGuard.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
