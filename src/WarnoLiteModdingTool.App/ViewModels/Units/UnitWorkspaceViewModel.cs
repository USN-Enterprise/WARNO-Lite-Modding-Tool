using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using WarnoLiteModdingTool.App.ViewModels.Backups;
using WarnoLiteModdingTool.App.ViewModels.Drafts;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Batch;

namespace WarnoLiteModdingTool.App.ViewModels.Units;

public sealed class UnitWorkspaceViewModel : ObservableObject
{
    private const string All = "全部";
    private readonly UnitWorkspaceData _data;
    private readonly WeaponWorkspaceData? _weaponData;
    private readonly DivisionWorkspaceData? _divisionData;
    private readonly DraftStore _draftStore;
    private readonly PendingFieldEdits<UnitFieldViewModel> _fieldEdits;
    private readonly Action<string> _setStatus;
    private readonly Func<Task> _reloadProject;
    private readonly UnitTransactionService _transactions = new();
    private readonly ICollectionView _unitsView;
    private readonly IReadOnlyDictionary<string, UnitFilterDimensionViewModel> _filterDimensions;
    private readonly object _pendingGate = new();
    private readonly HashSet<Task> _pendingEdits = [];
    private UnitListItemViewModel? _selectedUnit;
    private string _textFilter = string.Empty;
    private bool _isFilterPanelOpen;
    private int _visibleUnitCount;
    private string _draftLoadError;
    private bool _isTransactionBusy;
    private string _selectedBatchScope = "已勾选 Unit";
    private UnitBatchFieldOption? _selectedBatchField;
    private UnitBatchOperationOption? _selectedBatchOperation;
    private UnitBatchRoundingOption? _selectedBatchRounding;
    private string _batchOperand = string.Empty;
    private string _batchMinimum = string.Empty;
    private string _batchMaximum = string.Empty;
    private UnitBatchPreview? _batchPreview;
    private bool _suppressBatchSelectionNotifications;
    private string _filterMatchMode = "全部条件";

    public UnitWorkspaceViewModel(
        UnitWorkspaceData data,
        WeaponWorkspaceData? weaponData,
        DivisionWorkspaceData? divisionData,
        DraftStore draftStore,
        DraftLoadResult loadResult,
        Action<string> setStatus,
        Func<Task> reloadProject)
    {
        _data = data;
        _weaponData = weaponData;
        _divisionData = divisionData;
        _draftStore = draftStore;
        _setStatus = setStatus;
        _reloadProject = reloadProject;
        Units = new ObservableCollection<UnitListItemViewModel>(data.Units.Select(unit => new UnitListItemViewModel(unit, BatchSelectionChanged)));
        Fields = [];
        _fieldEdits = new(Fields, field => field.FlushAsync(), field => field.HasUnsavedEdit);
        FieldSections = [];
        CommonBatchFields = [];
        ActiveFilterTags = [];
        DraftItems = [];
        Backups = [];
        CoalitionOptions = Options(data.Units.Select(unit => unit.Coalition));
        CountryOptions = Options(data.Units.Select(unit => unit.Country));
        CategoryOptions = Options(data.Units.SelectMany(unit => SplitValues(unit.Category)));
        FactoryOptions = Options(data.Units.Select(unit => unit.Factory));
        RoleOptions = Options(data.Units.Select(unit => unit.Role));
        DivisionOptions = Options(data.Units.SelectMany(unit => unit.Divisions));
        WeaponOptions = Options(data.Units.SelectMany(unit => unit.Weapons));
        AmmoOptions = Options(data.Units.SelectMany(unit => unit.Ammunition));
        DraftOptions = [All, "有草稿", "无草稿", "冲突"];
        FilterMatchModes = ["全部条件", "任一条件"];
        FilterDimensions =
        [
            new UnitFilterDimensionViewModel("coalition", "阵营", data.Units.Select(unit => unit.Coalition), FilterOptionChanged),
            new UnitFilterDimensionViewModel("country", "国家", data.Units.Select(unit => unit.Country), FilterOptionChanged),
            new UnitFilterDimensionViewModel("category", "单位类别", data.Units.SelectMany(unit => SplitValues(unit.Category)), FilterOptionChanged),
            new UnitFilterDimensionViewModel("factory", "生产栏位", data.Units.Select(unit => unit.Factory), FilterOptionChanged),
            new UnitFilterDimensionViewModel("role", "角色", data.Units.Select(unit => unit.Role), FilterOptionChanged),
            new UnitFilterDimensionViewModel("division", "所属师", data.Units.SelectMany(unit => unit.Divisions), FilterOptionChanged),
            new UnitFilterDimensionViewModel("draft", "草稿状态", ["有草稿", "无草稿", "冲突"], FilterOptionChanged)
        ];
        _filterDimensions = FilterDimensions.ToDictionary(dimension => dimension.Key, StringComparer.Ordinal);
        BatchScopeOptions = ["已勾选 Unit", "当前筛选结果"];
        BatchFieldOptions = UnitFieldDefinitions.All
            .Where(definition => data.Units.Any(unit => unit.Field(definition.Key)?.CanEdit == true))
            .Select(definition => new UnitBatchFieldOption(definition))
            .ToArray();
        BatchOperationOptions = [];
        BatchRoundingOptions =
        [
            new("不取整", UnitBatchRounding.None),
            new("四舍五入", UnitBatchRounding.Nearest),
            new("向下取整", UnitBatchRounding.Floor),
            new("向上取整", UnitBatchRounding.Ceiling)
        ];
        _selectedBatchRounding = BatchRoundingOptions[3];
        _unitsView = new ListCollectionView(Units);
        _unitsView.Filter = FilterUnit;
        _draftLoadError = loadResult.Error ?? string.Empty;
        RefreshDraftState();
        RefreshBackups();
        RefreshFilter();
        SelectedUnit = Units.FirstOrDefault();
        SelectedBatchField = BatchFieldOptions.FirstOrDefault();
    }

    public ObservableCollection<UnitListItemViewModel> Units { get; }

    public UnitWorkspaceData Data => _data;
    public IEnumerable<UnitBatchFieldOption> VisibleBatchFields => BatchFieldOptions.Where(f => Advanced.EditorMode.CanEdit(f.Definition.Key));
    public IEnumerable<UnitBatchOperationOption> VisibleBatchOperations => BatchOperationOptions.Where(o => Advanced.EditorMode.IsAdvanced || o.Operation is UnitBatchOperation.Set or UnitBatchOperation.IncreasePercent or UnitBatchOperation.DecreasePercent);
    public void RefreshMode()
    {
        foreach (var field in Fields) field.RefreshMode();
        var expanded=FieldSections.ToDictionary(s=>s.Title,s=>s.IsExpanded);
        foreach(var section in FieldSections)section.PropertyChanged-=SectionExpansionChanged;
        FieldSections.Clear();
        foreach(var section in FieldSectionBuilder.Build(Fields.Where(f=>f.IsVisible),f=>f.Section,f=>f.Group))
        {section.IsExpanded=expanded.GetValueOrDefault(section.Title);section.PropertyChanged+=SectionExpansionChanged;FieldSections.Add(section);}
        foreach(var option in FilterDimensions.Where(d=>d.Key=="category").SelectMany(d=>d.Options).Where(o=>!Localisation.GameText.VisibleCategory(o.Value))) option.IsSelected=false;
        OnPropertyChanged(nameof(VisibleBatchFields));
        if (SelectedBatchField is not null && !VisibleBatchFields.Contains(SelectedBatchField)) SelectedBatchField = VisibleBatchFields.FirstOrDefault();
        RebuildCommonBatchFields();
        if (SelectedBatchOperation is not null && !VisibleBatchOperations.Contains(SelectedBatchOperation)) SelectedBatchOperation = VisibleBatchOperations.FirstOrDefault();
        OnPropertyChanged(nameof(VisibleBatchOperations));
        OnPropertyChanged(nameof(ShowsBatchFormulaOptions));
    }
    public WarnoLiteModdingTool.Core.Strategic.StrategicWorkspace? StrategicData { get; set; }

