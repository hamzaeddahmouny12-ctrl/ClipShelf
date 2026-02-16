using System;
using System.Runtime.InteropServices;

namespace ClipShelf.Services
{
    public class HotkeyService
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly IntPtr _windowHandle;

        public HotkeyService(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
        }

        public void RegisterHotkey(int id, int modifiers, int key)
        {
            if (!RegisterHotKey(_windowHandle, id, modifiers, key))
            {
                throw new InvalidOperationException("Hotkey registration failed.");
            }
        }

        public void UnregisterHotkey(int id)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}