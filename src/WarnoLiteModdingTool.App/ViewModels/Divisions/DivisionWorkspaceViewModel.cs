using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.IO;
using System.Windows.Data;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;

namespace WarnoLiteModdingTool.App.ViewModels.Divisions;

public sealed class DivisionListItemViewModel : ObservableObject
{
    private string _draftStatus = string.Empty;

    public DivisionListItemViewModel(DivisionRecord division) => Division = division;

    public DivisionRecord Division { get; }
    public string DisplayName => Division.DisplayName;
    public string InternalName => Division.Name;
    public string Coalition => Division.Baseline.Coalition;
    public string Country => Division.Baseline.CountryId;
    public string Type => Division.Baseline.TypeToken;
    public bool CanEdit => Division.CanEdit;
    public string EditReason => Division.CanEdit ? string.Empty : Division.EditReason;
    public string DraftStatus { get => _draftStatus; private set => SetProperty(ref _draftStatus, value); }

    public void SetDraftStatus(bool hasDraft, bool hasConflict) =>
        DraftStatus = hasConflict ? "冲突" : hasDraft ? "有草稿" : string.Empty;
}

public sealed class DivisionUnitRuleViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _availableWithoutTransport;
    private string _availableTransportsText;
    private int _maxPackNumber;
    private int _numberOfUnitInPack;
    private string _xpMultipliersText;

    public DivisionUnitRuleViewModel(DivisionUnitRuleState state, Action changed)
    {
        Unit = state.Unit;
        _availableWithoutTransport = state.AvailableWithoutTransport;
        _availableTransportsText = string.Join(", ", state.AvailableTransports);
        _maxPackNumber = state.MaxPackNumber;
        _numberOfUnitInPack = state.NumberOfUnitInPack;
        _xpMultipliersText = string.Join(", ", state.XpMultipliers.Select(FormatNumber));
        _changed = changed;
    }

    public string Unit { get; }
    public bool AvailableWithoutTransport { get => _availableWithoutTransport; set => Change(ref _availableWithoutTransport, value); }
    public string AvailableTransportsText { get => _availableTransportsText; set => Change(ref _availableTransportsText, value); }
    public int MaxPackNumber { get => _maxPackNumber; set => Change(ref _maxPackNumber, value); }
    public int NumberOfUnitInPack { get => _numberOfUnitInPack; set => Change(ref _numberOfUnitInPack, value); }
    public string XpMultipliersText { get => _xpMultipliersText; set => Change(ref _xpMultipliersText, value); }

    public DivisionUnitRuleState ToState() => new(
        Unit,
        AvailableWithoutTransport,
        Split(AvailableTransportsText),
        MaxPackNumber,
        NumberOfUnitInPack,
        ParseDoubles(XpMultipliersText));

    private void Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            _changed();
        }
    }

    internal static IReadOnlyList<string> Split(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    internal static IReadOnlyList<double> ParseDoubles(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(item => double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1.0)
        .ToArray();

    private static string FormatNumber(double value)
    {
        var text = value.ToString("0.##########", CultureInfo.InvariantCulture);
        return text.Contains('.') ? text : text + ".0";
    }
}

public sealed record DivisionUnitOption(string Name, string DisplayName, bool IsVerifiedTransporter, string Country = "", string Coalition = "", string Factory = "", string Role = "")
{
    public string SearchText => string.Equals(Name, DisplayName, StringComparison.Ordinal) ? Name : $"{DisplayName} · {Name}";
    public override string ToString() => SearchText;
}

public sealed class DivisionTransportOptionViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected;

    public DivisionTransportOptionViewModel(DivisionUnitOption unit, Action changed)
    {
        Unit = unit;
        _changed = changed;
    }

    public DivisionUnitOption Unit { get; }
    public string Name => Unit.Name;
    public string DisplayName => Unit.DisplayName;
    public string SearchText => Unit.SearchText;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _changed();
            }
        }
    }

    public void SetSilently(bool selected)
    {
        _isSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }
}

public sealed record DivisionSelectedTransportViewModel(string Name, string DisplayName, bool IsVerified)
{
    public string DisplayText => IsVerified ? DisplayName : $"{DisplayName}（原值，未确认运输能力）";
    public string Detail => IsVerified ? Name : $"{Name}；当前值原样保留，但不会作为新增运输候选";
}

public sealed class DivisionTagOptionViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected;

    public DivisionTagOptionViewModel(string value, bool selected, Action changed)
    {
        Value = value;
        _isSelected = selected;
        _changed = changed;
    }

    public string Value { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _changed();
            }
        }
    }

    public void SetPropertyForIdentity(bool selected)
    {
        _isSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }
}