    public ObservableCollection<UnitFieldViewModel> Fields { get; }

    public ObservableCollection<FieldSectionViewModel<UnitFieldViewModel>> FieldSections { get; }

    public ObservableCollection<UnitBatchCommonFieldViewModel> CommonBatchFields { get; }

    private bool _isBatchToolsOpen;
    public bool IsBatchToolsOpen
    {
        get => _isBatchToolsOpen;
        set { if (SetProperty(ref _isBatchToolsOpen, value)) OnPropertyChanged(nameof(HasBatchInspector)); }
    }
    public bool HasBatchInspector => BatchSelectedCount >= 2 || IsBatchToolsOpen;

    public ObservableCollection<UnitFilterTagViewModel> ActiveFilterTags { get; }

    public IReadOnlyList<UnitFilterDimensionViewModel> FilterDimensions { get; }

    public ObservableCollection<DraftItemViewModel> DraftItems { get; }

    public ObservableCollection<BackupItemViewModel> Backups { get; }

    public ICollectionView UnitsView => _unitsView;

    public IReadOnlyList<string> CoalitionOptions { get; }

    public IReadOnlyList<string> CountryOptions { get; }

    public IReadOnlyList<string> CategoryOptions { get; }

    public IReadOnlyList<string> FactoryOptions { get; }

    public IReadOnlyList<string> RoleOptions { get; }

    public IReadOnlyList<string> DivisionOptions { get; }

    public IReadOnlyList<string> WeaponOptions { get; }

    public IReadOnlyList<string> AmmoOptions { get; }

    public IReadOnlyList<string> DraftOptions { get; }

    public IReadOnlyList<string> FilterMatchModes { get; }

    public string FilterMatchMode
    {
        get => _filterMatchMode;
        set
        {
            if (SetProperty(ref _filterMatchMode, value))
            {
                RefreshFilter();
            }
        }
    }

    public IReadOnlyList<string> BatchScopeOptions { get; }

    public IReadOnlyList<UnitBatchFieldOption> BatchFieldOptions { get; }

    public ObservableCollection<UnitBatchOperationOption> BatchOperationOptions { get; }

    public IReadOnlyList<UnitBatchRoundingOption> BatchRoundingOptions { get; }

    public string DraftLoadError
    {
        get => _draftLoadError;
        private set
        {
            if (SetProperty(ref _draftLoadError, value))
            {
                OnPropertyChanged(nameof(HasDraftLoadError));
                OnPropertyChanged(nameof(CanClearDrafts));
            }
        }
    }

    public bool HasDraftLoadError => DraftLoadError.Length > 0;

    public int DraftCount => DraftItems.Count;

    public bool HasDrafts => DraftCount > 0;

    public bool CanClearDrafts => HasDrafts || HasDraftLoadError;

