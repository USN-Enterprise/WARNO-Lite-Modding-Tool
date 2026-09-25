using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.ViewModels.Weapons;

public sealed partial class AmmoWorkspaceViewModel
{
    public sealed record OperationOption(string Label,UnitBatchOperation Operation);
    public sealed record RoundingOption(string Label,UnitBatchRounding Rounding);
    private bool _batchOpen, _batchBusy, _selectingBatch;
    private int _batchRevision;
    private string _batchScope = "已勾选弹药", _batchOperand = "", _batchMinimum = "", _batchMaximum = "", _batchMessage = "", _impactSearch = "";
    private AmmoBatchFieldViewModel? _batchField;
    private OperationOption? _batchOperation;
    private RoundingOption? _batchRounding;
    private AmmoBatchPreview? _batchPreview;
    private CancellationTokenSource? _batchCancel;
    private Task _batchPending = Task.CompletedTask;
    internal void RestoreBatchChecks(IReadOnlySet<string> names)
    {
        _selectingBatch = true;
        try { foreach (var ammo in Ammunition) ammo.IsBatchSelected = names.Contains(ammo.Name); }
        finally { _selectingBatch = false; }
        RebuildBatch();
    }
    public ObservableCollection<AmmoBatchFieldViewModel> CommonBatchFields { get; } = [];
    public System.ComponentModel.ICollectionView CommonBatchFieldsView { get; private set; } = null!;
    public ObservableCollection<OperationOption> BatchOperations { get; } = [];
    public IReadOnlyList<RoundingOption> BatchRoundings { get; } = [new("不取整",UnitBatchRounding.None),new("四舍五入",UnitBatchRounding.Nearest),new("向下取整",UnitBatchRounding.Floor),new("向上取整",UnitBatchRounding.Ceiling)];
    public IReadOnlyList<string> BatchScopes { get; } = ["已勾选弹药","当前筛选结果"];
    public bool BatchOpen { get => _batchOpen; set { if(SetProperty(ref _batchOpen,value)) OnPropertyChanged(nameof(HasBatchInspector)); } }
    public bool HasBatchInspector => BatchOpen || BatchSelectedCount >= 2;
    public int BatchSelectedCount => Ammunition.Count(a => a.IsBatchSelected);
    public string BatchSelectionText => UiText.T("已选弹药")+$" {BatchSelectedCount} · "+UiText.T("隐藏的已选")+$" {Ammunition.Count(a => a.IsBatchSelected && !MatchesSearch(a))}";
    public string BatchScope { get => _batchScope; set { if(SetProperty(ref _batchScope,value)) RebuildBatch(); } }
    private AmmoRecord[] BatchTargets => (BatchScope == "当前筛选结果" ? AmmunitionView.Cast<AmmoListItemViewModel>() : Ammunition.Where(a => a.IsBatchSelected)).Select(a => a.Ammo).DistinctBy(AmmoBatchPlanner.Identity).ToArray();
    public string BatchTargetText => UiText.T("目标弹药")+$" {BatchTargets.Length} · "+UiText.T("直接修改弹药本体，影响全部使用者；筛选不限制使用者范围。");
    public string BatchEmptyText => BatchTargets.Length == 0 ? "请先选择弹药" : CommonBatchFields.Count == 0 ? "所选弹药没有共有可编辑字段" : "";
    public bool CanEditBatch => !_batchBusy && !_transactions.IsTransactionBusy && !_draftStore.IsBlocked;
    public bool CanCalculateBatch => CanEditBatch && BatchField is not null && BatchTargets.Length > 0;
    public AmmoBatchFieldViewModel? BatchField
    {
        get => _batchField;
        set
        {
            if(!SetProperty(ref _batchField,value)) return;
            BuildBatchOperations(); BatchRounding = BatchRoundings[value?.Definition.ValueKind == WeaponValueKind.Integer ? 3 : 0];
            BatchOperand = ""; InvalidateBatch(); OnPropertyChanged(nameof(CanCalculateBatch)); OnPropertyChanged(nameof(BatchNumeric)); OnPropertyChanged(nameof(BatchChoices));
        }
    }
    public bool BatchNumeric => BatchField?.IsNumeric == true;
    public IReadOnlyList<string> BatchChoices => BatchField?.Choices ?? [];
    public OperationOption? BatchOperation { get => _batchOperation; set { if(SetProperty(ref _batchOperation,value)) InvalidateBatch(); } }
    public RoundingOption? BatchRounding { get => _batchRounding; set { if(SetProperty(ref _batchRounding,value)) InvalidateBatch(); } }
    public string BatchOperand { get => _batchOperand; set { if(SetProperty(ref _batchOperand,value ?? "")) InvalidateBatch(); } }
    public string BatchMinimum { get => _batchMinimum; set { if(SetProperty(ref _batchMinimum,value ?? "")) InvalidateBatch(); } }
    public string BatchMaximum { get => _batchMaximum; set { if(SetProperty(ref _batchMaximum,value ?? "")) InvalidateBatch(); } }
    public string BatchMessage { get => _batchMessage; private set => SetProperty(ref _batchMessage,value); }
    public IReadOnlyList<AmmoBatchRow> BatchRows => _batchPreview?.Rows ?? [];
    public string BatchSummary => _batchPreview is null ? "" : UiText.T("目标弹药")+$" {_batchPreview.Rows.Count} · "+UiText.T("变化")+$" {_batchPreview.Rows.Count(r => r.Current != r.Target)} · "+UiText.T("不变")+$" {_batchPreview.Rows.Count(r => r.Status == "不变")} · "+UiText.T("错误")+$" {_batchPreview.Errors.Count}";
    public string BatchImpactText => _batchPreview is null ? "" : $"{_batchPreview.References.Select(r => r.Weapon).Distinct().Count()} Weapon · {_batchPreview.References.Select(r => r.Unit).Where(n => n.Length > 0).Distinct().Count()} Unit";
    public string BatchImpactSearch { get => _impactSearch; set { if(SetProperty(ref _impactSearch,value ?? "")) OnPropertyChanged(nameof(BatchReferences)); } }
    public IEnumerable<AmmoBatchReference> BatchReferences => (_batchPreview?.References ?? []).Where(r => (r.Ammo+" "+r.Weapon+" "+r.Unit+" "+r.DisplayName).Contains(BatchImpactSearch,StringComparison.OrdinalIgnoreCase));
    private void InitializeBatch()
    {
        CommonBatchFieldsView = new ListCollectionView(CommonBatchFields);
        CommonBatchFieldsView.GroupDescriptions.Add(new PropertyGroupDescription("Definition.Group"));
        foreach(var item in Ammunition) item.PropertyChanged += (_,e) => { if(e.PropertyName == nameof(AmmoListItemViewModel.IsBatchSelected) && !_selectingBatch) RebuildBatch(); };
        System.ComponentModel.PropertyChangedEventManager.AddHandler(UiText.Current,(_,_) =>
        {
            if(_batchPreview is not null) _batchPreview = _batchPreview with { Rows = _batchPreview.Rows.Select(r => r with { DisplayName = Ammunition.FirstOrDefault(a => a.Name == r.Name)?.DisplayName ?? r.Name }).ToArray() };
            OnPropertyChanged(nameof(BatchTargetText)); OnPropertyChanged(nameof(BatchSelectionText)); RaiseBatchPreview();
        },nameof(UiText.Version));
        RebuildBatch();
    }
    public void SelectFilteredBatch(bool select)
    {
        _selectingBatch = true;
        foreach(var item in select ? AmmunitionView.Cast<AmmoListItemViewModel>() : Ammunition) item.IsBatchSelected = select;
        _selectingBatch = false; RebuildBatch();
    }
    public void RefreshMode()
    {
        foreach(var field in Fields) field.RefreshMode();
        BuildBatchOperations();
        if(!Advanced.EditorMode.IsAdvanced) { BatchMinimum = ""; BatchMaximum = ""; }
        RebuildBatch();
    }
    private void BuildBatchOperations()
    {
        var selected = BatchOperation?.Operation; BatchOperations.Clear();
        BatchOperations.Add(new("设为固定值",UnitBatchOperation.Set));
        if(BatchNumeric)
        {
            BatchOperations.Add(new("当前值增加百分比",UnitBatchOperation.IncreasePercent)); BatchOperations.Add(new("当前值减少百分比",UnitBatchOperation.DecreasePercent));
            if(Advanced.EditorMode.IsAdvanced)
            {
                BatchOperations.Add(new("当前值 × 系数",UnitBatchOperation.Multiply)); BatchOperations.Add(new("当前值增加固定数值",UnitBatchOperation.Add)); BatchOperations.Add(new("当前值减少固定数值",UnitBatchOperation.Subtract));
            }
        }
        BatchOperation = BatchOperations.FirstOrDefault(o => o.Operation == selected) ?? BatchOperations[0];
    }
    private void RebuildBatch()
    {
        if(_selectingBatch) return;
        InvalidateBatch(); var key = BatchField?.Definition.Key; var targets = BatchTargets; CommonBatchFields.Clear();
        foreach(var definition in AmmoBatchPlanner.CommonFields(targets).Where(f => Advanced.EditorMode.CanEdit(f.Key)))
        {
            string[] values; var error = "";
            try { values = targets.Select(a => AmmoBatchPlanner.Current(_transactions.Data,_data,_draftStore.Operations,a,definition.Key)).Distinct().ToArray(); }
            catch(TransactionValidationException ex) { values = []; error = ex.Message; }
            CommonBatchFields.Add(new(definition,values.Length == 1 ? values[0] : "",values.Length > 1,AmmoBatchPlanner.Choices(targets,definition.Key),error,InvalidateBatch));
        }
        BatchField = CommonBatchFields.FirstOrDefault(f => f.Definition.Key == key) ?? CommonBatchFields.FirstOrDefault();
        foreach(var name in new[] { nameof(BatchSelectedCount),nameof(BatchSelectionText),nameof(HasBatchInspector),nameof(BatchTargetText),nameof(BatchEmptyText),nameof(CanCalculateBatch) }) OnPropertyChanged(name);
    }
    private void InvalidateBatch()
    {
        _batchRevision++; _batchCancel?.Cancel(); _batchPreview = null;
        BatchMessage = "输入不会自动保存；加入草稿时重新计算。";
        RaiseBatchPreview();
    }
    private void RaiseBatchPreview() { foreach(var n in new[] { nameof(BatchRows),nameof(BatchSummary),nameof(BatchImpactText),nameof(BatchReferences) }) OnPropertyChanged(n); }
    public Task RunBatchAsync(bool save,AmmoBatchFieldViewModel? common = null)
    {
        if(!CanEditBatch) return Task.CompletedTask;
        _batchBusy = true; OnPropertyChanged(nameof(CanEditBatch)); OnPropertyChanged(nameof(CanCalculateBatch));
        _batchPending = RunAndUnlockAsync(save,common); return _batchPending;
    }
    private async Task RunAndUnlockAsync(bool save,AmmoBatchFieldViewModel? common)
    {
        try { await ComputeBatchAsync(save,common); }
        finally { _batchBusy = false; OnPropertyChanged(nameof(CanEditBatch)); OnPropertyChanged(nameof(CanCalculateBatch)); }
    }
    private async Task ComputeBatchAsync(bool save,AmmoBatchFieldViewModel? common)
    {
        await _fieldEdits.FlushAsync();
        var field = common ?? BatchField; if(field is null) return;
        var request = new AmmoBatchRequest(BatchTargets.Select(AmmoBatchPlanner.Identity).ToArray(),field.Definition.Key,common is null ? BatchOperation?.Operation ?? UnitBatchOperation.Set : UnitBatchOperation.Set,
            common?.EditValue ?? BatchOperand,BatchRounding?.Rounding ?? UnitBatchRounding.None,BatchMinimum,BatchMaximum);
        _batchCancel?.Cancel(); _batchCancel?.Dispose(); _batchCancel = new(); var token = _batchCancel.Token; var revision = _batchRevision;
        var drafts = _draftStore.Operations.ToArray(); var draftState = JsonSerializer.Serialize(drafts); _batchPreview = null; RaiseBatchPreview();
        OnPropertyChanged(nameof(CanEditBatch)); OnPropertyChanged(nameof(CanCalculateBatch)); BatchMessage = "正在重新读取项目并计算完整影响…";
        try
        {
            var preview = await Task.Run(async () =>
            {
                using var disk = new DraftStore(_draftStore.ProjectRoot); var read = await disk.LoadAsync(token);
                if(read.IsBlocked || JsonSerializer.Serialize(disk.Operations) != draftState) throw new InvalidOperationException("草稿已在外部变化，请重新加载项目");
                var context = new ModProjectDetector().Detect(_draftStore.ProjectRoot); var index = await new ProjectIndexer().IndexAsync(context,cancellationToken:token);
                if(index.Modules.Any(m => m.Key is "ammo" or "weapons" or "units" && m.Availability == ModuleAvailability.ParseError)) throw new InvalidOperationException("弹药或引用模块解析失败，请处理项目诊断");
                var units = await new UnitProjectLoader().LoadAsync(context,index,token); var data = await new WeaponProjectLoader().LoadAsync(context,index,units,token);
                var result = AmmoBatchPlanner.Preview(units,data,drafts,request);
                if(result.CanSave)
                {
                    var replaced = result.Upserts.Select(o => o.Id).Concat(result.Removals).ToHashSet();
                    var combined = drafts.Where(o => !replaced.Contains(o.Id)).Concat(result.Upserts).ToArray();
                    var related = UnitDraftLinks.Expand(result.Upserts,combined).Where(WeaponBatch.IsWeaponEdit).ToArray();
                    if(related.Length > 0) WeaponBatchApplyPlanner.Plan(units,data,related,path => TextFileSnapshot.Load(_draftStore.ProjectRoot,path,FormalTextFileKind.Ndf));
                }
                var names = _data.Ammunition.ToDictionary(a => a.Name,a => UiText.Current.English ? a.DisplayName : a.ChineseName);
                return result with { Rows = result.Rows.Select(r => r with { DisplayName = names.GetValueOrDefault(r.Name,r.Name) }).ToArray() };
            },token);
            if(token.IsCancellationRequested || revision != _batchRevision) return;
            if(JsonSerializer.Serialize(_draftStore.Operations) != draftState) throw new InvalidOperationException("草稿已变化，请重新预览");
            if(save && preview.CanSave)
            {
                await _draftStore.ApplyBatchAsync(preview.Upserts,preview.Removals);
                _transactions.RefreshExternalDraftState(); RefreshFromDrafts();
            }
            _batchPreview = preview; RaiseBatchPreview();
            BatchMessage = preview.Errors.Count > 0 ? string.Join("\n",preview.Errors.Select(UiText.T)) : save && preview.CanSave ? "弹药批量草稿已保存，正式文件未改变。" : "预览未保存；请检查结果及全部使用者。";
            _setStatus(UiText.T(BatchMessage));
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { if(!token.IsCancellationRequested) BatchMessage = ex.Message; }
    }
}

public sealed class AmmoBatchFieldViewModel(WeaponFieldDefinition definition,string value,bool mixed,IReadOnlyList<string> choices,string error,Action changed) : ObservableObject
{
    private string _editValue = value;
    public WeaponFieldDefinition Definition => definition;
    public string Label => definition.Label+(definition.Suffix ?? "");
    public string Parameter => definition.FieldName+(definition.ArgumentName is {} arg ? "."+arg : "")+(definition.MapKey is {} map ? "["+map+"]" : "");
    public string Hint => definition.Hint;
    public bool IsMixed => mixed;
    public bool IsNumeric => definition.ValueKind is WeaponValueKind.Integer or WeaponValueKind.Decimal;
    public bool IsChoice => !IsNumeric;
    public IReadOnlyList<string> Choices => choices;
    public string Error => error;
    public bool CanAdd => error.Length == 0 && EditValue.Length > 0;
    public string EditValue { get => _editValue; set { if(SetProperty(ref _editValue,value ?? "")) { changed(); OnPropertyChanged(nameof(CanAdd)); } } }
}
