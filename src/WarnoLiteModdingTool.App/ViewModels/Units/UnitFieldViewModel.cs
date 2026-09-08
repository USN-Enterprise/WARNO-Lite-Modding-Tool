using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Units;
using System.Collections.ObjectModel;

namespace WarnoLiteModdingTool.App.ViewModels.Units;

public sealed class UnitFieldViewModel : ObservableObject
{
    private readonly Func<UnitFieldViewModel, Task> _changed;
    private readonly Action<Task> _trackPending;
    private readonly bool _baseEditable;
    private string _editValue;
    private string _persistedValue;
    private string _statusText;
    private bool _hasDraft;
    private bool _isBusy;
    private bool _suppressChange;
    private CancellationTokenSource? _debounceCancellation;
    private Task _pendingPersistence = Task.CompletedTask;
    private bool _transactionLocked;
    private bool _suppressChoiceSync;

    private UnitFieldViewModel(
        UnitRecord unit,
        UnitFieldValue? field,
        bool isName,
        string key,
        string section,
        string group,
        string label,
        string hint,
        string baseValue,
        string rawValue,
        string reason,
        bool canEdit,
        bool isChoiceEditor,
        IReadOnlyList<string> choices,
        DraftOperation? activeDraft,
        string? conflictReason,
        Func<UnitFieldViewModel, Task> changed,
        Action<Task> trackPending)
    {
        Unit = unit;
        Field = field;
        IsName = isName;
        Key = key;
        Section = section;
        Group = group;
        Label = label;
        Hint = key=="structure.upgradeFrom"?"只影响大厅显示，基本不必修改。":hint;
        BaseValue = baseValue;
        RawValue = rawValue;
        ReadOnlyReason = conflictReason ?? reason;
        _baseEditable = canEdit && conflictReason is null;
        IsDeploymentEditor = field?.Definition.Key == "deployment.shift";
        IsChoiceEditor = isChoiceEditor && !IsDeploymentEditor;
        Choices = choices;
        DeploymentPresets = IsDeploymentEditor
            ? choices.Select(value => new DeploymentPresetViewModel(value, DeploymentPresetLabel(value))).ToArray()
            : [];
        ActiveDraft = activeDraft;
        _editValue = activeDraft?.TargetValue ?? baseValue;
        _persistedValue = _editValue;
        _hasDraft = activeDraft is not null;
        _statusText = conflictReason is null ? activeDraft is null ? string.Empty : "草稿已恢复" : $"草稿冲突：{conflictReason}";
        _changed = changed;
        _trackPending = trackPending;
        ChoiceToggles = new ObservableCollection<UnitChoiceToggleViewModel>();
        if (field is not null && (field.Definition.ValueKind is UnitValueKind.PathList or UnitValueKind.StringList || field.Definition.Key == "structure.role"))
        {
            var selected = SplitDisplay(_editValue).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            foreach (var choice in choices.Concat(SplitDisplay(_editValue)).Distinct(StringComparer.CurrentCultureIgnoreCase))
            {
                var toggle = new UnitChoiceToggleViewModel(
                    choice,
                    selected.Contains(choice),
                    field.Definition.Key == "structure.tags" && choice.StartsWith("UNITE_", StringComparison.OrdinalIgnoreCase));
                toggle.Kind = field.Definition.Key;
                toggle.SelectionChanged += ChoiceToggle_SelectionChanged;
                ChoiceToggles.Add(toggle);
            }
        }
    }

    public UnitRecord Unit { get; }

    public UnitFieldValue? Field { get; }

    public DraftOperation? ActiveDraft { get; private set; }

    public bool IsName { get; }

    public string Key { get; }

    public string Section { get; }

    public string Group { get; }

    public string Label { get; }
    public string DisplayLabel => Key=="armor.front.family"?"护甲类型":Label;
    public bool IsVisible => Key.StartsWith("armor.") && Key.EndsWith(".family")
        ? Advanced.EditorMode.IsAdvanced && Key == "armor.front.family"
        : Advanced.EditorMode.IsAdvanced || Key is not ("structure.upgradeFrom" or "survival.suppression" or "survival.stun" or "recon.vision.low" or "recon.vision.high" or "recon.optics.low" or "recon.optics.high");
    public bool IsReconPresetEditor => Key == "recon.optics.standard";
    public int[] ReconPresets => [0, 1768, 2651, 3535, 5301, 7068];

