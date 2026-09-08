using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.App.Settings;
namespace WarnoLiteModdingTool.App.Theming;

public static class BackgroundAppearance
{
    public static BitmapImage Load(string path)
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.DecodePixelWidth = 2560; bitmap.UriSource = new Uri(Path.GetFullPath(path)); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    public static void Apply(UiPreferences? settings = null)
    {
        var resources = Application.Current.Resources;
        var preferences = settings ?? new UiSettings().Load();
        var background = (Brush)resources["BackgroundBrush"];
        resources["MainBackgroundBrush"] = background;
        foreach (var key in new[] { "SurfaceBrush", "SurfaceAltBrush" })
        { var brush = ((Brush)resources[key]).Clone(); brush.Opacity = 1; resources[key] = brush; }
        if (!preferences.BackgroundEnabled || !File.Exists(preferences.BackgroundImage)) return;
        try
        {
            var bitmap = Load(preferences.BackgroundImage!);
            var picture = new ImageBrush(bitmap) { Opacity = Math.Clamp(preferences.BackgroundOpacity, 0, .3), Stretch = preferences.BackgroundLayout == "fit" ? Stretch.Uniform : Stretch.UniformToFill };
            if (preferences.BackgroundLayout == "tile") { picture.TileMode = TileMode.Tile; picture.Viewport = new Rect(0, 0, .25, .25); }
            resources["MainBackgroundBrush"] = picture;
            foreach (var key in new[] { "SurfaceBrush", "SurfaceAltBrush" }) { var brush = ((Brush)resources[key]).Clone(); brush.Opacity = .82; resources[key] = brush; }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.IO.FileFormatException or ArgumentException) { /* Missing/unavailable user image falls back to the chosen palette. */ }
    }
}
