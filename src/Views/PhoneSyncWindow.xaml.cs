using System.Windows;
using ClipShelf.ViewModels;
using ClipShelf.Services;

namespace ClipShelf.Views;

public partial class PhoneSyncWindow : Window
{
    public PhoneSyncWindow(BackgroundAgent? agent = null)
    {
        InitializeComponent();
        DataContext = new PhoneSyncViewModel(agent ?? (System.Windows.Application.Current as App)?.BackgroundAgent);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
