using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Changes;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.App.Changes;

public sealed class ChangeRecordsWindow : Window
{
    private readonly MainViewModel? _main;
    private readonly TextBox _source = Input("change-source"), _baseline = Input("change-baseline"), _target = Input("change-target");
    private readonly ComboBox _policy = new() { MinWidth = 200, SelectedIndex = -1, Tag = "change-policy" };
    private readonly TextBox _status = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 48, MaxHeight = 105, Tag = "change-status" };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly DataGrid _files = Table(("文件", "File", "Path", 4), ("组", "Group", "Group", 1), ("类型", "Type", "Kind", 1), ("状态", "Status", "Status", 2));
    private readonly DataGrid _details = Table(("对象", "Object", "Object", 2), ("字段", "Field", "Field", 2), ("旧基础", "Old base", "Before", 1), ("旧修改", "Old Mod", "After", 1), ("新基础", "New base", "Target", 1), ("结果", "Result", "Result", 1));
    private readonly TextBox _before = Code(), _after = Code();
    private readonly TextBox _filter = new() { Tag = "change-filter", Width = 220, Margin = new(12, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _coverage = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _selectionInfo = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 65, Visibility = Visibility.Collapsed };
    private readonly StackPanel _actions = new();
    private readonly WrapPanel _reviewActions = new();
    private HashSet<string>? _selectedPaths;
    private readonly List<ChangeFileMapping> _mappings = [];
    private readonly List<ChangeTextDecision> _decisions = [];
    private readonly WrapPanel _conflictActions = new() { Visibility = Visibility.Collapsed };
    private readonly TabControl _steps = new();
    private readonly Button _apply, _cancel;
    private ChangePackage? _package;
    private ChangePreview? _preview;
    private CancellationTokenSource? _cancellation;
    private bool _loading;
    public bool ModifiedTarget { get; private set; }
    public ChangePackage? Package => _package;
    public ChangePreview? Preview => _preview;
    public string Status => _status.Text;
    public bool IsWorking => _cancellation is not null;
    private static string L(string zh, string en) => UiText.Current.English ? en : zh;
    public ChangeRecordsWindow(MainViewModel? main = null)
    {
        _main = main; Title = L("修改记录与还原", "Change records and restore"); Width = 1240; Height = 920; MinWidth = 850; MinHeight = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new(18) }; root.SetResourceReference(Panel.BackgroundProperty, "SurfaceBrush"); Content = root;
        var bottom = new DockPanel { Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        _cancel = Button("取消任务", "Cancel task", () => { _cancellation?.Cancel(); return Task.CompletedTask; }, "change-cancel");
        _cancel.IsEnabled = false; DockPanel.SetDock(_cancel, Dock.Right); bottom.Children.Add(_cancel); bottom.Children.Add(_status);
        var actionScroll = new ScrollViewer { Content = _actions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 435 };
        DockPanel.SetDock(actionScroll, Dock.Top); root.Children.Add(actionScroll);
        var heading = Heading("修改记录与还原", "Change records and restore");
        heading.ToolTip = L("读取正式文件并保存完整差异；还原前先预览，旧 Mod 和未应用草稿保持。", "Read formal files and preserve complete changes. Preview before restoring; original Mods and pending drafts are retained."); _actions.Children.Add(heading);
        var policyRow = new WrapPanel { Margin = new(0, 4, 0, 8) };
        policyRow.Children.Add(new TextBlock { Text = L("统一数值方式", "Numeric policy for the entire record"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) });
        _policy.Items.Add(L("按增减数值", "Keep the difference")); _policy.Items.Add(L("按比例", "Keep the ratio")); policyRow.Children.Add(_policy);
        policyRow.Children.Add(Button("打开修改文件", "Open change file", OpenDialogAsync, "change-open"));
        _actions.Children.Add(policyRow);
        var capturePanel = new StackPanel { Margin = new(6) }; var restorePanel = new StackPanel { Margin = new(6) };
        _steps.Items.Add(new TabItem { Header = L("从已有文件提取", "Capture existing files"), Content = capturePanel });
        _steps.Items.Add(new TabItem { Header = L("在新基础上还原", "Restore onto a new baseline"), Content = restorePanel }); _actions.Children.Add(_steps);
        capturePanel.Children.Add(PathRow("已有 Mod", "Existing Mod", _source, () => Folder(_source)));
        capturePanel.Children.Add(PathRow("同版本基础", "Matching baseline", _baseline, () => Folder(_baseline), () => SelectBaseZip()));
        var captureActions = new WrapPanel();
        captureActions.Children.Add(Button("分析全部修改", "Analyze all changes", CaptureAsync, "change-capture"));
        captureActions.Children.Add(Button("导出修改文件", "Export change file", SaveDialogAsync, "change-save"));
        captureActions.Children.Add(Button("创建空白 Mod", "Create a baseline Mod", CreateAsync, "change-create")); capturePanel.Children.Add(captureActions);
        restorePanel.Children.Add(PathRow("目标空白 Mod", "Target baseline Mod", _target, () => Folder(_target)));
        var restoreActions = new WrapPanel();
        restoreActions.Children.Add(Button("预览还原结果", "Preview restore", PreviewAsync, "change-preview"));
        _apply = Button("应用还原", "Apply restore", ApplyDialogAsync, "change-apply"); _apply.IsEnabled = false; restoreActions.Children.Add(_apply);
        restoreActions.Children.Add(Button("备份与恢复", "Backups and recovery", RecoverDialogAsync, "change-recover"));
        restoreActions.Children.Add(Button("创建空白 Mod", "Create a baseline Mod", CreateAsync, "change-create-target")); restorePanel.Children.Add(restoreActions);
        _target.ToolTip = L("选择确认未修改的新版基础。部分还原后继续使用原目标，程序会核对已完成回执。", "Select a confirmed clean baseline. After a partial restore, keep the same target; completed receipts will be verified.");
        _reviewActions.Children.Add(Button("暂缓所选组", "Defer selected group", () => ToggleGroupAsync(false), "change-defer"));
        _reviewActions.Children.Add(Button("纳入所选组", "Include selected group", () => ToggleGroupAsync(true), "change-include"));
        _reviewActions.Children.Add(Button("选择对应对象", "Map target objects", MapDialogAsync, "change-map"));
        _reviewActions.Children.Add(Button("清除所选对应", "Clear selected mapping", ClearMappingAsync, "change-clear-map"));
        restorePanel.Children.Add(_reviewActions);
        var listHeader = new DockPanel(); DockPanel.SetDock(listHeader, Dock.Top); root.Children.Add(listHeader);
        var search = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(search, Dock.Right); listHeader.Children.Add(search);
        search.Children.Add(new TextBlock { Text = L("筛选文件", "Filter files"), VerticalAlignment = VerticalAlignment.Center }); search.Children.Add(_filter); listHeader.Children.Add(_summary);
        _filter.TextChanged += (_, _) => Populate();
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
        grid.RowDefinitions.Add(new() { Height = new GridLength(6) }); grid.RowDefinitions.Add(new() { Height = new GridLength(1.5, GridUnitType.Star), MinHeight = 200 }); root.Children.Add(grid);
        // Reserve room for the selected conflict, including its evidence and choice buttons.
        void ResizeActions() => actionScroll.MaxHeight = Math.Clamp(root.ActualHeight - bottom.ActualHeight - bottom.Margin.Top - listHeader.ActualHeight - 306, 120, 435);
        root.SizeChanged += (_, _) => ResizeActions(); bottom.SizeChanged += (_, _) => ResizeActions(); listHeader.SizeChanged += (_, _) => ResizeActions();
        _files.Tag = "change-files"; grid.Children.Add(_files);
        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch }; Grid.SetRow(splitter, 1); grid.Children.Add(splitter);
        var tabs = new TabControl { Tag = "change-detail-tabs" }; Grid.SetRow(tabs, 2); grid.Children.Add(tabs);
        var detailPanel = new DockPanel(); DockPanel.SetDock(_selectionInfo, Dock.Bottom); detailPanel.Children.Add(_selectionInfo);
        _conflictActions.Children.Add(Button("保留新版内容", "Keep target value", () => ChooseTextAsync(TextConflictChoice.KeepTarget), "change-keep-text"));
        _conflictActions.Children.Add(Button("使用记录内容", "Use recorded value", () => ChooseTextAsync(TextConflictChoice.UseRecorded), "change-use-text"));
        _conflictActions.Children.Add(Button("清除冲突选择", "Clear conflict choice", () => ChooseTextAsync(null), "change-clear-text"));
        DockPanel.SetDock(_conflictActions, Dock.Top); detailPanel.Children.Add(_conflictActions); detailPanel.Children.Add(_details);
        _details.Tag = "change-details"; _details.SelectionChanged += (_, _) => DetailSelection();
        _details.SizeChanged += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (_details.SelectedItem is { } selected) _details.ScrollIntoView(selected);
        }));
        tabs.Items.Add(new TabItem { Header = L("修改明细", "Change details"), Content = detailPanel });
        var raw = new Grid(); raw.ColumnDefinitions.Add(new()); raw.ColumnDefinitions.Add(new());
        var left = new GroupBox { Header = L("旧基础／目标原文", "Old base / target original"), Content = _before, Margin = new(0, 0, 5, 0) };
        var right = new GroupBox { Header = L("旧修改／还原候选", "Old Mod / restore candidate"), Content = _after, Margin = new(5, 0, 0, 0) }; Grid.SetColumn(right, 1); raw.Children.Add(left); raw.Children.Add(right);
        tabs.Items.Add(new TabItem { Header = L("完整文件内容", "Full file content"), Content = raw });
        tabs.Items.Add(new TabItem { Header = L("记录范围与问题", "Coverage and issues"), Content = _coverage });
        var review = new StackPanel { Margin = new(8) };
        review.Children.Add(Note("核对进度只保存在此目标内，包含对应、暂缓和冲突选择。关闭窗口前主动保存；重新打开同一记录与目标后可读取。目标或回执变化时须重新核对。", "Review progress is saved inside this target and includes mappings, deferred groups and conflict choices. Save before closing, then reopen the same record and target to resume. Target or receipt changes require a new review."));
        var progressActions = new WrapPanel();
        progressActions.Children.Add(Button("保存核对进度", "Save review progress", SaveReviewAsync, "change-save-review"));
        progressActions.Children.Add(Button("读取核对进度", "Load review progress", LoadReviewAsync, "change-load-review")); review.Children.Add(progressActions);
        var inspect = new WrapPanel(); review.Children.Add(inspect);
        inspect.Children.Add(Button("导出所选旧内容", "Save selected original", () => ExportSelected(false), "change-extract-before"));
        inspect.Children.Add(Button("导出所选新内容", "Save selected modified", () => ExportSelected(true), "change-extract-after"));
        inspect.Children.Add(Button("清除所选文件冲突选择", "Clear selected file conflict choices", ClearFileTextAsync, "change-clear-file-text"));
        tabs.Items.Add(new TabItem { Header = L("核对与进度", "Review and progress"), Content = new ScrollViewer { Content = review, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        _files.SelectionChanged += (_, _) => Selection();
        _policy.SelectionChanged += (_, _) => { if (_loading) return; if (_package is not null && _policy.SelectedIndex >= 0) _package = _package.WithPolicy((NumericPolicy)_policy.SelectedIndex); _decisions.Clear(); Invalidate(); Populate(); _status.Text = L("数值方式已改变，请对干净基础重新预览。", "Numeric policy changed; preview again against a clean baseline."); };
        _source.TextChanged += (_, _) => { if (!_loading) ClearPackage(); }; _baseline.TextChanged += (_, _) => { if (!_loading) ClearPackage(); };
        _target.TextChanged += (_, _) => { _selectedPaths = null; _mappings.Clear(); _decisions.Clear(); Invalidate(); };
        Closing += (_, e) => { if (_cancellation is not null) { _cancellation.Cancel(); e.Cancel = true; } };
        if (main?.HasOpenProject == true) _source.Text = main.ProjectPath;
        _status.Text = L("先选择已有 Mod 与对应基础，或直接打开修改文件。基础必须由你确认与旧 Mod 对应；当前创建器只能创建当前安装版本。", "Select an existing Mod and its matching baseline, or open a change file. Confirm that the baseline matches the old Mod; the creator only provides the currently installed version.");
    }
    private sealed record FileRow(string Path, string Group, string Kind, string Status);
    private void Invalidate() { _preview = null; _apply.IsEnabled = false; }
    private void ClearPackage() { _package = null; _selectedPaths = null; _mappings.Clear(); _decisions.Clear(); Invalidate(); _files.ItemsSource = null; _details.ItemsSource = null; _summary.Text = ""; _coverage.Clear(); _before.Clear(); _after.Clear(); }
    public void SetInputs(string source, string baseline, string target, NumericPolicy policy)
    { _source.Text = source; _baseline.Text = baseline; _target.Text = target; _policy.SelectedIndex = (int)policy; }
    public Task CaptureAsync() => Work(async token =>
    {
        if (_policy.SelectedIndex < 0) throw new InvalidOperationException(L("请选择统一数值方式。", "Select a numeric policy."));
        var source = _source.Text.Trim(); var baseline = _baseline.Text.Trim(); var policy = (NumericPolicy)_policy.SelectedIndex;
        var progress = Progress(); _package = await Task.Run(() => ChangeCapture.Capture(source, baseline, policy, progress, token), token); _selectedPaths = null; _mappings.Clear(); _decisions.Clear(); Invalidate(); Populate();
        _status.Text = _package.Manifest.Complete ? L("全部纳入范围的修改已记录，可以导出。基线正确性以你选择的对应版本为依据。", "All changes in scope are captured and ready to export. Baseline correctness depends on the matching version you selected.") : string.Join("\n", _package.Manifest.Issues);
    });
    public Task OpenPackageAsync(string path) => Work(async token =>
    {
        _package = await Task.Run(() => ChangePackageStore.Load(path, token), token); _selectedPaths = null; _mappings.Clear(); _decisions.Clear(); _loading = true;
        try { _policy.SelectedIndex = (int)_package.Manifest.NumericPolicy; } finally { _loading = false; }
        Invalidate(); Populate(); _steps.SelectedIndex = 1; _status.Text = L("记录已读取，不需要原 Mod 目录。", "Record loaded; the original Mod directory is not required.");
    });
    public Task ExportAsync(string path) => Work(async token =>
    {
        var package = _package ?? throw new InvalidOperationException(L("请先分析或打开记录。", "Analyze or open a record first."));
        await Task.Run(() => ChangePackageStore.Save(package, path, token), token); _status.Text = L("已导出并回读验证：", "Exported and verified: ") + path;
    });
    public Task PreviewAsync() => Work(async token =>
    {
        _steps.SelectedIndex = 1;
        var package = _package ?? throw new InvalidOperationException(L("请先分析或打开记录。", "Analyze or open a record first.")); var target = _target.Text.Trim(); var progress = Progress();
        var options = new ChangeRestoreOptions(_selectedPaths?.ToArray(), _mappings.ToArray(), _decisions.ToArray());
        _preview = await Task.Run(() => ChangeRestore.Prepare(package, target, progress, token, options), token);
        _mappings.Clear(); _mappings.AddRange(_preview.Mappings); _decisions.Clear(); _decisions.AddRange(_preview.Decisions); _apply.IsEnabled = _preview.CanApply; Populate();
        _status.Text = _preview.AllProcessed && _preview.RetainedChanges > 0 ? L("已处理完毕；保留新版、未还原项：", "Processing complete; changes omitted to retain the target: ") + _preview.RetainedChanges : _preview.AlreadyApplied ? L("此记录已经应用，未重复计算。", "This record is already applied; no arithmetic was repeated.") : _preview.Errors.Count > 0 ? string.Join("\n", _preview.Errors) :
            _preview.SelectedPaths.Count == 0 ? L("没有纳入本次还原的组；暂缓内容仍在原记录中。", "No groups selected; deferred content remains in the original record.") :
            L("预览完成。已选择的依赖组将一起提交；暂缓内容保留。", "Preview ready. Selected dependency groups will be committed together; deferred content is retained.");
    });
    public Task ApplyAsync() => Work(async token =>
    {
        var preview = _preview ?? throw new InvalidOperationException(L("请先预览。", "Preview first."));
        var journal = await Task.Run(() => ChangeTransactions.Commit(preview, token), token); ModifiedTarget = true; _selectedPaths = null; Invalidate();
        var retained = journal.Decisions.Count(d => d.Choice == TextConflictChoice.KeepTarget);
        _status.Text = (journal.DeferredPaths.Count > 0 ? L("部分还原完成，剩余文件数：", "Partial restore completed; remaining files: ") + journal.DeferredPaths.Count + "\n" : retained > 0 ? L("本批处理完成。", "Batch processed. ") : L("还原完成。", "Restore completed. ")) +
            (retained > 0 ? L("保留新版、未还原项：", "Changes omitted to retain the target: ") + retained + "\n" : "") + L("备份编号：", "Backup: ") + journal.Id + "\n" + L("重新预览可继续剩余组；文件成功不代表游戏效果已验证。", "Preview again to continue remaining groups. File success does not establish in-game success.");
    });
    public Task RecoverAsync(string id) => Work(async _ => { var root = _target.Text.Trim(); await Task.Run(() => ChangeTransactions.Recover(root, id)); ModifiedTarget = true; _selectedPaths = null; _mappings.Clear(); _decisions.Clear(); Invalidate(); _status.Text = L("已恢复该批次应用前文件。", "Files restored to their state before this batch."); });
    private async Task ApplyDialogAsync()
    {
        if (_preview?.CanApply != true) return;
        var scope = L("本次文件：", "Files in this batch: ") + _preview.SelectedPaths.Count + L("；暂缓：", "; deferred: ") + _preview.DeferredPaths.Count;
        if (MessageBox.Show(this, scope + "\n" + L("将还原到以下目录，并为本次改动创建备份：\n", "Restore to the following folder and back up this batch:\n") + _preview.Root, Title, MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK) await ApplyAsync();
    }
    private Task SaveDialogAsync()
    {
        var dialog = new SaveFileDialog { Filter = "WLMT Changes|*.wlmtchanges", FileName = (_package?.Manifest.SourceName ?? "Mod") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".wlmtchanges", OverwritePrompt = true };
        return dialog.ShowDialog(this) == true ? ExportAsync(dialog.FileName) : Task.CompletedTask;
    }
    private Task OpenDialogAsync()
    { var dialog = new OpenFileDialog { Filter = "WLMT Changes|*.wlmtchanges" }; return dialog.ShowDialog(this) == true ? OpenPackageAsync(dialog.FileName) : Task.CompletedTask; }
    private Task RecoverDialogAsync()
    {
        var root = _target.Text.Trim(); var journals = ChangeTransactions.List(root).Where(j => j.State != "RolledBack").ToArray();
        if (journals.Length == 0) { _status.Text = L("此目标没有可恢复记录。", "No recoverable records in this target."); return Task.CompletedTask; }
        var dialog = new Window { Owner = this, Title = L("选择还原备份", "Select a restore backup"), Width = 700, Height = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty, "SurfaceBrush"); var dock = new DockPanel { Margin = new(16) }; dialog.Content = dock;
        var list = new ListBox { ItemsSource = journals.Select(j => new { Journal = j, Label = j.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " · " + j.State + " · " + j.Files.Count + L(" 文件 · ", " files · ") + j.Id }).ToArray(), DisplayMemberPath = "Label", SelectedIndex = journals.Length - 1 };
        var submit = Button("恢复所选备份", "Recover selected backup", () => { dialog.DialogResult = true; return Task.CompletedTask; }, "change-confirm-recover"); DockPanel.SetDock(submit, Dock.Bottom); dock.Children.Add(submit); dock.Children.Add(list);
        return dialog.ShowDialog() == true && list.SelectedIndex >= 0 ? RecoverAsync(journals[list.SelectedIndex].Id) : Task.CompletedTask;
    }
    private async Task CreateAsync()
    {
        var vm = _main ?? new MainViewModel(); var dialog = new CreateModDialog(vm) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var capture = _steps.SelectedIndex == 0;
        await Work(async token => { var result = await new ModCreationService().CreateAsync(dialog.ModsRoot, dialog.ModName, token); if (capture) _baseline.Text = result.ModRoot; else _target.Text = result.ModRoot; _status.Text = L("已创建当前版本空白 Mod：", "Created a baseline for the installed version: ") + result.ModRoot; });
    }
    public async Task SelectGroupsAsync(IEnumerable<string> paths)
    { _selectedPaths = paths.ToHashSet(StringComparer.OrdinalIgnoreCase); Invalidate(); await PreviewAsync(); }
    public Task SaveReviewAsync() => Work(async token =>
    {
        var preview = _preview ?? throw new InvalidOperationException(L("请先预览。", "Preview first."));
        var saved = await Task.Run(() => ChangeReviewSessions.Save(preview, token), token);
        _status.Text = L("核对进度已保存于当前目标：", "Review progress saved inside this target: ") + saved.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "\n" + L("对应、暂缓和冲突选择已记录；正式 Mod 文件保持。", "Mappings, deferred groups and conflict choices recorded; formal Mod files retained.");
    });
    public Task LoadReviewAsync() => Work(async token =>
    {
        var package = _package ?? throw new InvalidOperationException(L("请先打开记录。", "Open a record first.")); var root = _target.Text.Trim();
        var preview = await Task.Run(() => ChangeReviewSessions.Load(package, root, token), token);
        _preview = preview; _selectedPaths = preview.SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _mappings.Clear(); _mappings.AddRange(preview.Mappings); _decisions.Clear(); _decisions.AddRange(preview.Decisions);
        _steps.SelectedIndex = 1; _apply.IsEnabled = preview.CanApply; Populate();
        _status.Text = L("已读取核对进度并重新验证。", "Review progress loaded and revalidated.") + (preview.Errors.Count > 0 ? "\n" + string.Join("\n", preview.Errors) : "");
    });
    public async Task SetTextDecisionAsync(ChangeTextConflict conflict, TextConflictChoice? choice)
    {
        if (_preview?.AppliedPaths.Contains(conflict.Path, StringComparer.OrdinalIgnoreCase) == true) throw new InvalidOperationException(L("已处理组须先恢复。", "Recover the processed group first."));
        _decisions.RemoveAll(d => d.Conflict.Path.Equals(conflict.Path, StringComparison.OrdinalIgnoreCase) && d.Conflict.Token == conflict.Token && d.Conflict.Column == conflict.Column);
        if (choice is not null) _decisions.Add(new(conflict, choice.Value));
        Invalidate(); await PreviewAsync();
    }
    private Task ChooseTextAsync(TextConflictChoice? choice)
    {
        if (_cancellation is not null) return Task.CompletedTask;
        if (_details.SelectedItem is not ChangeDetail { Conflict: { } conflict })
        { _status.Text = L("先在明细中选择受支持的字段或词典冲突。", "Select a supported field or dictionary conflict in the details first."); return Task.CompletedTask; }
        return SetTextDecisionAsync(conflict, choice);
    }
    private async Task ClearFileTextAsync()
    {
        if (_cancellation is not null || _files.SelectedItem is not FileRow row) return;
        if (_preview?.AppliedPaths.Contains(row.Path, StringComparer.OrdinalIgnoreCase) == true) throw new InvalidOperationException(L("已处理组须先恢复。", "Recover the processed group first."));
        _decisions.RemoveAll(d => d.Conflict.Path.Equals(row.Path, StringComparison.OrdinalIgnoreCase));
        Invalidate(); await PreviewAsync();
    }
    public async Task SetMappingAsync(ChangeFileMapping mapping)
    {
        _mappings.RemoveAll(m => m.SourcePath.Equals(mapping.SourcePath, StringComparison.OrdinalIgnoreCase)); _mappings.Add(mapping);
        _selectedPaths = null; Invalidate(); await PreviewAsync();
    }
    private async Task ToggleGroupAsync(bool include)
    {
        if (_preview is null || _files.SelectedItem is not FileRow row) { _status.Text = L("先预览并选择文件。", "Preview and select a file first."); return; }
        var group = _preview.Groups.Single(g => g.Paths.Contains(row.Path, StringComparer.OrdinalIgnoreCase));
        if (group.Applied) { _status.Text = L("此组已应用；需先恢复才能改变。", "This group is applied; recover it before changing it."); return; }
        var selected = _preview.SelectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (include) selected.UnionWith(group.Paths); else selected.ExceptWith(group.Paths);
        await SelectGroupsAsync(selected);
    }
    private async Task MapDialogAsync()
    {
        if (_package is null || _preview is null || _files.SelectedItem is not FileRow row) { _status.Text = L("先预览并选择文件。", "Preview and select a file first."); return; }
        if (_preview.AppliedPaths.Contains(row.Path, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException(L("已应用组须先恢复。", "Recover the applied group first."));
        var sources = ChangeMerge.MappingSources(_package, row.Path); IReadOnlyList<ChangeObjectTarget>? targets = null; var root = _target.Text.Trim();
        await Work(async token => { targets = await Task.Run(() => ChangeMerge.MappingTargets(root, token), token); });
        if (targets is null) return;
        var dialog = new ChangeMappingWindow(sources, targets, _mappings.SingleOrDefault(m => m.SourcePath.Equals(row.Path, StringComparison.OrdinalIgnoreCase))) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null) await SetMappingAsync(dialog.Result);
    }
    private async Task ClearMappingAsync()
    {
        if (_files.SelectedItem is not FileRow row) return;
        if (_preview?.AppliedPaths.Contains(row.Path, StringComparer.OrdinalIgnoreCase) == true) throw new InvalidOperationException(L("已应用组须先恢复。", "Recover the applied group first."));
        _mappings.RemoveAll(m => m.SourcePath.Equals(row.Path, StringComparison.OrdinalIgnoreCase)); _selectedPaths = null; Invalidate(); await PreviewAsync();
    }
    private void Populate()
    {
        if (_package is null) return;
        var selectedRow = (_files.SelectedItem as FileRow)?.Path;
        _summary.Text = $"{_package.Manifest.SourceName} · {_package.Manifest.Files.Count} " + L("个变化文件", "changed files") + " · " + (_package.Manifest.Complete ? L("记录完整", "Complete record") : L("基础不完整", "Incomplete baseline"));
        if (_preview is not null) _summary.Text += " · " + L("已处理 ", "Processed ") + _preview.AppliedPaths.Count + L("，本次 ", ", selected ") + _preview.SelectedPaths.Count + L("，暂缓 ", ", deferred ") + _preview.DeferredPaths.Count;
        _files.ItemsSource = _package.Manifest.Files.Where(f => f.Path.Contains(_filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Select(f =>
        {
            var group = _preview?.Groups.FirstOrDefault(g => g.Paths.Contains(f.Path, StringComparer.OrdinalIgnoreCase));
            var status = TranslateStatus(_preview?.AllFiles.FirstOrDefault(p => p.RecordPath == f.Path)?.Status) ?? L("已记录", "Captured");
            if (group is { Selected: false, Applied: false }) status = L("暂缓 · ", "Deferred · ") + status;
            return new FileRow(f.Path, group?.Id ?? "", Kind(f), status);
        }).ToArray();
        _coverage.Text = L("基础来源：", "Baseline: ") + _package.Manifest.BaselineName + "\n" + L("参与比较文件数：", "Compared files: ") + _package.Manifest.Inventory.Count + "\n\n" +
            L("问题（含暂缓内容）：", "Issues (including deferred content): ") + "\n" + string.Join("\n", _package.Manifest.Issues.Concat(_preview?.Errors ?? []).Concat(_preview?.AllFiles.Where(f => f.Error is not null).Select(f => f.RecordPath + ": " + f.Error) ?? []).Distinct()) + "\n\n" +
            L("排除的缓存／基线／生成结果：", "Excluded caches, baseline archives and generated files: ") + "\n" + string.Join("\n", _package.Manifest.Excluded);
        if (_preview is not null) _coverage.Text += "\n\n" + L("目标中识别的直接／间接引用（同名范围需结合冲突核对）：", "Recognized direct/indirect target references (review namespace conflicts where names overlap): ") + "\n" + string.Join("\n", _preview.Impacts);
        if (_preview is not null)
        {
            _coverage.Text += "\n\n" + string.Join("\n\n", _preview.Groups.Select(g => L("组 ", "Group ") + g.Id + " · " + TranslateStatus(g.Reason) + "\n" + string.Join("\n", g.Paths)));
            _coverage.Text += "\n\n" + L("显式对应：", "Explicit mappings: ") + "\n" + string.Join("\n", _preview.Mappings.Select(m => m.SourcePath + " → " + m.TargetPath + "\n" + string.Join("\n", m.Objects.Select(p => p.Key + " → " + p.Value))));
            _coverage.Text += "\n\n" + L("冲突处置（保留新版项表示原修改未还原）：", "Conflict decisions (keeping the target omits the recorded change): ") + "\n" +
                string.Join("\n", _preview.Decisions.Select(d => d.Conflict.Path + " · " + d.Conflict.Token + " · " + d.Conflict.Column + " · " + (d.Choice == TextConflictChoice.KeepTarget ? L("保留新版", "Keep target") : L("使用记录", "Use recorded"))));
        }
        if (_files.Items.Count > 0) _files.SelectedItem = _files.Items.Cast<FileRow>().FirstOrDefault(r => r.Path == selectedRow) ?? _files.Items[0];
        else { _details.ItemsSource = null; _before.Clear(); _after.Clear(); _selectionInfo.Visibility = Visibility.Collapsed; }
    }
    private static string Kind(ChangeFile f) => f.Before.Presence == FilePresence.Unknown ? L("基础未知", "Unknown baseline") : f.Before.Presence == FilePresence.Missing ? L("新增", "Added") : f.After.Presence == FilePresence.Missing ? L("删除", "Deleted") : L("修改", "Modified");
    private static string? TranslateStatus(string? value) { if (value is null) return null; var parts = value.Split(" / ", 2); return parts.Length == 2 ? L(parts[0], parts[1]) : value; }
    private void Selection()
    {
        if (_files.SelectedItem is not FileRow row || _package is null) return;
        _files.ScrollIntoView(_files.SelectedItem);
        var file = _package.Manifest.Files.Single(f => f.Path == row.Path); var preview = _preview?.AllFiles.FirstOrDefault(f => f.RecordPath == row.Path);
        _details.ItemsSource = preview is { Details.Count: > 0 } ? preview.Details : ChangeMerge.Describe(file.Path, _package.Before(file), _package.After(file));
        if (_details.Items.Count > 0) _details.SelectedIndex = 0;
        DetailSelection();
        _before.Text = FileContent(preview is null ? _package.Before(file) : preview.Before); _after.Text = FileContent(preview is null ? _package.After(file) : preview.After);
    }
    private void DetailSelection()
    {
        var preview = _files.SelectedItem is FileRow row ? _preview?.AllFiles.FirstOrDefault(f => f.RecordPath == row.Path) : null;
        var detail = _details.SelectedItem as ChangeDetail;
        _conflictActions.Visibility = _details.Items.Cast<ChangeDetail>().Any(d => d.Conflict is not null) ? Visibility.Visible : Visibility.Collapsed;
        _conflictActions.IsEnabled = detail?.Conflict is not null && _cancellation is null;
        var problem = TranslateStatus(preview?.Error); var status = TranslateStatus(detail?.Status);
        if (detail?.Conflict?.Kind == ChangeConflictKind.NdfScalar && preview?.Error?.StartsWith("NDF 字段双方均修改", StringComparison.Ordinal) == true) problem = null;
        var field = (detail?.Field.Contains('[') == true || detail?.Field.Contains('.') == true) && problem?.Contains(detail.Field, StringComparison.Ordinal) != true ? detail.Field : null;
        _selectionInfo.Text = (preview?.SourcePath is not null && preview.Path != preview.RecordPath ? L("对应目标：", "Mapped target: ") + preview.Path + "\n" : "") +
            string.Join("\n", new[] { problem, field, status == problem ? null : status }.Where(s => !string.IsNullOrEmpty(s)));
        _selectionInfo.Text = _selectionInfo.Text.Trim(); _selectionInfo.Visibility = _selectionInfo.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static string FileContent(byte[]? bytes)
    {
        if (bytes is null) return L("文件不存在", "File does not exist");
        try { using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true); var buffer = new char[200_000]; var count = reader.ReadBlock(buffer); return new string(buffer, 0, count) + (reader.Peek() >= 0 ? L("\n显示已截断，包内仍保留全部内容。", "\nDisplay truncated; the package retains all content.") : ""); }
        catch (DecoderFallbackException) { return L("二进制内容已完整保存：", "Full binary content preserved: ") + bytes.Length + " bytes"; }
    }
    private Task ExportSelected(bool after)
    {
        if (_files.SelectedItem is not FileRow row || _package is null) return Task.CompletedTask;
        var file = _package.Manifest.Files.Single(f => f.Path == row.Path); var bytes = after ? _package.After(file) : _package.Before(file);
        if (bytes is null) { _status.Text = L("此状态没有文件内容。", "This state has no file content."); return Task.CompletedTask; }
        var dialog = new SaveFileDialog { FileName = Path.GetFileName(file.Path) + (after ? ".modified" : ".original"), OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        if (File.Exists(dialog.FileName)) throw new IOException(L("请另存为新文件。", "Choose a new file name."));
        File.WriteAllBytes(dialog.FileName, bytes); _status.Text = L("已导出核对副本，原记录保持。", "Review copy exported; the original record is retained."); return Task.CompletedTask;
    }
    private Progress<ChangeProgress> Progress() => new(p => _status.Text = $"{p.Phase} · {p.Completed}/{p.Total}\n{p.Path}");
    private async Task Work(Func<CancellationToken, Task> action)
    {
        if (_cancellation is not null) return;
        _cancellation = new(); _actions.IsEnabled = false; _cancel.IsEnabled = true;
        try { await action(_cancellation.Token); }
        catch (OperationCanceledException) { _status.Text = L("任务已取消。", "Task cancelled."); Invalidate(); }
        catch (Exception ex) { _status.Text = ex.Message; Invalidate(); }
        finally { _cancellation.Dispose(); _cancellation = null; _actions.IsEnabled = true; _cancel.IsEnabled = false; DetailSelection(); }
    }
    private void Folder(TextBox input) { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) { input.Text = dialog.FolderName; if (input == _source && _baseline.Text.Length == 0 && File.Exists(Path.Combine(input.Text, "base.zip"))) _baseline.Text = Path.Combine(input.Text, "base.zip"); } }
    private void SelectBaseZip() { var dialog = new OpenFileDialog { Filter = "Official base.zip|base.zip|ZIP|*.zip" }; if (dialog.ShowDialog(this) == true) _baseline.Text = dialog.FileName; }
    private FrameworkElement PathRow(string zh, string en, TextBox input, Action browse, Action? zip = null)
    {
        var grid = new Grid { Margin = new(0, 3, 0, 3) }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(150) }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = L(zh, en), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) });
        Grid.SetColumn(input, 1); grid.Children.Add(input); var buttons = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(buttons, 2); grid.Children.Add(buttons);
        buttons.Children.Add(Button("浏览", "Browse", () => { browse(); return Task.CompletedTask; }, "browse"));
        if (zip is not null) buttons.Children.Add(Button("选择 ZIP", "Choose ZIP", () => { zip(); return Task.CompletedTask; }, "zip")); return grid;
    }
    private Button Button(string zh, string en, Func<Task> action, string tag)
    {
        var button = new Button { Content = L(zh, en), Tag = tag, Margin = new(6, 3, 0, 3) }; button.SetResourceReference(StyleProperty, "SecondaryButton");
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { _status.Text = ex.Message; } }; return button;
    }
    private static TextBox Input(string tag) => new() { Tag = tag, MinWidth = 150, VerticalContentAlignment = VerticalAlignment.Center };
    private static TextBlock Heading(string zh, string en) { var t = new TextBlock { Text = L(zh, en), Margin = new(0, 8, 0, 5) }; t.SetResourceReference(StyleProperty, "RuleObjectHeading"); return t; }
    private static TextBlock Note(string zh, string en) { var t = new TextBlock { Text = L(zh, en), TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) }; t.SetResourceReference(ForegroundProperty, "MutedTextBrush"); return t; }
    private static TextBox Code() => new() { IsReadOnly = true, FontFamily = new("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, AcceptsReturn = true };
    private static DataGrid Table(params (string Zh, string En, string Property, int Width)[] columns)
    {
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, RowHeaderWidth = 0, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single };
        foreach (var c in columns) grid.Columns.Add(new DataGridTextColumn { Header = L(c.Zh, c.En), Binding = new Binding(c.Property), Width = new DataGridLength(c.Width, DataGridLengthUnitType.Star), MinWidth = 65, ElementStyle = (Style?)Application.Current?.TryFindResource("EditorGridValueText") });
        return grid;
    }
}