    public bool HasMixedArmor {get;set;}
    public bool ShowMixedArmor => HasMixedArmor;
    public string? ChoiceValue {get=>ShowMixedArmor?null:EditValue;set{if(value is null)return;var mixed=ShowMixedArmor;HasMixedArmor=false;if(mixed&&value==EditValue)SchedulePersistence();else EditValue=value;OnPropertyChanged(nameof(ShowMixedArmor));}}
    public string Hint { get; }

    public string BaseValue { get; }

    public string RawValue { get; }

    public string ReadOnlyReason { get; }

    public bool IsEditable => _baseEditable && !_transactionLocked && Advanced.EditorMode.CanEdit(Key);
    public void RefreshMode() { OnPropertyChanged(nameof(IsEditable));OnPropertyChanged(nameof(IsVisible));OnPropertyChanged(nameof(DisplayLabel));OnPropertyChanged(nameof(OriginalParameter));ChoicesView.Refresh();OnPropertyChanged(nameof(ShowMixedArmor));OnPropertyChanged(nameof(ChoiceValue)); }

    public bool IsChoiceEditor { get; }

    public bool IsTextEditor => !IsChoiceEditor && !IsPillEditor && !IsDeploymentEditor;

    public bool IsDeploymentEditor { get; }

    public IReadOnlyList<string> Choices { get; }

    public IReadOnlyList<DeploymentPresetViewModel> DeploymentPresets { get; }

    public ObservableCollection<UnitChoiceToggleViewModel> ChoiceToggles { get; }
    private System.ComponentModel.ICollectionView? _choicesView;
    private string _choiceSearch="";
    public string ChoiceSearch {get=>_choiceSearch;set{if(SetProperty(ref _choiceSearch,value))ChoicesView.Refresh();}}
    public System.ComponentModel.ICollectionView ChoicesView => _choicesView ??= new System.Windows.Data.ListCollectionView(ChoiceToggles) {Filter=o=>o is UnitChoiceToggleViewModel c && (c.Kind!="structure.category" || Localisation.GameText.VisibleCategory(c.Display)) && (c.Display.Contains(ChoiceSearch,StringComparison.OrdinalIgnoreCase)||Localisation.GameText.Display(c.Kind,c.Display).Contains(ChoiceSearch,StringComparison.OrdinalIgnoreCase))};
    public void SelectVisibleChoices(bool selected){_suppressChoiceSync=true;try{foreach(var c in ChoicesView.Cast<UnitChoiceToggleViewModel>().Where(c=>c.IsEnabled))c.SetSilently(selected);}finally{_suppressChoiceSync=false;}ChoiceToggle_SelectionChanged(null,EventArgs.Empty);}


    public bool IsMultiChoiceEditor => Field?.Definition.ValueKind is UnitValueKind.PathList or UnitValueKind.StringList;

    public bool IsPillEditor => IsMultiChoiceEditor || Field?.Definition.Key == "structure.role";

    public bool IsTagEditor => Field?.Definition.ValueKind == UnitValueKind.StringList;

    public int SelectedChoiceCount => ChoiceToggles.Count(c=>c.IsSelected);
    public IEnumerable<UnitChoiceToggleViewModel> SelectedChoices => ChoiceToggles.Where(c=>c.IsSelected);
    public string ChoiceSummary => ChoiceToggles.Count(item => item.IsSelected) == 0
        ? "尚未选择"
        : string.Join(" · ", ChoiceToggles.Where(item => item.IsSelected).Select(item => item.Display));

    public bool IsOptionalMissing => Field is { Definition.CanInsertWhenMissing: true, Availability: UnitFieldAvailability.Missing };

    public string UnitSuffix => Field?.Definition.UnitSuffix ?? string.Empty;

