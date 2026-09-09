using System.Globalization;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Localisation;

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
        private bool _queued;
        private string _contentSignature = "";
        private readonly Dictionary<object, double[]> _measurements = new();
        private readonly HashSet<INotifyPropertyChanged> _observed = [];
        private readonly Dictionary<DataGridColumn, double> _weights = [];
        public void Loaded(object sender, RoutedEventArgs e)
        {
            grid.LayoutUpdated -= Updated;
            grid.LayoutUpdated += Updated;
            ((INotifyCollectionChanged)grid.Items).CollectionChanged -= ItemsChanged;
            ((INotifyCollectionChanged)grid.Items).CollectionChanged += ItemsChanged;
            _last = "";
        }
        public void Unloaded(object sender, RoutedEventArgs e)
        {
            grid.LayoutUpdated -= Updated;
            ((INotifyCollectionChanged)grid.Items).CollectionChanged -= ItemsChanged;
            foreach (var item in _observed) item.PropertyChanged -= ItemChanged;
            _observed.Clear(); _measurements.Clear();
        }

        private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) { _last = ""; Queue(); }
        private void ItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not null) _measurements.Remove(sender);
            _last = ""; Queue();
        }
        private void Queue()
        {
            if (_queued) return;
            _queued = true;
            grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _queued = false; Layout(); }));
        }

        private string Signature(DataGridColumn[] columns) => grid.FontFamily + ":" + grid.FontSize + ":" + VisualTreeHelper.GetDpi(grid).PixelsPerDip + ":" + UiText.Current.Version + ":" + string.Join("|", columns.Select(c => c.GetHashCode() + ":" + HeaderText(c.Header)));
        private void Updated(object? sender, EventArgs args)
        {
            if (!grid.IsVisible || _layingOut) return;
            var columns = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToArray();
            var available = Math.Max(0, grid.ActualWidth - 24 - grid.RowHeaderActualWidth);
            if (_last != available.ToString("F1", CultureInfo.InvariantCulture) + ":" + Signature(columns)) Queue();
        }
        private void Layout()
        {
            if (_layingOut || !grid.IsVisible || grid.ActualWidth <= 0 || System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) return;
            var columns = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToArray();
            if (columns.Length == 0) return;
            var labels = columns.Select(c => HeaderText(c.Header)).ToArray();
            var available = Math.Max(0, grid.ActualWidth - 24 - grid.RowHeaderActualWidth);
            var contentSignature = Signature(columns);
            if (_contentSignature != contentSignature) { _measurements.Clear(); _contentSignature = contentSignature; }
            var signature = available.ToString("F1", CultureInfo.InvariantCulture) + ":" + contentSignature;
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
                var items = grid.Items.Cast<object>().Where(item => item != CollectionView.NewItemPlaceholder).ToArray();
                var current = items.ToHashSet();
                foreach (var old in _observed.Where(item => !current.Contains(item)).ToArray()) { old.PropertyChanged -= ItemChanged; _observed.Remove(old); }
                foreach (var old in _measurements.Keys.Where(item => !current.Contains(item)).ToArray()) _measurements.Remove(old);
                foreach (var item in items.OfType<INotifyPropertyChanged>()) if (_observed.Add(item)) item.PropertyChanged += ItemChanged;
                // One reusable measuring element per column; never realize every DataGrid row.
                var probes = columns.Select(column => CreateProbe(column, grid)).ToArray();
                var bindings = columns.Select((column, i) => column is DataGridTextColumn text ? text.Binding :
                    probes[i] is TextBlock label ? BindingOperations.GetBindingBase(label, TextBlock.TextProperty) : null).ToArray();
                foreach (var item in items)
                {
                    if (_measurements.ContainsKey(item)) continue;
                    var row = new double[columns.Length];
                    for (var i = 0; i < columns.Length; i++)
                    {
                        var probe = probes[i];
                        if (probe is null) { row[i] = preferred[i]; continue; }
                        probe.DataContext = item;
                        if (probe is TextBlock text && bindings[i] is { } binding)
                            BindingOperations.SetBinding(text, TextBlock.TextProperty, WithSource(binding, item));
                        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        row[i] = Math.Ceiling(probe.DesiredSize.Width + 18);
                    }
                    _measurements[item] = row;
                }
                foreach (var probe in probes) if (probe is not null) { probe.DataContext = null; BindingOperations.ClearAllBindings(probe); }
                preferred = ContentTargets(items.Select(item => _measurements[item]).ToArray(), preferred);
                var sizes = Allocate(available, minima, preferred, weights);
                for (var i = 0; i < columns.Length; i++) columns[i].Width = new DataGridLength(sizes[i]);
                foreach (var header in Descendants<DataGridColumnHeader>(grid))
                    if (header.Column is not null) header.ToolTip = HeaderText(header.Column.Header);
            }
            finally { _layingOut = false; }
        }
    }

    private static FrameworkElement? CreateProbe(DataGridColumn column, DataGrid grid)
    {
        if (column is DataGridTextColumn textColumn)
        {
            if (textColumn.ElementStyle is null || textColumn.ElementStyle == DataGridTextColumn.DefaultElementStyle)
            {
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
                style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0)));
                style.Setters.Add(new Setter(TextBlock.FontSizeProperty, new Binding(nameof(grid.FontSize)) { Source = grid }));
                style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new Binding(nameof(grid.FontFamily)) { Source = grid }));
                style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
                style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Text") { RelativeSource = RelativeSource.Self }));
                textColumn.ElementStyle = style;
            }
            var text = new TextBlock { FontFamily = grid.FontFamily, FontSize = grid.FontSize, FontWeight = grid.FontWeight };
            if (textColumn.ElementStyle is not null)
            {
                text.ClearValue(TextBlock.FontFamilyProperty); text.ClearValue(TextBlock.FontSizeProperty); text.ClearValue(TextBlock.FontWeightProperty);
                text.Style = textColumn.ElementStyle;
            }
            if (textColumn.Binding is not null) BindingOperations.SetBinding(text, TextBlock.TextProperty, textColumn.Binding);
            return text;
        }
        return column is DataGridTemplateColumn template ? template.CellTemplate?.LoadContent() as FrameworkElement : null;
    }

    private static BindingBase WithSource(BindingBase original, object item)
    {
        if (original is Binding binding)
            return new Binding
            {
                Path = binding.Path, Source = binding.Source ?? item, Mode = BindingMode.OneWay,
                Converter = binding.Converter, ConverterParameter = binding.ConverterParameter,
                ConverterCulture = binding.ConverterCulture, StringFormat = binding.StringFormat,
                TargetNullValue = binding.TargetNullValue, FallbackValue = binding.FallbackValue
            };
        if (original is MultiBinding multi)
        {
            var copy = new MultiBinding { Converter = multi.Converter, ConverterParameter = multi.ConverterParameter,
                ConverterCulture = multi.ConverterCulture, StringFormat = multi.StringFormat, Mode = BindingMode.OneWay };
            foreach (var child in multi.Bindings) copy.Bindings.Add(WithSource(child, item));
            return copy;
        }
        return original;
    }

    // Select a shared set of rows: independent per-column percentiles can leave <90% of rows readable.
    public static double[] ContentTargets(double[][] rows, double[] headers)
    {
        var target = headers.ToArray();
        if (rows.Length == 0) return target;
        var count = (int)Math.Ceiling(rows.Length * .9);
        var ranked = rows.OrderBy(row => row.Select((value, i) => Math.Max(value, headers[i])).Sum()).ToArray();
        for (var r = 0; r < count; r++)
            for (var c = 0; c < target.Length; c++) target[c] = Math.Max(target[c], ranked[r][c]);
        return target;
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
