using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class MissileRepairWindow : Window
{
    public sealed class Row(MissileDebugRepairRow data, Action changed) : INotifyPropertyChanged
    {
        private bool _selected = data.CanRepair;
        public MissileDebugRepairRow Data { get; } = data;
        public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value && Data.CanRepair; changed(); PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public bool CanRepair => Data.CanRepair;
        public string Name => Data.Name;
        public string Before => Data.Before;
        public string After => Data.After;
        public string Reason => Local(Data.Reason);
        public string Details => Data.File + "\n" + Data.Name + "\n" + L("来源：", "Origin: ") + Data.Origin + "\n" + Data.Before + " → " + Data.After +
            "\n\n" + L("引用位置（保持不变）", "Reference locations (preserved)") + "\n" + (Data.Users.Length > 0 ? Data.Users :
                L("未找到明确引用；保留的声明仍须核对debug重名。", "No references found; retained declarations still require a debug-name check."));
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private readonly MainViewModel _main;
    private readonly DataGrid _table;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 10) };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _save;
    private readonly Button _cancel;
    private readonly StackPanel _panel;
    private IReadOnlyList<Row> _rows = [];
    private IReadOnlyList<DraftOperation>? _preview;
    private ApplyPreview? _plan;
    private CancellationTokenSource? _work;
    private bool _closeWhenIdle;
    private string _planDetails = "";
    public bool IsBusy { get; private set; }
    private static string L(string zh, string en) => UiText.Current.English ? en : zh;
    private static string Local(string text) { var n = text.IndexOf(" / ", StringComparison.Ordinal); return n < 0 ? text : UiText.Current.English ? text[(n + 3)..] : text[..n]; }
    public MissileRepairWindow(MainViewModel main)
    {
        _main = main; DamageUi.Window(this, "修复导弹副本"); MinWidth = 780; Width = 1180; Height = 820;
        var root = new DockPanel { Margin = new(16) }; Content = root;
        _panel = new StackPanel(); DockPanel.SetDock(_panel, Dock.Top); root.Children.Add(_panel);
        _panel.Children.Add(DamageUi.Heading("修复导弹副本"));
        _panel.Children.Add(DamageUi.Text(L("扫描当前Mod中重名的工具导弹副本。只修改副本的ClassNameForDebug，保留原实体、GUID、引用和运动参数。",
            "Scan duplicate tool-created missile clones in this Mod. Only the clone's ClassNameForDebug changes; original entities, GUIDs, references and movement values are preserved."), true));
        var actions = new WrapPanel(); _panel.Children.Add(actions);
        actions.Children.Add(DamageUi.AsyncButton("扫描", () => Run(ScanAsync), Error, "missile-repair-scan"));
        actions.Children.Add(DamageUi.AsyncButton("预览修复", () => Run(PreviewAsync), Error, "missile-repair-preview"));
        _save = DamageUi.Button("加入草稿", () => { }, "missile-repair-save"); _save.IsEnabled = false;
        _save.Click += async (_, _) => { try { await Run(SaveAsync); } catch (Exception ex) { Error(ex); } finally { _save.IsEnabled = _preview is not null; } };
        actions.Children.Add(_save);
        _panel.Children.Add(_status);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var expander = new Expander { Header = UiText.T("技术详情"), Margin = new(0, 10, 0, 8), Content = new ScrollViewer { Content = _details, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        footer.Children.Add(expander);
        footer.Children.Add(DamageUi.Text(L("勾选可修复项 → 预览 → 加入草稿，再到草稿总览预览并应用。应用前自动备份；扫描不会改写Mod。",
            "Select repairable rows, preview, then add drafts. Preview and apply in Draft overview to create a backup. Scanning does not write Mod files."), true));
        var footerActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _cancel = DamageUi.Button(L("取消", "Cancel"), () => _work?.Cancel(), "missile-repair-cancel"); _cancel.IsEnabled = false; footerActions.Children.Add(_cancel);
        footerActions.Children.Add(DamageUi.Button("关闭", Close)); footer.Children.Add(footerActions);
        _table = DamageUi.Table(("导弹实体", "Name"), ("原debug名称", "Before"), ("修复后debug名称", "After"), ("状态", "Reason"));
        _table.Tag = "missile-repair-table"; _table.IsReadOnly = false;
        var check = new DataGridCheckBoxColumn { Header = UiText.T("选择"), Binding = new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 52, MinWidth = 45 };
        var checkStyle = new Style(typeof(CheckBox), Application.Current.TryFindResource(typeof(CheckBox)) as Style); checkStyle.Setters.Add(new Setter(IsEnabledProperty, new Binding("CanRepair")));
        check.ElementStyle = checkStyle; check.EditingElementStyle = checkStyle; _table.Columns.Insert(0, check);
        foreach (var column in _table.Columns.OfType<DataGridTextColumn>())
        {
            column.IsReadOnly = true;
            column.Header = new TextBlock { Text = column.Header?.ToString(), TextWrapping = TextWrapping.Wrap };
            var style = new Style(typeof(TextBlock), column.ElementStyle);
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.None));
            style.Setters.Add(new Setter(ToolTipProperty, column.Binding)); column.ElementStyle = style;
        }
        _table.SelectionChanged += (_, _) => UpdateDetails();
        root.Children.Add(_table);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        _details.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        _status.Text = L("点击扫描，列出当前Mod中的重复名称。", "Scan to list duplicate names in the current Mod.");
        Closing += (_, e) => { if (IsBusy) { e.Cancel = true; _closeWhenIdle = true; _work?.Cancel(); } };
    }
    private void UpdateDetails() => _details.Text = ((_table.SelectedItem as Row)?.Details ?? "") + (_planDetails.Length == 0 ? "" : "\n\n" + _planDetails);
    private void Invalidate() { _preview = null; _plan = null; _planDetails = ""; _save.IsEnabled = false; UpdateDetails(); }
    private void Error(Exception ex) { Invalidate(); _status.Text = Local(ex.Message); }
    private async Task Run(Func<Task> action)
    {
        if (IsBusy || _main.UnitWorkspace?.IsTransactionBusy == true) return;
        _work = new CancellationTokenSource(); IsBusy = true; _panel.IsEnabled = false; _table.IsEnabled = false; _cancel.IsEnabled = true;
        try { await action(); }
        catch (OperationCanceledException) { Invalidate(); _status.Text = L("已取消，未写入正式Mod。", "Cancelled; no Mod files were written."); }
        finally
        {
            _work.Dispose(); _work = null; IsBusy = false; _panel.IsEnabled = true; _table.IsEnabled = true; _cancel.IsEnabled = false;
            if (_closeWhenIdle) Close();
        }
    }
    private IProgress<MissileRepairProgress> Progress()
    {
        var work = _work; long last = 0;
        return new Progress<MissileRepairProgress>(p =>
        {
            if (!IsBusy || work != _work || work?.IsCancellationRequested != false) return;
            var now = Environment.TickCount64; if (p.Completed != 0 && p.Completed != p.Total && now - last < 100) return;
            last = now; _status.Text = Local(p.Stage) + (p.Total > 0 ? $" · {p.Completed}/{p.Total}" : "");
        });
    }
    private async Task ScanAsync()
    {
        Invalidate(); _status.Text = L("正在扫描…", "Scanning…");
        var token = _work!.Token; var progress = Progress();
        var result = await Task.Run(() => MissileDebugRepair.Analyze(_main.ProjectPath, token, progress).Scan(true, token, progress), token);
        token.ThrowIfCancellationRequested();
        _rows = result.Rows.Select(r => new Row(r, Invalidate)).ToArray(); _table.ItemsSource = _rows;
        _status.Text = L($"发现{_rows.Count}个相关实体，其中{_rows.Count(r => r.CanRepair)}个副本可修复。", $"Found {_rows.Count} related entities; {_rows.Count(r => r.CanRepair)} clones can be repaired.") +
            (result.Warnings.Count > 0 ? "\n" + string.Join("\n", result.Warnings.Select(Local)) : "");
    }
    private async Task PreviewAsync()
    {
        Invalidate(); var selected = _rows.Where(r => r.Selected && r.CanRepair).Select(r => r.Name).ToArray();
        var token = _work!.Token; var progress = Progress();
        var plan = await Task.Run(() => new UnitApplyPlanner().PrepareRepairsAsync(_main.ProjectPath, selected, token, progress), token);
        token.ThrowIfCancellationRequested();
        var ops = plan.Operations.Where(MissileDebugRepair.IsRepair).ToArray();
        if (ops.Any(o => _rows.FirstOrDefault(r => r.Name == o.ObjectName) is { } row && (row.Before != o.BaselineValue || row.After != o.TargetValue)))
            throw new InvalidOperationException(L("修复目标已变化，请重新扫描。", "Repair targets changed; scan again."));
        _preview = ops; _plan = plan; _save.IsEnabled = true;
        _status.Text = L($"已验证{ops.Length}项修复，关联其他草稿{plan.Operations.Count - ops.Length}项。加入草稿后，在草稿总览预览并应用。", $"Validated {ops.Length} repairs and {plan.Operations.Count - ops.Length} related drafts. Add drafts, then preview and apply in Draft overview.");
        _planDetails = string.Join("\n", plan.Operations.Select(o => o.ObjectName + " · " + o.FieldPath + ": " + o.BaselineValue + " → " + o.TargetValue)) +
            "\n\n" + L("最终影响文件", "Final affected files") + "\n" + string.Join("\n", plan.Files.Where(f => f.Kind != FormalTextFileKind.Log).Select(f => f.RelativePath));
        UpdateDetails();
    }
    private async Task SaveAsync()
    {
        if (_preview is not { } ops || _plan is not { } plan) throw new InvalidOperationException(L("请先预览修复。", "Preview the repairs first."));
        var token = _work!.Token;
        var refreshScope = new ProjectReadScope();
        _status.Text = L("正在复核预览与草稿…", "Checking preview and drafts…");
        await Task.Run(async () =>
        {
            using var entered = refreshScope.Enter();
            MissileRepairPlanner.VerifyInputs(plan, token); await MissileRepairPlanner.VerifyDraftsAsync(plan, token);
            // Draft overview and the selected unit both resolve drafts during refresh.
            // Prepare their shared repair analysis here, not synchronously on the UI thread.
            _ = MissileDebugRepair.Analyze(_main.ProjectPath, token);
            if (!refreshScope.IsStable()) throw new InvalidOperationException(L("文件已变化，请重新预览。", "Files changed; preview again."));
            token.ThrowIfCancellationRequested(); await _main.SourceDraftStore.ApplyBatchAsync(ops, [], token);
        }, token);
        using (refreshScope.Enter()) _main.SourceDraftsChanged();
        Invalidate();
        _status.Text = L($"已保存{ops.Count}项修复草稿，正式Mod未改变。请到草稿总览应用。", $"Saved {ops.Count} repair drafts. Mod files are unchanged; apply from Draft overview.");
    }
}
