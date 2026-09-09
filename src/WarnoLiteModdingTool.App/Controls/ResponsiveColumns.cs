using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class ResponsiveColumns : DependencyObject
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ResponsiveColumns), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void Changed(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not DataGrid grid) return;
        if ((bool)args.NewValue)
        {
            var state = new LayoutState(grid);
            grid.Loaded += state.Loaded;
            grid.Unloaded += state.Unloaded;
            state.Loaded(grid, new RoutedEventArgs());
        }
    }

    private sealed class LayoutState(DataGrid grid)
    {
        private string _last = "";
        private bool _layingOut;
        private readonly Dictionary<DataGridColumn, double> _weights = [];
        public void Loaded(object sender, RoutedEventArgs e)
        {
            grid.LayoutUpdated -= Updated;
            grid.LayoutUpdated += Updated;
            _last = "";
        }
        public void Unloaded(object sender, RoutedEventArgs e) => grid.LayoutUpdated -= Updated;

        private void Updated(object? sender, EventArgs args)
        {
            if (_layingOut || !grid.IsVisible || grid.ActualWidth <= 0) return;
            var columns = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToArray();
            if (columns.Length == 0) return;
            var labels = columns.Select(c => HeaderText(c.Header)).ToArray();
            var available = Math.Max(0, grid.ActualWidth - 24 - grid.RowHeaderActualWidth);
            var signature = available.ToString("F1", CultureInfo.InvariantCulture) + ":" + grid.FontSize + ":" + string.Join("|", labels);
            if (_last == signature) return;
            _last = signature;
            _layingOut = true;
            try
            {
                var minima = new double[columns.Length];
                var preferred = new double[columns.Length];
                var weights = new double[columns.Length];
                for (var i = 0; i < columns.Length; i++)
                {
                    var column = columns[i];
                    var label = labels[i];
                    if (!_weights.TryGetValue(column, out var weight))
                    {
                        weight = column.Width.IsStar ? Math.Max(1, column.Width.Value) : 0;
                        _weights[column] = weight;
                    }
                    weights[i] = weight;
                    if (label.Length == 0)
                    {
                        minima[i] = preferred[i] = Math.Max(36, column.Width.IsAbsolute ? Math.Min(80, column.Width.Value) : 40);
                        weights[i] = 0;
                    }
                    else
                    {
                        minima[i] = Math.Ceiling(Measure(label.Length < 2 ? label : "汉字", grid) + 38);
                        preferred[i] = Math.Max(minima[i], Math.Ceiling(Measure(label, grid) + 38));
                    }
                    column.MinWidth = minima[i];
                }
                var sizes = Allocate(available, minima, preferred, weights);
                for (var i = 0; i < columns.Length; i++) columns[i].Width = new DataGridLength(sizes[i]);
                foreach (var header in Descendants<DataGridColumnHeader>(grid))
                    if (header.Column is not null) header.ToolTip = HeaderText(header.Column.Header);
            }
            finally { _layingOut = false; }
        }
    }

    public static double[] Allocate(double available, double[] minima, double[] preferred, double[] weights)
    {
        var sizes = minima.ToArray();
        var remaining = Math.Max(0, available - minima.Sum());
        var needed = preferred.Zip(minima, (p, m) => Math.Max(0, p - m)).Sum();
        var fraction = needed == 0 ? 0 : Math.Min(1, remaining / needed);
        for (var i = 0; i < sizes.Length; i++) sizes[i] += Math.Max(0, preferred[i] - minima[i]) * fraction;
        remaining = Math.Max(0, available - sizes.Sum());
        var totalWeight = weights.Sum();
        if (totalWeight > 0)
            for (var i = 0; i < sizes.Length; i++) sizes[i] += remaining * weights[i] / totalWeight;
        return sizes;
    }

    private static double Measure(string text, Control grid) => new FormattedText(text,
        CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(grid.FontFamily, grid.FontStyle, grid.FontWeight, grid.FontStretch),
        grid.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(grid).PixelsPerDip).WidthIncludingTrailingWhitespace;

    private static string HeaderText(object? header) => header switch
    {
        TextBlock text => text.Text,
        Panel panel => string.Join(" ", panel.Children.Cast<UIElement>().Where(c => c is not ParameterNote).Select(HeaderText)),
        ContentControl content => HeaderText(content.Content),
        null => "",
        _ => header.ToString() ?? ""
    };

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