public sealed class DivisionPackViewModel : ObservableObject
{
    public const string NoTransport = "（无运输）";
    private readonly Action _changed;
    private string _unit;
    private string _transport;
    private int _xp;
    private int _number;

    public DivisionPackViewModel(DivisionDeckPackState state, Action changed)
    {
        OriginalPackName = state.OriginalPackName;
        _unit = state.Unit;
        _transport = state.Transport ?? NoTransport;
        _xp = state.Xp;
        _number = state.Number;
        _changed = changed;
    }

    public string? OriginalPackName { get; }
    public string Unit { get => _unit; set => Change(ref _unit, value); }
    public string Transport { get => _transport; set => Change(ref _transport, value); }
    public int Xp { get => _xp; set => Change(ref _xp, value); }
    public int Number { get => _number; set => Change(ref _number, value); }
    public string Summary => $"{Unit} · {(Transport == NoTransport ? "无运输" : Transport)} · XP{Xp} · {Number} 卡";

    public DivisionDeckPackState ToState() => new(
        OriginalPackName,
        Unit,
        Transport == NoTransport || string.IsNullOrWhiteSpace(Transport) ? null : Transport,
        Xp,
        Number);

    private void Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(Summary));
            _changed();
        }
    }
}

public sealed class DivisionCostCurveViewModel : ObservableObject
{
    private readonly Action _changed;
    private string _costsText;

    public DivisionCostCurveViewModel(DivisionCostCurveState state, Action changed)
    {
        Category = state.Category;
        _costsText = string.Join(", ", state.Costs);
        _changed = changed;
        CostSlots = new ObservableCollection<DivisionCostSlotViewModel>();
        for (var index = 0; index < 10; index++)
        {
            var value = index < state.Costs.Count ? state.Costs[index].ToString(CultureInfo.InvariantCulture) : string.Empty;
            CostSlots.Add(new DivisionCostSlotViewModel(index + 1, value, SlotChanged));
        }
        TailCosts = state.Costs.Skip(10).ToArray();
    }

    public string Category { get; }
    public ObservableCollection<DivisionCostSlotViewModel> CostSlots { get; }
    public IReadOnlyList<int> TailCosts { get; private set; }
    public string CostsText
    {
        get => _costsText;
        set
        {
            if (SetProperty(ref _costsText, value))
            {
                var values = value.Split(',', StringSplitOptions.TrimEntries);
                for (var index = 0; index < CostSlots.Count; index++)
                {
                    CostSlots[index].SetSilently(index < values.Length ? values[index] : string.Empty);
                }
                TailCosts = values.Skip(10)
                    .Select(item => int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1)
                    .ToArray();
                _changed();
            }
        }
    }

    public DivisionCostCurveState ToState()
    {
        var last = TailCosts.Count > 0
            ? 9
            : CostSlots.Select((slot, index) => (slot, index)).Where(item => item.slot.Value.Length > 0).Select(item => item.index).DefaultIfEmpty(-1).Last();
        var values = CostSlots.Take(last + 1)
            .Select(item => int.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1)
            .Concat(TailCosts)
            .ToArray();
        return new DivisionCostCurveState(Category, values);
    }

    private void SlotChanged()
    {
        _costsText = string.Join(", ", CostSlots.Select(item => item.Value).Concat(TailCosts.Select(item => item.ToString(CultureInfo.InvariantCulture))));
        OnPropertyChanged(nameof(CostsText));
        _changed();
    }
}

public sealed class DivisionCostSlotViewModel : ObservableObject
{
    private readonly Action _changed;
    private string _value;

    public DivisionCostSlotViewModel(int number, string value, Action changed)
    {
        Number = number;
        _value = value;
        _changed = changed;
    }

    public int Number { get; }
    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value ?? string.Empty))
            {
                _changed();
            }
        }
    }

    public void SetSilently(string value)
    {
        _value = value;
        OnPropertyChanged(nameof(Value));
    }
}

