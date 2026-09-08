using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.App.ViewModels.Units;

public sealed class UnitListItemViewModel(UnitRecord unit, Action? batchSelectionChanged = null) : ObservableObject
{
    private bool _hasDraft;
    private bool _hasConflict;
    private string? _draftDisplayName;
    private bool _isWeaponScopeSelected;
    private bool _isBatchSelected;

    public UnitRecord Unit { get; private set; } = unit;
    public void UpdateProjection(UnitRecord value){Unit=value;foreach(var key in new[]{nameof(DisplayName),nameof(Coalition),nameof(Country),nameof(Category),nameof(Factory),nameof(Role)})OnPropertyChanged(key);}

    public string DisplayName => _draftDisplayName ?? Unit.DisplayName;

    public string InternalName => Unit.Name;

    public string Coalition => Unit.Coalition;

    public string Country => Unit.Country;

    public string Category => Unit.Category;

    public string Factory => Unit.Factory;

    public string Role => Unit.Role;

    public string DivisionsText => Unit.Divisions.Count == 0 ? "无已识别师规则" : string.Join(", ", Unit.Divisions);

    public string WeaponsText => Unit.Weapons.Count == 0 ? "无已识别 Weapon" : string.Join(", ", Unit.Weapons);

    public string AmmunitionText => Unit.Ammunition.Count == 0 ? "无已识别 Ammo" : string.Join(", ", Unit.Ammunition);

    public string DraftStatus => _hasConflict ? "冲突" : _hasDraft ? "有草稿" : string.Empty;

    public bool IsWeaponScopeSelected
    {
        get => _isWeaponScopeSelected;
        set => SetProperty(ref _isWeaponScopeSelected, value);
    }

    public bool IsBatchSelected
    {
        get => _isBatchSelected;
        set
        {
            if (SetProperty(ref _isBatchSelected, value))
            {
                batchSelectionChanged?.Invoke();
            }
        }
    }

    public bool HasDraft
    {
        get => _hasDraft;
        private set
        {
            if (SetProperty(ref _hasDraft, value))
            {
                OnPropertyChanged(nameof(DraftStatus));
            }
        }
    }

    public bool HasConflict
    {
        get => _hasConflict;
        private set
        {
            if (SetProperty(ref _hasConflict, value))
            {
                OnPropertyChanged(nameof(DraftStatus));
            }
        }
    }

    public void UpdateDraftState(bool hasDraft, bool hasConflict, string? draftDisplayName)
    {
        HasDraft = hasDraft;
        HasConflict = hasConflict;
        if (SetProperty(ref _draftDisplayName, draftDisplayName, nameof(DisplayName)))
        {
            OnPropertyChanged(nameof(DisplayName));
        }
    }
}
