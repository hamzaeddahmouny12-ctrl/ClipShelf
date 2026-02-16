using System.Windows;

namespace ClipShelf.Views
{
    public partial class ShutdownTimerWindow : Window
    {
        public ShutdownTimerWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}