    public string OriginalParameter => Field is null ? "NameToken → UNITS.csv.REFTEXT" : Controls.ParameterNote.ForUnit(Field.Definition, true);
    public string FieldPath => Field?.Location?.FieldPath ?? "UNITS.csv.REFTEXT";

    public string SourceLocation => Field?.Location is null
        ? Unit.UnitsCsvRelativePath ?? "—"
        : $"{Field.Location.RelativeSourceFile}:{Field.Location.LineNumber}";

    public string ConversionSource => Field?.Definition.ConversionSource ?? "直接显示已解析值";

    public string TargetRaw => ActiveDraft?.TargetRaw ?? RawValue;

    public bool HasTargetDifference => HasDraft && !string.Equals(TargetRaw, RawValue, StringComparison.Ordinal);

    public string EditValue
    {
        get => _editValue;
        set
        {
            if (!SetProperty(ref _editValue, value) || _suppressChange || !IsEditable)
            {
                return;
            }

            SchedulePersistence();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasDraft
    {
        get => _hasDraft;
        private set
        {
            if (SetProperty(ref _hasDraft, value))
            {
                OnPropertyChanged(nameof(HasTargetDifference));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public static UnitFieldViewModel ForField(
        UnitRecord unit,
        UnitFieldValue field,
        DraftOperation? activeDraft,
        string? conflictReason,
        bool draftStoreBlocked,
        Func<UnitFieldViewModel, Task> changed,
        Action<Task> trackPending,
        bool forceReadOnly = false) =>
        new(
            unit,
            field,
            false,
            field.Definition.Key,
            field.Definition.Section,
            field.Definition.Group,
            field.Definition.Label,
            field.Definition.ShortHint,
            field.DisplayValue,
            field.RawValue,
            draftStoreBlocked ? "现有草稿不可解析；请先清空" : forceReadOnly ? "步兵护甲族的装甲索引固定为 1" :
                field.Definition.CanInsertWhenMissing && field.Availability == UnitFieldAvailability.Missing ? "缺少字段；编辑后将通过草稿安全补建" : field.Reason,
            (field.CanEdit || field.Definition.CanInsertWhenMissing && field.Availability == UnitFieldAvailability.Missing) && !draftStoreBlocked && !forceReadOnly,
            field.Definition.EditorKind == UnitEditorKind.Choice && field.Definition.ValueKind is not (UnitValueKind.PathList or UnitValueKind.StringList) && field.Definition.Key != "structure.role" ||
                field.Definition.CanInsertWhenMissing && field.Availability == UnitFieldAvailability.Missing && field.Definition.ValueKind != UnitValueKind.StringList,
            field.Choices.Select(item => item.Display).ToArray(),
            activeDraft,
            conflictReason,
            changed,
            trackPending);

    public static UnitFieldViewModel ForName(
        UnitRecord unit,
        DraftOperation? activeDraft,
        string? conflictReason,
        bool draftStoreBlocked,
        Func<UnitFieldViewModel, Task> changed,
        Action<Task> trackPending) =>
        new(
            unit,
            null,
            true,
            "localisation.gameName",
            "基本信息",
            "名称",
            "游戏内名称",
            $"名称来源：{unit.NameSource}",
            unit.DisplayName,
            unit.DisplayName,
            draftStoreBlocked ? "现有草稿不可解析；请先清空" : unit.NameEditReason,
            unit.CanEditName && !draftStoreBlocked,
            false,
            [],
            activeDraft,
            conflictReason,
            changed,
            trackPending);

    public void SetTransactionLocked(bool value)
    {
        if (_transactionLocked == value)
        {
            return;
        }

        _transactionLocked = value;
        OnPropertyChanged(nameof(IsEditable));
    }

    public async Task FlushAsync()
    {
        if (!_baseEditable)
        {
            return;
        }

        _debounceCancellation?.Cancel();
        try
        {
            await _pendingPersistence;
        }
        catch (OperationCanceledException)
        {
        }

        if (string.Equals(EditValue, _persistedValue, StringComparison.Ordinal))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _debounceCancellation = cancellation;
        _pendingPersistence = PersistChangedValueAsync(cancellation, false);
        _trackPending(_pendingPersistence);
        await _pendingPersistence;
    }

    public void MarkPersisted(DraftOperation? operation, string normalizedValue, string message)
    {
        ActiveDraft = operation;
        _persistedValue = normalizedValue;
        SetEditValue(normalizedValue);
        HasDraft = operation is not null;
        StatusText = message;
        OnPropertyChanged(nameof(TargetRaw));
        OnPropertyChanged(nameof(HasTargetDifference));
    }

    public void RevertAfterFailure(string message)
    {
        SetEditValue(_persistedValue);
        StatusText = message;
    }

    public bool IsCurrentEdit(string value) =>
        string.Equals(EditValue, value, StringComparison.Ordinal);

    private void SchedulePersistence()
    {
        _debounceCancellation?.Cancel();
        _debounceCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _debounceCancellation = cancellation;
        _pendingPersistence = PersistChangedValueAsync(cancellation, true);
        _trackPending(_pendingPersistence);
    }

    private async Task PersistChangedValueAsync(CancellationTokenSource cancellation, bool useDelay)
    {
        try
        {
            if (useDelay && !IsChoiceEditor)
            {
                await Task.Delay(300, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在保存草稿…";
        var attemptedValue = EditValue;
        try
        {
            await _changed(this);
        }
        catch (Exception exception)
        {
            if (IsCurrentEdit(attemptedValue))
            {
                RevertAfterFailure($"保存失败：{exception.Message}");
            }
        }
        finally
        {
            if (_debounceCancellation == cancellation)
            {
                IsBusy = false;
            }
        }
    }

    private void SetEditValue(string value)
    {
        _suppressChange = true;
        try
        {
            if (SetProperty(ref _editValue, value, nameof(EditValue)))
            {
                SyncChoiceToggles(value);
                OnPropertyChanged(nameof(EditValue));
            }
        }
        finally
        {
            _suppressChange = false;
        }
    }

    private void ChoiceToggle_SelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressChoiceSync)
        {
            return;
        }

        if (Field?.Definition.Key == "structure.role" && sender is UnitChoiceToggleViewModel { IsSelected: true } selected)
        {
            foreach (var choice in ChoiceToggles.Where(item => !ReferenceEquals(item, selected)))
            {
                choice.SetSilently(false);
            }
        }

        EditValue = Field?.Definition.Key == "structure.role"
            ? ChoiceToggles.FirstOrDefault(item => item.IsSelected)?.Display ?? string.Empty
            : string.Join(", ", SplitDisplay(EditValue).Where(v => ChoiceToggles.Any(c=>c.IsSelected && c.Display == v)).Concat(ChoiceToggles.Where(c=>c.IsSelected).Select(c=>c.Display)).Distinct(StringComparer.Ordinal));
        OnPropertyChanged(nameof(ChoiceSummary));
        OnPropertyChanged(nameof(SelectedChoices)); OnPropertyChanged(nameof(SelectedChoiceCount));
    }

    private void SyncChoiceToggles(string value)
    {
        var selected = SplitDisplay(value).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (Field?.Definition.Key == "structure.specialties") foreach(var v in selected.Where(v=>!ChoiceToggles.Any(c=>c.Display==v)))
        {var toggle=new UnitChoiceToggleViewModel(v,true,false){Kind=Key};toggle.SelectionChanged+=ChoiceToggle_SelectionChanged;ChoiceToggles.Add(toggle);}
        _suppressChoiceSync = true;
        try
        {
            foreach (var choice in ChoiceToggles)
            {
                choice.SetSilently(selected.Contains(choice.Display));
            }
            OnPropertyChanged(nameof(ChoiceSummary));
        OnPropertyChanged(nameof(SelectedChoices)); OnPropertyChanged(nameof(SelectedChoiceCount));
        }
        finally
        {
            _suppressChoiceSync = false;
        }
    }

    private static IReadOnlyList<string> SplitDisplay(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string DeploymentPresetLabel(string value) => value switch
    {
        "2473.49823322" => "2473.49823322（侦察）",
        "3533.56890459" => "3533.56890459（空降）",
        _ => value
    };
}

public sealed record DeploymentPresetViewModel(string Value, string Display);
