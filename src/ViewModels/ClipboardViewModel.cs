using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClipShelf.ViewModels
{
    public class ClipboardViewModel : INotifyPropertyChanged
    {
        private string? _text;
        private DateTime _timestamp;

        public string? Text
        {
            get => _text;
            set
            {
                _text = value;
                OnPropertyChanged();
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                _timestamp = value;
                OnPropertyChanged();
            }
        }

        public ClipboardViewModel(string text, DateTime timestamp)
        {
            Text = text;
            Timestamp = timestamp;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}