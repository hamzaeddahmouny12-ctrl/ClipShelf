using System;
using System.Windows.Media.Imaging;

namespace ClipShelf.Models
{
    public enum ClipboardItemType { Text, Image }

    public class ClipboardItem
    {
        // either text or image will be populated depending on the type
        public string? Text { get; set; }
        public BitmapImage? Image { get; set; }
        public ClipboardItemType Type { get; }
        public DateTime Timestamp { get; set; }

        public ClipboardItem(string text)
        {
            Type = ClipboardItemType.Text;
            Text = text;
            Timestamp = DateTime.Now;
        }

        public ClipboardItem(BitmapImage image)
        {
            Type = ClipboardItemType.Image;
            Image = image;
            Timestamp = DateTime.Now;
        }
    }
}