using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.App.ViewModels.Units;

public sealed partial class UnitWorkspaceViewModel
{
    private bool SuppressedByDeletion(DraftOperation operation) => operation.TargetKind != DraftTargetKind.UnitDelete && _draftStore.Operations.Any(o => o.TargetKind == DraftTargetKind.UnitDelete && o.ObjectName == operation.ObjectName);
    public bool SelectedPendingDelete => SelectedUnit is { } selected && _draftStore.Operations.Any(o => o.TargetKind == DraftTargetKind.UnitDelete && o.ObjectName == selected.InternalName);
    public string SelectedIdentityText => SelectedUnit is not { } selected ? "" : _draftStore.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.UnitRename && o.ObjectName == selected.InternalName) is { } rename ? selected.InternalName + " → " + UnitIdentityEditing.Read(rename).NewName : selected.InternalName;
    public string LifecycleState => SelectedPendingDelete ? "待删除；撤销删除后可继续编辑" : "";
    public bool CanEditSelectedLifecycle => SelectedUnit is not null && !SelectedPendingDelete && !IsTransactionBusy && !_draftStore.IsBlocked;
    private void RefreshLifecycle()
    {
        OnPropertyChanged(nameof(SelectedPicture));
        OnPropertyChanged(nameof(SelectedPendingDelete)); OnPropertyChanged(nameof(SelectedIdentityText)); OnPropertyChanged(nameof(LifecycleState)); OnPropertyChanged(nameof(CanEditSelectedLifecycle));
        foreach (var field in Fields) field.SetTransactionLocked(IsTransactionBusy || SelectedPendingDelete);
    }
    public async Task EditCapabilitiesAsync(Window owner)
    {
        await FlushAsync();
        if (!CanEditSelectedLifecycle || SelectedUnit is null) throw new InvalidOperationException(UiText.T("请先选择可编辑单位"));
        var unit = SelectedUnit.Unit;
        var graph = await Task.Run(() => new UnitProjectGraph(_draftStore.ProjectRoot));
        var creation = _draftStore.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.UnitCreate && o.ObjectName == unit.Name);
        var old = _draftStore.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.UnitCapabilities && o.ObjectName == unit.Name);
        var state = old is not null ? UnitCapabilities.Read(old) : UnitCapabilities.FromBody(UnitCreation.Source(unit));
        var rawLabels = _draftStore.Operations.Where(o=>o.ObjectName==unit.Name && o.TargetKind is DraftTargetKind.NdfField or DraftTargetKind.OptionalUnitModule && o.FieldKey is "structure.specialties" or "structure.tags").ToArray();
        foreach(var raw in rawLabels)
        {
            var document=new WarnoLiteModdingTool.Core.Ndf.NdfSyntaxDocument("X is TObject(Value = "+raw.TargetRaw+")");
            var values=document.ReadArrayElements(document.FindDirectAssignments(document.FindConstructors("TObject").Single(),"Value").Single()).Select(e=>WarnoLiteModdingTool.Core.Ndf.NdfSyntaxDocument.Unquote(document.Raw(e))).ToArray();
            state=raw.FieldKey=="structure.tags" ? state with {Tags=values} : state with {Specialties=values};
        }
        var window = new UnitCapabilitiesWindow(unit, graph, state) { Owner = owner };
        if (window.ShowDialog() != true) return;
        if (creation is not null)
        {
            var current = UnitCreation.Read(creation) with { Capabilities = window.State };
            var mother = _data.Units.Single(u => u.Name == current.Mother);
            await _draftStore.UpsertAsync(UnitCreation.Operation(mother, current, creation.BaselineRaw));
        }
        else await _draftStore.ApplyBatchAsync([UnitCapabilities.Operation(unit, window.State, old?.BaselineRaw)],rawLabels.Select(o=>o.Id).ToArray());
        RefreshExternalDraftState(); _setStatus("特性与实际能力已保存草稿");
    }
    public async Task EditIdentityAsync(Window owner, bool registration)
    {
        await FlushAsync();
        if (!CanEditSelectedLifecycle || SelectedUnit is null) throw new InvalidOperationException(UiText.T("请先选择可编辑单位"));
        if (!registration && !Advanced.EditorMode.IsAdvanced) throw new InvalidOperationException(UiText.T("变量名编辑仅专业模式可用"));
        var unit = SelectedUnit.Unit;
        var graph = await Task.Run(() => new UnitProjectGraph(_draftStore.ProjectRoot));
        var creation = _draftStore.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.UnitCreate && o.ObjectName == unit.Name);
        if (registration && creation is not null) throw new InvalidOperationException(UiText.T("待创建单位将在应用时注册"));
        var old = _draftStore.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.UnitRename && o.ObjectName == unit.Name);
        var window = LifecycleWindow(registration ? "检查单位注册" : "修改单位变量名", out var panel, out var actions, out var error);
        window.Owner = owner;
        panel.Children.Add(new TextBlock { Text = unit.DisplayName + "\n" + unit.Name, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var input = new TextBox { Text = old is null ? unit.Name : UnitIdentityEditing.Read(old).NewName, IsReadOnly = registration, Margin = new(0, 4, 0, 12) }; panel.Children.Add(input);
        if (registration)
        {
            var obj = graph.RequireObject(unit.Source.RelativeSourceFile, unit.Name); var (file, _, entry) = UnitIdentityEditing.Registration(graph, obj);
            panel.Children.Add(new TextBlock { Text = file.Syntax.Raw(entry.Key) + "\n→ " + graph.ReferenceTo(file.Path, obj), TextWrapping = TextWrapping.Wrap });
        }
        else panel.Children.Add(new TextBlock { Text = UiText.T("显示名、GUID、名称token和牌组编号保持。引用影响在应用预览中列出。"), TextWrapping = TextWrapping.Wrap });
        var save = new Button { Content = UiText.T("保存草稿"), Margin = new(8), Padding = new(12, 6, 12, 6) }; actions.Children.Add(save);
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var name = input.Text.Trim();
                if (!registration) UnitIdentityEditing.RequireAvailable(name, graph, _draftStore.Operations, unit.Name);
                if (creation is not null)
                {
                    var state = UnitCreation.Read(creation) with { Id = name };
                    state = state with { Divisions = state.Divisions.ToDictionary(p => p.Key, p => p.Value with { Unit = name }) };
                    await UnitDraftLinks.ReplaceCreationAsync(_draftStore, creation, UnitCreation.Operation(_data.Units.Single(u => u.Name == state.Mother), state, creation.BaselineRaw));
                }
                else if (!registration && name == unit.Name) { if (old is not null) await _draftStore.RemoveAsync(old.Id); }
                else await _draftStore.UpsertAsync(UnitIdentityEditing.Operation(unit, name, registration));
                RefreshExternalDraftState(); if (creation is not null) SelectedUnit = Units.FirstOrDefault(u => u.InternalName == name);
                window.DialogResult = true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { error.Text = UiText.T(ex.Message); }
            finally { save.IsEnabled = true; }
        };
        window.ShowDialog();
    }
    public async Task DeleteNewUnitAsync(Window owner)
    {
        await FlushAsync();
        if (SelectedUnit is null || IsTransactionBusy || _draftStore.IsBlocked) throw new InvalidOperationException(UiText.T("请先选择单位"));
        var unit = SelectedUnit.Unit;
        var pending = _draftStore.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.UnitDelete && o.ObjectName == unit.Name);
        if (pending is not null) { await _draftStore.RemoveAsync(pending.Id); RefreshExternalDraftState(); return; }
        var creation = _draftStore.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.UnitCreate && o.ObjectName == unit.Name);
        var graph = await Task.Run(() => new UnitProjectGraph(_draftStore.ProjectRoot));
        var uses = creation is null ? UnitDeletion.Uses(graph, graph.RequireObject(unit.Source.RelativeSourceFile, unit.Name)) : [];
        var provenance = creation is null ? UnitCreationHistory.Require(graph, unit.Source.RelativeSourceFile, unit.Name) : null;
        var window = LifecycleWindow(creation is null ? "删除新建单位" : "取消创建", out var panel, out var actions, out var error); window.Owner = owner;
        panel.Children.Add(new TextBlock { Text = unit.DisplayName + "\n" + unit.Name, FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = UiText.T(creation is null ? "删除先保存为草稿，可撤销；正式应用后通过文件级备份恢复。共享武器、弹药和能力保留。" : "取消未应用创建及必要依赖草稿，正式文件保持。"), TextWrapping = TextWrapping.Wrap });
        if (provenance is not null) panel.Children.Add(new TextBlock { Text = UiText.T("创建来源") + " · " + provenance.Evidence + "\n" + UiText.T("母版") + " · " + provenance.Mother + "\n" + "GUID · " + provenance.Guid + "\n" + "Serializer ID · " + provenance.SerializerId, TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 6) });
        foreach (var related in _draftStore.Operations.Where(o => UnitDraftLinks.Touches(o, unit.Name))) panel.Children.Add(new TextBlock { Text = related.Summary, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var replacements = new Dictionary<string, SearchPicker>();
        foreach (var use in uses)
        {
            panel.Children.Add(new TextBlock { Text = use.Description + "\n" + use.File + (use.Automatic ? " · " + UiText.T("联动清理") : ""), TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 6) });
            if (!use.Automatic && use.Field is not null)
            {
                var picker = new SearchPicker { ItemsSource = _data.Units.Where(u => u.Name != unit.Name), DisplayMemberPath = "DisplayName", SecondaryMemberPath = "Name", Placeholder = UiText.T("选择替代单位") };
                replacements[use.Key] = picker; panel.Children.Add(picker);
            }
        }
        var save = new Button { Content = UiText.T(creation is null ? "保存删除草稿" : "取消创建"), Margin = new(8), Padding = new(12, 6, 12, 6) }; actions.Children.Add(save);
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                if (creation is not null) await UnitDraftLinks.CancelCreationAsync(_draftStore, creation);
                else
                {
                    var choices = replacements.ToDictionary(p => p.Key, p => p.Value.SelectedItem is UnitRecord target ? graph.ReferenceTo(uses.Single(u => u.Key == p.Key).File, graph.RequireObject(target.Source.RelativeSourceFile, target.Name)) : "");
                    var operation = UnitDeletion.Operation(unit, choices);
                    await Task.Run(() => _transactions.PrepareApplyAsync(_draftStore.ProjectRoot, [operation]));
                    await _draftStore.UpsertAsync(operation);
                }
                RefreshExternalDraftState(); window.DialogResult = true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { error.Text = UiText.T(ex.Message); }
            finally { save.IsEnabled = true; }
        };
        window.ShowDialog();
    }
    private static Window LifecycleWindow(string title, out StackPanel panel, out StackPanel actions, out TextBlock error)
    {
        var window = new Window { Title = UiText.T(title), Width = 720, Height = 560, MinWidth = 520, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Window.BackgroundProperty, "SurfaceBrush"); window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new(18) }; window.Content = root;
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        error = new TextBlock { TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(error);
        actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; bottom.Children.Add(actions);
        var cancel = new Button { Content = UiText.T("取消"), Margin = new(8), Padding = new(12, 6, 12, 6) }; cancel.Click += (_, _) => window.Close(); actions.Children.Add(cancel);
        panel = new StackPanel(); root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); return window;
    }
}