    public bool IsTransactionBusy
    {
        get => _isTransactionBusy;
        private set
        {
            if (SetProperty(ref _isTransactionBusy, value))
            {
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanRestoreBackups));
                OnPropertyChanged(nameof(CanPreviewBatch));
                OnPropertyChanged(nameof(CanAddBatchDrafts));
            }
        }
    }

    public bool CanApply =>
        HasDrafts &&
        !HasDraftLoadError &&
        !IsTransactionBusy &&
        DraftItems.All(item => item.Resolved.Status == DraftResolutionStatus.Active);

    public bool CanRestoreBackups => !HasDrafts && !IsTransactionBusy;

    public UnitListItemViewModel? SelectedUnit
    {
        get => _selectedUnit;
        set
        {
            if (SetProperty(ref _selectedUnit, value))
            {
                RebuildFields();
            }
        }
    }

    public int VisibleUnitCount
    {
        get => _visibleUnitCount;
        private set => SetProperty(ref _visibleUnitCount, value);
    }

    public string TextFilter
    {
        get => _textFilter;
        set
        {
            if (SetProperty(ref _textFilter, value ?? string.Empty))
            {
                RefreshFilter();
            }
        }
    }

    public bool IsFilterPanelOpen
    {
        get => _isFilterPanelOpen;
        set => SetProperty(ref _isFilterPanelOpen, value);
    }

    public int ActiveFilterCount => ActiveFilterTags.Count;

    public bool HasActiveFilters => ActiveFilterCount > 0;

    public string SelectedBatchScope
    {
        get => _selectedBatchScope;
        set
        {
            if (SetProperty(ref _selectedBatchScope, value))
            {
                InvalidateBatchPreview();
                OnPropertyChanged(nameof(BatchTargetCount));
                OnPropertyChanged(nameof(CanPreviewBatch));
            }
        }
    }

    public UnitBatchFieldOption? SelectedBatchField
    {
        get => _selectedBatchField;
        set
        {
            if (!SetProperty(ref _selectedBatchField, value))
            {
                return;
            }

            RebuildBatchOperations();
            InvalidateBatchPreview();
            OnPropertyChanged(nameof(BatchValueChoices));
            OnPropertyChanged(nameof(IsBatchChoiceEditor));
            OnPropertyChanged(nameof(IsBatchTextEditor));
            OnPropertyChanged(nameof(CanPreviewBatch));
        }
    }

    public UnitBatchOperationOption? SelectedBatchOperation
    {
        get => _selectedBatchOperation;
        set
        {
            if (SetProperty(ref _selectedBatchOperation, value))
            {
                InvalidateBatchPreview();
                OnPropertyChanged(nameof(ShowsBatchFormulaOptions));
                OnPropertyChanged(nameof(BatchOperandHint));
                OnPropertyChanged(nameof(CanPreviewBatch));
            }
        }
    }

    public UnitBatchRoundingOption? SelectedBatchRounding
    {
        get => _selectedBatchRounding;
        set
        {
            if (SetProperty(ref _selectedBatchRounding, value))
            {
                InvalidateBatchPreview();
            }
        }
    }

    public string BatchOperand
    {
        get => _batchOperand;
        set
        {
            if (SetProperty(ref _batchOperand, value ?? string.Empty))
            {
                InvalidateBatchPreview();
                OnPropertyChanged(nameof(CanPreviewBatch));
            }
        }
    }

    public string BatchMinimum
    {
        get => _batchMinimum;
        set
        {
            if (SetProperty(ref _batchMinimum, value ?? string.Empty))
            {
                InvalidateBatchPreview();
            }
        }
    }

    public string BatchMaximum
    {
        get => _batchMaximum;
        set
        {
            if (SetProperty(ref _batchMaximum, value ?? string.Empty))
            {
                InvalidateBatchPreview();
            }
        }
    }

    public IReadOnlyList<string> BatchValueChoices => SelectedBatchField is null
        ? []
        : _data.Units
            .Select(unit => unit.Field(SelectedBatchField.Definition.Key))
            .Where(field => field?.CanEdit == true)
            .SelectMany(field => field!.Choices)
            .Select(choice => choice.Display)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public bool IsBatchChoiceEditor => SelectedBatchField?.Definition.EditorKind == UnitEditorKind.Choice;

    public bool IsBatchTextEditor => !IsBatchChoiceEditor;

    public bool ShowsBatchFormulaOptions => Advanced.EditorMode.IsAdvanced && SelectedBatchOperation?.Operation != UnitBatchOperation.Set;

    public string BatchOperandHint => SelectedBatchOperation?.Operation switch
    {
        UnitBatchOperation.Set => "目标值",
        UnitBatchOperation.Multiply => "系数，例如 1.1",
        UnitBatchOperation.Add => "增加的数值，可为负数",
        UnitBatchOperation.IncreasePercent => "增加百分比，例如 10",
        UnitBatchOperation.DecreasePercent => "减少百分比，例如 10",
        _ => "参数"
    };

    public int BatchSelectedCount => Units.Count(unit => unit.IsBatchSelected);

    public int BatchTargetCount => SelectedBatchScope == "当前筛选结果"
        ? _unitsView.Cast<object>().Count()
        : BatchSelectedCount;

    public bool CanPreviewBatch =>
        !IsTransactionBusy &&
        SelectedBatchField is not null &&
        SelectedBatchOperation is not null &&
        BatchOperand.Trim().Length > 0 &&
        BatchTargetCount > 0;

    public bool HasBatchPreview => _batchPreview is not null;

    public bool CanAddBatchDrafts => SelectedBatchField is not null && !IsTransactionBusy && !_draftStore.IsBlocked;

    public string BatchPreviewSummary => _batchPreview is null
        ? "先预览，不会写入草稿或正式文件。"
        : $"目标 {_batchPreview.TargetCount:N0} · 兼容 {_batchPreview.CompatibleCount:N0} · 将变化 {_batchPreview.ChangedCount:N0} · 不变/不可编辑 {_batchPreview.UnchangedCount:N0}";

    public string BatchSamplesText => _batchPreview is null || _batchPreview.Samples.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, _batchPreview.Samples.Select(sample => $"{sample.DisplayName}：{sample.CurrentValue} → {sample.TargetValue}"));

    public string BatchImpactText => _batchPreview is null
        ? string.Empty
        : $"关联影响：师 {_batchPreview.Impact.DivisionCount:N0} · Weapon {_batchPreview.Impact.WeaponCount:N0} · Ammo {_batchPreview.Impact.AmmoCount:N0}";

    public string BatchMessagesText => _batchPreview is null
        ? string.Empty
        : string.Join(Environment.NewLine, _batchPreview.Errors.Select(error => $"错误：{error}")
            .Concat(_batchPreview.Warnings.Select(warning => $"提示：{warning}")));

    public bool HasBatchMessages => BatchMessagesText.Length > 0;

    public void SelectVisibleForBatch()
    {
        _suppressBatchSelectionNotifications = true;
        try
        {
            foreach (var unit in _unitsView.Cast<UnitListItemViewModel>())
            {
                unit.IsBatchSelected = true;
            }
        }
        finally
        {
            _suppressBatchSelectionNotifications = false;
        }

        BatchSelectionChanged();
    }

    public void ClearBatchSelection()
    {
        _suppressBatchSelectionNotifications = true;
        try
        {
            foreach (var unit in Units.Where(unit => unit.IsBatchSelected))
            {
                unit.IsBatchSelected = false;
            }
        }
        finally
        {
            _suppressBatchSelectionNotifications = false;
        }

        IsBatchToolsOpen = false;
        BatchSelectionChanged();
    }

    public UnitBatchPreview PreviewBatch()
    {
        var targets = BatchTargets();
        var preview = UnitBatchPlanner.Preview(new UnitBatchRequest(
            Guid.NewGuid().ToString("N"),
            targets.Select(item => item.Unit).ToArray(),
            SelectedBatchField?.Definition.Key ?? string.Empty,
            SelectedBatchOperation?.Operation ?? UnitBatchOperation.Set,
            BatchOperand,
            SelectedBatchRounding?.Rounding ?? UnitBatchRounding.None,
            BatchMinimum,
            BatchMaximum,
            _draftStore.Operations));
        _batchPreview = preview;
        RaiseBatchPreviewProperties();
        _setStatus(preview.Errors.Count > 0
            ? $"批量预览有 {preview.Errors.Count:N0} 项错误，尚未写入草稿"
            : $"批量预览完成 · 将变化 {preview.ChangedCount:N0} 个 Unit · 尚未写入草稿");
        return preview;
    }

    public async Task AddBatchToDraftsAsync()
    {
        var fresh = PreviewBatch();
        if (!fresh.CanAddToDrafts) throw new InvalidOperationException(string.Join("\n", fresh.Errors.DefaultIfEmpty("没有可加入的修改。")));

        if (IsTransactionBusy)
        {
            throw new InvalidOperationException("事务处理中，暂时不能修改草稿。");
        }

        var changed = fresh.ChangedCount;
        await SaveBatchAsync(fresh.Upserts, fresh.RemoveOperationIds);
        InvalidateBatchPreview();
        RefreshDraftState();
        RebuildFields();
        _setStatus($"批量结果已加入草稿 · {changed:N0} 项 · 正式 Mod 文件未改变");
    }

    private async Task SaveBatchAsync(IReadOnlyList<DraftOperation> upserts,IReadOnlyList<string> removals)
    {
        var result=upserts.ToList(); var removed=removals.ToList();
        if(!Advanced.EditorMode.IsAdvanced)
        {
            foreach(var old in _draftStore.Operations.Where(o=>removals.Contains(o.Id)&&o.FieldKey is "recon.vision.standard" or "recon.optics.standard"))
            {
                var prefix=old.FieldKey[..^8];removed.AddRange(_draftStore.Operations.Where(o=>o.ObjectName==old.ObjectName&&o.FieldKey.StartsWith(prefix,StringComparison.Ordinal)).Select(o=>o.Id));
            }
            foreach(var op in upserts.Where(o=>o.FieldKey is "recon.vision.standard" or "recon.optics.standard"))
            {
                var unit=Units.Single(u=>u.InternalName==op.ObjectName).Unit;
                var creation=_draftStore.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.UnitCreate&&o.ObjectName==unit.Name);
                if(creation is not null)unit=_data.Units.Single(u=>u.Name==UnitCreation.Read(creation).Mother);
                var linked=VisionRatio.Preview(unit,op.FieldKey,op.TargetValue,_draftStore.Operations);
                result.Remove(op);result.AddRange(linked.Upserts.Select(o=>o with {ObjectName=op.ObjectName,Id=DraftOperation.CreateId(o.TargetKind,o.RelativeSourceFile,op.ObjectName,o.FieldKey)}));removed.AddRange(linked.RemoveOperationIds);
            }
        }
        foreach(var creation in _draftStore.Operations.Where(o=>o.TargetKind==DraftTargetKind.UnitCreate))
        {
            var changes=result.Where(o=>o.ObjectName==creation.ObjectName).ToArray();if(changes.Length==0)continue;
            var state=UnitCreation.Read(creation);var fields=new Dictionary<string,string>(state.Fields);foreach(var change in changes)fields[change.FieldKey]=change.TargetValue;
            state=state with {Fields=fields};var mother=_data.Units.Single(u=>u.Name==state.Mother);_=UnitCreation.Project(mother,state,creation.BaselineRaw);
            result.RemoveAll(o=>o.ObjectName==creation.ObjectName);result.Add(UnitCreation.Operation(mother,state,creation.BaselineRaw));
        }
        await _draftStore.ApplyBatchAsync(result,removed);
    }

    public async Task AddCommonBatchFieldAsync(UnitBatchCommonFieldViewModel common)
    {
        if (!common.CanAddDraft) { common.StatusText = "请输入目标值"; return; }
        var targets = Units.Where(item => item.IsBatchSelected).Select(item => item.Unit).ToArray();
        if (targets.Length < 2)
        {
            return;
        }

        var preview = UnitBatchPlanner.Preview(new UnitBatchRequest(
            Guid.NewGuid().ToString("N"),
            targets,
            common.Definition.Key,
            UnitBatchOperation.Set,
            common.EditValue,
            UnitBatchRounding.Ceiling,
            null,
            null,
            _draftStore.Operations));
        if (!preview.CanAddToDrafts)
        {
            common.StatusText = string.Join("；", preview.Errors.Concat(preview.Warnings));
            return;
        }

        await SaveBatchAsync(preview.Upserts, preview.RemoveOperationIds);
        common.StatusText = $"已加入 {preview.ChangedCount} 项草稿";
        RefreshDraftState();
        RebuildFields();
        RebuildCommonBatchFields();
        _setStatus($"共有字段已加入草稿 · {preview.ChangedCount} 个 Unit · 正式文件未改变");
    }

    public async Task UndoFieldAsync(UnitFieldViewModel field)
    {
        var operation = _draftStore.Operations.FirstOrDefault(item =>
            item.ObjectName == field.Unit.Name && item.FieldKey == field.Key);
        if (operation is null)
        {
            return;
        }

        try
        {
            if (operation.GroupId?.StartsWith("vision:") == true || operation.GroupId?.StartsWith("armor:") == true)
                await _draftStore.ApplyBatchAsync([], _draftStore.Operations.Where(o => o.GroupId == operation.GroupId).Select(o => o.Id).ToArray());
            else await _draftStore.RemoveAsync(operation.Id);
            field.MarkPersisted(null, field.BaseValue, "已撤销草稿");
            RefreshDraftState();
            _setStatus("已撤销一项草稿");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _setStatus($"撤销失败：{exception.Message}");
        }
    }

    public async Task UndoDraftAsync(DraftItemViewModel item)
    {
        try
        {
            await _draftStore.RemoveAsync(item.Resolved.Operation.Id);
            RefreshDraftState();
            RebuildFields();
            _setStatus("已移除选中的草稿");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _setStatus($"移除失败：{exception.Message}");
        }
    }

    public void RefreshExternalDraftState()
    {
        RefreshDraftState();
        RebuildFields();
    }

    public async Task RemoveSelectedDraftsAsync(IReadOnlySet<string> ids)
    {
        await WaitForPendingEditsAsync();
        await _draftStore.ApplyBatchAsync([], ids.ToArray());
        RefreshExternalDraftState();
    }

    public async Task ClearDraftsAsync()
    {
        try
        {
            await _draftStore.ClearAsync();
            DraftLoadError = string.Empty;
            RefreshDraftState();
            RebuildFields();
            _setStatus("已清空本工具草稿；正式 Mod 文件未改变");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _setStatus($"清空失败：{exception.Message}");
        }
    }

    public async Task FlushAsync()
    {
        await _fieldEdits.FlushAsync();
        await WaitForPendingEditsAsync();
    }

    public async Task<ApplyPreview> PrepareApplyAsync(Func<Task>? flushRelatedEditors = null, IReadOnlySet<string>? selectedIds = null)
    {
        if (IsTransactionBusy)
        {
            throw new InvalidOperationException("已有事务正在进行。");
        }

        SetTransactionBusy(true);
        try
        {
            _setStatus("正在刷新未提交编辑并构建应用预览…");
            await Task.Yield();
            if (flushRelatedEditors is not null)
            {
                await flushRelatedEditors();
            }

            await FlushAsync();
            RefreshDraftState();
            if (!HasDrafts || DraftItems.Any(item => (selectedIds == null || selectedIds.Contains(item.Resolved.Operation.Id)) && item.Resolved.Status == DraftResolutionStatus.Conflict))
            {
                throw new TransactionValidationException("没有可安全应用的草稿，或草稿仍存在冲突。");
            }

            _setStatus("正在重新索引并校验应用候选…");
            var projectRoot = _draftStore.ProjectRoot;
            var operations = _draftStore.Operations.Where(o => selectedIds == null || selectedIds.Contains(o.Id)).ToArray();
            if (operations.Length == 0) throw new TransactionValidationException("请先选择草稿");
            return await Task.Run(() => _transactions.PrepareApplyAsync(projectRoot, operations));
        }
        catch
        {
            SetTransactionBusy(false);
            throw;
        }
    }

    public void CancelPreparedTransaction()
    {
        SetTransactionBusy(false);
        _setStatus("已取消本次事务；正式文件未改变");
    }

    public async Task<TransactionResult> CommitApplyAsync(ApplyPreview preview)
    {
        try
        {
            _setStatus($"正在应用 · 备份 {preview.BackupId}");
            var result = await _transactions.CommitApplyAsync(preview, _draftStore);
            _setStatus($"{result.Message} · 备份 {result.BackupId}");
            await _reloadProject();
            return result;
        }
        catch
        {
            RefreshBackups();
            throw;
        }
        finally
        {
            SetTransactionBusy(false);
        }
    }

    public RestorePreview PrepareRestore(BackupItemViewModel item)
    {
        if (HasDrafts)
        {
            throw new TransactionValidationException("请先应用或清空当前草稿，再恢复历史备份。");
        }

        if (!item.CanRestore || IsTransactionBusy)
        {
            throw new TransactionValidationException("该备份当前不可恢复。");
        }

        SetTransactionBusy(true);
        try
        {
            _setStatus($"正在检查备份 {item.BackupId}");
            return _transactions.PrepareRestore(_draftStore.ProjectRoot, item.BackupId);
        }
        catch
        {
            SetTransactionBusy(false);
            throw;
        }
    }

    public async Task<TransactionResult> CommitRestoreAsync(RestorePreview preview)
    {
        try
        {
            _setStatus($"正在恢复 · 新备份 {preview.RestoreBackupId}");
            var result = await _transactions.CommitRestoreAsync(preview);
            _setStatus($"{result.Message} · 恢复前备份 {result.BackupId}");
            await _reloadProject();
            return result;
        }
        catch
        {
            RefreshBackups();
            throw;
        }
        finally
        {
            SetTransactionBusy(false);
        }
    }

    private async Task PersistFieldAsync(UnitFieldViewModel fieldViewModel)
    {
        var input = fieldViewModel.EditValue;
        var creation=_draftStore.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.UnitCreate&&o.ObjectName==fieldViewModel.Unit.Name);
        if(creation is not null){var state=UnitCreation.Read(creation);var mother=_data.Units.Single(u=>u.Name==state.Mother);if(fieldViewModel.IsName)state=state with{Name=input};else {var fields=new Dictionary<string,string>(state.Fields){[fieldViewModel.Key]=input};if(fieldViewModel.Key=="armor.front.family")foreach(var side in new[]{"side","rear","top"})fields["armor."+side+".family"]=input;
            if (!Advanced.EditorMode.IsAdvanced && fieldViewModel.Key is "recon.vision.standard" or "recon.optics.standard")
            {
                var linked=VisionRatio.Preview(mother,fieldViewModel.Key,input,[]);
                var prefix=fieldViewModel.Key[..^8];
                foreach(var height in new[]{"standard","low","high"}) fields[prefix+height]=mother.Field(prefix+height)!.DisplayValue;
                foreach(var op in linked.Upserts)fields[op.FieldKey]=op.TargetValue;
            }
            state=state with{Fields=fields};}_=UnitCreation.Project(mother,state,creation.BaselineRaw);var createOp=UnitCreation.Operation(mother,state,creation.BaselineRaw);await _draftStore.UpsertAsync(createOp);fieldViewModel.MarkPersisted(null,input,"草稿已保存");RefreshDraftState();return;}
        if(fieldViewModel.Key=="armor.front.family")
        {
            var family=_data.DamageResistance.ResistanceFamilies.FirstOrDefault(f=>f.Name==NdfSyntaxDocument.Leaf(input));
            var upserts=new List<DraftOperation>();var removes=new List<string>();
            foreach(var side in new[]{"front","side","rear","top"})
            {
                var key="armor."+side;
                var value=_draftStore.Operations.LastOrDefault(o=>o.ObjectName==fieldViewModel.Unit.Name&&o.FieldKey==key)?.TargetValue ?? fieldViewModel.Unit.Field(key)?.DisplayValue;
                if(family is null || !int.TryParse(value,out var index) || index<1 || index>family.MaximumIndex || family.Name=="ResistanceFamily_infanterie"&&index!=1){fieldViewModel.RevertAfterFailure("四个方向的护甲数值必须适用于所选类型，请先分别调整数值。");return;}
                var preview=UnitBatchPlanner.Preview(new(Guid.NewGuid().ToString("N"),[fieldViewModel.Unit],key+".family",UnitBatchOperation.Set,input,UnitBatchRounding.None,null,null,_draftStore.Operations));
                if(preview.Errors.Count>0){fieldViewModel.RevertAfterFailure(string.Join("\n",preview.Errors));return;}
                upserts.AddRange(preview.Upserts);removes.AddRange(preview.RemoveOperationIds);
            }
            var group="armor:"+Guid.NewGuid().ToString("N");
            await _draftStore.ApplyBatchAsync(upserts.Select(o=>o with {GroupId=group}).ToArray(),removes);
            if (fieldViewModel.IsCurrentEdit(input)) fieldViewModel.MarkPersisted(_draftStore.Operations.LastOrDefault(o => o.ObjectName == fieldViewModel.Unit.Name && o.FieldKey == fieldViewModel.Key), input, "草稿已保存");
            RefreshDraftState();RebuildFields();return;
        }
        if (!Advanced.EditorMode.IsAdvanced && fieldViewModel.Key is "recon.vision.standard" or "recon.optics.standard")
        {
            try
            {
                var preview = VisionRatio.Preview(fieldViewModel.Unit, fieldViewModel.Key, input, _draftStore.Operations);
                await _draftStore.ApplyBatchAsync(preview.Upserts, preview.RemoveOperationIds);
                if (fieldViewModel.IsCurrentEdit(input)) fieldViewModel.MarkPersisted(_draftStore.Operations.LastOrDefault(o => o.ObjectName == fieldViewModel.Unit.Name && o.FieldKey == fieldViewModel.Key), input, "草稿已保存");
                RefreshDraftState(); RebuildFields();
            }
            catch (Exception ex) { fieldViewModel.RevertAfterFailure(ex.Message); }
            return;
        }
        string normalized;
        string targetRaw;
        string error;
        DraftOperation operation;

        if (fieldViewModel.IsName)
        {
            normalized = input.Trim();
            if (normalized.Length == 0)
            {
                if (fieldViewModel.IsCurrentEdit(input))
                {
                    fieldViewModel.RevertAfterFailure("名称不能为空");
                }
                return;
            }

            targetRaw = normalized;
            var existingNameDraft = _draftStore.Operations.FirstOrDefault(item =>
                item.ObjectName == fieldViewModel.Unit.Name && item.FieldKey == fieldViewModel.Key);
            var token = existingNameDraft?.NameToken ?? fieldViewModel.Unit.NameToken;
            var requiresTokenChange = fieldViewModel.Unit.NameTokenRequiresReplacement || existingNameDraft?.RequiresNameTokenChange == true;
            if (requiresTokenChange && existingNameDraft is null)
            {
                token = _data.Localisation.GenerateToken();
            }

            var relative = fieldViewModel.Unit.UnitsCsvRelativePath!;
            operation = new DraftOperation(
                DraftOperation.CreateId(DraftTargetKind.UnitName, relative, fieldViewModel.Unit.Name, fieldViewModel.Key),
                $"name:{fieldViewModel.Unit.Name}",
                DraftTargetKind.UnitName,
                "units",
                relative,
                fieldViewModel.Unit.Name,
                fieldViewModel.Unit.Source.TypeName,
                fieldViewModel.Key,
                "UNITS.csv.REFTEXT",
                "String",
                fieldViewModel.BaseValue,
                fieldViewModel.RawValue,
                normalized,
                targetRaw,
                $"{fieldViewModel.Unit.DisplayName} · 游戏内名称：{fieldViewModel.BaseValue} → {normalized}",
                token,
                requiresTokenChange,
                DateTimeOffset.UtcNow,
                fieldViewModel.Unit.NameToken);
        }
        else
        {
            if (!UnitValueConverter.TryFormatTarget(fieldViewModel.Field!, input, out normalized, out targetRaw, out error))
            {
                if (fieldViewModel.IsCurrentEdit(input))
                {
                    fieldViewModel.RevertAfterFailure(error);
                }
                return;
            }

            if (fieldViewModel.Field!.Definition.ValueKind == UnitValueKind.UnitReference &&
                WouldCreateUpgradeCycle(fieldViewModel.Unit.Name, NdfSyntaxDocument.Leaf(targetRaw)))
            {
                if (fieldViewModel.IsCurrentEdit(input))
                {
                    fieldViewModel.RevertAfterFailure("该升级关系会形成循环引用");
                }
                return;
            }

            var field = fieldViewModel.Field;
            var isOptionalInsertion = field.Definition.CanInsertWhenMissing && field.Availability == UnitFieldAvailability.Missing;
            var relative = isOptionalInsertion ? fieldViewModel.Unit.Source.RelativeSourceFile : field.Location!.RelativeSourceFile;
            var targetKind = isOptionalInsertion ? DraftTargetKind.OptionalUnitModule : DraftTargetKind.NdfField;
            operation = new DraftOperation(
                DraftOperation.CreateId(targetKind, relative, fieldViewModel.Unit.Name, field.Definition.Key),
                null,
                targetKind,
                "units",
                relative,
                fieldViewModel.Unit.Name,
                fieldViewModel.Unit.Source.TypeName,
                field.Definition.Key,
                field.Location?.FieldPath ?? field.Definition.Selector.DisplayPath,
                field.Definition.ValueKind.ToString(),
                field.DisplayValue,
                field.RawValue,
                normalized,
                targetRaw,
                $"{fieldViewModel.Unit.DisplayName} · {field.Definition.Label}：{field.DisplayValue} → {normalized}",
                null,
                false,
                DateTimeOffset.UtcNow);
        }

        DraftOperation? linkedArmorIndex = null;
        if (!fieldViewModel.IsName &&
            fieldViewModel.Key.EndsWith(".family", StringComparison.Ordinal) &&
            string.Equals(NdfSyntaxDocument.Leaf(targetRaw), "ResistanceFamily_infanterie", StringComparison.Ordinal))
        {
            var indexKey = fieldViewModel.Key[..^".family".Length];
            var indexField = fieldViewModel.Unit.Field(indexKey);
            if (indexField?.CanEdit != true || indexField.Location is null)
            {
                fieldViewModel.RevertAfterFailure("步兵护甲族需要可唯一定位的装甲索引，当前 Unit 无法安全联动");
                return;
            }

            if (indexField.DisplayValue != "1")
            {
                linkedArmorIndex = new DraftOperation(
                    DraftOperation.CreateId(DraftTargetKind.NdfField, indexField.Location.RelativeSourceFile, fieldViewModel.Unit.Name, indexKey),
                    $"armor:{fieldViewModel.Unit.Name}:{indexKey}",
                    DraftTargetKind.NdfField,
                    "units",
                    indexField.Location.RelativeSourceFile,
                    fieldViewModel.Unit.Name,
                    fieldViewModel.Unit.Source.TypeName,
                    indexKey,
                    indexField.Location.FieldPath,
                    indexField.Definition.ValueKind.ToString(),
                    indexField.DisplayValue,
                    indexField.RawValue,
                    "1",
                    "1",
                    $"{fieldViewModel.Unit.DisplayName} · {indexField.Definition.Label}：{indexField.DisplayValue} → 1（步兵护甲族联动）",
                    null,
                    false,
                    DateTimeOffset.UtcNow);
            }
        }

        if (string.Equals(normalized, fieldViewModel.BaseValue, StringComparison.Ordinal))
        {
            var existing = _draftStore.Operations.FirstOrDefault(item => item.Id == operation.Id);
            if (existing is not null)
            {
                await _draftStore.RemoveAsync(existing.Id);
            }

            if (fieldViewModel.IsCurrentEdit(input))
            {
                fieldViewModel.MarkPersisted(null, fieldViewModel.BaseValue, "已恢复基线，草稿已移除");
            }
        }
        else
        {
            if (linkedArmorIndex is null)
            {
                await _draftStore.UpsertAsync(operation);
            }
            else
            {
                await _draftStore.ApplyBatchAsync([operation, linkedArmorIndex], []);
            }
            if (fieldViewModel.IsCurrentEdit(input))
            {
                fieldViewModel.MarkPersisted(operation, normalized, "草稿已保存");
            }
        }

        RefreshDraftState();
        _setStatus($"草稿已保存 · {DraftCount} 项 · 正式 Mod 文件未改变");
    }

    private string? _sectionUnit;
    private string? _lastExpandedSection;
    private bool _sectionInteraction;
    private void SectionExpansionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.PropertyName!=nameof(FieldSectionViewModel<UnitFieldViewModel>.IsExpanded)||sender is not FieldSectionViewModel<UnitFieldViewModel> section)return;
        _sectionInteraction=true;
        if(section.IsExpanded)_lastExpandedSection=section.Title;
        else if(_lastExpandedSection==section.Title)_lastExpandedSection=null;
    }
    private void RebuildFields()
    {
        var sameUnit=_sectionUnit==SelectedUnit?.InternalName;
        var prior=FieldSections.ToDictionary(s=>s.Title,s=>s.IsExpanded);
        foreach(var section in FieldSections)section.PropertyChanged-=SectionExpansionChanged;
        _sectionUnit=SelectedUnit?.InternalName;
        Fields.Clear();
        FieldSections.Clear();
        if (SelectedUnit is null)
        {
            return;
        }

        var unit = SelectedUnit.Unit;
        var resolved = DraftResolver.Resolve(_data, _weaponData, _divisionData, _draftStore.Operations, StrategicData)
            .Where(item => item.Operation.ObjectName == unit.Name)
            .ToArray();
        var nameDraft = resolved.FirstOrDefault(item => item.Operation.FieldKey == "localisation.gameName");
        Fields.Add(UnitFieldViewModel.ForName(
            unit,
            nameDraft?.Status == DraftResolutionStatus.Active ? nameDraft.Operation : null,
            nameDraft?.Status == DraftResolutionStatus.Conflict ? nameDraft.Reason : null,
            _draftStore.IsBlocked,
            PersistFieldAsync,
            TrackPendingEdit));

        foreach (var field in unit.Fields)
        {
            var preparedField = field;
            if (field.Definition.Key == "deployment.shift")
            {
                var presets = _data.Units
                    .Select(item => item.Field(field.Definition.Key))
                    .Where(item => item?.CanEdit == true)
                    .Select(item => new UnitChoice(item!.DisplayValue, item.RawValue))
                    .DistinctBy(item => item.Display, StringComparer.Ordinal)
                    .OrderBy(item => double.TryParse(item.Display, out var number) ? number : double.MaxValue)
                    .ToArray();
                preparedField = field with { Choices = presets };
            }
            var draft = resolved.FirstOrDefault(item => item.Operation.FieldKey == field.Definition.Key);
            var forceReadOnly = resolved.Any(r=>r.Operation.TargetKind==DraftTargetKind.UnitCreate) && field.Definition.Key is "structure.tags" or "structure.upgradeFrom";
            if (field.Definition.Key.StartsWith("armor.", StringComparison.Ordinal) &&
                !field.Definition.Key.EndsWith(".family", StringComparison.Ordinal))
            {
                var familyKey = field.Definition.Key + ".family";
                var familyDraft = resolved.FirstOrDefault(item => item.Operation.FieldKey == familyKey && item.Status == DraftResolutionStatus.Active);
                var familyRaw = familyDraft?.Operation.TargetRaw ?? unit.Field(familyKey)?.RawValue ?? string.Empty;
                forceReadOnly = string.Equals(NdfSyntaxDocument.Leaf(familyRaw), "ResistanceFamily_infanterie", StringComparison.Ordinal);
            }
            Fields.Add(UnitFieldViewModel.ForField(
                unit,
                preparedField,
                draft?.Status == DraftResolutionStatus.Active ? draft.Operation : null,
                draft?.Status == DraftResolutionStatus.Conflict ? draft.Reason : null,
                _draftStore.IsBlocked,
                PersistFieldAsync,
                TrackPendingEdit,
                forceReadOnly));
            if(field.Definition.Key=="armor.front.family")Fields.Last().HasMixedArmor=new[]{"front","side","rear","top"}.Select(side=>resolved.FirstOrDefault(r=>r.Status==DraftResolutionStatus.Active&&r.Operation.FieldKey=="armor."+side+".family")?.Operation.TargetValue??unit.Field("armor."+side+".family")?.DisplayValue).Distinct().Count()>1;
        }

        for (var i = 0; i < Fields.Count; i++)
        {
            var current = Fields[i];
            Fields[i] = _fieldEdits.Restore(current, old => old.Unit.Name == current.Unit.Name && old.Key == current.Key);
        }
        foreach (var section in FieldSectionBuilder.Build(Fields.Where(f=>f.IsVisible), field => field.Section, field => field.Group))
        {
            if(sameUnit&&prior.TryGetValue(section.Title,out var expanded))section.IsExpanded=expanded;
            else if(_sectionInteraction)section.IsExpanded=section.Title==_lastExpandedSection;
            section.PropertyChanged+=SectionExpansionChanged;
            FieldSections.Add(section);
        }
    }

    public ICollectionView DraftView => CollectionViewSource.GetDefaultView(DraftItems);

    private void RefreshDraftState()
    {
        var creates=_draftStore.Operations.Where(o=>o.TargetKind==DraftTargetKind.UnitCreate).ToArray();
        var originalIds=_data.Units.Select(u=>u.Name).ToHashSet();
        foreach(var pending in Units.Where(u=>!originalIds.Contains(u.InternalName)&&!creates.Any(o=>o.ObjectName==u.InternalName)).ToArray()){if(SelectedUnit==pending)SelectedUnit=null;Units.Remove(pending);}
        foreach(var op in creates){try{var state=UnitCreation.Read(op);var mother=_data.Units.Single(u=>u.Name==state.Mother);var projected=UnitCreation.Project(mother,state,op.BaselineRaw);var existing=Units.FirstOrDefault(u=>u.InternalName==op.ObjectName);if(existing is null)Units.Add(new UnitListItemViewModel(projected));else existing.UpdateProjection(projected);}catch{}}
        var resolved = DraftResolver.Resolve(_data, _weaponData, _divisionData, _draftStore.Operations, StrategicData);
        var selected = DraftItems.Where(i => i.IsSelected).Select(i => i.Resolved.Operation.Id).ToHashSet();
        DraftItems.Clear();
        foreach (var item in resolved.OrderBy(item => item.Operation.RelativeSourceFile).ThenBy(item => item.Operation.Summary))
        {
            DraftItems.Add(new DraftItemViewModel(item, StrategicData) { IsSelected = selected.Contains(item.Operation.Id) });
        }

        if (DraftView.GroupDescriptions.Count == 0) DraftView.GroupDescriptions.Add(new PropertyGroupDescription("BatchKey"));
        OnPropertyChanged(nameof(DraftView));
        foreach (var unit in Units)
        {
            var matches = resolved.Where(item => item.Operation.ObjectName == unit.Unit.Name).ToArray();
            var draftName = matches.FirstOrDefault(item =>
                item.Status == DraftResolutionStatus.Active && item.Operation.TargetKind == DraftTargetKind.UnitName)?.Operation.TargetValue;
            unit.UpdateDraftState(
                matches.Length > 0,
                matches.Any(item => item.Status == DraftResolutionStatus.Conflict),
                draftName);
        }

        OnPropertyChanged(nameof(DraftCount));
        OnPropertyChanged(nameof(HasDrafts));
        OnPropertyChanged(nameof(CanClearDrafts));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRestoreBackups));
        UpdateBackupRestoreAvailability();
        RefreshFilter();
        RebuildCommonBatchFields();
    }

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var backup in _transactions.ListBackups(_draftStore.ProjectRoot))
        {
            var item = new BackupItemViewModel(backup);
            item.SetWorkspaceAllowsRestore(CanRestoreBackups);
            Backups.Add(item);
        }
    }

    private void SetTransactionBusy(bool value)
    {
        IsTransactionBusy = value;
        foreach (var field in Fields)
        {
            field.SetTransactionLocked(value);
        }

        UpdateBackupRestoreAvailability();
    }

    private void UpdateBackupRestoreAvailability()
    {
        foreach (var backup in Backups)
        {
            backup.SetWorkspaceAllowsRestore(CanRestoreBackups);
        }
    }

    private void TrackPendingEdit(Task task)
    {
        lock (_pendingGate)
        {
            _pendingEdits.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_pendingGate)
                {
                    _pendingEdits.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task WaitForPendingEditsAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_pendingGate)
            {
                pending = _pendingEdits.ToArray();
            }

            if (pending.Length == 0)
            {
                return;
            }

            await Task.WhenAll(pending);
        }
    }

    private void RebuildBatchOperations()
    {
        BatchOperationOptions.Clear();
        BatchOperationOptions.Add(new UnitBatchOperationOption("设为固定值", UnitBatchOperation.Set));
        if (SelectedBatchField?.IsNumeric == true)
        {
            BatchOperationOptions.Add(new UnitBatchOperationOption("当前值 × 系数", UnitBatchOperation.Multiply));
            BatchOperationOptions.Add(new UnitBatchOperationOption("当前值增加固定数值", UnitBatchOperation.Add));
            BatchOperationOptions.Add(new UnitBatchOperationOption("当前值减少固定数值", UnitBatchOperation.Subtract));
            BatchOperationOptions.Add(new UnitBatchOperationOption("当前值增加百分比", UnitBatchOperation.IncreasePercent));
            BatchOperationOptions.Add(new UnitBatchOperationOption("当前值减少百分比", UnitBatchOperation.DecreasePercent));
        }

        _batchOperand = string.Empty;
        OnPropertyChanged(nameof(BatchOperand));
        SelectedBatchOperation = BatchOperationOptions.FirstOrDefault();
        OnPropertyChanged(nameof(VisibleBatchOperations));
    }

    private IReadOnlyList<UnitListItemViewModel> BatchTargets() =>
        SelectedBatchScope == "当前筛选结果"
            ? _unitsView.Cast<UnitListItemViewModel>().ToArray()
            : Units.Where(unit => unit.IsBatchSelected).ToArray();

    private void BatchSelectionChanged()
    {
        if (_suppressBatchSelectionNotifications)
        {
            return;
        }

        InvalidateBatchPreview();
        OnPropertyChanged(nameof(BatchSelectedCount));
        OnPropertyChanged(nameof(BatchTargetCount));
        OnPropertyChanged(nameof(CanPreviewBatch));
        OnPropertyChanged(nameof(HasBatchInspector));
        RebuildCommonBatchFields();
    }

    private void InvalidateBatchPreview()
    {
        if (_batchPreview is null)
        {
            return;
        }

        _batchPreview = null;
        RaiseBatchPreviewProperties();
    }

    private void RebuildCommonBatchFields()
    {
        CommonBatchFields.Clear();
        var targets = Units.Where(item => item.IsBatchSelected).Select(item => item.Unit).ToArray();
        if (targets.Length < 2)
        {
            return;
        }

        var activeDrafts = _draftStore.Operations
            .Where(item => item.TargetKind == DraftTargetKind.NdfField && item.Module == "units")
            .ToDictionary(item => (item.ObjectName, item.FieldKey), item => item, EqualityComparer<(string, string)>.Default);
        foreach (var definition in UnitFieldDefinitions.All.Where(definition =>
                     definition.ValueKind is not (UnitValueKind.PathList or UnitValueKind.StringList or UnitValueKind.UnitReference) &&
                     Advanced.EditorMode.CanEdit(definition.Key) && targets.All(unit => unit.Field(definition.Key)?.CanEdit == true)))
        {
            var values = targets.Select(unit =>
                    activeDrafts.GetValueOrDefault((unit.Name, definition.Key))?.TargetValue ?? unit.Field(definition.Key)!.DisplayValue)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            var display = values.Length == 1 ? values[0] : string.Empty;
            var choices = targets.SelectMany(unit => unit.Field(definition.Key)!.Choices)
                .Select(item => item.Display).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
            CommonBatchFields.Add(new UnitBatchCommonFieldViewModel(definition, display, values.Length > 1, choices));
        }
    }

    private void RaiseBatchPreviewProperties()
    {
        OnPropertyChanged(nameof(HasBatchPreview));
        OnPropertyChanged(nameof(CanAddBatchDrafts));
        OnPropertyChanged(nameof(BatchPreviewSummary));
        OnPropertyChanged(nameof(BatchSamplesText));
        OnPropertyChanged(nameof(BatchImpactText));
        OnPropertyChanged(nameof(BatchMessagesText));
        OnPropertyChanged(nameof(HasBatchMessages));
    }

    private bool FilterUnit(object item)
    {
        if (item is not UnitListItemViewModel unit)
        {
            return false;
        }

        var textMatches = string.IsNullOrWhiteSpace(TextFilter) ||
                          unit.DisplayName.Contains(TextFilter, StringComparison.CurrentCultureIgnoreCase) ||
                          unit.InternalName.Contains(TextFilter, StringComparison.OrdinalIgnoreCase);
        if (!textMatches)
        {
            return false;
        }

        var selectedDimensions = _filterDimensions.Values.Where(dimension => dimension.HasSelection).ToArray();
        if (selectedDimensions.Length == 0)
        {
            return true;
        }

        bool MatchesDimension(UnitFilterDimensionViewModel dimension) => dimension.Key switch
        {
            "coalition" => dimension.Matches(unit.Coalition),
            "country" => dimension.Matches(unit.Country),
            "category" => dimension.MatchesAny(SplitValues(unit.Category)),
            "factory" => dimension.Matches(unit.Factory),
            "role" => dimension.Matches(unit.Role),
            "division" => dimension.MatchesAny(unit.Unit.Divisions),
            "draft" => DraftMatches(unit),
            _ => true
        };

        return FilterMatchMode == "任一条件"
            ? selectedDimensions.Any(MatchesDimension)
            : selectedDimensions.All(MatchesDimension);
    }

    private bool DraftMatches(UnitListItemViewModel unit)
    {
        var dimension = _filterDimensions["draft"];
        if (!dimension.HasSelection)
        {
            return true;
        }

        return dimension.Options.Any(option => option.IsSelected && option.Value switch
        {
            "有草稿" => unit.HasDraft,
            "无草稿" => !unit.HasDraft,
            "冲突" => unit.HasConflict,
            _ => false
        });
    }

    public void RemoveFilterTag(UnitFilterTagViewModel tag)
    {
        if (!_filterDimensions.TryGetValue(tag.DimensionKey, out var dimension))
        {
            return;
        }

        var option = dimension.Options.FirstOrDefault(item => string.Equals(item.Value, tag.Value, StringComparison.CurrentCultureIgnoreCase));
        if (option is not null)
        {
            option.IsSelected = false;
        }
    }

    public void ClearFilters()
    {
        foreach (var option in FilterDimensions.SelectMany(dimension => dimension.Options))
        {
            option.SetSelected(false, false);
        }

        RebuildActiveFilterTags();
        RefreshFilter();
    }

    private void FilterOptionChanged(UnitFilterOptionViewModel option)
    {
        RebuildActiveFilterTags();
        RefreshFilter();
    }

    private void RebuildActiveFilterTags()
    {
        ActiveFilterTags.Clear();
        foreach (var option in FilterDimensions.SelectMany(dimension => dimension.Options).Where(option => option.IsSelected))
        {
            ActiveFilterTags.Add(new UnitFilterTagViewModel(option.DimensionKey, option.DimensionLabel, option.Value));
        }

        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    private void RefreshFilter()
    {
        _unitsView.Refresh();
        VisibleUnitCount = _unitsView.Cast<object>().Count();
        InvalidateBatchPreview();
        OnPropertyChanged(nameof(BatchTargetCount));
        OnPropertyChanged(nameof(CanPreviewBatch));
    }

    private static IReadOnlyList<string> Options(IEnumerable<string> values) =>
        new[] { All }
            .Concat(values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase))
            .ToArray();

    private static IReadOnlyList<string> SplitValues(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private bool WouldCreateUpgradeCycle(string sourceUnit, string targetUnit)
    {
        var current = targetUnit;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current))
        {
            if (string.Equals(current, sourceUnit, StringComparison.Ordinal))
            {
                return true;
            }

            var unit = _data.Units.FirstOrDefault(item => item.Name == current);
            if (unit is null)
            {
                return false;
            }

            var draft = _draftStore.Operations.FirstOrDefault(item =>
                item.ObjectName == current && item.FieldKey == "structure.upgradeFrom");
            if (draft is not null)
            {
                current = NdfSyntaxDocument.Leaf(draft.TargetRaw);
                continue;
            }

            var field = unit.Field("structure.upgradeFrom");
            if (field is null || string.IsNullOrWhiteSpace(field.DisplayValue))
            {
                return false;
            }

            current = NdfSyntaxDocument.Leaf(field.RawValue);
        }

        return true;
    }
}

