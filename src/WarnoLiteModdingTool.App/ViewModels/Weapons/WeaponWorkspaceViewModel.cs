using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using WarnoLiteModdingTool.App.ViewModels.Units;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.ViewModels.Weapons;

public sealed class WeaponWorkspaceViewModel : ObservableObject
{
    private readonly WeaponWorkspaceData _data;
    private readonly IReadOnlyList<WeaponFieldViewModel.ReferenceChoice> _ammoChoicesEnglish;
    private readonly IReadOnlyList<WeaponFieldViewModel.ReferenceChoice> _ammoChoicesChinese;
    private readonly DraftStore _draftStore;
    private readonly UnitWorkspaceViewModel _transactions;
    private readonly Action<string> _setStatus;
    private UnitListItemViewModel? _selectedUnit;
    private WeaponRecord? _selectedWeapon;
    private WeaponMountItemViewModel? _selectedMount;
    private string _selectedScope = "仅当前 Unit";
    private string _replacementWeapon = string.Empty;
    private string _unitSearchText = string.Empty;

    public WeaponWorkspaceViewModel(
        WeaponWorkspaceData data,
        DraftStore draftStore,
        UnitWorkspaceViewModel transactions,
        Action<string> setStatus)
    {
        _data = data;
        _ammoChoicesEnglish=data.Ammunition.OrderBy(a=>a.Name,StringComparer.Ordinal).Select(a=>new WeaponFieldViewModel.ReferenceChoice(a.Name,a.DisplayName,a.SearchText)).ToArray();
        _ammoChoicesChinese=data.Ammunition.OrderBy(a=>a.Name,StringComparer.Ordinal).Select(a=>new WeaponFieldViewModel.ReferenceChoice(a.Name,a.ChineseName,a.SearchText)).ToArray();
        _draftStore = draftStore;
        _transactions = transactions;
        _setStatus = setStatus;
        Units = new ObservableCollection<UnitListItemViewModel>(data.Units.Where(item => item.Weapons.Count > 0).Select(item => new UnitListItemViewModel(item)));
        FilterRows = data.Units.Select(Controls.FilterRows.Unit).ToArray();
        UnitsView = new ListCollectionView(Units) { Filter = MatchesUnitSearch };
        Weapons = [];
        Mounts = [];
        Fields = [];
        FieldSections = [];
        ScopeOptions = ["仅当前 Unit", "所选 Unit", "全部引用"];
        WeaponChoices = data.Weapons.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        SelectedUnit = Units.FirstOrDefault();
    }

    public IReadOnlyList<Controls.FilterRow> FilterRows { get; private set; } = [];
    public Func<string,bool>? UnitFilter { get; set; }
    public ObservableCollection<UnitListItemViewModel> Units { get; }
    public ICollectionView UnitsView { get; }
    public ObservableCollection<WeaponRecord> Weapons { get; }
    public ObservableCollection<WeaponMountItemViewModel> Mounts { get; }
    public ObservableCollection<WeaponFieldViewModel> Fields { get; }

    public ObservableCollection<FieldSectionViewModel<WeaponFieldViewModel>> FieldSections { get; }
    public IReadOnlyList<string> ScopeOptions { get; }
    public IEnumerable<string> VisibleScopeOptions => ScopeOptions.Where(s => Advanced.EditorMode.IsAdvanced || s != "全部引用");
    public void RefreshMode()
    {
        if (!Advanced.EditorMode.IsAdvanced && SelectedScope == "全部引用") SelectedScope = ScopeOptions[0];
        OnPropertyChanged(nameof(VisibleScopeOptions));
        foreach (var field in Fields) field.RefreshMode();
    }
    public IReadOnlyList<string> WeaponChoices { get; }

    public string UnitSearchText
    {
        get => _unitSearchText;
        set
        {
            if (SetProperty(ref _unitSearchText, value ?? string.Empty))
            {
                UnitsView.Refresh();
            }
        }
    }

