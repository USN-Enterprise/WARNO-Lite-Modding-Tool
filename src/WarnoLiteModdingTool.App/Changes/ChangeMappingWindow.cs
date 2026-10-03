using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.App.Changes;

public sealed class ChangeMappingWindow : Window
{
    private readonly IReadOnlyList<ChangeObjectTarget> _sources, _targets;
    private readonly ChangeFileMapping? _existing;
    private readonly ComboBox _file = new() { MinWidth = 240, Tag = "change-map-file" };
    private readonly StackPanel _rows = new();
    private readonly Dictionary<string, ComboBox> _objects = new();
    private readonly Button _accept = new() { Tag = "change-map-accept" };
    public ChangeFileMapping? Result { get; private set; }
    private static string L(string zh, string en) => UiText.Current.English ? en : zh;
    public ChangeMappingWindow(IReadOnlyList<ChangeObjectTarget> sources, IReadOnlyList<ChangeObjectTarget> targets, ChangeFileMapping? existing = null)
    {
        _sources = sources; _targets = targets; _existing = existing;
        Title = L("选择新版对应对象", "Choose corresponding target objects"); Width = 820; Height = 560; MinWidth = 670; MinHeight = 430; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new(18) }; root.SetResourceReference(Panel.BackgroundProperty, "SurfaceBrush"); Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        _accept.Content = L("使用对应并重新预览", "Use mapping and preview"); _accept.SetResourceReference(StyleProperty, "SecondaryButton"); _accept.IsEnabled = false;
        _accept.Click += (_, _) => { Result = new(_sources[0].Path, (string)_file.SelectedItem, _objects.ToDictionary(p => p.Key, p => (string)p.Value.SelectedItem)); DialogResult = true; }; actions.Children.Add(_accept);
        var cancel = new Button { Content = L("取消", "Cancel"), IsCancel = true, Margin = new(8, 0, 0, 0) }; cancel.SetResourceReference(StyleProperty, "SecondaryButton"); actions.Children.Add(cancel);
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var heading = new TextBlock { Text = Title, Margin = new(0, 0, 0, 8) }; heading.SetResourceReference(StyleProperty, "RuleObjectHeading"); top.Children.Add(heading);
        var note = new TextBlock { Text = L("只对应已知数值修改。请选择同类型的新对象，随后核对四方数值和共享影响；类型相同不代表含义一定相同。", "Only known quantity changes can be mapped. Select same-type targets, then review all four values and shared effects; matching types alone do not establish equivalent meaning."), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        note.SetResourceReference(ForegroundProperty, "MutedTextBrush"); top.Children.Add(note);
        top.Children.Add(new TextBlock { Text = L("记录文件：", "Recorded file: ") + sources[0].Path, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) });
        top.Children.Add(new TextBlock { Text = L("新版目标文件", "Target file"), Margin = new(0, 0, 0, 4) }); top.Children.Add(_file);
        _file.ItemsSource = targets.GroupBy(t => t.Path).Where(g => sources.All(s => g.Any(t => t.Type == s.Type))).Select(g => g.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        _file.SelectionChanged += (_, _) => Populate();
        root.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 12, 0, 0) });
        _file.SelectedItem = existing?.TargetPath ?? sources[0].Path;
        if (_file.SelectedIndex < 0) Populate();
    }
    private void Populate()
    {
        _rows.Children.Clear(); _objects.Clear(); _accept.IsEnabled = false;
        foreach (var source in _sources)
        {
            var panel = new StackPanel { Margin = new(0, 0, 0, 12) };
            panel.Children.Add(new TextBlock { Text = source.Name + " · " + source.Type, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 4) });
            var choices = new ComboBox { Tag = "change-map-object", ItemsSource = _targets.Where(t => t.Path == (string?)_file.SelectedItem && t.Type == source.Type).Select(t => t.Name).ToArray() };
            _objects.Add(source.Name, choices); choices.SelectionChanged += (_, _) => Validate();
            choices.SelectedItem = _existing?.Objects.GetValueOrDefault(source.Name) ?? source.Name; panel.Children.Add(choices); _rows.Children.Add(panel);
        }
        Validate();
    }
    private void Validate() => _accept.IsEnabled = _file.SelectedItem is string && _objects.Count == _sources.Count && _objects.Values.All(c => c.SelectedItem is string) &&
        _objects.Values.Select(c => (string)c.SelectedItem).Distinct(StringComparer.Ordinal).Count() == _objects.Count;
}
