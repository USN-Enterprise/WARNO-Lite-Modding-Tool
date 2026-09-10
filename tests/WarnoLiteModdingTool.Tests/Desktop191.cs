using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    // Opt-in desktop QA; default tests continue to use offscreen windows.
    private static void CaptureDesktop191(Window window)
    {
        window.Left = 40; window.Top = 40; window.Width = 1200; window.Height = 680;
        var hwnd = new WindowInteropHelper(window).Handle;
        SetForegroundWindow191(hwnd);
        DrainDispatcher(window.Dispatcher);
        // Let DWM present the restored outline before copying its screen pixels.
        var until = DateTime.UtcNow.AddMilliseconds(500);
        while (DateTime.UtcNow < until) { DrainDispatcher(window.Dispatcher); Thread.Sleep(20); }
        GetWindowRect191(hwnd, out var rect);
        var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        var screen = GetDC191(IntPtr.Zero); var memory = CreateCompatibleDC191(screen);
        var bitmap = CreateCompatibleBitmap191(screen, width, height); var previous = SelectObject191(memory, bitmap);
        try
        {
            Assert(BitBlt191(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x00CC0020), "复制真实窗口桌面图像");
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = File.Create(Path.GetFullPath("publish/qa-1.9.3/native-window.png")); encoder.Save(output);
        }
        finally { SelectObject191(memory, previous); DeleteObject191(bitmap); DeleteDC191(memory); ReleaseDC191(IntPtr.Zero, screen); }
        window.Left = -10000; window.Top = -10000;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect191 { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] private static extern bool SetForegroundWindow191(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool GetWindowRect191(IntPtr hwnd, out Rect191 rect);
    [DllImport("user32.dll", EntryPoint = "GetDC")] private static extern IntPtr GetDC191(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "ReleaseDC")] private static extern int ReleaseDC191(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")] private static extern IntPtr CreateCompatibleDC191(IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")] private static extern IntPtr CreateCompatibleBitmap191(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll", EntryPoint = "SelectObject")] private static extern IntPtr SelectObject191(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll", EntryPoint = "BitBlt")] private static extern bool BitBlt191(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")] private static extern bool DeleteObject191(IntPtr value);
    [DllImport("gdi32.dll", EntryPoint = "DeleteDC")] private static extern bool DeleteDC191(IntPtr dc);
}
