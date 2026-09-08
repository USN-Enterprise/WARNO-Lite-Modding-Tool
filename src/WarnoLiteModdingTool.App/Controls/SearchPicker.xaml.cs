using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Controls;

public partial class SearchPicker : UserControl
{
    private const int ResultLimit = 160;
    private readonly ObservableCollection<SearchPickerEntry> _visible = [];
    private IReadOnlyList<SearchPickerEntry> _cache = [];
    private bool _updating;
    private INotifyCollectionChanged? _observableSource;

    public SearchPicker()
    {
        InitializeComponent();
        ResultList.ItemsSource = _visible;
        MiniFilter.FilterChanged += (_,_) => RefreshResults();
        Loaded += (_, _) => UpdateSelectedText();
        PickerPopup.Opened += (_, _) =>
        {
            SearchBox.Text = string.Empty;
            RefreshResults();
            SearchBox.Focus();
        };
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(SearchPicker),
        new PropertyMetadata(null, (owner, args) => ((SearchPicker)owner).OnItemsSourceChanged(args.OldValue as IEnumerable, args.NewValue as IEnumerable)));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(SearchPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (owner, _) => ((SearchPicker)owner).OnSelectedItemChanged()));

    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath), typeof(string), typeof(SearchPicker), new PropertyMetadata(string.Empty, (owner, _) => ((SearchPicker)owner).RebuildCache()));

    public static readonly DependencyProperty SecondaryMemberPathProperty = DependencyProperty.Register(
        nameof(SecondaryMemberPath), typeof(string), typeof(SearchPicker), new PropertyMetadata(string.Empty, (owner, _) => ((SearchPicker)owner).RebuildCache()));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchPicker), new PropertyMetadata("请选择", (owner, _) => ((SearchPicker)owner).UpdateSelectedText()));

    public bool EnableUnitFilters { get; set; }
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public string DisplayMemberPath { get => (string)GetValue(DisplayMemberPathProperty); set => SetValue(DisplayMemberPathProperty, value); }
    public string SecondaryMemberPath { get => (string)GetValue(SecondaryMemberPathProperty); set => SetValue(SecondaryMemberPathProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    private void RebuildCache()
    {
        if (MiniFilter is not null) { MiniFilter.Visibility = EnableUnitFilters ? Visibility.Visible : Visibility.Collapsed; MiniFilter.ItemsSource = EnableUnitFilters ? ItemsSource : null; }
        _cache = ItemsSource?.Cast<object>().Select(item =>
        {
            var primary = Read(item, DisplayMemberPath);
            var secondary = Read(item, SecondaryMemberPath);
            return new SearchPickerEntry(item, primary, secondary, $"{primary}\n{secondary}\n{item}".ToUpperInvariant());
        }).ToArray() ?? [];
        UpdateSelectedText();
        RefreshResults();
    }

    private void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (_observableSource is not null)
        {
            _observableSource.CollectionChanged -= Source_CollectionChanged;
        }

        _observableSource = newValue as INotifyCollectionChanged;
        if (_observableSource is not null)
        {
            _observableSource.CollectionChanged += Source_CollectionChanged;
        }
        RebuildCache();
    }

    private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildCache();

    private void RefreshResults()
    {
        var query = SearchBox.Text.Trim().ToUpperInvariant();
        var matches = (query.Length == 0 ? _cache : _cache.Where(item => item.SearchKey.Contains(query, StringComparison.Ordinal)))
            .Where(item => !EnableUnitFilters || MiniFilter.Matches(item.Item))
            .Take(ResultLimit)
            .ToArray();
        _updating = true;
        try
        {
            _visible.Clear();
            foreach (var item in matches)
            {
                _visible.Add(item);
            }
        }
        finally
        {
            _updating = false;
        }

        ResultHint.Text = _cache.Count > matches.Length
            ? $"显示前 {matches.Length} 项；输入任意片段继续缩小范围"
            : $"{matches.Length} 项；支持名称任意片段匹配";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshResults();

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || ResultList.SelectedItem is not SearchPickerEntry entry)
        {
            return;
        }

        SelectedItem = entry.Item;
        OpenButton.IsChecked = false;
    }

    public event EventHandler? SelectedItemChanged;
    private void OnSelectedItemChanged() { UpdateSelectedText(); SelectedItemChanged?.Invoke(this, EventArgs.Empty); }

    private void UpdateSelectedText()
    {
        if (SelectedText is null)
        {
            return;
        }

        SelectedText.Text = SelectedItem is null ? Localisation.UiText.T(Placeholder) : Read(SelectedItem, DisplayMemberPath);
    }

    private static string Read(object item, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return item.ToString() ?? string.Empty;
        }

        return item.GetType().GetProperty(path, BindingFlags.Instance | BindingFlags.Public)?.GetValue(item)?.ToString() ?? string.Empty;
    }

    private sealed record SearchPickerEntry(object Item, string Primary, string Secondary, string SearchKey)
    {
        public bool HasSecondary => Secondary.Length > 0 && !string.Equals(Primary, Secondary, StringComparison.Ordinal);
    }
}
