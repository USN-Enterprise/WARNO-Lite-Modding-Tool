using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels.Weapons;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.Controls;

/// <summary>The same structure editor is used for existing units and the create-unit wizard.</summary>
public sealed class WeaponStructureWindow : Window
{
    private sealed record Target(string Id, string Label, UnitRecord Mother, bool Pending);
    private sealed record MountItem(WeaponStructureRow Row, string Label);
    private readonly WeaponWorkspaceData _data;
    private readonly UnitProjectGraph _graph;
    private readonly DraftStore? _store;
    private readonly Action? _refresh;
    private readonly IReadOnlyList<DraftOperation> _drafts;
    private readonly Dictionary<string, WeaponStructureState> _states = [];
    private readonly SearchPicker _unit = new() { DisplayMemberPath = "Label", SecondaryMemberPath = "Id" };
    private readonly SearchPicker _weapon = new() { DisplayMemberPath = "Name" };
    private readonly ListBox _mounts = new() { DisplayMemberPath = "Label" };
    private readonly StackPanel _fields = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly CheckBox _shared = new() { Margin = new Thickness(0, 8, 0, 8) };
    private readonly List<WeaponFieldViewModel> _editors = [];
    private readonly SemaphoreSlim _saving = new(1, 1);
    private Target? _target;
    private WeaponStructureState? _state;
    private string? _selectedMount;
    private bool _loading, _closing;
    public IReadOnlyList<WeaponStructureState> Plans => _states.Values.Where(s => s.HasChanges).Select(WeaponStructure.Copy).ToArray();
    public WeaponStructureState? CurrentState => _state;

