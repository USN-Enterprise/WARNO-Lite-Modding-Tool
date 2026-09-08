using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.ViewModels.Weapons;

public sealed class AmmoListItemViewModel : ObservableObject
{
    public AmmoListItemViewModel(AmmoRecord ammo, WeaponReferenceIndex references)
    {
        Ammo = ammo;
        System.ComponentModel.PropertyChangedEventManager.AddHandler(Localisation.UiText.Current,(_,_)=>OnPropertyChanged(nameof(DisplayName)),string.Empty);
        WeaponCount = references.AmmoWeapons.GetValueOrDefault(ammo.Name)?.Count ?? 0;
        UnitCount = references.AmmoUnits.GetValueOrDefault(ammo.Name)?.Count ?? 0;
    }

    public AmmoRecord Ammo { get; }
    public string Name => Ammo.Name;
    private string? _draftName;
    public string DisplayName=>_draftName??(Localisation.UiText.Current.English?Ammo.DisplayName:Ammo.ChineseName);
    public void SetDraftName(string? name){_draftName=name;OnPropertyChanged(nameof(DisplayName));}
    public string RelativeSourceFile => Ammo.Source.RelativeSourceFile;
    public int WeaponCount { get; }
    public int UnitCount { get; }
    public string ReferencesText => $"{WeaponCount} Weapon · {UnitCount} Unit";
}
