using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarnoLiteModdingTool.App.Controls;

public static class RoundedContent
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(RoundedContent), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    private static void Changed(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not Border border) return;
        border.SizeChanged -= Resized; border.Loaded -= Loaded;
        if ((bool)args.NewValue) { border.SizeChanged += Resized; border.Loaded += Loaded; Update(border); }
        else border.ClearValue(UIElement.ClipProperty);
    }
    private static void Resized(object sender, SizeChangedEventArgs args) => Update((Border)sender);
    private static void Loaded(object sender, RoutedEventArgs args) => Update((Border)sender);
    private static void Update(Border border) => border.Clip = new RectangleGeometry(
        new Rect(0, 0, border.ActualWidth, border.ActualHeight), border.CornerRadius.TopLeft, border.CornerRadius.TopLeft);
}