public sealed class UnitBatchCommonFieldViewModel : ObservableObject
{
    private string _editValue;
    private string _statusText = string.Empty;

    public UnitBatchCommonFieldViewModel(UnitFieldDefinition definition, string displayValue, bool isMixed, IReadOnlyList<string> choices)
    {
        Definition = definition;
        DisplayValue = displayValue;
        IsMixed = isMixed;
        Choices = choices;
        _editValue = isMixed && definition.ValueKind is not (UnitValueKind.Integer or UnitValueKind.Decimal or UnitValueKind.EcmPercent)
            ? string.Empty
            : displayValue;
    }

    public UnitFieldDefinition Definition { get; }
    public string Label => Definition.Label;
    public string OriginalParameter => Controls.ParameterNote.ForUnit(Definition);
    public string Hint => IsMixed ? "所选 Unit 当前值不同；输入目标值后再加入草稿" : "所选 Unit 当前值相同";
    public string DisplayValue { get; }
    public bool IsMixed { get; }
    public IReadOnlyList<string> Choices { get; }
    public bool IsChoiceEditor => Definition.EditorKind == UnitEditorKind.Choice;
    public bool IsTextEditor => !IsChoiceEditor;
    public bool CanAddDraft => !string.IsNullOrWhiteSpace(EditValue);
    public string EditValue { get => _editValue; set { if (SetProperty(ref _editValue, value ?? string.Empty)) OnPropertyChanged(nameof(CanAddDraft)); } }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
}

public sealed record UnitBatchFieldOption(UnitFieldDefinition Definition)
{
    public string OriginalParameter => Controls.ParameterNote.ForUnit(Definition);
    public string DisplayName => $"{Definition.Group} · {Definition.Label}";

    public bool IsNumeric => Definition.ValueKind is UnitValueKind.Integer or UnitValueKind.Decimal or UnitValueKind.EcmPercent;
}

public sealed record UnitBatchOperationOption(string DisplayName, UnitBatchOperation Operation);

public sealed record UnitBatchRoundingOption(string DisplayName, UnitBatchRounding Rounding);
