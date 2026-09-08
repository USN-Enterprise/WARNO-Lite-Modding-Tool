using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.App.Advanced;

public sealed class DiffWindow : Window
{
    private readonly TextBox _before = CodeBox();
    private readonly TextBox _after = CodeBox();
    private string[] _oldLines = [];
    private string[] _newLines = [];
    private int _page;
    private readonly TextBlock _pageText = new();
    public DiffWindow(ApplyPreview preview)
    {
        Width = 1200; Height = 800; MinWidth = 900; MinHeight = 550; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiText.Bind(this, TitleProperty, "原文与差异"); SetResourceReference(BackgroundProperty, "BackgroundBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var dock = new DockPanel { Margin = new Thickness(14) }; Content = dock;
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions);
        Button(actions, "上一页", () => { if (_page > 0) { _page--; Show(); } });
        Button(actions, "下一页", () => { if ((_page + 1) * 160 < Math.Max(_oldLines.Length, _newLines.Length)) { _page++; Show(); } });
        actions.Children.Add(_pageText);
        Button(actions, "应用全部草稿", () => { DialogResult = true; });
        Button(actions, "取消", () => { DialogResult = false; });
        var files = new ComboBox { ItemsSource = preview.Files.Where(f => f.Kind != FormalTextFileKind.Log).ToArray(), DisplayMemberPath = "RelativePath", Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(files, Dock.Top); dock.Children.Add(files);
        var summary = new TextBox { IsReadOnly = true, Height = 120, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join("\n", preview.Operations.Select(o => o.Summary)) + "\n" + string.Join("\n", preview.ValidationMessages) + "\n" + preview.BackupId };
        DockPanel.SetDock(summary, Dock.Top); dock.Children.Add(summary);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(5) }); grid.ColumnDefinitions.Add(new()); dock.Children.Add(grid);
        var left = new GroupBox { Content = _before }; UiText.Bind(left, HeaderedContentControl.HeaderProperty, "修改前"); grid.Children.Add(left);
        var right = new GroupBox { Content = _after }; UiText.Bind(right, HeaderedContentControl.HeaderProperty, "修改后"); Grid.SetColumn(right, 2); grid.Children.Add(right);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(splitter, 1); grid.Children.Add(splitter);
        files.SelectionChanged += (_, _) =>
        {
            if (files.SelectedItem is not PlannedFileChange file) return;
            _oldLines = Lines(file.OriginalBytes); _newLines = Lines(file.CandidateBytes);
            var first = 0; while (first < Math.Min(_oldLines.Length, _newLines.Length) && _oldLines[first] == _newLines[first]) first++;
            _page = Math.Max(0, first - 5) / 160; Show();
        };
        files.SelectedIndex = 0;
    }
    private new void Show()
    {
        _before.Text = Page(_oldLines); _after.Text = Page(_newLines);
        _pageText.Text = $"  {_page + 1} / {Math.Max(1, (Math.Max(_oldLines.Length, _newLines.Length) + 159) / 160)}  ";
    }
    private string Page(string[] lines) => string.Join("\n", lines.Skip(_page * 160).Take(160).Select((line, index) => $"{_page * 160 + index + 1,7}  {line}"));
    private static string[] Lines(byte[] bytes) { using var stream = new MemoryStream(bytes); using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true); return reader.ReadToEnd().Replace("\r\n", "\n").Split('\n'); }
    private static TextBox CodeBox() => new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static void Button(Panel panel, string text, Action action) { var button = new Button { Margin = new Thickness(5), Padding = new Thickness(10, 7, 10, 7) }; UiText.Bind(button, ContentControl.ContentProperty, text); button.Click += (_, _) => action(); panel.Children.Add(button); }
}
