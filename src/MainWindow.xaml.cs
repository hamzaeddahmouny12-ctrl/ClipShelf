using System;
using System.Windows;
using ClipShelf.ViewModels;
using ClipShelf.Models;

namespace ClipShelf
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            try
            {
                this.InitializeComponent();
                var viewModel = new MainViewModel();
                this.DataContext = viewModel;
                viewModel.AddClipboardItem(new ClipboardItem("Welcome to ClipShelf"));

                // when the main window closes, shut the application down completely
                this.Closed += (s, e) =>
                {
                    if (System.Windows.Application.Current is App app)
                    {
                        app.ExitApp();
                    }
                    else
                    {
                        System.Windows.Application.Current.Shutdown();
                    }
                };
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error: {ex.Message}\n\n{ex.StackTrace}", "ClipShelf Error");
                throw;
            }
        }
    }
}