    public UnitListItemViewModel? SelectedUnit
    {
        get => _selectedUnit;
        set
        {
            if (SetProperty(ref _selectedUnit, value))
            {
                if (value is not null)
                {
                    value.IsWeaponScopeSelected = true;
                }

                RebuildWeapons();
                OnPropertyChanged(nameof(WeaponScopeSelectedCount));
            }
        }
    }

    public WeaponRecord? SelectedWeapon
    {
        get => _selectedWeapon;
        set
        {
            if (SetProperty(ref _selectedWeapon, value))
            {
                ReplacementWeapon = value?.Name ?? string.Empty;
                RebuildMounts();
                OnPropertyChanged(nameof(ImpactText));
                OnPropertyChanged(nameof(SelectedUnitsText));
            }
        }
    }

    public WeaponMountItemViewModel? SelectedMount
    {
        get => _selectedMount;
        set
        {
            if (SetProperty(ref _selectedMount, value))
            {
                RebuildFields();
                OnPropertyChanged(nameof(ImpactText));
                OnPropertyChanged(nameof(TotalAmmoText));
                OnPropertyChanged(nameof(PresentationText));
            }
        }
    }

    public string SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (SetProperty(ref _selectedScope, value ?? ScopeOptions[0]))
            {
                OnPropertyChanged(nameof(SelectedUnitsText));
                RebuildFields();
            }
        }
    }

    public string ReplacementWeapon
    {
        get => _replacementWeapon;
        set
        {
            if (SetProperty(ref _replacementWeapon, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanReplaceWeapon));
            }
        }
    }

    public bool CanReplaceWeapon => SelectedWeapon is not null && ReplacementWeapon.Length > 0 && ReplacementWeapon != SelectedWeapon.Name && !_transactions.IsTransactionBusy;

    public string ImpactText
    {
        get
        {
            if (SelectedWeapon is null)
            {
                return "尚未选择 Weapon";
            }

            var weaponUnits = _data.References.WeaponUnits.GetValueOrDefault(SelectedWeapon.Name) ?? [];
            if (SelectedMount is null || SelectedMount.Mount.AmmoName.Length == 0)
            {
                return $"Weapon 被 {weaponUnits.Count} 个 Unit 引用";
            }

            var ammo = SelectedMount.Mount.AmmoName;
            var ammoWeapons = _data.References.AmmoWeapons.GetValueOrDefault(ammo) ?? [];
            var ammoUnits = _data.References.AmmoUnits.GetValueOrDefault(ammo) ?? [];
            return $"Weapon：{weaponUnits.Count} 个 Unit · Ammo：{ammoWeapons.Count} 个 Weapon / {ammoUnits.Count} 个 Unit";
        }
    }

    public string SelectedUnitsText => SelectedScope switch
    {
        "全部引用" => "作用域：全部引用者；将直接修改共享对象",
        "所选 Unit" => $"作用域：{Units.Count(item => item.IsWeaponScopeSelected)} 个勾选 Unit",
        _ => $"作用域：{SelectedUnit?.DisplayName ?? "未选择"}"
    };

    public int WeaponScopeSelectedCount => Units.Count(item => item.IsWeaponScopeSelected);

    public string TotalAmmoText
    {
        get
        {
            if (SelectedWeapon is null || SelectedMount?.Mount.AmmoBoxIndex is not int box)
            {
                return "总弹量：无法推导";
            }

            var salves = SelectedWeapon.Field($"weapon.salves.{box}");
            var ammo = _data.Ammo(SelectedMount.Mount.AmmoName);
            if (!int.TryParse(salves?.DisplayValue, out var salvoCount) || ammo?.ShotsPerSalvo is not int shots)
            {
                return "总弹量：缺少 Salves 或 ShotsCountPerSalvo";
            }

            return $"推导总弹量：{salvoCount} × {shots} = {salvoCount * shots}；界面每轮 {ammo.DisplayPerSalvo?.ToString() ?? "?"}";
        }
    }

    public string PresentationText
    {
        get
        {
            if (SelectedMount is null)
            {
                return "尚未选择挂载";
            }

            var values = new[]
            {
                $"EffectTag: {SelectedMount.Mount.EffectTag}",
                $"WeaponAlternative: {SelectedMount.Mount.WeaponAlternative}",
                $"动画键: {SelectedMount.Mount.AnimationKeys}",
                $"Weapon 表现引用: {SelectedMount.Mount.PresentationReferences}",
                $"Unit 表现引用: {string.Join(", ", SelectedUnit?.Unit.PresentationReferences ?? [])}"
            };
            return string.Join(Environment.NewLine, values);
        }
    }

    public void ScopeSelectionChanged()
    {
        OnPropertyChanged(nameof(WeaponScopeSelectedCount));
        OnPropertyChanged(nameof(SelectedUnitsText));
        RebuildFields();
    }

    public void SelectVisibleScopeUnits()
    {
        if (SelectedWeapon is null)
        {
            return;
        }

        var visible = UnitsView.Cast<UnitListItemViewModel>().Select(item => item.InternalName).ToHashSet(StringComparer.Ordinal);
        var affected = (_data.References.WeaponUnits.GetValueOrDefault(SelectedWeapon.Name) ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var unit in Units)
        {
            if (!affected.Contains(unit.InternalName))
            {
                unit.IsWeaponScopeSelected = false;
            }
            else if (visible.Contains(unit.InternalName))
            {
                unit.IsWeaponScopeSelected = true;
            }
        }

        ScopeSelectionChanged();
    }

    public void ClearScopeSelection()
    {
        foreach (var unit in Units.Where(item => item.IsWeaponScopeSelected))
        {
            unit.IsWeaponScopeSelected = false;
        }

        ScopeSelectionChanged();
    }

    public void RefreshFromDrafts() => RebuildFields();

    public async Task FlushAsync()
    {
        foreach (var field in Fields.ToArray())
        {
            await field.FlushAsync();
        }
    }

    public async Task ReplaceWeaponAsync()
    {
        if (!CanReplaceWeapon || SelectedWeapon is null)
        {
            return;
        }

        var selected = ToScope() == DraftEditScope.AllReferences
            ? _data.References.WeaponUnits.GetValueOrDefault(SelectedWeapon.Name) ?? []
            : ScopeUnits(SelectedWeapon.Name, null);
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("当前 Weapon 没有可替换的 Unit 引用。");
        }
        var group = $"weapon-replace:{Guid.NewGuid():N}";
        foreach (var unitName in selected)
        {
            var unit = _data.Units.Single(item => item.Name == unitName);
            var relative = unit.Source.RelativeSourceFile;
            var key = $"weapon.reference.{SelectedWeapon.Name}";
            var operation = new DraftOperation(
                DraftOperation.CreateId(DraftTargetKind.UnitWeaponReference, relative, unit.Name, key),
                group,
                DraftTargetKind.UnitWeaponReference,
                "units",
                relative,
                unit.Name,
                unit.Source.TypeName,
                key,
                "ModulesDescriptors.WeaponDescriptor",
                "Reference",
                SelectedWeapon.Name,
                $"$/GFX/Weapon/{SelectedWeapon.Name}",
                ReplacementWeapon,
                $"$/GFX/Weapon/{ReplacementWeapon}",
                $"{unit.DisplayName} · Weapon：{SelectedWeapon.Name} → {ReplacementWeapon}",
                null,
                false,
                DateTimeOffset.UtcNow,
                EditScope: ToScope(),
                SelectedUnitNames: selected,
                ContextWeaponName: SelectedWeapon.Name);
            await _draftStore.UpsertAsync(operation);
        }

        _transactions.RefreshExternalDraftState();
        _setStatus($"已保存 {selected.Count} 个 Unit 的 Weapon 替换草稿；正式文件未改变");
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
        _transactions.RefreshExternalDraftState();
        _setStatus("已撤销一项 Weapon/Ammo 草稿");
    }

    public void SetTransactionLocked(bool locked)
    {
        foreach (var field in Fields)
        {
            field.SetLocked(locked);
        }

        OnPropertyChanged(nameof(CanReplaceWeapon));
    }

    private async Task PersistFieldAsync(WeaponFieldViewModel viewModel)
    {
        if (!WeaponValueConverter.TryFormat(viewModel.Field, viewModel.EditValue, out var normalized, out var raw, out var error))
        {
            viewModel.Revert(error);
            return;
        }

        var owner = viewModel.Field.Definition.Owner;
        var kind = owner switch
        {
            WeaponFieldOwner.Ammo => DraftTargetKind.AmmoField,
            WeaponFieldOwner.MountedWeapon when viewModel.Field.Definition.FieldName == "Ammunition" => DraftTargetKind.MountedWeaponAmmo,
            _ => DraftTargetKind.WeaponField
        };
        var scopeUnits = ScopeUnits(SelectedWeapon?.Name, owner == WeaponFieldOwner.Ammo ? viewModel.Field.OwnerObjectName : null);
        var relative = viewModel.Field.Location.RelativeSourceFile;
        var operation = new DraftOperation(
            DraftOperation.CreateId(kind, relative, viewModel.Field.OwnerObjectName, viewModel.Field.Key),
            null,
            kind,
            kind == DraftTargetKind.AmmoField ? "ammo" : "weapons",
            relative,
            viewModel.Field.OwnerObjectName,
            viewModel.Field.OwnerObjectType,
            viewModel.Field.Key,
            viewModel.Field.Location.FieldPath,
            viewModel.Field.Definition.ValueKind.ToString(),
            viewModel.Field.DisplayValue,
            viewModel.Field.RawValue,
            normalized,
            raw,
            $"{viewModel.Field.OwnerObjectName} · {viewModel.Field.Definition.Label}：{viewModel.Field.DisplayValue} → {normalized}（{SelectedScope}）",
            null,
            false,
            DateTimeOffset.UtcNow,
            EditScope: ToScope(),
            SelectedUnitNames: scopeUnits,
            ContextWeaponName: SelectedWeapon?.Name,
            ContextIndex: SelectedMount?.Mount.Index);

        if (normalized == viewModel.Field.DisplayValue)
        {
            if (_draftStore.Operations.Any(item => item.Id == operation.Id))
            {
                await _draftStore.RemoveAsync(operation.Id);
            }
            viewModel.MarkPersisted(null, viewModel.Field.DisplayValue, "已恢复基线");
        }
        else
        {
            await _draftStore.UpsertAsync(operation);
            viewModel.MarkPersisted(operation, normalized, "草稿已保存");
        }

        _transactions.RefreshExternalDraftState();
        _setStatus($"Weapon/Ammo 草稿已保存 · {_draftStore.Operations.Count} 项 · 正式 Mod 文件未改变");
    }

    private IReadOnlyList<string> ScopeUnits(string? weaponName, string? ammoName)
    {
        if (ToScope() == DraftEditScope.AllReferences)
        {
            return [];
        }

        var selected = ToScope() == DraftEditScope.CurrentUnit
            ? new[] { SelectedUnit?.InternalName ?? string.Empty }
            : Units.Where(item => item.IsWeaponScopeSelected).Select(item => item.InternalName).ToArray();
        selected = selected.Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var affected = ammoName is not null
            ? _data.References.AmmoUnits.GetValueOrDefault(ammoName) ?? []
            : _data.References.WeaponUnits.GetValueOrDefault(weaponName ?? string.Empty) ?? [];
        var valid = selected.Where(item => affected.Contains(item, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        if (valid.Length != selected.Length)
        {
            throw new InvalidOperationException("所选 Unit 中存在不引用当前 Weapon/Ammo 的对象，请取消这些勾选。");
        }
        if (valid.Length == 0)
        {
            throw new InvalidOperationException("当前作用域没有引用该 Weapon/Ammo 的 Unit。");
        }

        return valid;
    }

    private DraftEditScope ToScope() => SelectedScope switch
    {
        "所选 Unit" => DraftEditScope.SelectedUnits,
        "全部引用" => DraftEditScope.AllReferences,
        _ => DraftEditScope.CurrentUnit
    };

    private void RebuildWeapons()
    {
        Weapons.Clear();
        foreach (var name in SelectedUnit?.Unit.Weapons ?? [])
        {
            if (_data.Weapon(name) is { } weapon)
            {
                Weapons.Add(weapon);
            }
        }

        SelectedWeapon = Weapons.FirstOrDefault();
    }

    private void RebuildMounts()
    {
        Mounts.Clear();
        foreach (var mount in SelectedWeapon?.Mounts ?? [])
        {
            Mounts.Add(new WeaponMountItemViewModel(mount, _data.Ammo(mount.AmmoName)));
        }

        SelectedMount = Mounts.FirstOrDefault();
    }

    private void RebuildFields()
    {
        Fields.Clear();
        FieldSections.Clear();
        if (SelectedWeapon is null || SelectedMount is null)
        {
            return;
        }

        var fields = new List<WeaponFieldValue>();
        if (SelectedMount.Mount.AmmoBoxIndex is int box && SelectedWeapon.Field($"weapon.salves.{box}") is { } salves)
        {
            fields.Add(salves);
        }
        fields.AddRange(SelectedMount.Mount.Fields);
        fields.AddRange(SelectedWeapon.Fields.Where(item => item.Key.StartsWith($"turret.{SelectedMount.Mount.TurretIndex}.", StringComparison.Ordinal)));
        if (_data.Ammo(SelectedMount.Mount.AmmoName) is { } ammo)
        {
            fields.AddRange(ammo.Fields);
        }

        var resolved = DraftResolver.Resolve(_transactions.Data, _data, _draftStore.Operations)
            .Where(item => item.Status == DraftResolutionStatus.Active)
            .ToDictionary(item => item.Operation.Id, item => item.Operation, StringComparer.Ordinal);
        foreach (var field in fields.DistinctBy(item => item.Key))
        {
            var kind = field.Definition.Owner switch
            {
                WeaponFieldOwner.Ammo => DraftTargetKind.AmmoField,
                WeaponFieldOwner.MountedWeapon when field.Definition.FieldName == "Ammunition" => DraftTargetKind.MountedWeaponAmmo,
                _ => DraftTargetKind.WeaponField
            };
            var id = DraftOperation.CreateId(kind, field.Location.RelativeSourceFile, field.OwnerObjectName, field.Key);
            var viewModel = new WeaponFieldViewModel(field, resolved.GetValueOrDefault(id), PersistFieldAsync);
            if(field.Definition.FieldName=="Ammunition")viewModel.SetAmmoChoices(Localisation.UiText.Current.English?_ammoChoicesEnglish:_ammoChoicesChinese);
            viewModel.SetLocked(_transactions.IsTransactionBusy);
            Fields.Add(viewModel);
        }

        foreach (var section in FieldSectionBuilder.Build(Fields, field => field.Section, field => field.Group))
        {
            FieldSections.Add(section);
        }
    }

    private bool MatchesUnitSearch(object item) =>
        item is UnitListItemViewModel unit && (UnitFilter?.Invoke(unit.InternalName) ?? true) &&
        (UnitSearchText.Length == 0 ||
         unit.DisplayName.Contains(UnitSearchText, StringComparison.CurrentCultureIgnoreCase) ||
         unit.InternalName.Contains(UnitSearchText, StringComparison.OrdinalIgnoreCase));
}
