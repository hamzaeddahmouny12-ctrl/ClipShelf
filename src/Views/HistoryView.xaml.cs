using System.Windows.Controls;
using ClipShelf.ViewModels;

namespace ClipShelf.Views
{
    public partial class HistoryView : System.Windows.Controls.UserControl
    {
        public HistoryView()
        {
            this.InitializeComponent();
            this.DataContext = new ClipboardViewModel(string.Empty, System.DateTime.Now);
        }
    }
}