using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.App.ViewModels.Units;
using WarnoLiteModdingTool.App.ViewModels.Weapons;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.Advanced;

public sealed class AdvancedWindow : Window
{
    private readonly MainViewModel _main;
    private readonly ListBox _objects = new() { DisplayMemberPath = "Name" };
    private readonly ListBox _references = new() { DisplayMemberPath = "Name" };
    private readonly ListBox _users = new() { DisplayMemberPath = "Name" };
    private readonly TextBox _source = CodeBox();
    private readonly TextBox _raw = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _fields = new() { DisplayMemberPath = "Label" };
    private Dictionary<string, NdfObjectInfo> _catalog = new();
    private readonly Dictionary<string, string[]> _edges = new();
    private readonly Dictionary<string, List<string>> _reverse = new();
    private readonly Dictionary<string, string> _sources = new();
    private IReadOnlyList<RawField> _fieldItems = [];
    public AdvancedWindow(MainViewModel main)
    {
        _main = main; Width = 1100; Height = 740; MinWidth = 850; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiText.Bind(this, TitleProperty, "引用工作台"); SetResourceReference(BackgroundProperty, "BackgroundBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var tabs = new TabControl { Margin = new Thickness(14) }; Content = tabs;
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(280) }); grid.ColumnDefinitions.Add(new());
        var left = new DockPanel { Margin = new Thickness(8) }; var search = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        search.TextChanged += (_, _) => _objects.ItemsSource = _catalog.Values.Where(o => o.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Take(160).ToArray();
        DockPanel.SetDock(search, Dock.Top); left.Children.Add(search); left.Children.Add(_objects); grid.Children.Add(left);
        var right = new Grid(); Grid.SetColumn(right, 1); grid.Children.Add(right);
        right.RowDefinitions.Add(new() { Height = new GridLength(180) }); right.RowDefinitions.Add(new());
        var relations = new Grid(); relations.ColumnDefinitions.Add(new()); relations.ColumnDefinitions.Add(new()); right.Children.Add(relations);
        var outgoing = Group("引用目标", _references); var incoming = Group("引用者", _users); Grid.SetColumn(incoming, 1); relations.Children.Add(outgoing); relations.Children.Add(incoming);
        Grid.SetRow(_source, 1); right.Children.Add(_source);
        _objects.SelectionChanged += (_, _) => ShowObject();
        _objects.MouseDoubleClick += (_, _) => Navigate(_objects.SelectedItem as NdfObjectInfo);
        foreach (var list in new[] { _references, _users }) list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is NdfObjectInfo target) { _objects.ItemsSource = new[] { target }; _objects.SelectedItem = target; Navigate(target); }
        };
        tabs.Items.Add(Tab("引用工作台", grid));
        var fieldsGrid = new Grid(); fieldsGrid.ColumnDefinitions.Add(new() { Width = new GridLength(340) }); fieldsGrid.ColumnDefinitions.Add(new());
        var fieldLeft = new DockPanel { Margin = new Thickness(8) }; var fieldSearch = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        fieldSearch.TextChanged += (_, _) => _fields.ItemsSource = _fieldItems.Where(f => f.Label.Contains(fieldSearch.Text, StringComparison.OrdinalIgnoreCase) || f.Path.Contains(fieldSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        DockPanel.SetDock(fieldSearch, Dock.Top); fieldLeft.Children.Add(fieldSearch); fieldLeft.Children.Add(_fields); fieldsGrid.Children.Add(fieldLeft);
        var fieldRight = new StackPanel { Margin = new Thickness(15) }; Grid.SetColumn(fieldRight, 1); fieldsGrid.Children.Add(fieldRight);
        var path = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        _fields.SelectionChanged += (_, _) => { if (_fields.SelectedItem is RawField field) { path.Text = field.Path; _raw.Text = field.Read(); } };
        fieldRight.Children.Add(path); fieldRight.Children.Add(_raw);
        var apply = new Button { Margin = new Thickness(0, 10, 0, 10), Padding = new Thickness(12, 7, 12, 7) }; UiText.Bind(apply, ContentControl.ContentProperty, "加入草稿");
        apply.Click += async (_, _) =>
        {
            if (_fields.SelectedItem is not RawField field) return;
            try { await field.Write(_raw.Text); _error.Text = UiText.T("草稿已保存"); }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException) { _error.Text = ex.Message; }
        };
        fieldRight.Children.Add(apply); fieldRight.Children.Add(_error); tabs.Items.Add(Tab("字段搜索", fieldsGrid));
        Loaded += async (_, _) =>
        {
            try
            {
                LoadFields();
                _catalog = _main.ProjectObjects.GroupBy(o => o.Name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
                await Task.Run(BuildReferences);
                _objects.ItemsSource = _catalog.Values.Take(160).ToArray();
                var initial = _main.SelectedObject?.Name ?? _main.UnitWorkspace?.SelectedUnit?.Unit.Name;
                if (initial is not null && _catalog.TryGetValue(initial, out var selected)) { _objects.ItemsSource = new[] { selected }; _objects.SelectedItem = selected; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { _source.Text = ex.Message; }
        };
    }
    private void BuildReferences()
    {
        foreach (var file in _catalog.Values.GroupBy(o => o.SourceFile))
        {
            var source = File.ReadAllText(file.Key);
            foreach (var obj in file)
            {
                if (obj.CharacterOffset + obj.CharacterLength > source.Length) continue;
                var text = source.Substring(obj.CharacterOffset, obj.CharacterLength); _sources[obj.Name] = text;
                _edges[obj.Name] = new NdfSyntaxDocument(text).FindReferences("").Select(r => r.Leaf).Where(n => n != obj.Name && _catalog.ContainsKey(n)).Distinct().ToArray();
            }
        }
        if (_main.StrategicWorkspace is { } strategic)
            foreach (var record in strategic.Data.Records.Where(r => r.Pawn is not null))
                _edges[record.Pawn!.Info.Name] = (_edges.GetValueOrDefault(record.Pawn.Info.Name) ?? []).Append(record.Deck.Info.Name).Distinct().ToArray();
        foreach (var pair in _edges) foreach (var target in pair.Value)
        { if (!_reverse.TryGetValue(target, out var list)) _reverse[target] = list = []; list.Add(pair.Key); }
    }
    private void ShowObject()
    {
        if (_objects.SelectedItem is not NdfObjectInfo obj) return;
        _references.ItemsSource = (_edges.GetValueOrDefault(obj.Name) ?? []).Where(_catalog.ContainsKey).Select(n => _catalog[n]).ToArray();
        _users.ItemsSource = (_reverse.GetValueOrDefault(obj.Name) ?? []).Where(_catalog.ContainsKey).Select(n => _catalog[n]).ToArray();
        _source.Text = _sources.GetValueOrDefault(obj.Name) ?? "";
    }
    private void Navigate(NdfObjectInfo? obj) { if (obj is not null) { _main.NavigateObject(obj); LoadFields(); } }
    private void LoadFields()
    {
        var fields = new List<RawField>();
        if (_main.IsUnitModule && _main.UnitWorkspace is { } units)
            foreach (var field in units.Fields.Where(f => f.Field is not null && f.IsEditable && f.Field.Definition.ValueKind is UnitValueKind.Integer or UnitValueKind.Decimal or UnitValueKind.EcmPercent))
                fields.Add(new(UiText.T(field.Label), field.FieldPath, () => field.ActiveDraft?.TargetRaw ?? field.RawValue, async raw =>
                {
                    var doc = new NdfSyntaxDocument("Value is T(Value = " + raw + ")");
                    var span = Core.Strategic.StrategicSyntax.Field(doc, "T", "Value");
                    if (span is null || doc.Raw(span) != raw.Trim() || !UnitValueConverter.TryReadDisplay(field.Field!.Definition, doc, span, out var display, out _)) throw new InvalidDataException(UiText.T("原始值无效"));
                    if (!UnitValueConverter.TryFormatTarget(field.Field!, display, out _, out _, out var error)) throw new InvalidDataException(error);
                    field.EditValue = display; await field.FlushAsync();
                }));
        var weaponFields = _main.IsAmmoModule ? _main.AmmoWorkspace?.Fields : _main.WeaponWorkspace?.Fields;
        if ((_main.IsAmmoModule || _main.IsWeaponModule) && weaponFields is not null)
            foreach (var field in weaponFields.Where(f => f.IsEditable && f.Field.Definition.ArgumentName != "Index" && (f.Field.Definition.ValueKind is WeaponValueKind.Integer or WeaponValueKind.Decimal or WeaponValueKind.Degrees)))
                fields.Add(new(UiText.T(field.Label), field.FieldPath, () => field.TargetRaw, async raw =>
                {
                    if (!WeaponValueConverter.TryRead(field.Field.Definition, raw, out var display)) throw new InvalidDataException(UiText.T("原始值无效"));
                    if (!WeaponValueConverter.TryFormat(field.Field, display, out _, out _, out var error)) throw new InvalidDataException(error);
                    field.EditValue = display; await field.FlushAsync();
                }));
        if (_main.IsRulesModule && _main.RulesWorkspace is {} rules)
            foreach(var group in rules.Groups.Where(g=>g.CanEdit)) foreach(var cell in group.Cells.Where(c=>!c.Cell.Boolean))
                fields.Add(new(group.Title + " · " + cell.Cell.Label, group.Group.Definition.RelativePath + " · " + cell.Cell.Key, () => cell.Value, async raw =>
                {
                    if(!decimal.TryParse(raw,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out _)) throw new InvalidDataException("只允许修改数值");
                    var values=group.Cells.ToDictionary(c=>c.Cell.Key,c=>c==cell?raw:c.Value);
                    Core.Rules.RuleWorkspace.Validate(group.Group,values);
                    cell.Value=raw; await group.SaveAsync(); if(group.HasPendingError)throw new InvalidDataException(group.Status);
                }));
        _fields.SelectedItem=null;_raw.Clear();_fieldItems = fields; _fields.ItemsSource = fields;
    }
    private static TextBox CodeBox() => new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(8) };
    private static GroupBox Group(string title, object content) { var group = new GroupBox { Content = content, Margin = new Thickness(8) }; UiText.Bind(group, HeaderedContentControl.HeaderProperty, title); return group; }
    private static TabItem Tab(string title, object content) { var tab = new TabItem { Content = content }; UiText.Bind(tab, HeaderedContentControl.HeaderProperty, title); return tab; }
    private sealed record RawField(string Label, string Path, Func<string> Read, Func<string, Task> Write);
}
