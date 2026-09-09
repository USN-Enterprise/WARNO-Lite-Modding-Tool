using MessageBox = WarnoLiteModdingTool.App.Localisation.LocalizedMessageBox;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels.Backups;
using WarnoLiteModdingTool.App.ViewModels.Drafts;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.App.ViewModels.Units;
using WarnoLiteModdingTool.App.ViewModels.Weapons;
using WarnoLiteModdingTool.App.ViewModels.Divisions;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.App;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private readonly MainViewModel _viewModel;
    private GridLength _objectDetailWidth = new(330);
    private bool _windowLayoutReady;
    private bool _themeSelectorReady;

    public MainWindow()
        : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        ThemeManager.ThemeChanged += UpdateNativeTitleBar;
        Closed += (_,_) => ThemeManager.ThemeChanged -= UpdateNativeTitleBar;
        _windowLayoutReady = true;
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.AdvancedMode = new Settings.UiSettings().Load().AdvancedMode;
        ThemeSelector.SelectedIndex = ThemeManager.IsDark ? 0 : 1;
        _themeSelectorReady = true;
        UpdateObjectDetailLayout(_viewModel.AdvancedMode);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    private void Window_SourceInitialized(object? sender, EventArgs e) => UpdateNativeTitleBar();

    private void OpenAdvanced_Click(object sender, RoutedEventArgs e) { if (_viewModel.AdvancedMode) new Advanced.AdvancedWindow(_viewModel) { Owner = this }.ShowDialog(); }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => new Settings.SettingsWindow(value => _viewModel.AdvancedMode = value) { Owner = this }.ShowDialog();

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_themeSelectorReady)
        {
            return;
        }

        ThemeManager.ApplyTheme(ThemeSelector.SelectedIndex == 1 ? AppTheme.LightBlue : AppTheme.DarkBlue);
        UpdateNativeTitleBar();
    }

    private void AdvancedMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_windowLayoutReady && sender is CheckBox checkBox)
        {
            UpdateObjectDetailLayout(checkBox.IsChecked == true);
        }
    }

    private void UpdateObjectDetailLayout(bool isVisible)
    {
        if (isVisible)
        {
            ObjectPage.ObjectDetailColumn.MinWidth = 280;
            ObjectPage.ObjectDetailColumn.Width = _objectDetailWidth;
            return;
        }

        if (ObjectPage.ObjectDetailColumn.ActualWidth >= 280)
        {
            _objectDetailWidth = new GridLength(ObjectPage.ObjectDetailColumn.ActualWidth);
        }

        ObjectPage.ObjectDetailColumn.MinWidth = 0;
        ObjectPage.ObjectDetailColumn.Width = new GridLength(0);
    }

    internal void ObjectIndex_Sorting(object sender, DataGridSortingEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(UpdateObjectSortPresentation, DispatcherPriority.Loaded);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ObjectsView))
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(EnsureInitialObjectSort, DispatcherPriority.DataBind);
    }

    private void EnsureInitialObjectSort()
    {
        // DataGrid clears SortDescriptions while accepting a replacement ICollectionView.
        // Reapply the documented initial order after the binding has accepted that view.
        if (_viewModel.ObjectsView.SortDescriptions.Count == 0)
        {
            _viewModel.ObjectsView.SortDescriptions.Add(new SortDescription(nameof(NdfObjectInfo.RelativeSourceFile), ListSortDirection.Ascending));
            _viewModel.ObjectsView.SortDescriptions.Add(new SortDescription(nameof(NdfObjectInfo.LineNumber), ListSortDirection.Ascending));
            _viewModel.ObjectsView.SortDescriptions.Add(new SortDescription(nameof(NdfObjectInfo.CharacterOffset), ListSortDirection.Ascending));
        }

        UpdateObjectSortPresentation();
    }

    private void UpdateObjectSortPresentation()
    {
        var visibleSorts = _viewModel.ObjectsView.SortDescriptions
            .Where(item => !string.Equals(item.PropertyName, "CharacterOffset", StringComparison.Ordinal))
            .Select(item => $"{ObjectSortName(item.PropertyName)} {(item.Direction == ListSortDirection.Ascending ? "↑" : "↓")}")
            .ToArray();
        ObjectPage.ObjectSortLabel.Text = visibleSorts.Length == 0
            ? "排序：索引原始顺序"
            : $"排序：{string.Join(" · ", visibleSorts)}";

        foreach (var column in ObjectPage.ObjectIndexGrid.Columns)
        {
            var sort = _viewModel.ObjectsView.SortDescriptions
                .Cast<SortDescription?>()
                .FirstOrDefault(item => string.Equals(item?.PropertyName, column.SortMemberPath, StringComparison.Ordinal));
            column.SortDirection = sort?.Direction;
        }
    }

    private static string ObjectSortName(string propertyName) => propertyName switch
    {
        "DisplayName" => "名称",
        "TypeName" => "内部类型",
        "RelativeSourceFile" => "源文件",
        "LineNumber" => "行号",
        _ => propertyName
    };

    private void UpdateNativeTitleBar()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        foreach(var (attribute,key) in new[]{(35,"BackgroundBrush"),(36,"TextBrush")}) { var color=((System.Windows.Media.SolidColorBrush)FindResource(key)).Color;var rgb=color.R|(color.G<<8)|(color.B<<16); _ = DwmSetWindowAttribute(windowHandle,attribute,ref rgb,sizeof(int)); }
        var darkMode = ThemeManager.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(windowHandle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(windowHandle, DwmwaUseImmersiveDarkModeLegacy, ref darkMode, sizeof(int));
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        if (_viewModel.ConsumeLastFatal() is { } priorFatal)
        {
            _viewModel.OpenProblems();
            MessageBox.Show(
                this,
                $"上次运行因工具错误异常退出。\n\n错误编号：{priorFatal.Id}\n可在问题中心复制完整诊断。",
                "上次异常退出",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenProblems_Click(object sender, RoutedEventArgs e) => _viewModel.OpenProblems();

    internal void RemoveFilterTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UnitFilterTagViewModel tag })
        {
            _viewModel.UnitWorkspace?.RemoveFilterTag(tag);
        }
    }

    internal void ClearUnitFilters_Click(object sender, RoutedEventArgs e) => _viewModel.UnitWorkspace?.ClearFilters();

    internal void CloseUnitFilters_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.UnitWorkspace is { } workspace)
        {
            workspace.IsFilterPanelOpen = false;
        }
    }

    private async void CreateMod_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateModDialog(_viewModel) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var target = Path.Combine(dialog.ModsRoot, dialog.ModName);
        if (MessageBox.Show(
                this,
                $"将调用 WARNO 官方 CreateNewMod.bat 创建：\n{target}\n\n创建失败时，官方脚本会清理本次新目录。是否继续？",
                "确认创建 Mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = await _viewModel.CreateModAsync(dialog.ModsRoot, dialog.ModName);
            MessageBox.Show(this, $"Mod 已创建：\n{result.ModRoot}", "创建完成", MessageBoxButton.OK, MessageBoxImage.Information);
            await OpenSelectedProjectAsync(result.ModRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, exception.Message, "创建 Mod 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal void CopyProblem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProblem is { } problem)
        {
            Clipboard.SetText(problem.DiagnosticText);
            return;
        }

        MessageBox.Show(this, "请先选择一条问题。", "复制诊断", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    internal void OpenProblemLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_viewModel.ProblemLogRoot);
            Process.Start(new ProcessStartInfo
            {
                FileName = _viewModel.ProblemLogRoot,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _viewModel.RecordToolProblem("无法打开问题日志目录", exception, _viewModel.ProblemLogRoot);
            MessageBox.Show(this, exception.Message, "无法打开日志目录", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void FindMods_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanOpenProject) return;
        var finder = new Controls.ModFinderWindow(new WarnoLiteModdingTool.Core.Projects.WarnoModsRootStore().Load()) { Owner = this };
        if (finder.ShowDialog() == true && finder.SelectedPath is { } path) { await OpenSelectedProjectAsync(path); }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 WARNO Mod 根目录",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await OpenSelectedProjectAsync(dialog.FolderName);
        }
    }

    private async void OpenRecent_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRecentProject is { } project)
        {
            await OpenSelectedProjectAsync(project.Path);
        }
    }

    private async void RemoveRecent_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RemoveSelectedRecentProjectAsync();
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelScan();
    }

    internal async void UndoField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UnitFieldViewModel field } &&
            _viewModel.UnitWorkspace is { } workspace)
        {
            await workspace.UndoFieldAsync(field);
        }
    }

    private async void UndoDraft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DraftItemViewModel item } &&
            _viewModel.UnitWorkspace is { } workspace)
        {
            if (_viewModel.RulesWorkspace is { } rules) await rules.FlushAsync();
            if (_viewModel.StrategicWorkspace is { } strategic) await strategic.FlushAsync();
            await workspace.UndoDraftAsync(item);
            _viewModel.RulesWorkspace?.Restore(); _viewModel.StrategicWorkspace?.Restore();
            _viewModel.WeaponWorkspace?.RefreshFromDrafts();
            _viewModel.AmmoWorkspace?.RefreshFromDrafts();
            _viewModel.DivisionWorkspace?.RefreshFromDrafts();
        }
    }

    internal async void ClearDrafts_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.UnitWorkspace is { } workspace)
        {
            if (_viewModel.RulesWorkspace is { } rules) await rules.FlushAsync();
            if (_viewModel.StrategicWorkspace is { } strategic) await strategic.FlushAsync();
            await workspace.ClearDraftsAsync();
            _viewModel.RulesWorkspace?.Restore(); _viewModel.StrategicWorkspace?.Restore();
            _viewModel.WeaponWorkspace?.RefreshFromDrafts();
            _viewModel.AmmoWorkspace?.RefreshFromDrafts();
            _viewModel.DivisionWorkspace?.RefreshFromDrafts();
        }
    }

    internal void SelectVisibleBatch_Click(object sender, RoutedEventArgs e) =>
        _viewModel.UnitWorkspace?.SelectVisibleForBatch();

    internal void ClearBatchSelection_Click(object sender, RoutedEventArgs e) =>
        _viewModel.UnitWorkspace?.ClearBatchSelection();

    internal async void AddCommonBatchField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UnitBatchCommonFieldViewModel field } && _viewModel.UnitWorkspace is { } workspace)
        {
            await workspace.AddCommonBatchFieldAsync(field);
        }
    }

    internal void PreviewBatch_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.UnitWorkspace?.PreviewBatch();
    }

    internal async void AddBatchDrafts_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.UnitWorkspace is not { } workspace)
        {
            return;
        }

        try
        {
            await workspace.AddBatchToDraftsAsync();
            _viewModel.WeaponWorkspace?.RefreshFromDrafts();
            _viewModel.AmmoWorkspace?.RefreshFromDrafts();
            _viewModel.DivisionWorkspace?.RefreshFromDrafts();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _viewModel.RecordModProblem("无法加入批量草稿", exception.Message, exception, _viewModel.ProjectPath);
            MessageBox.Show(this, exception.Message, "无法加入批量草稿", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SelectDivisionTags_Click(object sender, RoutedEventArgs e) { if (_viewModel.DivisionWorkspace is { } vm && sender is FrameworkElement b) foreach (var o in vm.TagOptions) o.IsSelected = Equals(b.Tag,"True"); }
    internal void SelectFilterDimension_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: UnitFilterDimensionViewModel dimension } b) foreach (var o in dimension.Options.Where(o => o.DimensionKey != "category" || Localisation.GameText.VisibleCategory(o.Value))) o.IsSelected = Equals(b.Tag,"True"); }
    internal void RemoveUnitChoice_Click(object sender, RoutedEventArgs e) { if(sender is FrameworkElement {DataContext:UnitChoiceToggleViewModel choice})choice.IsSelected=false; }
    internal void ReconPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: UnitFieldViewModel field, SelectedItem: int value })
            field.EditValue = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    internal void SelectFieldChoices_Click(object sender, RoutedEventArgs e) { if(sender is FrameworkElement {DataContext:UnitFieldViewModel field} b && field.IsMultiChoiceEditor)field.SelectVisibleChoices(Equals(b.Tag,"True")); }
    internal void TransportFilter_Changed(object? sender, EventArgs e) { if (_viewModel.DivisionWorkspace is { } vm && sender is Controls.UnitMiniFilter filter) { vm.TransportFilter = filter.Matches; vm.TransportCandidatesView.Refresh(); } }
    internal void SelectTransports_Click(object sender, RoutedEventArgs e) { if (_viewModel.DivisionWorkspace is { } vm && sender is FrameworkElement b) foreach (var item in vm.TransportCandidatesView.Cast<DivisionTransportOptionViewModel>()) item.IsSelected = Equals(b.Tag,"True"); }

    internal void SelectAllDrafts_Click(object sender, RoutedEventArgs e) { if (_viewModel.UnitWorkspace is { } vm) foreach (var item in vm.DraftItems) item.IsSelected = true; RefreshDraftChecks(); }
    internal void ClearDraftSelection_Click(object sender, RoutedEventArgs e) { if (_viewModel.UnitWorkspace is { } vm) foreach (var item in vm.DraftItems) item.IsSelected = false; RefreshDraftChecks(); }
    internal void DraftGroupSelect_Click(object sender, RoutedEventArgs e) { if (sender is CheckBox { DataContext: System.Windows.Data.CollectionViewGroup group } box) { foreach (var item in group.Items.OfType<DraftItemViewModel>()) item.IsSelected = box.IsChecked == true; RefreshDraftChecks(); } }
    internal void DraftGroup_Loaded(object sender, RoutedEventArgs e) { if (sender is CheckBox box) UpdateDraftCheck(box); }
    private static void UpdateDraftCheck(CheckBox box) { if (box.DataContext is System.Windows.Data.CollectionViewGroup group) { var items = group.Items.OfType<DraftItemViewModel>().ToArray(); box.IsChecked = items.All(i => i.IsSelected) ? true : items.Any(i => i.IsSelected) ? null : false; } }
    internal void DraftSelection_Click(object sender, RoutedEventArgs e) => RefreshDraftChecks();
    private void RefreshDraftChecks() { foreach (var box in VisualChildren<CheckBox>(DraftPage.DraftOverviewGrid)) UpdateDraftCheck(box); }
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject { for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++) { var child = System.Windows.Media.VisualTreeHelper.GetChild(parent,i); if (child is T match) yield return match; foreach (var next in VisualChildren<T>(child)) yield return next; } }
    internal async void DeleteSelectedDrafts_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.UnitWorkspace is not { } vm) return;
        var ids = vm.DraftItems.Where(i => i.IsSelected).Select(i => i.Resolved.Operation.Id).ToHashSet(); if (ids.Count == 0) return;
        if (MessageBox.Show(this, $"删除选中的 {ids.Count} 项草稿？", "删除草稿", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { if (_viewModel.WeaponWorkspace is { } weapon) await weapon.FlushAsync(); if (_viewModel.AmmoWorkspace is { } ammo) await ammo.FlushAsync(); if (_viewModel.DivisionWorkspace is { } division) await division.FlushAsync(); if (_viewModel.RulesWorkspace is { } rules) await rules.FlushAsync();
            if (_viewModel.StrategicWorkspace is { } strategic) await strategic.FlushAsync(); await vm.RemoveSelectedDraftsAsync(ids); _viewModel.RulesWorkspace?.Restore(); _viewModel.StrategicWorkspace?.Restore(); _viewModel.WeaponWorkspace?.RefreshFromDrafts(); _viewModel.AmmoWorkspace?.RefreshFromDrafts(); _viewModel.DivisionWorkspace?.RefreshFromDrafts(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "删除草稿失败"); }
    }

    internal async void PreviewApply_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.UnitWorkspace is not { } workspace)
        {
            return;
        }

        IReadOnlySet<string>? selectedIds = sender is FrameworkElement { Tag: "selected" } ? workspace.DraftItems.Where(i => i.IsSelected).Select(i => i.Resolved.Operation.Id).ToHashSet() : null;
        if (selectedIds?.Count == 0) { MessageBox.Show(this, "请先选择草稿", "草稿"); return; }
        ApplyPreview preview;
        try
        {
            preview = await workspace.PrepareApplyAsync(async () =>
            {
                if (_viewModel.WeaponWorkspace is { } weaponWorkspace)
                {
                    await weaponWorkspace.FlushAsync();
                }
                if (_viewModel.AmmoWorkspace is { } ammoWorkspace)
                {
                    await ammoWorkspace.FlushAsync();
                }
                if (_viewModel.DivisionWorkspace is { } divisionWorkspace)
                {
                    await divisionWorkspace.FlushAsync();
                }
                if (_viewModel.RulesWorkspace is { } rulesWorkspace) await rulesWorkspace.FlushAsync();
                if (_viewModel.StrategicWorkspace is { } strategicWorkspace) { await strategicWorkspace.FlushAsync(); strategicWorkspace.SetTransactionLocked(true); }
            }, selectedIds);
            _viewModel.StrategicWorkspace?.SetTransactionLocked(true);
            _viewModel.WeaponWorkspace?.SetTransactionLocked(true);
            _viewModel.AmmoWorkspace?.SetTransactionLocked(true);
            _viewModel.DivisionWorkspace?.SetTransactionLocked(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _viewModel.StrategicWorkspace?.SetTransactionLocked(false);
            _viewModel.RecordModProblem("无法准备应用", exception.Message, exception, _viewModel.ProjectPath);
            MessageBox.Show(this, exception.Message, "无法应用", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var operationLines = preview.Operations.Take(12).Select(item =>
            _viewModel.AdvancedMode
                ? $"• {item.Summary}\n  {item.FieldPath}: {item.BaselineRaw} → {item.TargetRaw}"
                : $"• {item.Summary}" + (item.TargetKind == WarnoLiteModdingTool.Core.Drafts.DraftTargetKind.StrategicPlan ? "\n" + string.Join("\n", WarnoLiteModdingTool.Core.Strategic.StrategicDiff.Compare(item)) : ""));
        var omitted = preview.Operations.Count > 12 ? $"\n……另有 {preview.Operations.Count - 12} 项" : string.Empty;
        var files = string.Join("\n", preview.Files.Where(item => item.Kind != FormalTextFileKind.Log).Select(item => $"• {item.RelativePath}"));
        var message =
            $"将应用 {preview.Operations.Count} 项草稿到 {preview.FormalFileCount} 个正式文件。\n" +
            $"备份编号：{preview.BackupId}\n\n" +
            string.Join("\n", operationLines) + omitted +
            $"\n\n文件：\n{files}\n\n确认后才会创建备份并提交。";
        if (_viewModel.AdvancedMode ? new Advanced.DiffWindow(preview) { Owner = this }.ShowDialog() != true : MessageBox.Show(this, message, "确认应用草稿", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            workspace.CancelPreparedTransaction();
            _viewModel.WeaponWorkspace?.SetTransactionLocked(false);
            _viewModel.AmmoWorkspace?.SetTransactionLocked(false);
            _viewModel.DivisionWorkspace?.SetTransactionLocked(false);
            _viewModel.StrategicWorkspace?.SetTransactionLocked(false);
            return;
        }

        try
        {
            var result = await workspace.CommitApplyAsync(preview);
            _viewModel.WeaponWorkspace?.SetTransactionLocked(false);
            _viewModel.AmmoWorkspace?.SetTransactionLocked(false);
            _viewModel.DivisionWorkspace?.SetTransactionLocked(false);
            _viewModel.StrategicWorkspace?.SetTransactionLocked(false);
            var warning = result.Warnings.Count == 0 ? string.Empty : $"\n\n{string.Join("\n", result.Warnings)}";
            MessageBox.Show(
                this,
                $"应用完成。\n备份编号：{result.BackupId}\n日志：{result.LogRelativePath}{warning}",
                "应用完成",
                MessageBoxButton.OK,
                result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            workspace.CancelPreparedTransaction();
            _viewModel.WeaponWorkspace?.SetTransactionLocked(false);
            _viewModel.AmmoWorkspace?.SetTransactionLocked(false);
            _viewModel.DivisionWorkspace?.SetTransactionLocked(false);
            _viewModel.StrategicWorkspace?.SetTransactionLocked(false);
            _viewModel.RecordModProblem("应用草稿失败", exception.Message, exception, _viewModel.ProjectPath);
            MessageBox.Show(this, exception.Message, "应用失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal void WeaponScopeCheck_Click(object sender, RoutedEventArgs e) =>
        _viewModel.WeaponWorkspace?.ScopeSelectionChanged();

    internal void SelectVisibleWeaponScope_Click(object sender, RoutedEventArgs e) =>
        _viewModel.WeaponWorkspace?.SelectVisibleScopeUnits();

    internal void ClearWeaponScopeSelection_Click(object sender, RoutedEventArgs e) =>
        _viewModel.WeaponWorkspace?.ClearScopeSelection();

    internal void DeploymentPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: UnitFieldViewModel field, SelectedItem: DeploymentPresetViewModel preset })
        {
            field.EditValue = preset.Value;
            ((ComboBox)sender).SelectedIndex = -1;
        }
    }

    internal async void ReplaceWeapon_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.WeaponWorkspace is { } workspace)
        {
            try
            {
                await workspace.ReplaceWeaponAsync();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _viewModel.RecordModProblem("无法保存 Weapon 替换", exception.Message, exception, _viewModel.ProjectPath);
                MessageBox.Show(this, exception.Message, "无法保存 Weapon 替换", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    internal async void UndoWeaponField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WeaponFieldViewModel field } && _viewModel.WeaponWorkspace is { } workspace)
        {
            await workspace.UndoFieldAsync(field);
        }
    }

    internal async void UndoAmmoField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WeaponFieldViewModel field } && _viewModel.AmmoWorkspace is { } workspace)
        {
            await workspace.UndoFieldAsync(field);
        }
    }

    internal void AddDivisionRule_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DivisionWorkspace?.AddRule();

    internal void RemoveDivisionRule_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DivisionWorkspace?.RemoveSelectedRule();

    private void AddDivisionTransport_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DivisionWorkspace?.AddTransport();

    internal void ApplyDivisionTransportSelection_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DivisionWorkspace?.ApplyTransportSelection();

    internal void CancelDivisionTransportSelection_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DivisionWorkspace?.CancelTransportSelection();

    internal void RemoveDivisionTransport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DivisionSelectedTransportViewModel transport })
        {
            _viewModel.DivisionWorkspace?.RemoveTransport(transport.Name);
        }
    }

    internal void SelectDivisionType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string type })
        {
            _viewModel.DivisionWorkspace?.SelectType(type);
        }
    }

    private void AddDivisionPack_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DivisionWorkspace?.AddPack();

    private void RemoveDivisionPack_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DivisionWorkspace?.RemoveSelectedPack();

    private void MoveDivisionPackUp_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DivisionWorkspace?.MoveSelectedPack(-1);

    private void MoveDivisionPackDown_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DivisionWorkspace?.MoveSelectedPack(1);

    internal async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BackupItemViewModel item } ||
            _viewModel.UnitWorkspace is not { } workspace)
        {
            return;
        }

        RestorePreview preview;
        try
        {
            preview = workspace.PrepareRestore(item);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _viewModel.RecordModProblem("无法准备恢复", exception.Message, exception, _viewModel.ProjectPath);
            MessageBox.Show(this, exception.Message, "无法恢复", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var files = string.Join("\n", preview.Files.Where(file => file.Kind != FormalTextFileKind.Log).Select(file => $"• {file.RelativePath}"));
        var message =
            $"将恢复备份 {preview.SourceBackupId} 的应用前文件：\n{files}\n\n" +
            $"恢复前会先创建新备份 {preview.RestoreBackupId}。是否继续？";
        if (MessageBox.Show(this, message, "确认恢复", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            workspace.CancelPreparedTransaction();
            return;
        }

        try
        {
            var result = await workspace.CommitRestoreAsync(preview);
            MessageBox.Show(this, $"恢复完成。\n恢复前备份：{result.BackupId}\n日志：{result.LogRelativePath}", "恢复完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            workspace.CancelPreparedTransaction();
            _viewModel.RecordModProblem("恢复备份失败", exception.Message, exception, _viewModel.ProjectPath);
            MessageBox.Show(this, exception.Message, "恢复失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RunGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "将运行目标 Mod 自带的 GenerateMod.bat。程序不会自动启动游戏或上传。是否继续？", "运行 GenerateMod", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOfficialCommandAsync(_viewModel.RunGenerateAsync, "生成 / 编译 Mod", "官方生成流程已结束。请在输出中核对生成结果。");
    }

    private async void RunDevMode_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "将运行当前 Mod 的 LaunchGameDevMode.bat 并启动 WARNO 开发模式。是否继续？", "启动开发模式", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOfficialCommandAsync(_viewModel.RunDevModeAsync, "启动开发模式", "开发模式流程已结束。");
    }

    private async void RunUpload_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "将运行当前 Mod 的 UploadMod.bat。该官方脚本会上传到 Workshop，并在上传命令后自动创建一次官方备份。\n\n是否继续？",
                "上传 Mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOfficialCommandAsync(_viewModel.RunUploadAsync, "上传 Mod", "官方上传流程已结束。退出码不能单独证明 Workshop 上传成功，请核对完整输出。");
    }

    private async Task RunOfficialCommandAsync(Func<Task<OfficialCommandResult>> command, string label, string successMessage)
    {
        try
        {
            var result = await command();
            MessageBox.Show(
                this,
                result.Succeeded ? successMessage : $"{label}失败，退出码 {result.ExitCode}。详情已记录到问题中心，并保留在 Mod 工具输出中。",
                label,
                MessageBoxButton.OK,
                result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, exception.Message, $"{label}无法运行", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    internal void WorkspaceFilter_Changed(object? sender, EventArgs e)
    {
        if(sender is not Controls.FacetFilter f)return;
        if(Equals(f.Tag,"weapon") && _viewModel.WeaponWorkspace is {} w){w.UnitFilter=f.Matches;w.UnitsView.Refresh();}
        if(Equals(f.Tag,"ammo") && _viewModel.AmmoWorkspace is {} a){a.AmmoFilter=f.Matches;a.RefreshFilter();}
        if(Equals(f.Tag,"rules") && _viewModel.DivisionWorkspace is {} d){d.RuleFilter=f.Matches;d.RulesView.Refresh();}
    }
    internal void ProblemAction_Click(object sender,RoutedEventArgs e)
    {
        var list=ProblemPage.ProblemTabs.SelectedIndex==0?ProblemPage.ModProblemList:ProblemPage.ProblemTabs.SelectedIndex==1?ProblemPage.ToolProblemList:ProblemPage.IgnoredProblemList;
        var action=(sender as FrameworkElement)?.Tag?.ToString();
        if(action=="all"){list.SelectAll();return;}if(action=="none"){list.UnselectAll();return;}
        if(action=="restore"&&ProblemPage.ProblemTabs.SelectedIndex!=2 || action=="ignore"&&ProblemPage.ProblemTabs.SelectedIndex==2)return;
        try{_viewModel.IgnoreProblems(list.SelectedItems.Cast<ProblemItemViewModel>(),action=="restore");}catch(Exception ex){_viewModel.RecordToolProblem("更新问题状态失败",ex);}
    }
    internal void DivisionTag_Click(object sender,RoutedEventArgs e){if(sender is FrameworkElement {DataContext:DivisionTagOptionViewModel tag})_viewModel.DivisionWorkspace?.ChooseTag(tag.Value);}
    internal void StandoutUnits_Click(object sender,RoutedEventArgs e){if(_viewModel.DivisionWorkspace is not {} vm)return;var picker=new Controls.UnitSelectionWindow(vm.UnitOptions.Select(u=>(u.Name,u.DisplayName)),DivisionUnitRuleViewModel.Split(vm.StandoutUnitsText)){Owner=this};if(picker.ShowDialog()==true)vm.StandoutUnitsText=string.Join(", ",picker.SelectedIds);}
    internal void RemoveStandout_Click(object sender,RoutedEventArgs e){if(_viewModel.DivisionWorkspace is {} vm&&sender is FrameworkElement {DataContext:DivisionUnitOption unit})vm.StandoutUnitsText=string.Join(", ",DivisionUnitRuleViewModel.Split(vm.StandoutUnitsText).Where(id=>id!=unit.Name));}
    internal async void CreateUnit_Click(object sender,RoutedEventArgs e){try{await _viewModel.CreateUnitAsync(this,Equals((sender as FrameworkElement)?.Tag,"edit"));}catch(Exception ex){MessageBox.Show(this,ex.Message,"无法创建单位");}}
    private async Task OpenSelectedProjectAsync(string root)
    {
        if (!_viewModel.CanOpenProject) return;
        try
        {
            CommitActiveEditor();
            await _viewModel.OpenProjectAsync(root);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "无法切换项目", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void CommitActiveEditor()
    {
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox text)
        {
            text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (Validation.GetHasError(text)) throw new InvalidOperationException("请先修正当前输入。");
        }
    }

    private void OpenDrafts_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedModule = _viewModel.Modules.FirstOrDefault(module => module.Key == "drafts");
    }

    private bool _closeAfterFlush;
    private bool _closePending;
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_closeAfterFlush)
        {
            e.Cancel = true;
            if (_closePending) return;
            _closePending = true;
            try { CommitActiveEditor(); await _viewModel.SaveBeforeLeavingAsync(); _closeAfterFlush = true; _ = Dispatcher.BeginInvoke(new Action(() => { _closePending=false; try { Close(); } catch(Exception ex) { _closeAfterFlush=false; _viewModel.RecordToolProblem("关闭窗口失败", ex); } })); }
            catch (Exception exception) { _closePending=false; _viewModel.RecordToolProblem("草稿保存失败", exception); }
            return;
        }
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Dispose();
    }
}
