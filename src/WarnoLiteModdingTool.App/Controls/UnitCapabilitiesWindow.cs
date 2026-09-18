using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class UnitCapabilitiesWindow : Window
{
    public UnitCapabilityState State { get; private set; }
    private readonly UnitProjectGraph _graph;
    private readonly UnitRecord _unit;
    private readonly IReadOnlyList<UnitCapabilityChoice> _choices;
    private readonly StackPanel _traits = new();
    private readonly ListBox _skills = new() { MinHeight = 100, MaxHeight = 230 };
    private readonly TextBlock _icons = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };

    public UnitCapabilitiesWindow(UnitRecord unit, UnitProjectGraph graph, UnitCapabilityState state)
    {
        _unit = unit; _graph = graph; State = state; _choices = UnitCapabilities.Choices(graph, unit.Source.RelativeSourceFile);
        Title = UiText.T("特性与实际能力") + " · " + unit.DisplayName;
        Width = 900; Height = 780; MinWidth = 660; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        var root = new DockPanel { Margin = new(18) }; Content = root;
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); bottom.Children.Add(_error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; bottom.Children.Add(actions);
        AddButton(actions, "保存草稿", () => { Validate(); DialogResult = true; }); AddButton(actions, "取消", Close);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll);
        var content = new StackPanel(); scroll.Content = content;
        content.Children.Add(new TextBlock { Text = UiText.T("配套状态描述配置，不代表游戏内实时生效。修改只影响当前单位，共享能力数值保持。"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        content.Children.Add(_traits);
        content.Children.Add(new TextBlock { Text = UiText.T("显示标签"), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 14, 0, 8) });
        content.Children.Add(_icons);
        AddButton(content, "仅修改显示标签", () =>
        {
            var choices = unit.Field("structure.specialties")?.Choices.Select(c => (NdfSyntaxDocument.Unquote(c.RawValue), c.Display)) ?? [];
            var picker = new UnitSelectionWindow(choices, State.Specialties) { Owner = this, Title = UiText.T("显示标签") };
            if (picker.ShowDialog() == true) { State = State with { Specialties = picker.SelectedIds.ToArray() }; Refresh(); }
        });
        if (Advanced.EditorMode.IsAdvanced)
        {
            content.Children.Add(new TextBlock { Text = UiText.T("实际能力"), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 14, 0, 8) });
            var picker = new SearchPicker { ItemsSource = _choices, DisplayMemberPath = "Name", SecondaryMemberPath = "Reference", Placeholder = UiText.T("搜索能力或完整引用") }; content.Children.Add(picker);
            var skillActions = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(skillActions);
            AddButton(skillActions, "添加能力", () =>
            {
                if (picker.SelectedItem is not UnitCapabilityChoice choice) throw new InvalidOperationException(UiText.T("请选择能力"));
                UnitCapabilities.ValidateReference(graph, unit.Source.RelativeSourceFile, choice.Reference);
                if (!State.Skills.Any(s => UnitProjectGraph.Same(graph.Resolve(unit.Source.RelativeSourceFile, s), graph.Resolve(unit.Source.RelativeSourceFile, choice.Reference)!)))
                    State = State with { Skills = State.Skills.Append(choice.Reference).ToArray() };
                Refresh();
            });
            AddButton(skillActions, "替换能力", () =>
            {
                if (_skills.SelectedItem is not string old || picker.SelectedItem is not UnitCapabilityChoice choice) throw new InvalidOperationException(UiText.T("请选择已有能力和替代能力"));
                UnitCapabilities.ValidateReference(graph, unit.Source.RelativeSourceFile, choice.Reference);
                if (State.Skills.Contains(choice.Reference) && old != choice.Reference) throw new InvalidOperationException(UiText.T("目标能力已存在"));
                State = State with { Skills = State.Skills.Select(s => s == old ? choice.Reference : s).ToArray() }; Refresh();
            });
            AddButton(skillActions, "移除能力", () => { if (_skills.SelectedItem is string value) { State = State with { Skills = State.Skills.Where(s => s != value).ToArray() }; Refresh(); } });
            AddButton(skillActions, "清空能力", () => { State = State with { Skills = [] }; Refresh(); });
            content.Children.Add(_skills);
            _skills.SelectionChanged += (_, _) => ShowDetail(_skills.SelectedItem as string);
            picker.SelectedItemChanged += (_, _) => { if (picker.SelectedItem is UnitCapabilityChoice choice) ShowDetail(choice.Reference); };
            content.Children.Add(new Expander { Header = UiText.T("当前Mod能力参数与条件"), Content = _details, Margin = new(0, 10, 0, 0) });
        }
        else content.Children.Add(new Expander { Header = UiText.T("实际能力摘要"), Content = _skills, Margin = new(0, 12, 0, 0) });
        Refresh();
    }
    private void AddButton(Panel parent, string label, Action action)
    {
        var button = new Button { Content = UiText.T(label), Margin = new(0, 5, 8, 5), Padding = new(10, 6, 10, 6) };
        button.Click += (_, _) => { try { _error.Text = ""; action(); } catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { _error.Text = UiText.T(ex.Message); } }; parent.Children.Add(button);
    }
    private void ShowDetail(string? reference)
    {
        var choice = _choices.FirstOrDefault(c => c.Reference == reference);
        _details.Text = choice is null ? reference : choice.Reference + "\n" + choice.Error + "\n" + choice.Summary;
    }
    private void Refresh()
    {
        _traits.Children.Clear();
        foreach (var trait in UnitCapabilities.Traits)
        {
            var row = new DockPanel { Margin = new(0, 1, 0, 1) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
            AddButton(buttons, "添加/补齐", () => { State = UnitCapabilities.Toggle(State, trait, true, _graph, _unit.Source.RelativeSourceFile); Refresh(); });
            AddButton(buttons, "移除", () => { State = UnitCapabilities.Toggle(State, trait, false, _graph, _unit.Source.RelativeSourceFile); Refresh(); });
            var facts = trait.Skills.Select(name => _choices.Where(c => c.Name == "Capacite_" + name).ToArray()).Select(matches => matches.Length == 1 ? matches[0].Reference + "\n" + matches[0].Error + "\n" + matches[0].Summary : UiText.T("当前Mod未提供唯一能力定义"));
            row.Children.Add(new Expander { Header = new TextBlock { Text = UiText.T(trait.Label) + " · " + UiText.T(UnitCapabilities.Status(State, trait, _graph, _unit.Source.RelativeSourceFile)), TextWrapping = TextWrapping.Wrap }, Content = new TextBlock { Text = string.Join("\n\n", facts), TextWrapping = TextWrapping.Wrap, Margin = new(18, 6, 8, 8) }, VerticalAlignment = VerticalAlignment.Center });
            _traits.Children.Add(row);
        }
        _skills.ItemsSource = State.Skills;
        _icons.Text = string.Join(", ", State.Specialties);
    }
    private void Validate()
    {
        var body = UnitCreation.Source(_unit); var baseline = UnitCapabilities.FromBody(body);
        foreach (var reference in State.Skills.Except(baseline.Skills)) UnitCapabilities.ValidateReference(_graph, _unit.Source.RelativeSourceFile, reference);
        if (!State.Skills.SequenceEqual(baseline.Skills)) UnitCapabilities.ValidateModuleScope(body, _graph, _unit.Source.RelativeSourceFile);
        _ = UnitCapabilities.Apply(body, baseline, State);
    }
}
