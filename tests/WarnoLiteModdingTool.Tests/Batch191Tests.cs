using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WarnoLiteModdingTool.App;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static Task ContentCoverage191()
    {
        var rows = Enumerable.Range(0, 100).Select(i => new[] { i < 8 ? 240d : 90d, i >= 8 && i < 16 ? 180d : 60d }).ToArray();
        var target = ResponsiveColumns.ContentTargets(rows, [50, 50]);
        Assert(rows.Count(row => row.Zip(target).All(pair => pair.First <= pair.Second)) >= 90, "交错长值仍保证90%整条显示");
        var sizes = ResponsiveColumns.Allocate(target.Sum(), [40, 40], target, [2, 1]);
        Assert(rows.Count(row => row.Zip(sizes).All(pair => pair.First <= pair.Second)) >= 90, "可用宽度满足目标时完整覆盖");
        var narrow = ResponsiveColumns.Allocate(30, [40, 40], target, [2, 1]);
        Assert(narrow.All(size => size >= 40), "窄窗口保留最小列宽允许横向滚动");
        var outlier = Enumerable.Range(0, 10).Select(i => new[] { i == 9 ? 10000d : 120d }).ToArray();
        Assert(ResponsiveColumns.ContentTargets(outlier, [50])[0] == 120, "极长的10%不拉宽整列");
        Assert(ResponsiveColumns.ContentTargets([], [70])[0] == 70, "空列表采用表头宽度");
        return Task.CompletedTask;
    }

    private sealed class Row191(string name, string country) : INotifyPropertyChanged
    {
        private string _name = name;
        public string Name { get => _name; set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
        public string Country { get; } = country;
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static void Verify191Ui(MainViewModel vm, MainWindow main)
    {
        var rows = new ObservableCollection<Row191>(Enumerable.Range(0, 20).Select(i => new Row191(i < 18 ? "2S31 Vena 机械化支援单位" : new string('W', 200), "捷克斯洛伐克")));
        var view = new ListCollectionView(rows);
        var grid = new DataGrid { ItemsSource = view, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
        grid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "国家", Binding = new Binding("Country"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding("Country"));
        grid.Columns.Add(new DataGridTemplateColumn { Header = "模板", CellTemplate = new DataTemplate { VisualTree = label }, Width = 80 });
        var border = new Border { CornerRadius = new CornerRadius(8), Child = grid };
        RoundedContent.SetEnabled(border, true);
        var window = new Window { Content = border, Width = 920, Height = 400, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); DrainDispatcher(window.Dispatcher);
        Assert(grid.Columns[0].ActualWidth < 700 && grid.Columns[1].ActualWidth >= 100, "真实表格按文本分配，保留中文国家宽度并排除极长例外");
        Assert(grid.Columns[2].ActualWidth >= 85, "文本模板列同样按实际显示内容测量");
        var realized = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
        Assert(realized.ActualHeight >= 38, "真实行高达到38DIP");
        var nameText = (TextBlock)grid.Columns[0].GetCellContent(rows[0]);
        nameText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert(nameText.DesiredSize.Width + 16 <= grid.Columns[0].ActualWidth, "实际文字完整显示含留白");
        var before = grid.Columns.Select(c => c.ActualWidth).ToArray();
        view.SortDescriptions.Add(new SortDescription(nameof(Row191.Name), ListSortDirection.Descending));
        DrainDispatcher(window.Dispatcher);
        Assert(before.Zip(grid.Columns).All(pair => Math.Abs(pair.First - pair.Second.ActualWidth) < 1), "排序不改变宽度");
        view.Filter = item => ((Row191)item).Name.Length > 50;
        DrainDispatcher(window.Dispatcher);
        Assert(grid.Columns[0].ActualWidth > before[0], "筛选集合变化重新测量列宽 before=" + string.Join(",", before) + " after=" + string.Join(",", grid.Columns.Select(c => c.ActualWidth)));
        view.Filter = item => ((Row191)item).Name.Length < 50;
        DrainDispatcher(window.Dispatcher);
        Assert(Math.Abs(grid.Columns[0].ActualWidth - before[0]) < 1, "恢复短条目集合重新分配");
        view.Filter = null;
        foreach (var row in rows.Take(18)) row.Name = "Very long updated unit display name with additional readable details";
        view.Refresh(); DrainDispatcher(window.Dispatcher);
        Assert(grid.Columns[0].ActualWidth > before[0], "条目内容变化使测量缓存失效");
        Assert(border.Clip is RectangleGeometry geometry && geometry.RadiusX == 8, "面板内容沿8DIP圆角裁切");
        window.Close();
        UiText.Current.SetLanguage("zh-CN");
        vm.SelectedModule = vm.Modules.First(m => m.Key == "units");
        main.Width = 1420; main.Height = 860; DrainDispatcher(main.Dispatcher);
        var modules = (ListBox)main.FindName("ModuleList");
        var texts = Descendants191<TextBlock>(modules).ToArray();
        Assert(texts.Any(text => text.Text == vm.Modules[0].Summary && text.IsVisible), "模块备注在导航中可见");
        Assert(((ColumnDefinition)main.FindName("ProjectPaneColumn")).ActualWidth >= 200, "导航为双层文字保留宽度");
        Capture19(main, "191-unit-final");
        main.WindowState = WindowState.Maximized; DrainDispatcher(main.Dispatcher);
        main.WindowState = WindowState.Normal; DrainDispatcher(main.Dispatcher);
        Assert(main.IsVisible, "窗口最大化还原后保持可用");
        if (Environment.GetEnvironmentVariable("WARNO_QA_DESKTOP") == "1") CaptureDesktop191(main);
    }

    private static IEnumerable<T> Descendants191<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants191<T>(child)) yield return descendant;
        }
    }
}