    public WeaponStructureWindow(WeaponWorkspaceData data, UnitProjectGraph graph, IReadOnlyList<DraftOperation> drafts,
        string? selectedUnit = null, DraftStore? store = null, Action? refresh = null, UnitRecord? template = null,
        IReadOnlyList<WeaponStructureState>? initial = null)
    {
        _data = data; _graph = graph; _drafts = drafts; _store = store; _refresh = refresh;
        foreach (var saved in initial ?? WeaponStructurePlanner.States(drafts))
        {
            var s = template is null ? saved : saved with { Unit = template.Name, CreationId = null, Shared = false };
            _states[Key(s)] = WeaponStructure.Copy(s);
        }
        Title = UiText.T("武器槽编辑器"); Width = 1080; Height = 780; MinWidth = 820; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var footer = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var close = Button(store is null ? "确认配置" : "完成", async () => { await FlushAsync(); _closing = true; DialogResult = true; });
        DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close); footer.Children.Add(_status);
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = UiText.T("武器槽与表现配套"), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var selectors = new Grid(); selectors.ColumnDefinitions.Add(new()); selectors.ColumnDefinitions.Add(new());
        selectors.Children.Add(new FieldRow { Header = UiText.T("目标单位"), Content = _unit, Margin = new Thickness(0, 0, 12, 0) });
        var wr = new FieldRow { Header = UiText.T("武器配置"), Content = _weapon }; Grid.SetColumn(wr, 1); selectors.Children.Add(wr); header.Children.Add(selectors);
        _shared.Content = UiText.T("修改共享配置的全部单位引用"); _shared.Visibility = Advanced.EditorMode.IsAdvanced && template is null ? Visibility.Visible : Visibility.Collapsed; header.Children.Add(_shared);
        var toolbar = new WrapPanel { Margin = new Thickness(0, 8, 0, 12) }; header.Children.Add(toolbar);
        toolbar.Children.Add(Button("添加武器槽", () => AddAsync(false)));
        toolbar.Children.Add(Button("复制为新槽", () => AddAsync(true)));
        toolbar.Children.Add(Button("删除槽位", RemoveAsync)); toolbar.Children.Add(Button("撤销删除", UndoAsync));
        toolbar.Children.Add(Button("撤销本槽参数", ResetFieldsAsync));
        if (template is null) { toolbar.Children.Add(Button("批量添加", () => BatchAsync(false))); toolbar.Children.Add(Button("批量删除", () => BatchAsync(true))); }
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(0.9, GridUnitType.Star), MinWidth = 270 }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1.3, GridUnitType.Star), MinWidth = 320 });
        grid.Children.Add(_mounts); var scroller = new ScrollViewer { Content = _fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18, 0, 0, 0) }; Grid.SetColumn(scroller, 1); grid.Children.Add(scroller); root.Children.Add(grid);
        var targets = template is not null ? new[] { new Target(template.Name, template.DisplayName, template, false) } :
            data.Units.Select(u => new Target(u.Name, u.DisplayName, u, false)).Concat(drafts.Where(o => o.TargetKind == DraftTargetKind.UnitCreate).Select(o =>
            { var s = UnitCreation.Read(o); return new Target(s.Id, s.Name + " · " + UiText.T("待创建"), data.Units.Single(u => u.Name == s.Mother), true); })).ToArray();
        _unit.ItemsSource = targets; _unit.IsEnabled = template is null;
        _unit.SelectedItemChanged += async (_, _) => await Guard(async () =>
        {
            if (_loading) return;
            await FlushAsync(); _target = _unit.SelectedItem as Target; _loading = true;
            _weapon.ItemsSource = _target?.Mother.Weapons.Count == 0 ? data.Weapons.ToArray() : (_target?.Mother.Weapons ?? []).Select(data.Weapon).Where(w => w is not null).ToArray();
            _weapon.SelectedItem = (_weapon.ItemsSource as WeaponRecord[])?.FirstOrDefault(); _loading = false; LoadState();
        });
        _weapon.SelectedItemChanged += async (_, _) => await Guard(async () => { if (_loading) return; await FlushAsync(); LoadState(); });
        _shared.Checked += async (_, _) => await Guard(async () => { if (_loading) return; await FlushAsync(); LoadState(); });
        _shared.Unchecked += async (_, _) => await Guard(async () => { if (_loading) return; await FlushAsync(); LoadState(); });
        _mounts.SelectionChanged += async (_, _) => await Guard(async () => { if (_loading) return; await FlushAsync(); BuildFields(); });
        Closing += async (_, e) => { if (_closing) return; e.Cancel = true; await Guard(async () => { await FlushAsync(); _closing = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }); };
        _unit.SelectedItem = targets.FirstOrDefault(t => t.Id == selectedUnit) ?? targets.FirstOrDefault();
    }
    private static string Key(WeaponStructureState s) => (s.Shared ? "*" : s.Unit) + "|" + s.Weapon.Name;
    private Button Button(string title, Func<Task> action)
    {
        var b = new Button { Content = UiText.T(title), MinHeight = 32, Margin = new Thickness(0, 0, 8, 0) };
        b.SetResourceReference(StyleProperty, "SecondaryButton");
        b.Click += async (_, _) => { b.IsEnabled = false; try { await Guard(action); } finally { b.IsEnabled = true; } }; return b;
    }
    private async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException or FormatException or OverflowException)
        {
            _loading = true;
            _unit.SelectedItem = _target;
            if (_state is not null)
            {
                _weapon.SelectedItem = (_weapon.ItemsSource as IEnumerable<WeaponRecord>)?.FirstOrDefault(w => w.Name == _state.Weapon.Name);
                _shared.IsChecked = _state.Shared;
                _mounts.SelectedItem = (_mounts.ItemsSource as IEnumerable<MountItem>)?.FirstOrDefault(m => m.Row.Id == _selectedMount);
            }
            _loading = false; _status.Text = UiText.T(e.Message);
        }
    }
    private void LoadState()
    {
        _editors.Clear(); _fields.Children.Clear();
        if (_target is null || _weapon.SelectedItem is not WeaponRecord w) { _state = null; _mounts.ItemsSource = null; _status.Text = UiText.T("目标单位没有可编辑的武器管理配置"); return; }
        var shared = _shared.IsChecked == true && !_target.Pending && _target.Mother.Weapons.Count > 0;
        var key = (shared ? "*" : _target.Id) + "|" + w.Name;
        if (!_states.TryGetValue(key, out _state)) _states[key] = _state = WeaponStructure.New(_target.Mother, w, shared, _target.Pending ? _target.Id : null);
        RefreshRows();
        _status.Text = _state.Initialize ? UiText.T("当前单位无武器。请选择参考配置，再添加兼容挂载；不会带入模板其他槽位。") : shared ? UiText.T("共享影响") + "：" + string.Join(", ", _data.References.WeaponUnits.GetValueOrDefault(w.Name) ?? []) : UiText.T("仅修改当前单位；必要配置会自动隔离。");
    }
    private void RefreshRows(string? id = null)
    {
        if (_state is null) return;
        id ??= (_mounts.SelectedItem as MountItem)?.Row.Id; _loading = true;
        var all = WeaponStructure.Rows(_state); var turrets = all.Select(r => r.Turret).Distinct().ToList(); var boxes = _state.Boxes.Keys.ToList();
        var rows = all.Where(r => !_state.Initialize || !r.Removed).Select((r, i) => new MountItem(r, (r.Removed ? UiText.T("待删除") + " · " : r.Added ? UiText.T("待新增") + " · " : "") +
            UiText.T("槽位") + " " + (i + 1) + " · " + (_data.Ammo(r.Ammo)?.DisplayName ?? r.Ammo) + "\n" + UiText.T("炮塔") + " " + (turrets.IndexOf(r.Turret) + 1) + " · " + UiText.T("弹药箱") + " " + (boxes.IndexOf(r.Box) + 1))).ToArray();
        _mounts.ItemsSource = rows; _mounts.SelectedItem = rows.FirstOrDefault(r => r.Row.Id == id) ?? rows.FirstOrDefault(); _loading = false; BuildFields();
    }
    public async Task FlushAsync() { foreach (var editor in _editors.ToArray()) await editor.FlushAsync(); }
    private async Task SaveAsync(WeaponStructureState candidate)
    {
        if (candidate.HasChanges && _states.Values.Any(s => s.HasChanges && s.Id != candidate.Id && s.Weapon.Name == candidate.Weapon.Name && (s.Shared || candidate.Shared || s.Unit == candidate.Unit)))
            throw new InvalidOperationException("同一武器存在重叠结构计划，请分别处理共享与局部计划");
        if (candidate.HasChanges && candidate.Initialize && _states.Values.Any(s => s.HasChanges && s.Id != candidate.Id && s.Initialize && s.Unit == candidate.Unit))
            throw new InvalidOperationException("无武器单位只能选择一套初始化参考配置");
        WeaponStructure.Validate(candidate, _data); _ = WeaponStructureRenderer.Render(candidate, candidate.Weapon.Body);
        WeaponStructurePresentation.Capture(_graph, candidate);
        WeaponStructurePresentation.Validate(_graph, candidate);
        if (_store is not null)
        {
            if (candidate.CreationId is not null)
            {
                var op = _store.Operations.Single(o => o.TargetKind == DraftTargetKind.UnitCreate && o.ObjectName == candidate.CreationId); var create = UnitCreation.Read(op);
                var states = create.WeaponStructures.Where(s => s.Weapon.Name != candidate.Weapon.Name).ToList(); if (candidate.HasChanges) states.Add(candidate);
                await _store.UpsertAsync(UnitCreation.Operation(_data.Units.Single(u => u.Name == create.Mother), create with { WeaponStructures = states }, op.BaselineRaw));
            }
            else
            {
                var op = WeaponStructure.Operation(candidate);
                await _store.ApplyBatchAsync(candidate.HasChanges ? [op] : [], candidate.HasChanges ? [] : [op.Id]);
            }
        }
        _states[Key(candidate)] = candidate; if (_state?.Id == candidate.Id) _state = candidate;
        _refresh?.Invoke(); _status.Text = UiText.T(_store is null ? "配置已更新，确认窗口后加入创建草稿。" : "草稿已保存；正式Mod未改变。");
    }
    private async Task AddAsync(bool copy)
    {
        await FlushAsync(); if (_state is null) return;
        var dialog = new WeaponSlotAddWindow(_data, _state, _graph, copy ? (_mounts.SelectedItem as MountItem)?.Row.Id : null) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        await SaveAsync(dialog.Result); RefreshRows(dialog.Result.Added.Last().Id);
    }
    private async Task RemoveAsync()
    {
        await FlushAsync(); if (_state is null || _mounts.SelectedItem is not MountItem selected) return;
        var siblings = WeaponStructure.Rows(_state).Where(r => !r.Removed && r.Box == selected.Row.Box && r.Id != selected.Row.Id).ToArray();
        var message = UiText.T("删除该槽位并加入草稿？") + "\n" + selected.Label + "\n" + UiText.T("共箱剩余槽位") + "：" + siblings.Length + "\n" + UiText.T("槽专属参数保留用于撤销；其他槽与共享弹药修改保持。");
        if (MessageBox.Show(this, message, UiText.T("删除槽位"), MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        var candidate = WeaponStructure.Copy(_state); WeaponStructure.Remove(candidate, selected.Row.Id); await SaveAsync(candidate); RefreshRows();
    }
    private async Task UndoAsync()
    {
        await FlushAsync(); if (_state is null || _mounts.SelectedItem is not MountItem selected || !selected.Row.Removed) return;
        var candidate = WeaponStructure.Copy(_state); WeaponStructure.UndoRemove(candidate, selected.Row.Id); await SaveAsync(candidate); RefreshRows(selected.Row.Id);
    }
    private async Task ResetFieldsAsync()
    {
        if (_state is null || _mounts.SelectedItem is not MountItem selected) return;
        foreach (var editor in _editors) editor.EditValue = editor.Field.DisplayValue;
        await FlushAsync();
        var candidate = WeaponStructure.Copy(_state); candidate.Fields.Remove(selected.Row.Id); candidate.AmmoFields.Remove(selected.Row.Id);
        await SaveAsync(candidate); RefreshRows(selected.Row.Id);
    }
    private void BuildFields()
    {
        _fields.Children.Clear(); _editors.Clear();
        if (_state is null || _mounts.SelectedItem is not MountItem selected) return;
        _selectedMount = selected.Row.Id;
        if (selected.Row.Removed) { _fields.Children.Add(new TextBlock { Text = UiText.T("该槽位待删除，可撤销后继续编辑。"), TextWrapping = TextWrapping.Wrap }); return; }
        var captured = _state; var row = selected.Row;
        _fields.Children.Add(new TextBlock { Text = _data.Ammo(row.Ammo)?.DisplayName ?? row.Ammo, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var fields = new List<WeaponFieldValue>(); var mountValues = WeaponStructureSyntax.Values(row.Body, "TMountedWeaponDescriptor", WeaponStructure.EditableMountFields);
        foreach (var def in new[] { WeaponFieldDefinitions.MountedAmmo(0), WeaponFieldDefinitions.MountedCount(0), WeaponFieldDefinitions.MountedHidden(0) })
            if (mountValues.TryGetValue(def.FieldName, out var raw) && WeaponValueConverter.TryRead(def, raw, out var value)) fields.Add(new(def, captured.Weapon.Name, "TMountedWeaponDescriptor", value, raw, new(captured.Weapon.File, def.FieldName, 0, 0, 1), def.ValueKind == WeaponValueKind.Reference ? _data.Ammunition.Select(a => a.Name).ToArray() : []));
        var boxValue = captured.Boxes[row.Box].ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields.Add(new(WeaponFieldDefinitions.Salves(0), captured.Weapon.Name, "TWeaponManagerModuleDescriptor", boxValue, boxValue, new(captured.Weapon.File, "Salves", 0, 0, 1), []));
        var ammo = _data.Ammo(row.Ammo); if (ammo is not null) fields.AddRange(ammo.Fields);
        string? group = null;
        foreach (var f in fields)
        {
            if (group != f.Definition.Group) { group = f.Definition.Group; _fields.Children.Add(new TextBlock { Text = UiText.T(group), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) }); }
            var effective = f;
            if (f.Definition.Owner == WeaponFieldOwner.Ammo && captured.AmmoFields.GetValueOrDefault(row.Id)?.TryGetValue(f.Key, out var v) == true) effective = f with { DisplayValue = v };
            var vm = new WeaponFieldViewModel(effective, null, async editor =>
            {
                await _saving.WaitAsync();
                try
                {
                    if (!WeaponValueConverter.TryFormat(editor.Field, editor.EditValue, out var value, out var raw, out var error)) throw new InvalidOperationException(error);
                    var next = WeaponStructure.Copy(_states[Key(captured)]);
                    if (next.Removed.Contains(row.Id) || !WeaponStructure.Rows(next).Any(r => r.Id == row.Id)) throw new InvalidOperationException("槽位已删除，不能保存旧输入");
                    if (f.Definition.Owner == WeaponFieldOwner.Weapon) next.Boxes[row.Box] = int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                    else if (f.Definition.Owner == WeaponFieldOwner.MountedWeapon)
                    {
                        if (!next.Fields.ContainsKey(row.Id)) next.Fields[row.Id] = [];
                        if (f.Definition.FieldName == "Ammunition" && next.AmmoFields.GetValueOrDefault(row.Id)?.Count > 0 && value != row.Ammo) throw new InvalidOperationException("该槽已有局部弹药参数，请先撤销这些修改再换弹药");
                        next.Fields[row.Id][f.Definition.FieldName] = raw;
                    }
                    else
                    {
                        if (!next.AmmoFields.ContainsKey(row.Id)) next.AmmoFields[row.Id] = [];
                        next.AmmoFields[row.Id][f.Key] = value;
                        var a = _data.Ammo(row.Ammo)!;
                        next.AmmoBaselines.TryAdd(a.Name, File.ReadAllText(a.Source.SourceFile).Substring(a.Source.CharacterOffset, a.Source.CharacterLength));
                    }
                    await SaveAsync(next); editor.MarkPersisted(null, value, UiText.T("草稿已保存"));
                    if (f.Definition.Owner == WeaponFieldOwner.MountedWeapon && f.Definition.FieldName == "Ammunition")
                        _ = Dispatcher.BeginInvoke(new Action(async () => await Guard(async () => { await FlushAsync(); RefreshRows(row.Id); })));
                }
                finally { _saving.Release(); }
            });
            _editors.Add(vm); FrameworkElement input;
            if (vm.IsReferenceEditor)
            {
                var picker = new SearchPicker { ItemsSource = _data.Ammunition, DisplayMemberPath = "DisplayName", SecondaryMemberPath = "Name", SelectedItem = _data.Ammo(vm.EditValue) };
                picker.SelectedItemChanged += (_, _) => { if (picker.SelectedItem is AmmoRecord a) vm.EditValue = a.Name; }; input = picker;
            }
            else if (vm.IsChoiceEditor)
            {
                var combo = new ComboBox { ItemsSource = vm.Choices.Select(v => new { Value = v, Label = UiText.T(v) }).ToArray(), DisplayMemberPath = "Label", SelectedValuePath = "Value" }; combo.SetBinding(ComboBox.SelectedValueProperty, new Binding("EditValue") { Source = vm, Mode = BindingMode.TwoWay }); input = combo;
            }
            else
            {
                var text = new TextBox(); text.SetBinding(TextBox.TextProperty, new Binding("EditValue") { Source = vm, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); input = text;
            }
            input.MinHeight = 28;
            input.IsEnabled = vm.IsEditable;
            var cell = new StackPanel(); cell.Children.Add(input); var status = new TextBlock { Text = UiText.T(vm.StatusText), TextWrapping = TextWrapping.Wrap };
            status.SetResourceReference(StyleProperty, "SecondaryGridText"); vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.StatusText)) status.Text = UiText.T(vm.StatusText); }; cell.Children.Add(status);
            _fields.Children.Add(new FieldRow { Header = UiText.T(vm.Label), Parameter = vm.OriginalParameter, Content = cell, Margin = new Thickness(0, 0, 0, 8) });
        }
    }
    private async Task BatchAsync(bool delete)
    {
        await FlushAsync(); if (_store is null) return;
        var picker = new UnitSelectionWindow(_data.Units.Where(u => u.Weapons.Count > 0).Select(u => (u.Name, u.DisplayName)), []) { Owner = this };
        if (picker.ShowDialog() != true) return;
        var results = new List<WeaponStructureState>();
        foreach (var name in picker.SelectedIds)
        {
            var unit = _data.Units.Single(u => u.Name == name);
            var choose = new WeaponStructureTargetWindow(_data, unit, delete) { Owner = this };
            if (choose.ShowDialog() != true || choose.Weapon is null) return;
            var w = choose.Weapon; var key = unit.Name + "|" + w.Name;
            var state = WeaponStructure.Copy(_states.GetValueOrDefault(key) ?? WeaponStructure.New(unit, w));
            if (delete)
            {
                if (choose.Mount is null) return; WeaponStructure.Remove(state, choose.Mount);
            }
            else
            {
                var add = new WeaponSlotAddWindow(_data, state, _graph) { Owner = this };
                if (add.ShowDialog() != true || add.Result is null) return; state = add.Result;
            }
            WeaponStructure.Validate(state, _data); _ = WeaponStructureRenderer.Render(state, state.Weapon.Body); WeaponStructurePresentation.Capture(_graph, state); WeaponStructurePresentation.Validate(_graph, state); results.Add(state);
        }
        if (results.Count == 0) return;
        if (results.Any(s => _states.Values.Any(existing => existing.HasChanges && existing.Shared && existing.Weapon.Name == s.Weapon.Name)))
            throw new InvalidOperationException("同一武器存在重叠结构计划，请分别处理共享与局部计划");
        var summary = string.Join("\n\n", results.Select(s => s.Unit + " · " + s.Weapon.Name + " · +" + s.Added.Count + " / −" + s.Removed.Count));
        var preview = new Window { Title = UiText.T("批量武器槽预览"), Width = 760, Height = 580, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var layout = new DockPanel { Margin = new Thickness(18) }; preview.Content = layout;
        var confirm = new Button { Content = UiText.T("将以上全部结果加入草稿？"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        confirm.SetResourceReference(StyleProperty, "SecondaryButton"); confirm.Click += (_, _) => preview.DialogResult = true; DockPanel.SetDock(confirm, Dock.Bottom); layout.Children.Add(confirm);
        layout.Children.Add(new ScrollViewer { Content = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (preview.ShowDialog() != true) return;
        await _store.ApplyBatchAsync(results.Select(WeaponStructure.Operation).ToArray(), []);
        foreach (var s in results) _states[Key(s)] = s;
        _refresh?.Invoke(); LoadState();
    }
}

internal sealed class WeaponStructureTargetWindow : Window
{
    public WeaponRecord? Weapon { get; private set; }
    public string? Mount { get; private set; }
    private sealed record Choice(string Id, string Label);
    public WeaponStructureTargetWindow(WeaponWorkspaceData data, UnitRecord unit, bool delete)
    {
        Title = UiText.T("选择批量目标") + " · " + unit.DisplayName; Width = 620; Height = 320; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var p = new StackPanel { Margin = new Thickness(18) }; Content = p;
        var w = new SearchPicker { ItemsSource = unit.Weapons.Select(data.Weapon).ToArray(), DisplayMemberPath = "Name" }; p.Children.Add(new FieldRow { Header = UiText.T("武器配置"), Content = w });
        var mounts = new ComboBox { DisplayMemberPath = "Label", Margin = new Thickness(0, 12, 0, 12), Visibility = delete ? Visibility.Visible : Visibility.Collapsed }; p.Children.Add(mounts);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; p.Children.Add(error);
        w.SelectedItemChanged += (_, _) => { try { if (w.SelectedItem is not WeaponRecord weapon) return; mounts.ItemsSource = new WeaponStructureSyntax(WeaponStructure.Source(weapon).Body).Mounts.Select(m => new Choice(m.Id, (data.Ammo(NdfSyntaxDocument.Leaf(m.Ammo))?.DisplayName ?? m.Ammo) + " · " + m.Id)).ToArray(); mounts.SelectedIndex = -1; } catch (Exception e) { error.Text = UiText.T(e.Message); } };
        var confirm = new Button { Content = UiText.T("确认"), HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 32, Margin = new Thickness(0, 12, 0, 0) }; confirm.SetResourceReference(StyleProperty, "SecondaryButton"); p.Children.Add(confirm);
        confirm.Click += (_, _) => { if (w.SelectedItem is not WeaponRecord weapon || delete && mounts.SelectedItem is not Choice) return; Weapon = weapon; Mount = (mounts.SelectedItem as Choice)?.Id; DialogResult = true; };
        w.SelectedItem = (w.ItemsSource as WeaponRecord[])?.FirstOrDefault();
    }
}
