using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class WeaponBatchWindow : Window
{
    public sealed class TargetRow(WeaponBatchTarget target, string name, string ammo, string detail, Action changed) : ObservableObject
    {
        private bool _selected;
        public bool Selected { get => _selected; set { if (SetProperty(ref _selected,value)) changed(); } }
        public WeaponBatchTarget Target => target;
        public string Unit => name;
        public string Ammo => ammo;
        public string Detail => detail;
        public string Identity => target.Unit+"|"+target.Weapon+"|"+target.Mount;
        public string Search => Unit+" "+Ammo+" "+Detail+" "+Identity;
    }
    public sealed record Choice(string Key, string Label);
    private readonly DraftStore _store;
    private readonly WeaponWorkspaceData _initial;
    private readonly Action _refresh;
    private readonly ObservableCollection<TargetRow> _rows = [];
    private readonly ListCollectionView _view;
    private readonly SearchPicker _parameter = new() { DisplayMemberPath = "Label", SecondaryMemberPath = "Key", MinWidth = 260, Width = 310, Tag = "batch-parameter" };
    private readonly ComboBox _operation = new() { DisplayMemberPath = "Label", Width = 170, Tag = "batch-operation" };
    private readonly TextBox _operand = new() { Width = 125, Text = "1", Tag = "batch-operand" };
    private readonly SearchPicker _choice = new() { DisplayMemberPath = "Label", SecondaryMemberPath = "Key", Width = 300, Visibility = Visibility.Collapsed, Tag = "batch-choice" };
    private readonly ComboBox _rounding = new() { DisplayMemberPath = "Label", Width = 140 };
    private readonly TextBox _minimum = new() { Width = 80 }, _maximum = new() { Width = 80 };
    private readonly CheckBox _global = new() { Content = UiText.T("全部引用（修改共享本体）"), Margin = new(8), Tag = "batch-global" };
    private readonly CheckBox _replace = new() { Content = UiText.T("替换重叠的批量草稿范围"), Margin = new(8), Tag = "batch-replace" };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new(4,8,4,8) };
    private readonly TextBlock _selection = new() { Margin = new(4,6,4,6) };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new(4,6,4,6) };
    private readonly DataGrid _preview = Table();
    private readonly ListBox _impacts = new();
    private readonly ListBox _batches = new() { DisplayMemberPath = "Summary" };
    private readonly DockPanel _body = new() { Margin = new(16) };
    private readonly Button _save = new() { Content = UiText.T("加入草稿"), Tag = "batch-save", Margin = new(4), Padding = new(16,7,16,7) };
    private CancellationTokenSource? _cancel;
    private int _revision;
    private bool _bulk;
    private bool _closed;
    private HashSet<TargetRow> _visibleTargets = [];

    public WeaponBatchWindow(WeaponWorkspaceData data, DraftStore store, IReadOnlyCollection<string> selectedUnits, IReadOnlyCollection<string> filteredUnits, Action refresh)
    {
        _initial = data; _store = store; _refresh = refresh;
        Title = UiText.T("批量修改武器"); Width = 1260; Height = 880; MinWidth = 850; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"SurfaceBrush"); SetResourceReference(ForegroundProperty,"TextBrush");
        _body.SetResourceReference(Panel.BackgroundProperty,"SurfaceBrush"); Content = _body;
        foreach (var u in data.Units)
        foreach (var wn in u.Weapons)
        if (data.Weapon(wn) is {} w)
        foreach (var m in w.Mounts)
        {
            var a = data.Ammo(m.AmmoName);
            _rows.Add(new(new(u.Name,w.Name,m.Index),u.DisplayName,
                (UiText.Current.English ? a?.DisplayName : a?.ChineseName) is { Length: > 0 } display ? display : m.AmmoName,
                $"#{m.Index} · {UiText.T("弹药箱")} {m.AmmoBoxIndex} · {UiText.T("炮塔")} {m.TurretIndex} · {UiText.T("共享单位")} {data.References.WeaponUnits.GetValueOrDefault(w.Name)?.Count ?? 0}\n{w.Name} / {m.AmmoName}", Changed));
        }
        _view = new(_rows);
        _visibleTargets = _rows.ToHashSet();
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(footer,Dock.Bottom); _body.Children.Add(footer);
        Add(footer,"预览",async () => await Compute(false)); footer.Children.Add(_save); _save.Click += async (_,_) => await Compute(true); Add(footer,"关闭",() => { Close(); return Task.CompletedTask; });
        var top = new StackPanel(); DockPanel.SetDock(top,Dock.Top); _body.Children.Add(top);
        top.Children.Add(new TextBlock { Text = UiText.T("批量修改武器"), FontSize = 21, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = UiText.T("勾选实际挂载，预览后加入草稿。所选挂载默认局部隔离；同箱库存、同炮塔射界会连带影响。"), TextWrapping = TextWrapping.Wrap, Margin = new(0,6,0,6) });
        var searchBar = new WrapPanel(); top.Children.Add(searchBar);
        var search = new TextBox { Width = 290, Margin = new(4), ToolTip = UiText.T("搜索单位、武器、弹药显示名或内部名"), Tag = "batch-search" }; searchBar.Children.Add(search);
        var facets = new FacetFilter { Width = 180 };
        var unitsByName = data.Units.ToDictionary(u => u.Name);
        facets.Rows = _rows.Select(r =>
        {
            var u = unitsByName[r.Target.Unit]; var m = data.Weapon(r.Target.Weapon)!.Mounts.Single(m => m.Index == r.Target.Mount); var a = data.Ammo(m.AmmoName);
            var values = new Dictionary<string,string[]>(FilterRows.Unit(u).Values) { ["伤害族"] = [a?.Field("ammo.damage.family")?.DisplayValue ?? ""] };
            foreach (var f in a?.Fields.Where(f => f.Definition.ValueKind == WeaponValueKind.Boolean) ?? []) values[f.Definition.Label] = [f.DisplayValue];
            return new FilterRow(r.Identity,values);
        }).ToArray();
        searchBar.Children.Add(facets);
        _view.Filter = o => o is TargetRow r && r.Search.Contains(search.Text,StringComparison.OrdinalIgnoreCase) && facets.Matches(r.Identity);
        void Filter() { _view.Refresh(); _visibleTargets = _view.Cast<TargetRow>().ToHashSet(); Changed(); }
        search.TextChanged += (_,_) => Filter(); facets.FilterChanged += (_,_) => Filter();
        Add(searchBar,"使用当前勾选单位",() => Select(r => selectedUnits.Contains(r.Target.Unit)));
        Add(searchBar,"使用单位筛选全部结果",() => Select(r => filteredUnits.Contains(r.Target.Unit)));
        Add(searchBar,"全选武器筛选结果",() => Select(r => _visibleTargets.Contains(r)));
        Add(searchBar,"清空选择",() => Select(_ => false));
        top.Children.Add(_selection);
        var pending = store.Operations.Where(o => o.TargetKind == DraftTargetKind.UnitCreate).Select(o => o.ObjectName).ToArray();
        if (pending.Length > 0) top.Children.Add(new TextBlock { Text = UiText.T("待创建单位：应用创建后可批改")+" · "+string.Join(", ",pending), TextWrapping = TextWrapping.Wrap });
        var deleted = store.Operations.Where(o => o.TargetKind == DraftTargetKind.UnitDelete).Select(o => o.ObjectName).ToHashSet();
        if (deleted.Count > 0) top.Children.Add(new TextBlock { Text = UiText.T("待删除单位不能加入批量草稿")+" · "+string.Join(", ",deleted), TextWrapping = TextWrapping.Wrap });
        var tools = new WrapPanel(); top.Children.Add(tools);
        Label(tools,"参数",_parameter); Label(tools,"运算",_operation); Label(tools,"数值",_operand); tools.Children.Add(_choice);
        _parameter.ItemsSource = WeaponBatch.Parameters(data).Select(p => new Choice(p.Key,UiText.T(p.Definition.Group)+" · "+UiText.T(p.Definition.Label))).ToArray();
        Watch(_parameter, ParameterChanged);
        var options = new WrapPanel(); top.Children.Add(options);
        Label(options,"取整",_rounding); _rounding.ItemsSource = new[] { new Choice("None",UiText.T("不取整")),new Choice("Nearest",UiText.T("四舍五入")),new Choice("Floor",UiText.T("向下取整")),new Choice("Ceiling",UiText.T("向上取整")) }; _rounding.SelectedIndex = 0;
        if (Advanced.EditorMode.IsAdvanced) { Label(options,"最小值",_minimum); Label(options,"最大值",_maximum); options.Children.Add(_global); }
        options.Children.Add(_replace); top.Children.Add(_hint); top.Children.Add(_summary);
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = new(1,GridUnitType.Star), MinHeight = 120 }); grid.RowDefinitions.Add(new() { Height = new(6) }); grid.RowDefinitions.Add(new() { Height = new(0.85,GridUnitType.Star), MinHeight = 100 }); _body.Children.Add(grid);
        var targets = Table(); targets.Tag = "batch-targets"; targets.ItemsSource = _view;
        var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetBinding(CheckBox.IsCheckedProperty,new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); check.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        targets.Columns.Add(new DataGridTemplateColumn { Header = UiText.T("选"), Width = 45, CellTemplate = new DataTemplate { VisualTree = check } });
        Column(targets,"单位","Unit",230); Column(targets,"武器 / 弹药","Ammo",240); Column(targets,"挂载与共享","Detail",1,true); grid.Children.Add(targets);
        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(splitter,1); grid.Children.Add(splitter);
        var tabs = new TabControl(); Grid.SetRow(tabs,2); grid.Children.Add(tabs);
        _preview.Tag = "batch-preview"; Column(_preview,"单位","Unit",180); Column(_preview,"武器","Weapon",220); Column(_preview,"挂载","Mount",50); Column(_preview,"参数","Parameter",150); Column(_preview,"当前值","Current",95); Column(_preview,"目标值","Target",95); Column(_preview,"推导总弹量","TotalAmmo",130); Column(_preview,"状态","Status",1,true);
        tabs.Items.Add(new TabItem { Header = UiText.T("全部结果"), Content = _preview }); tabs.Items.Add(new TabItem { Header = UiText.T("完整影响与隔离"), Content = _impacts });
        var batchPanel = new DockPanel(); var remove = new WrapPanel(); DockPanel.SetDock(remove,Dock.Bottom); batchPanel.Children.Add(remove); batchPanel.Children.Add(_batches);
        Add(remove,"移除所选批次",async () => { if (_batches.SelectedItem is DraftOperation o) { await _store.RemoveAsync(o.Id); _refresh(); ReloadBatches(); Changed(); } });
        tabs.Items.Add(new TabItem { Header = UiText.T("已保存批次"), Content = batchPanel }); ReloadBatches();
        _operand.TextChanged += (_,_) => Changed(); _minimum.TextChanged += (_,_) => Changed(); _maximum.TextChanged += (_,_) => Changed();
        Watch(_choice, Changed); _operation.SelectionChanged += (_,_) => Changed(); _rounding.SelectionChanged += (_,_) => Changed();
        _global.Click += (_,_) => Changed(); _replace.Click += (_,_) => Changed();
        _parameter.SelectedItem = ((IEnumerable<Choice>)_parameter.ItemsSource).FirstOrDefault(); ParameterChanged();
        Closed += (_,_) => { _closed = true; _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null; }; Changed();
    }
    private void Watch(SearchPicker picker, Action action)
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(SearchPicker.SelectedItemProperty, typeof(SearchPicker));
        EventHandler handler = (_,_) => action(); descriptor.AddValueChanged(picker,handler);
        Closed += (_,_) => descriptor.RemoveValueChanged(picker,handler);
    }
    private void ReloadBatches() => _batches.ItemsSource = _store.Operations.Where(o => o.TargetKind == DraftTargetKind.WeaponBatch).ToArray();
    private Task Select(Func<TargetRow,bool> predicate) { _bulk = true; foreach (var r in _rows) r.Selected = predicate(r); _bulk = false; Changed(); return Task.CompletedTask; }
    private void Changed()
    {
        if (_bulk || _closed) return; _revision++; _cancel?.Cancel();
        if (_view is null) return;
        _selection.Text = UiText.T("已选挂载")+$" {_rows.Count(r => r.Selected)} · "+UiText.T("隐藏的已选")+$" {_rows.Count(r => r.Selected && !_visibleTargets.Contains(r))} · "+UiText.T("筛选结果")+$" {_visibleTargets.Count}";
        _preview.ItemsSource = null; _impacts.ItemsSource = null; _summary.Text = UiText.T("条件变化后请重新预览；加入草稿会重新计算。");
    }
    private void ParameterChanged()
    {
        if (_parameter.SelectedItem is not Choice p) return;
        var definition = WeaponBatch.Parameters(_initial).First(x => x.Key == p.Key).Definition;
        var numeric = definition.ValueKind is WeaponValueKind.Integer or WeaponValueKind.Decimal or WeaponValueKind.Degrees;
        _operation.ItemsSource = new[] { ("Set","设为固定值"),("IncreasePercent","增加百分比"),("DecreasePercent","减少百分比"),("Multiply","乘以"),("Add","增加固定值"),("Subtract","减少固定值") }
            .Where((o,i) => i == 0 || numeric && (i < 3 || Advanced.EditorMode.IsAdvanced)).Select(o => new Choice(o.Item1,UiText.T(o.Item2))).ToArray(); _operation.SelectedIndex = 0;
        _operand.Visibility = numeric ? Visibility.Visible : Visibility.Collapsed; _choice.Visibility = numeric ? Visibility.Collapsed : Visibility.Visible;
        _rounding.IsEnabled = numeric; _rounding.SelectedIndex = definition.ValueKind == WeaponValueKind.Integer ? 1 : 0;
        _minimum.IsEnabled = _maximum.IsEnabled = numeric;
        var choices = definition.ValueKind == WeaponValueKind.Boolean ? new[] { new Choice("True",UiText.T("是")),new Choice("False",UiText.T("否")) }
            : definition.ValueKind == WeaponValueKind.Reference ? _initial.Ammunition.Select(a => new Choice(a.Name,(UiText.Current.English ? a.DisplayName : a.ChineseName) is { Length: > 0 } label ? label : a.Name)).ToArray()
            : _initial.Ammunition.SelectMany(a => a.Fields).Where(f => f.Key == p.Key).SelectMany(f => f.Choices).Distinct().Select(c => new Choice(WarnoLiteModdingTool.Core.Ndf.NdfSyntaxDocument.Leaf(c),c)).ToArray();
        _choice.ItemsSource = choices; _choice.SelectedItem = choices.FirstOrDefault(); _hint.Text = UiText.T(definition.Hint)+(definition.Suffix is {} suffix ? " · "+suffix : ""); Changed();
    }
    private async Task Compute(bool save)
    {
        if (_parameter.SelectedItem is not Choice p || _operation.SelectedItem is not Choice op) return;
        _cancel?.Cancel(); _cancel?.Dispose(); _cancel = new(); var token = _cancel.Token; var revision = _revision;
        var request = new WeaponBatchRequest(_rows.Where(r => r.Selected).Select(r => r.Target).ToArray(),p.Key,Enum.Parse<UnitBatchOperation>(op.Key),
            _choice.Visibility == Visibility.Visible ? (_choice.SelectedItem as Choice)?.Key ?? "" : _operand.Text,
            Enum.Parse<UnitBatchRounding>((_rounding.SelectedItem as Choice)?.Key ?? "None"),_minimum.Text,_maximum.Text,_global.IsChecked == true,_replace.IsChecked == true);
        var drafts = _store.Operations.ToArray(); var draftState = JsonSerializer.Serialize(drafts); _save.IsEnabled = false; _summary.Text = UiText.T("正在重新读取项目并计算完整影响…");
        try
        {
            var result = await Task.Run(async () =>
            {
                using var disk = new DraftStore(_store.ProjectRoot); var loaded = await disk.LoadAsync(token);
                if (loaded.IsBlocked || JsonSerializer.Serialize(disk.Operations) != draftState) throw new InvalidOperationException("草稿已在外部变化，请重新加载项目");
                var context = new ModProjectDetector().Detect(_store.ProjectRoot); var index = await new ProjectIndexer().IndexAsync(context,cancellationToken:token);
                var units = await new UnitProjectLoader().LoadAsync(context,index,token); var data = await new WeaponProjectLoader().LoadAsync(context,index,units,token);
                foreach (var name in request.Targets.Select(t => t.Weapon).Distinct())
                    if (data.Weapon(name) is not {} w || WeaponBatch.Shape(w) != WeaponBatch.Shape(_initial.Weapon(name)!)) throw new InvalidOperationException("挂载结构已变化，请重新加载项目");
                var preview = WeaponBatch.Preview(data,drafts,request);
                if (preview.CanSave)
                {
                    var combined = drafts.Where(o => !preview.Removals.Contains(o.Id)).Concat(preview.Upserts).ToArray();
                    var plan = WeaponBatchApplyPlanner.Plan(units,data,combined,path => TextFileSnapshot.Load(_store.ProjectRoot,path,FormalTextFileKind.Ndf));
                    preview = preview with { Impacts = preview.Impacts.Concat(plan.ValidationMessages).Concat(plan.Replacements.Where(r => r.Summary.Contains(" → ")).Select(r => r.Summary)).ToArray() };
                }
                return preview;
            },token);
            if (token.IsCancellationRequested || revision != _revision || JsonSerializer.Serialize(_store.Operations) != draftState) return;
            _preview.ItemsSource = result.Rows.Select(r => r with { Parameter = UiText.T(r.Parameter), Status = UiText.T(r.Status) }).ToArray(); _impacts.ItemsSource = result.Errors.Concat(result.Impacts).Select(UiText.T).ToArray();
            _summary.Text = UiText.T("实际字段")+$" {result.FieldCount} · "+UiText.T("结果行")+$" {result.Rows.Count} · "+UiText.T("将修改")+$" {result.Rows.Count(r => r.Status == "将修改")} · "+UiText.T("不变")+$" {result.Rows.Count(r => r.Status == "不变")} · "+UiText.T("跳过")+$" {result.Rows.Count(r => r.Status.StartsWith("跳过",StringComparison.Ordinal))} · "+UiText.T("错误")+$" {result.Errors.Count}\n"+string.Join("\n",result.Errors.Take(2).Select(UiText.T));
            if (save && result.CanSave)
            {
                await _store.ApplyBatchAsync(result.Upserts,result.Removals); _refresh(); ReloadBatches();
                _summary.Text += "\n"+UiText.T("批量草稿已保存，正式文件未改变。应用武器草稿时会包含相关武器批次。");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!token.IsCancellationRequested) _summary.Text = UiText.T(e.Message); }
        finally { _save.IsEnabled = true; }
    }
    private static DataGrid Table() => new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0 };
    private static void Column(DataGrid grid,string label,string path,double width,bool star = false) => grid.Columns.Add(new DataGridTextColumn { Header = UiText.T(label), Binding = new Binding(path), Width = new(width,star ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel) });
    private static void Label(Panel panel,string label,FrameworkElement control) { panel.Children.Add(new TextBlock { Text = UiText.T(label), VerticalAlignment = VerticalAlignment.Center, Margin = new(6) }); control.Margin = new(4); panel.Children.Add(control); }
    private void Add(Panel panel,string label,Func<Task> action)
    {
        var button = new Button { Content = UiText.T(label), Tag = label, Margin = new(4), Padding = new(9,6,9,6) }; panel.Children.Add(button);
        button.Click += async (_,_) => { try { await action(); } catch (Exception e) { _summary.Text = UiText.T(e.Message); } };
    }
}
