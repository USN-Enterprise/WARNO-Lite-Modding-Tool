using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WarnoLiteModdingTool.App.Controls;

internal static class WindowCorners
{
    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        void Update()
        {
            // DWM rounds the actual native outline on supporting Windows versions.
            var preference = window.WindowState == WindowState.Maximized ? 1 : 2;
            if (DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int)) == 0) return;
            if (window.WindowState == WindowState.Minimized) return;
            if (window.WindowState == WindowState.Maximized) { SetWindowRgn(handle, IntPtr.Zero, true); return; }
            if (!GetWindowRect(handle, out var rect)) return;
            var diameter = (int)Math.Round(24 * VisualTreeHelper.GetDpi(window).DpiScaleX);
            var region = CreateRoundRectRgn(0, 0, rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1, diameter, diameter);
            if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
        }
        window.SizeChanged += (_, _) => Update();
        window.StateChanged += (_, _) => Update();
        window.DpiChanged += (_, _) => Update();
        Update();
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
}
