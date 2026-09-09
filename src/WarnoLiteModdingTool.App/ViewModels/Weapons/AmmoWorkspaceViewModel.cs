using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using WarnoLiteModdingTool.App.ViewModels.Units;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.ViewModels.Weapons;

public sealed class AmmoWorkspaceViewModel : ObservableObject
{
    private readonly WeaponWorkspaceData _data;
    private readonly DraftStore _draftStore;
    private readonly PendingFieldEdits<WeaponFieldViewModel> _fieldEdits;
    private readonly UnitWorkspaceViewModel _transactions;
    private readonly Action<string> _setStatus;
    private AmmoListItemViewModel? _selectedAmmo;
    private string _searchText = string.Empty;
    private string _referenceSearchText = string.Empty;

    public AmmoWorkspaceViewModel(
        WeaponWorkspaceData data,
        DraftStore draftStore,
        UnitWorkspaceViewModel transactions,
        Action<string> setStatus)
    {
        _data = data;
        _draftStore = draftStore;
        _transactions = transactions;
        _setStatus = setStatus;
        transactions.Data.Localisation.AddKnownTokens(draftStore.Operations.Select(o=>o.NameToken??""));
        Ammunition = new ObservableCollection<AmmoListItemViewModel>(
            data.Ammunition
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => new AmmoListItemViewModel(item, data.References)));
        var unitsById=data.Units.ToDictionary(u=>u.Name);
        FilterRows=data.Ammunition.SelectMany(a=> {
            var users=data.References.AmmoUnits.GetValueOrDefault(a.Name)??[];
            return users.DefaultIfEmpty("").Select(id=> {
                var values=unitsById.TryGetValue(id,out var unit)?new Dictionary<string,string[]>(Controls.FilterRows.Unit(unit).Values):new Dictionary<string,string[]>();
                values["草稿"]=[draftStore.Operations.Any(o=>o.ObjectName==a.Name)?"有草稿":"无草稿"];
                values["伤害类型"]=[a.Field("ammo.damage.family")?.DisplayValue??"未知"];
                foreach(var (key,label) in new[]{("position","允许指定地点射击"),("moving","允许行进间射击"),("indirect","使用间接射击"),("fireAndForget","射后不理")})values[label]=[a.Field("ammo.behavior."+key)?.DisplayValue??"未知"];
                return new Controls.FilterRow(a.Name,values);
            });
        }).ToArray();
        AmmunitionView = new ListCollectionView(Ammunition);
        Fields = [];
        _fieldEdits = new(Fields, field => field.FlushAsync(), field => field.HasUnsavedEdit);
        FieldSections = [];
        References = [];
        AmmunitionView.Filter = MatchesSearch;
        RefreshNames();SelectedAmmo = Ammunition.FirstOrDefault();
    }

    public IReadOnlyList<Controls.FilterRow> FilterRows { get; private set; } = [];
    public Func<string,bool>? AmmoFilter {get;set;}
    public ObservableCollection<AmmoListItemViewModel> Ammunition { get; }
    public ICollectionView AmmunitionView { get; }
    public ObservableCollection<WeaponFieldViewModel> Fields { get; }

    public ObservableCollection<FieldSectionViewModel<WeaponFieldViewModel>> FieldSections { get; }
    public ObservableCollection<AmmoReferenceItemViewModel> References { get; }

    public AmmoListItemViewModel? SelectedAmmo
    {
        get => _selectedAmmo;
        set
        {
            if (SetProperty(ref _selectedAmmo, value))
            {
                RebuildFields();
                OnPropertyChanged(nameof(ImpactText));
                OnPropertyChanged(nameof(SourceText));
                RebuildReferences();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                AmmunitionView.Refresh();
                OnPropertyChanged(nameof(VisibleAmmoCount));
            }
        }
    }

    public int VisibleAmmoCount => AmmunitionView.Cast<object>().Count();

    public string ImpactText => SelectedAmmo is null
        ? "尚未选择 Ammo"
        : $"全部引用修改 · {SelectedAmmo.WeaponCount} 个 Weapon / {SelectedAmmo.UnitCount} 个 Unit";

    public string SourceText => SelectedAmmo?.RelativeSourceFile ?? "—";

    public string ReferenceSearchText
    {
        get => _referenceSearchText;
        set
        {
            if (SetProperty(ref _referenceSearchText, value ?? string.Empty))
            {
                RebuildReferences();
            }
        }
    }

    public bool HasReferences => References.Count > 0;

    public string ReferenceDraftStatus => SelectedAmmo is null
        ? string.Empty
        : _draftStore.Operations.Any(item => item.TargetKind is (DraftTargetKind.AmmoField or DraftTargetKind.AmmoName) && item.ObjectName == SelectedAmmo.Name)
            ? "当前 Ammo 含未应用草稿；引用链按项目基线显示"
            : "引用链来自本次项目索引，无需重新扫描";

    private void RefreshNames(){foreach(var item in Ammunition)item.SetDraftName(_draftStore.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.AmmoName&&o.ObjectName==item.Name)?.TargetValue);}
    public void RefreshFromDrafts()
    {
        RefreshNames();
        RebuildFields();
        RebuildReferences();
        RefreshFilter();
    }
    public void RefreshFilter(){var changed=_draftStore.Operations.Select(o=>o.ObjectName).ToHashSet();foreach(var row in FilterRows)((Dictionary<string,string[]>)row.Values)["草稿"]=[changed.Contains(row.Id)?"有草稿":"无草稿"];AmmunitionView.Refresh();OnPropertyChanged(nameof(VisibleAmmoCount));}

    public async Task FlushAsync()
    {
        await _fieldEdits.FlushAsync();
    }

    public async Task UndoFieldAsync(WeaponFieldViewModel field)
    {
        var operation = _draftStore.Operations.FirstOrDefault(item => item.Id == field.Draft?.Id);
        if (operation is null)
        {
            return;
        }

        await _draftStore.RemoveAsync(operation.Id);
        field.MarkPersisted(null, field.Field.DisplayValue, "已撤销草稿");
        RefreshNames();
        RefreshFilter();
        _transactions.RefreshExternalDraftState();
        _setStatus("已撤销一项 Ammo 草稿");
    }

    public void SetTransactionLocked(bool locked)
    {
        foreach (var field in Fields)
        {
            field.SetLocked(locked);
        }
    }

    private async Task PersistFieldAsync(WeaponFieldViewModel viewModel)
    {
        if(viewModel.Field.Key=="ammo.name"){
            var ammo=_data.Ammo(viewModel.Field.OwnerObjectName)!;var input=viewModel.EditValue.Trim();if(input.Length==0){viewModel.Revert("名称不能为空");return;}
            var existing=_draftStore.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.AmmoName&&o.ObjectName==ammo.Name);
            if(input==viewModel.Field.DisplayValue){if(existing is not null)await _draftStore.RemoveAsync(existing.Id);viewModel.MarkPersisted(null,input,"已恢复基线");}
            else{var op=AmmoNames.Operation(_transactions.Data,ammo,input,existing?.NameToken??_transactions.Data.Localisation.GenerateToken());await _draftStore.UpsertAsync(op);viewModel.MarkPersisted(op,input,"草稿已保存");}
            RefreshNames();RefreshFilter();_transactions.RefreshExternalDraftState();return;
        }
        if (!WeaponValueConverter.TryFormat(viewModel.Field, viewModel.EditValue, out var normalized, out var raw, out var error))
        {
            viewModel.Revert(error);
            return;
        }

        var field = viewModel.Field;
        var operation = new DraftOperation(
            DraftOperation.CreateId(DraftTargetKind.AmmoField, field.Location.RelativeSourceFile, field.OwnerObjectName, field.Key),
            null,
            DraftTargetKind.AmmoField,
            "ammo",
            field.Location.RelativeSourceFile,
            field.OwnerObjectName,
            field.OwnerObjectType,
            field.Key,
            field.Location.FieldPath,
            field.Definition.ValueKind.ToString(),
            field.DisplayValue,
            field.RawValue,
            normalized,
            raw,
            $"{field.OwnerObjectName} · {field.Definition.Label}：{field.DisplayValue} → {normalized}（全部引用）",
            null,
            false,
            DateTimeOffset.UtcNow,
            EditScope: DraftEditScope.AllReferences,
            SelectedUnitNames: []);

        if (normalized == field.DisplayValue)
        {
            if (_draftStore.Operations.Any(item => item.Id == operation.Id))
            {
                await _draftStore.RemoveAsync(operation.Id);
            }

            viewModel.MarkPersisted(null, field.DisplayValue, "已恢复基线");
        }
        else
        {
            await _draftStore.UpsertAsync(operation);
            viewModel.MarkPersisted(operation, normalized, "草稿已保存");
        }

        RefreshFilter();
        _transactions.RefreshExternalDraftState();
        _setStatus($"Ammo 草稿已保存 · {_draftStore.Operations.Count} 项 · 正式 Mod 文件未改变");
    }

    private void RebuildFields()
    {
        Fields.Clear();
        FieldSections.Clear();
        if (SelectedAmmo is null)
        {
            return;
        }

        var resolved = DraftResolver.Resolve(_transactions.Data, _data, _draftStore.Operations)
            .Where(item => item.Status == DraftResolutionStatus.Active)
            .ToDictionary(item => item.Operation.Id, item => item.Operation, StringComparer.Ordinal);
        var nameAmmo=SelectedAmmo.Ammo;
        if(nameAmmo.CanEditName&&nameAmmo.NameLocation is {} nameLocation){var definition=new WeaponFieldDefinition("ammo.name","名称","游戏内名称","修改当前弹药名称，影响全部引用者。",WeaponFieldOwner.Ammo,"Name",WeaponValueKind.Text,Section:"基本信息");
            var nameField=new WeaponFieldValue(definition,nameAmmo.Name,nameAmmo.Source.TypeName,Localisation.UiText.Current.English?nameAmmo.DisplayName:nameAmmo.ChineseName,nameAmmo.NameRaw,nameLocation,[]);var draft=resolved.Values.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.AmmoName&&o.ObjectName==nameAmmo.Name);var nameVm=new WeaponFieldViewModel(nameField,draft,PersistFieldAsync);nameVm.SetLocked(_transactions.IsTransactionBusy);Fields.Add(nameVm);}
        foreach (var field in SelectedAmmo.Ammo.Fields)
        {
            var id = DraftOperation.CreateId(DraftTargetKind.AmmoField, field.Location.RelativeSourceFile, field.OwnerObjectName, field.Key);
            var viewModel = new WeaponFieldViewModel(field, resolved.GetValueOrDefault(id), PersistFieldAsync);
            viewModel.SetLocked(_transactions.IsTransactionBusy);
            Fields.Add(viewModel);
        }

        for (var i = 0; i < Fields.Count; i++)
        {
            var current = Fields[i];
            Fields[i] = _fieldEdits.Restore(current, old => old.Field.OwnerObjectName == current.Field.OwnerObjectName &&
                old.Field.Key == current.Field.Key && old.EditContext == current.EditContext);
        }
        foreach (var section in FieldSectionBuilder.Build(Fields, field => field.Section, field => field.Group))
        {
            FieldSections.Add(section);
        }
    }

    private bool MatchesSearch(object item) =>
        item is AmmoListItemViewModel ammo && (AmmoFilter?.Invoke(ammo.Name)??true) &&
        (SearchText.Length == 0 ||
         (ammo.Ammo.SearchText.Contains(SearchText,StringComparison.OrdinalIgnoreCase)||ammo.DisplayName.Contains(SearchText,StringComparison.OrdinalIgnoreCase)) ||
         ammo.RelativeSourceFile.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    private void RebuildReferences()
    {
        References.Clear();
        if (SelectedAmmo is null)
        {
            OnPropertyChanged(nameof(HasReferences));
            OnPropertyChanged(nameof(ReferenceDraftStatus));
            return;
        }

        var query = ReferenceSearchText.Trim();
        foreach (var weapon in _data.References.AmmoWeapons.GetValueOrDefault(SelectedAmmo.Name) ?? [])
        {
            var units = _data.References.WeaponUnits.GetValueOrDefault(weapon) ?? [];
            if (query.Length > 0 &&
                !weapon.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !units.Any(unit => unit.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            References.Add(new AmmoReferenceItemViewModel(weapon, units));
        }

        OnPropertyChanged(nameof(HasReferences));
        OnPropertyChanged(nameof(ReferenceDraftStatus));
    }
}

public sealed record AmmoReferenceItemViewModel(string Weapon, IReadOnlyList<string> Units)
{
    public string UnitsText => Units.Count == 0 ? "无 Unit 引用" : string.Join(", ", Units);
    public string CountText => $"{Units.Count} Unit";
}
