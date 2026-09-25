using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WarnoLiteModdingTool.App;

public partial class MainWindow
{
    private Dictionary<string, (double Horizontal, double Vertical)>? _scrollPositions;
    private Dictionary<string, bool>? _expandedSections;
    private void CaptureWorkspacePosition()
    {
        _scrollPositions = Elements(this).OfType<ScrollViewer>().GroupBy(PositionKey).ToDictionary(g => g.Key, g => (g.First().HorizontalOffset, g.First().VerticalOffset));
        _expandedSections = Elements(this).OfType<Expander>().Where(e => !UnitSection(e)).GroupBy(PositionKey).ToDictionary(g => g.Key, g => g.First().IsExpanded);
    }
    private void RestoreWorkspacePosition()
    {
        var scrolls = _scrollPositions; var expanded = _expandedSections;
        _scrollPositions = null; _expandedSections = null;
        if (scrolls is null || expanded is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            foreach (var expander in Elements(this).OfType<Expander>().Where(e => !UnitSection(e))) if (expanded.TryGetValue(PositionKey(expander), out var value)) expander.SetCurrentValue(Expander.IsExpandedProperty, value);
            UpdateLayout();
            foreach (var scroll in Elements(this).OfType<ScrollViewer>()) if (scrolls.TryGetValue(PositionKey(scroll), out var value))
            { scroll.ScrollToHorizontalOffset(value.Horizontal); scroll.ScrollToVerticalOffset(value.Vertical); }
        }));
    }
    private static IEnumerable<DependencyObject> Elements(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Elements(child)) yield return nested; }
    }
    // These sections are restored by WorkspaceNavigation, including sections not currently visible.
    private static bool UnitSection(Expander e) => e.DataContext is ViewModels.FieldSectionViewModel<ViewModels.Units.UnitFieldViewModel>;
    private static string PositionKey(DependencyObject element)
    {
        var parts = new List<string>();
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Name.Length: > 0 } named) parts.Add(named.Name);
            else if (current is Expander expander) parts.Add("section:" + (expander.Header is TextBlock title ? title.Text : expander.Header?.ToString()));
            else if (current is FrameworkElement view && view.GetType().Namespace == "WarnoLiteModdingTool.App.Workspaces") parts.Add(view.GetType().Name);
        }
        return string.Join("/", parts);
    }
}
