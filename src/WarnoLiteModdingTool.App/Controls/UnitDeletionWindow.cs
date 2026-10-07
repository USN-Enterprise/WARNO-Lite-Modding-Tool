using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class UnitDeletionWindow : Window
{
    private readonly UnitRecord _unit;
    private readonly UnitWorkspaceData _units;
    private readonly UnitProjectGraph _graph;
    private readonly Func<DraftOperation, Task> _save;
    private readonly Dictionary<string, (CheckBox Keep, SearchPicker? Picker)> _rows = [];
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly Button _submit = new() { Margin = new(8, 0, 0, 0) };
    public CheckBox Force { get; } = new();
    public CheckBox Acknowledge { get; } = new();
    public (string Path, int Offset)? OpenRequested { get; private set; }
    public UnitDeleteAnalysis Analysis { get; }

    public UnitDeletionWindow(UnitRecord unit, UnitWorkspaceData units, UnitProjectGraph graph, Func<DraftOperation, Task> save)
    {
        _unit = unit; _units = units; _graph = graph; _save = save;
        Analysis = UnitDeletionV2.Analyze(graph, graph.RequireObject(unit.Source.RelativeSourceFile, unit.Name));
        Title = UiText.T("删除单位"); Width = 880; Height = 740; MinWidth = 600; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        var root = new DockPanel { Margin = new(18) }; Content = root;
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        Acknowledge.Content = Text("已了解残留引用和未验证范围，仍保存强行删除草稿");
        bottom.SizeChanged += (_, _) => ((TextBlock)Acknowledge.Content).MaxWidth = Math.Max(120, bottom.ActualWidth - 36);
        Acknowledge.Margin = new(0, 8, 0, 10); bottom.Children.Add(Acknowledge); bottom.Children.Add(_error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var cancel = new Button { Content = UiText.T("取消") }; cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(cancel); actions.Children.Add(_submit); bottom.Children.Add(actions);
        _submit.SetResourceReference(StyleProperty, "PrimaryButton");
        var content = new StackPanel(); root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Heading(content, "删除目标"); content.Children.Add(Text(unit.DisplayName));
        content.Children.Add(Text(unit.Name + "\n" + unit.Source.RelativeSourceFile, raw: true, muted: true));
        content.Children.Add(Text("可删除任意来源单位；保存草稿后仍需在应用预览中提交。共享武器、弹药、模型和图片保留。"));
        Force.Content = Text("强行删除（允许保留未解决引用）"); Force.Margin = new(0, 12, 0, 10);
        Force.Visibility = Advanced.EditorMode.IsAdvanced ? Visibility.Visible : Visibility.Collapsed; content.Children.Add(Force);
        var warning = Text("残留引用可能导致生成或游戏加载失败。正式应用前创建备份，恢复会影响整份文件。");
        warning.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush"); content.Children.Add(warning);
        Heading(content, "引用处置");
        content.Children.Add(Text(UiText.T("引用数量") + " · " + Analysis.Uses.Count, raw: true));
        foreach (var use in Analysis.Uses)
        {
            var panel = new StackPanel(); var card = new Border { Child = panel, Padding = new(12), Margin = new(0, 0, 0, 8) };
            card.SetResourceReference(StyleProperty, "CardBorder"); content.Children.Add(card);
            panel.Children.Add(Text(use.Description, raw: true)); panel.Children.Add(Text(use.File, raw: true, muted: true));
            var controls = new WrapPanel { Margin = new(0, 6, 0, 0) }; panel.Children.Add(controls);
            var keep = new CheckBox { Content = Text("保留引用"), Margin = new(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(keep);
            var locate = new Button { Content = UiText.T("在工作台定位") }; controls.Children.Add(locate);
            locate.Click += (_, _) => { OpenRequested = (use.File, use.Offset); DialogResult = false; };
            SearchPicker? picker = null;
            if (use.Automatic) panel.Children.Add(Text("默认联动清理", muted: true));
            else if (use.CanReplace)
            {
                picker = new SearchPicker { ItemsSource = units.Units.Where(u => u.Name != unit.Name && (use.Field != "Transport" || u.HasUniqueTransporterModule)), DisplayMemberPath = "DisplayName", SecondaryMemberPath = "Name", Placeholder = UiText.T("选择替代单位"), Margin = new(0, 6, 0, 0) };
                picker.SelectedItemChanged += (_, _) => Acknowledge.IsChecked = false;
                panel.Children.Add(picker);
            }
            else panel.Children.Add(Text("此引用不能自动处理；普通删除需先解除，强行删除将保留原文。", muted: true));
            _rows[use.Key] = (keep, picker);
            keep.Checked += (_, _) => { Acknowledge.IsChecked = false; if (picker is not null) picker.IsEnabled = false; };
            keep.Unchecked += (_, _) => { Acknowledge.IsChecked = false; if (picker is not null) picker.IsEnabled = true; };
        }
        if (Analysis.Coverage.Length > 0)
        { Heading(content, "未验证范围"); foreach (var issue in Analysis.Coverage) content.Children.Add(Text(issue, raw: true)); }
        _submit.Click += async (_, _) => await SaveAsync();
        Force.Checked += (_, _) => UpdateMode(); Force.Unchecked += (_, _) => UpdateMode();
        UpdateMode();
    }
    private static TextBlock Text(string text, bool raw = false, bool muted = false)
    {
        var block = new TextBlock { Text = raw ? text : UiText.T(text), TextWrapping = TextWrapping.Wrap, Margin = new(0, 3, 0, 3) };
        if (muted) block.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        return block;
    }
    private static void Heading(Panel parent, string text)
    { var block = Text(text); block.ClearValue(FrameworkElement.MarginProperty); block.SetResourceReference(FrameworkElement.StyleProperty, "EditorSectionHeading"); parent.Children.Add(block); }
    private void UpdateMode()
    {
        var force = Force.IsChecked == true; Acknowledge.IsChecked = false;
        Acknowledge.Visibility = force ? Visibility.Visible : Visibility.Collapsed;
        _submit.Content = UiText.T(force ? "保存强行删除草稿" : "保存删除草稿");
        foreach (var row in _rows.Values) { row.Keep.IsEnabled = force; if (!force) row.Keep.IsChecked = false; }
    }
    public async Task SaveAsync()
    {
        _submit.IsEnabled = false; _error.Text = "";
        try
        {
            var force = Force.IsChecked == true;
            if (force && Acknowledge.IsChecked != true) throw new InvalidOperationException(UiText.T("请先核对残留引用和未验证范围"));
            var choices = new Dictionary<string, string>();
            foreach (var use in Analysis.Uses)
            {
                var row = _rows[use.Key];
                if (row.Keep.IsChecked == true) choices[use.Key] = UnitDeletionV2.Keep;
                else if (row.Picker?.SelectedItem is UnitRecord target) choices[use.Key] = _graph.ReferenceTo(use.File, _graph.RequireObject(target.Source.RelativeSourceFile, target.Name));
            }
            await _save(UnitDeletionV2.Operation(_unit, _graph, force, choices)); DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { _error.Text = UiText.T(ex.Message); }
        finally { _submit.IsEnabled = true; }
    }
}
