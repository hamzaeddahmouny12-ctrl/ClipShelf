using System;
using System.Collections.Generic;
using System.Linq;
using ClipShelf.Models;

namespace ClipShelf.Services
{
    public class ClipboardService
    {
        private List<ClipboardItem> clipboardHistory;

        public ClipboardService()
        {
            clipboardHistory = new List<ClipboardItem>();
        }

        public void AddClipboardItem(string text)
        {
            var newItem = new ClipboardItem(text);
            clipboardHistory.Add(newItem);
        }

        public List<ClipboardItem> GetClipboardHistory()
        {
            return clipboardHistory.OrderByDescending(item => item.Timestamp).ToList();
        }

        public void ClearClipboardHistory()
        {
            clipboardHistory.Clear();
        }
    }
}