public sealed class DivisionWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly DivisionWorkspaceData _data;
    private readonly DraftStore _draftStore;
    private readonly Action<string> _setStatus;
    private readonly Action _draftsChanged;
    private readonly Dictionary<string, CancellationTokenSource> _persistCancellations = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _pendingPersists = [];
    private DivisionListItemViewModel? _selectedDivision;
    private DivisionUnitRuleViewModel? _selectedRule;
    private DivisionPackViewModel? _selectedPack;
    private DivisionUnitOption? _newRuleUnit;
    private DivisionUnitOption? _newTransportUnit;
    private int _maxActivationPoints;
    private double _interfaceOrder;
    private string _coalition = string.Empty;
    private string _tagsText = string.Empty;
    private string _countryId = string.Empty;
    private string _typeToken = string.Empty;
    private string _standoutUnitsText = string.Empty;
    private string _validationText = string.Empty;
    private string _transportSearchText = string.Empty;
    private bool _isTransportPickerOpen;
    private bool _loading;
    private bool _transactionLocked;

    public DivisionWorkspaceViewModel(
        DivisionWorkspaceData data,
        DraftStore draftStore,
        Action<string> setStatus,
        Action draftsChanged)
    {
        _data = data;
        _draftStore = draftStore;
        _setStatus = setStatus;
        _draftsChanged = draftsChanged;
        Divisions = new ObservableCollection<DivisionListItemViewModel>(data.Divisions.OrderBy(item=>string.IsNullOrWhiteSpace(item.Baseline.CountryId)?1:0).ThenBy(item=>item.Baseline.CountryId,StringComparer.OrdinalIgnoreCase).Select(item => new DivisionListItemViewModel(item)));
        Rules = [];
        RuleFilterRows=data.Units.Units.Select(Controls.FilterRows.Unit).ToArray();
        RulesView=new ListCollectionView(Rules){Filter=o=>o is DivisionUnitRuleViewModel r && (RuleFilter?.Invoke(r.Unit)??true)};
        DivisionsView=new ListCollectionView(Divisions){Filter=o=>o is DivisionListItemViewModel d && (d.DisplayName.Contains(Search,StringComparison.OrdinalIgnoreCase)||d.InternalName.Contains(Search,StringComparison.OrdinalIgnoreCase)||Localisation.DivisionNames.Display(d.DisplayName,false).Contains(Search,StringComparison.OrdinalIgnoreCase))};
        Packs = [];
        CostCurves = [];
        TagOptions = [];
        UnitOptions = data.Units.Units
            .Select(item => new DivisionUnitOption(item.Name, item.DisplayName, item.HasUniqueTransporterModule, item.Country, item.Coalition, item.Factory, item.Role))
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        TransportCandidates = new ObservableCollection<DivisionTransportOptionViewModel>(
            UnitOptions.Where(item => item.IsVerifiedTransporter)
                .Select(item => new DivisionTransportOptionViewModel(item, TransportCandidateSelectionChanged)));
        TransportCandidatesView = new ListCollectionView(TransportCandidates) { Filter = MatchesTransportCandidate };
        DivisionTypeOptions = data.Divisions.Select(item => item.Baseline.TypeToken)
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        KnownTagOptions = data.Divisions.SelectMany(item => item.Baseline.Tags)
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        CoalitionOptions = data.Divisions.Select(item => item.Baseline.Coalition).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        RefreshDraftStatuses();
        SelectedDivision = Divisions.FirstOrDefault();
    }

    private string _search="";
    public string Search {get=>_search;set{if(SetProperty(ref _search,value))DivisionsView.Refresh();}}
    public ICollectionView DivisionsView {get;}
    public ICollectionView RulesView {get;}
    public IReadOnlyList<Controls.FilterRow> RuleFilterRows {get;}
    public Func<string,bool>? RuleFilter {get;set;}
    public ObservableCollection<DivisionListItemViewModel> Divisions { get; }
    public ObservableCollection<DivisionUnitRuleViewModel> Rules { get; }
    public ObservableCollection<DivisionPackViewModel> Packs { get; }
    public ObservableCollection<DivisionCostCurveViewModel> CostCurves { get; }
    public ObservableCollection<DivisionTagOptionViewModel> TagOptions { get; }
    public ObservableCollection<DivisionTransportOptionViewModel> TransportCandidates { get; }
    public ICollectionView TransportCandidatesView { get; }
    public IReadOnlyList<DivisionUnitOption> UnitOptions { get; }
    public IReadOnlyList<string> DivisionTypeOptions { get; }
    public IReadOnlyList<string> KnownTagOptions { get; }
    public IReadOnlyList<string> CoalitionOptions { get; }
    public string CompatibilitySummary => _data.SkippedNonTacticalDescriptorCount == 0
        ? string.Empty
        : $" · 另跳过 {_data.SkippedNonTacticalDescriptorCount:N0} 个其他用途描述符";
    public IReadOnlyList<string> PackUnitOptions => Rules.Select(item => item.Unit).ToArray();
    public IReadOnlyList<string> TransportOptions
    {
        get
        {
            var rule = Rules.FirstOrDefault(item => item.Unit == SelectedPack?.Unit);
            var values = new List<string>();
            if (rule?.AvailableWithoutTransport == true)
            {
                values.Add(DivisionPackViewModel.NoTransport);
            }

            if (rule is not null)
            {
                values.AddRange(DivisionUnitRuleViewModel.Split(rule.AvailableTransportsText));
            }

            return values;
        }
    }

    public DivisionListItemViewModel? SelectedDivision
    {
        get => _selectedDivision;
        set
        {
            if (SetProperty(ref _selectedDivision, value))
            {
                LoadSelected();
                OnPropertyChanged(nameof(CanEditSelected));
            }
        }
    }

    public DivisionUnitRuleViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                NewTransportUnit = null;
                SyncTransportCandidates();
                TransportCandidatesView.Refresh();
                OnPropertyChanged(nameof(AvailableTransportOptions));
                OnPropertyChanged(nameof(SelectedTransportNames));
                OnPropertyChanged(nameof(SelectedTransportItems));
            }
        }
    }
    public DivisionPackViewModel? SelectedPack
    {
        get => _selectedPack;
        set
        {
            if (SetProperty(ref _selectedPack, value))
            {
                OnPropertyChanged(nameof(TransportOptions));
                OnPropertyChanged(nameof(CanMovePackUp));
                OnPropertyChanged(nameof(CanMovePackDown));
            }
        }
    }

    public DivisionUnitOption? NewRuleUnit { get => _newRuleUnit; set => SetProperty(ref _newRuleUnit, value); }
    public DivisionUnitOption? NewTransportUnit { get => _newTransportUnit; set => SetProperty(ref _newTransportUnit, value); }
    public IReadOnlyList<DivisionUnitOption> AvailableTransportOptions => UnitOptions
        .Where(item => item.IsVerifiedTransporter && item.Name != SelectedRule?.Unit && !DivisionUnitRuleViewModel.Split(SelectedRule?.AvailableTransportsText ?? string.Empty).Contains(item.Name, StringComparer.Ordinal))
        .ToArray();
    public IReadOnlyList<string> SelectedTransportNames => DivisionUnitRuleViewModel.Split(SelectedRule?.AvailableTransportsText ?? string.Empty);
    public IReadOnlyList<DivisionSelectedTransportViewModel> SelectedTransportItems => SelectedTransportNames
        .Select(name =>
        {
            var option = UnitOptions.FirstOrDefault(item => item.Name == name);
            return new DivisionSelectedTransportViewModel(name, option?.DisplayName ?? name, option?.IsVerifiedTransporter == true);
        })
        .ToArray();
    public string TransportSearchText
    {
        get => _transportSearchText;
        set
        {
            if (SetProperty(ref _transportSearchText, value ?? string.Empty))
            {
                TransportCandidatesView.Refresh();
            }
        }
    }
    public bool IsTransportPickerOpen
    {
        get => _isTransportPickerOpen;
        set
        {
            if (SetProperty(ref _isTransportPickerOpen, value) && value)
            {
                TransportSearchText = string.Empty;
                SyncTransportCandidates();
                TransportCandidatesView.Refresh();
            }
        }
    }
    public int SelectedTransportCandidateCount => TransportCandidates.Count(item => item.IsSelected && item.Name != SelectedRule?.Unit);
    public string TransportPickerButtonText => $"选择运输（已选 {SelectedTransportCandidateCount}）";
    public int MaxActivationPoints { get => _maxActivationPoints; set => Change(ref _maxActivationPoints, value); }
    public double InterfaceOrder { get => _interfaceOrder; set => Change(ref _interfaceOrder, value); }
    public string Coalition { get => _coalition; set => ChangeIdentity(ref _coalition, value); }
    public string TagsText
    {
        get => _tagsText;
        set
        {
            if (!SetProperty(ref _tagsText, value ?? string.Empty) || _loading)
            {
                return;
            }

            var selected = DivisionUnitRuleViewModel.Split(_tagsText).ToHashSet(StringComparer.Ordinal);
            foreach (var option in TagOptions)
            {
                option.SetPropertyForIdentity(selected.Contains(option.Value));
            }
            SchedulePersist();
        }
    }
    public string CountryId { get => _countryId; set => ChangeIdentity(ref _countryId, value); }
    public string TypeToken { get => _typeToken; set => ChangeIdentity(ref _typeToken, value); }
    public IEnumerable<DivisionUnitOption> StandoutItems=>DivisionUnitRuleViewModel.Split(StandoutUnitsText).Select(id=>UnitOptions.FirstOrDefault(u=>u.Name==id)??new DivisionUnitOption(id,id,false));
    public sealed record TagGroup(string Label,DivisionTagOptionViewModel[] Options);
    private string TagGroupKey(string tag)=>CoalitionOptions.Contains(tag)?"阵营":UnitOptions.Any(u=>u.Country==tag)||_data.Divisions.Any(d=>d.Baseline.CountryId==tag)||tag=="RDA"?"国家":new[]{"AIRBORNE","AIRMOBIL","ARMORED","ARMORREC","DEFAULT","DIVNAVAL","INFANREG","MECHANIZ","MOTORIZD"}.Contains(tag)?"师类型":"其他";
    public IEnumerable<TagGroup> TagGroups=>new[]{"阵营","国家","师类型"}.Select(k=>new TagGroup(k,TagOptions.Where(o=>TagGroupKey(o.Value)==k).ToArray()));
    public void ChooseTag(string value){var group=TagGroupKey(value);TagsText=string.Join(", ",DivisionUnitRuleViewModel.Split(TagsText).Where(t=>TagGroupKey(t)!=group).Append(value));OnPropertyChanged(nameof(TagGroups));}
    public string StandoutUnitsText { get => _standoutUnitsText; set {Change(ref _standoutUnitsText, value);OnPropertyChanged(nameof(StandoutItems));} }
    public string ValidationText { get => _validationText; private set => SetProperty(ref _validationText, value); }
    public bool HasValidationMessage => ValidationText.Length > 0;
    public bool CanEditSelected => SelectedDivision?.CanEdit == true && !_transactionLocked && !_draftStore.IsBlocked;
    public bool CanMovePackUp => SelectedPack is not null && Packs.IndexOf(SelectedPack) > 0 && CanEditSelected;
    public bool CanMovePackDown => SelectedPack is not null && Packs.IndexOf(SelectedPack) >= 0 && Packs.IndexOf(SelectedPack) < Packs.Count - 1 && CanEditSelected;
    public int ActivationPoints => SelectedDivision is null ? 0 : DivisionStateValidator.CalculateActivationPoints(_data, BuildState());

    public void AddRule()
    {
        if (!CanEditSelected || NewRuleUnit is null || Rules.Any(item => item.Unit == NewRuleUnit.Name))
        {
            return;
        }

        var rule = new DivisionUnitRuleViewModel(new DivisionUnitRuleState(NewRuleUnit.Name, true, [], 1, 1, [1.0, 0.0, 0.0, 0.0]), SchedulePersist);
        Rules.Add(rule);
        SelectedRule = rule;
        NewRuleUnit = null;
        NotifyRuleOptionsChanged();
        SchedulePersist();
    }

    public void AddTransport()
    {
        if (!CanEditSelected || SelectedRule is null || NewTransportUnit is not { IsVerifiedTransporter: true })
        {
            return;
        }

        var transports = DivisionUnitRuleViewModel.Split(SelectedRule.AvailableTransportsText).ToList();
        if (!transports.Contains(NewTransportUnit.Name, StringComparer.Ordinal))
        {
            transports.Add(NewTransportUnit.Name);
            SelectedRule.AvailableTransportsText = string.Join(", ", transports);
        }
        NewTransportUnit = null;
        NotifyTransportSelectionChanged();
    }

    public void ApplyTransportSelection()
    {
        if (!CanEditSelected || SelectedRule is null)
        {
            IsTransportPickerOpen = false;
            return;
        }

        var verifiedNames = TransportCandidates.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var selectedNames = TransportCandidates.Where(item => item.IsSelected && item.Name != SelectedRule.Unit)
            .Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var current = DivisionUnitRuleViewModel.Split(SelectedRule.AvailableTransportsText);
        var result = current.Where(name => !verifiedNames.Contains(name) || selectedNames.Contains(name)).ToList();
        foreach (var candidate in TransportCandidates.Where(item => item.IsSelected && item.Name != SelectedRule.Unit))
        {
            if (!result.Contains(candidate.Name, StringComparer.Ordinal))
            {
                result.Add(candidate.Name);
            }
        }

        SelectedRule.AvailableTransportsText = string.Join(", ", result);
        IsTransportPickerOpen = false;
        NotifyTransportSelectionChanged();
    }

    public void CancelTransportSelection()
    {
        IsTransportPickerOpen = false;
        SyncTransportCandidates();
    }

    public void RemoveTransport(string unit)
    {
        if (!CanEditSelected || SelectedRule is null)
        {
            return;
        }

        SelectedRule.AvailableTransportsText = string.Join(", ", DivisionUnitRuleViewModel.Split(SelectedRule.AvailableTransportsText).Where(item => item != unit));
        SyncTransportCandidates();
        NotifyTransportSelectionChanged();
    }

    public void SelectType(string type)
    {
        if (CanEditSelected)
        {
            TypeToken = type;
        }
    }

    public void RemoveSelectedRule()
    {
        if (!CanEditSelected || SelectedRule is null)
        {
            return;
        }

        if (Packs.Any(item => item.Unit == SelectedRule.Unit))
        {
            ValidationText = "该 Unit 仍被只读默认卡组引用，不能删除对应单位池规则。";
            OnPropertyChanged(nameof(HasValidationMessage));
            return;
        }

        Rules.Remove(SelectedRule);
        SelectedRule = Rules.FirstOrDefault();
        NotifyRuleOptionsChanged();
        SchedulePersist();
    }

    public void AddPack()
    {
        if (!CanEditSelected || Rules.Count == 0)
        {
            return;
        }

        var rule = Rules[0].ToState();
        var xp = Enumerable.Range(0, rule.XpMultipliers.Count).FirstOrDefault(index => rule.XpMultipliers[index] > 0);
        var transport = rule.AvailableWithoutTransport ? null : rule.AvailableTransports.FirstOrDefault();
        var pack = new DivisionPackViewModel(new DivisionDeckPackState(null, rule.Unit, transport, xp, 1), PackChanged);
        Packs.Add(pack);
        SelectedPack = pack;
        NotifyPackPosition();
        SchedulePersist();
    }

    public void RemoveSelectedPack()
    {
        if (!CanEditSelected || SelectedPack is null)
        {
            return;
        }

        var index = Packs.IndexOf(SelectedPack);
        Packs.Remove(SelectedPack);
        SelectedPack = Packs.ElementAtOrDefault(Math.Min(index, Packs.Count - 1));
        NotifyPackPosition();
        SchedulePersist();
    }

    public void MoveSelectedPack(int delta)
    {
        if (!CanEditSelected || SelectedPack is null)
        {
            return;
        }

        var index = Packs.IndexOf(SelectedPack);
        var target = index + delta;
        if (target < 0 || target >= Packs.Count)
        {
            return;
        }

        Packs.Move(index, target);
        NotifyPackPosition();
        SchedulePersist();
    }

    public async Task FlushAsync()
    {
        if (SelectedDivision is { } selected)
        {
            if (_persistCancellations.Remove(selected.InternalName, out var cancellation))
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }

            var currentRaw = DivisionDraftCodec.Serialize(BuildState());
            var storedRaw = _draftStore.Operations.FirstOrDefault(item =>
                    item.TargetKind == DraftTargetKind.DivisionPlan && item.ObjectName == selected.InternalName)?.TargetRaw
                ?? DivisionDraftCodec.Serialize(selected.Division.Baseline);
            if (!string.Equals(currentRaw, storedRaw, StringComparison.Ordinal))
            {
                await PersistStateAsync(selected, BuildState());
            }
        }

        Task[] pending;
        lock (_pendingPersists)
        {
            pending = _pendingPersists.ToArray();
        }

        await Task.WhenAll(pending);
    }

    public void RefreshFromDrafts()
    {
        RefreshDraftStatuses();
        LoadSelected();
    }

    public void SetTransactionLocked(bool value)
    {
        _transactionLocked = value;
        OnPropertyChanged(nameof(CanEditSelected));
        NotifyPackPosition();
    }

    public void Dispose()
    {
        foreach (var cancellation in _persistCancellations.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _persistCancellations.Clear();
    }

    private void LoadSelected()
    {
        _loading = true;
        try
        {
            Rules.Clear();
            Packs.Clear();
            CostCurves.Clear();
            TagOptions.Clear();
            if (SelectedDivision is null)
            {
                return;
            }

            var state = SelectedDivision.Division.Baseline;
            var operation = _draftStore.Operations.FirstOrDefault(item => item.TargetKind == DraftTargetKind.DivisionPlan && item.ObjectName == SelectedDivision.InternalName);
            if (operation is not null && DivisionDraftCodec.TryDeserialize(operation.TargetRaw, out var target, out _))
            {
                state = target;
            }

            _maxActivationPoints = state.MaxActivationPoints;
            _interfaceOrder = state.InterfaceOrder;
            _coalition = state.Coalition;
            _tagsText = string.Join(", ", state.Tags);
            _countryId = state.CountryId;
            _typeToken = state.TypeToken;
            _standoutUnitsText = string.Join(", ", state.StandoutUnits);
            OnPropertyChanged(nameof(MaxActivationPoints));
            OnPropertyChanged(nameof(InterfaceOrder));
            OnPropertyChanged(nameof(Coalition));
            OnPropertyChanged(nameof(TagsText));
            OnPropertyChanged(nameof(CountryId));
            OnPropertyChanged(nameof(TypeToken));
            OnPropertyChanged(nameof(StandoutUnitsText));OnPropertyChanged(nameof(StandoutItems));
            foreach (var rule in state.UnitRules)
            {
                Rules.Add(new DivisionUnitRuleViewModel(rule, SchedulePersist));
            }

            foreach (var pack in state.DefaultDeck)
            {
                Packs.Add(new DivisionPackViewModel(pack, PackChanged));
            }

            foreach (var curve in state.CostCurves)
            {
                CostCurves.Add(new DivisionCostCurveViewModel(curve, SchedulePersist));
            }

            foreach (var tag in KnownTagOptions.Concat(state.Tags).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                TagOptions.Add(new DivisionTagOptionViewModel(tag, state.Tags.Contains(tag, StringComparer.Ordinal), TagsChanged));
                OnPropertyChanged(nameof(TagGroups));
            }

            SelectedRule = Rules.FirstOrDefault();
            SelectedPack = Packs.FirstOrDefault();
            var resolved = _draftStore.Operations
                .Where(item => item.TargetKind == DraftTargetKind.DivisionPlan && item.ObjectName == SelectedDivision.InternalName)
                .Select(item => DraftResolver.Resolve(_data.Units, null, _data, [item]).Single())
                .FirstOrDefault();
            ValidationText = !SelectedDivision.CanEdit
                ? SelectedDivision.EditReason
                : resolved?.Status == DraftResolutionStatus.Conflict ? resolved.Reason : string.Empty;
            OnPropertyChanged(nameof(HasValidationMessage));
            NotifyRuleOptionsChanged();
            NotifyPackPosition();
            OnPropertyChanged(nameof(ActivationPoints));
        }
        finally
        {
            _loading = false;
        }
    }

    private void TagsChanged()
    {
        if (_loading)
        {
            return;
        }

        TagsText = string.Join(", ", TagOptions.Where(item => item.IsSelected).Select(item => item.Value));
    }

    private void ChangeIdentity(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        var previous = field;
        if (!SetProperty(ref field, value ?? string.Empty, propertyName) || _loading)
        {
            return;
        }

        var tags = DivisionUnitRuleViewModel.Split(TagsText).ToList();
        if (!string.IsNullOrWhiteSpace(previous))
        {
            tags.RemoveAll(item => string.Equals(item, previous, StringComparison.Ordinal));
        }
        if (!string.IsNullOrWhiteSpace(value) && !tags.Contains(value, StringComparer.Ordinal))
        {
            tags.Add(value);
        }
        _tagsText = string.Join(", ", tags);
        OnPropertyChanged(nameof(TagsText));
        foreach (var option in TagOptions)
        {
            option.SetPropertyForIdentity(tags.Contains(option.Value, StringComparer.Ordinal));
        }
        SchedulePersist();
    }

    private void Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName) && !_loading)
        {
            SchedulePersist();
        }
    }

    private void PackChanged()
    {
        OnPropertyChanged(nameof(TransportOptions));
        OnPropertyChanged(nameof(ActivationPoints));
        SchedulePersist();
    }

    private void SchedulePersist()
    {
        if (_loading || SelectedDivision is null || !CanEditSelected)
        {
            return;
        }

        var selected = SelectedDivision;
        var state = BuildState();
        if (_persistCancellations.Remove(selected.InternalName, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        var cancellation = new CancellationTokenSource();
        _persistCancellations[selected.InternalName] = cancellation;
        var task = PersistAfterDelayAsync(selected, state, cancellation.Token);
        lock (_pendingPersists)
        {
            _pendingPersists.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_pendingPersists)
                {
                    _pendingPersists.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        OnPropertyChanged(nameof(ActivationPoints));
    }

    private async Task PersistAfterDelayAsync(
        DivisionListItemViewModel selected,
        DivisionEditState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            await PersistStateAsync(selected, state);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _setStatus($"战术师草稿保存失败：{exception.Message}");
        }
    }

    private async Task PersistStateAsync(DivisionListItemViewModel selected, DivisionEditState state)
    {
        if (!selected.CanEdit)
        {
            return;
        }

        var division = selected.Division;
        var baselineRaw = DivisionDraftCodec.Serialize(division.Baseline);
        var targetRaw = DivisionDraftCodec.Serialize(state);
        var id = DraftOperation.CreateId(DraftTargetKind.DivisionPlan, division.Source.RelativeSourceFile, division.Name, "division.plan");
        if (string.Equals(baselineRaw, targetRaw, StringComparison.Ordinal))
        {
            if (_draftStore.Operations.Any(item => item.Id == id))
            {
                await _draftStore.RemoveAsync(id);
            }
        }
        else
        {
            var operation = new DraftOperation(
                id,
                $"division:{division.Name}",
                DraftTargetKind.DivisionPlan,
                "divisions",
                division.Source.RelativeSourceFile,
                division.Name,
                division.Source.TypeName,
                "division.plan",
                "Division+DivisionRule+Deck+DeckPack+CostMatrix",
                "DivisionEditState",
                $"{division.Baseline.UnitRules.Count} 条规则 / {division.Baseline.DefaultDeck.Count} 个 Pack",
                baselineRaw,
                $"{state.UnitRules.Count} 条规则 / {state.DefaultDeck.Count} 个 Pack",
                targetRaw,
                $"{division.DisplayName} · 战术师综合修改",
                null,
                false,
                DateTimeOffset.UtcNow);
            await _draftStore.UpsertAsync(operation);
        }

        RefreshDraftStatuses();
        var errors = DivisionStateValidator.Validate(_data, division, state);
        if (SelectedDivision?.InternalName == selected.InternalName)
        {
            ValidationText = errors.Count == 0 ? string.Empty : string.Join("；", errors.Take(3));
            OnPropertyChanged(nameof(HasValidationMessage));
        }
        _draftsChanged();
        _setStatus(errors.Count == 0
            ? "战术师草稿已保存 · 正式 Mod 文件未改变"
            : "战术师草稿已保存，但需先修正校验问题");
    }

    private DivisionEditState BuildState() => new(
        MaxActivationPoints,
        InterfaceOrder,
        Coalition?.Trim() ?? string.Empty,
        DivisionUnitRuleViewModel.Split(TagsText),
        CountryId?.Trim() ?? string.Empty,
        TypeToken?.Trim() ?? string.Empty,
        DivisionUnitRuleViewModel.Split(StandoutUnitsText),
        Rules.Select(item => item.ToState()).ToArray(),
        Packs.Select(item => item.ToState()).ToArray(),
        CostCurves.Select(item => item.ToState()).ToArray());

    private void RefreshDraftStatuses()
    {
        var resolved = DraftResolver.Resolve(_data.Units, null, _data, _draftStore.Operations)
            .Where(item => item.Operation.TargetKind == DraftTargetKind.DivisionPlan)
            .ToArray();
        foreach (var item in Divisions)
        {
            var matches = resolved.Where(entry => entry.Operation.ObjectName == item.InternalName).ToArray();
            item.SetDraftStatus(matches.Length > 0, matches.Any(entry => entry.Status == DraftResolutionStatus.Conflict));
        }
    }

    private void NotifyRuleOptionsChanged()
    {
        OnPropertyChanged(nameof(PackUnitOptions));
        OnPropertyChanged(nameof(TransportOptions));
        OnPropertyChanged(nameof(AvailableTransportOptions));
    }

    private void SyncTransportCandidates()
    {
        var selected = SelectedTransportNames.ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in TransportCandidates)
        {
            candidate.SetSilently(selected.Contains(candidate.Name));
        }

        OnPropertyChanged(nameof(SelectedTransportCandidateCount));
        OnPropertyChanged(nameof(TransportPickerButtonText));
    }

    private void TransportCandidateSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedTransportCandidateCount));
        OnPropertyChanged(nameof(TransportPickerButtonText));
    }

    public Func<object,bool>? TransportFilter { get; set; }
    private bool MatchesTransportCandidate(object item) =>
        (TransportFilter?.Invoke(item) ?? true) &&
        item is DivisionTransportOptionViewModel candidate &&
        candidate.Name != SelectedRule?.Unit &&
        (TransportSearchText.Length == 0 ||
         candidate.DisplayName.Contains(TransportSearchText, StringComparison.CurrentCultureIgnoreCase) ||
         candidate.Name.Contains(TransportSearchText, StringComparison.OrdinalIgnoreCase));

    private void NotifyTransportSelectionChanged()
    {
        OnPropertyChanged(nameof(AvailableTransportOptions));
        OnPropertyChanged(nameof(SelectedTransportNames));
        OnPropertyChanged(nameof(SelectedTransportItems));
        OnPropertyChanged(nameof(TransportOptions));
        SyncTransportCandidates();
    }

    private void NotifyPackPosition()
    {
        OnPropertyChanged(nameof(CanMovePackUp));
        OnPropertyChanged(nameof(CanMovePackDown));
    }
}
