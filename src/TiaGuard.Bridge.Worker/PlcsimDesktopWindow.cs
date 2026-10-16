using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TiaGuard.Bridge.Worker
{
    internal static class PlcsimDesktopWindow
    {
        private delegate bool WindowCallback(IntPtr window, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int state);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        public static IntPtr FindOwnedWindow(int pid)
        {
            var matches = new List<IntPtr>();
            EnumWindows((window, _) => {
                uint actual; GetWindowThreadProcessId(window, out actual);
                if (actual == pid) {
                    var text = new StringBuilder(256); GetClassName(window, text, text.Capacity);
                    if (text.ToString() == "Chrome_WidgetWin_1") {
                        var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
                        if (title.ToString() == "SIMATIC S7-PLCSIM") matches.Add(window);
                    }
                }
                return true;
            }, IntPtr.Zero);
            return matches.Count == 1 ? matches[0] : IntPtr.Zero;
        }
        public static void Prepare(IntPtr window, int pid)
        {
            Require(window, pid);
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot initialize physical-pixel UI context.");
            if (!IsWindowVisible(window) || IsIconic(window)) ShowWindowAsync(window, 9);
            IntPtr ignored;
            SendMessageTimeout(window, 0x003D, IntPtr.Zero, new IntPtr(1), 2, 1000, out ignored);
            SendMessageTimeout(window, 0x003D, IntPtr.Zero, new IntPtr(-4), 2, 1000, out ignored);
        }
        public static void DoubleClick(IntPtr window, int pid, int screenX, int screenY) => Click(window, pid, screenX, screenY, true);
        public static void Click(IntPtr window, int pid, int screenX, int screenY, bool twice = false)
        {
            Require(window, pid);
            var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            if (previous == IntPtr.Zero) throw new InvalidOperationException("Cannot establish physical-pixel coordinates.");
            try {
                var point = new Point { X = screenX, Y = screenY }; Rect bounds;
                if (!ScreenToClient(window, ref point) || !GetClientRect(window, out bounds) ||
                    point.X < 0 || point.Y < 0 || point.X >= bounds.Right || point.Y >= bounds.Bottom || point.X > 32767 || point.Y > 32767)
                    throw new InvalidOperationException("Control is outside the owned client window.");
                var packed = new IntPtr((point.Y << 16) | (point.X & 0xffff));
                foreach (var message in twice ? new uint[] { 0x0201, 0x0202, 0x0203, 0x0202 } : new uint[] { 0x0201, 0x0202 }) {
                    Require(window, pid);
                    if (!PostMessage(window, message, new IntPtr(message == 0x0201 || message == 0x0203 ? 1 : 0), packed))
                        throw new InvalidOperationException("Owned control click failed.");
                }
            } finally { SetThreadDpiAwarenessContext(previous); }
        }
        public static void Enter(IntPtr window, int pid)
        {
            Require(window, pid);
            if (!PostMessage(window, 0x0100, new IntPtr(13), IntPtr.Zero) || !PostMessage(window, 0x0101, new IntPtr(13), IntPtr.Zero))
                throw new InvalidOperationException("Owned editor commit failed.");
        }
        private static void Require(IntPtr window, int pid)
        {
            uint actual; GetWindowThreadProcessId(window, out actual);
            if (window == IntPtr.Zero || actual != pid) throw new InvalidOperationException("Owned window identity changed.");
        }
    }
}
