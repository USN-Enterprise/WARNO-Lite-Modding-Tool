using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.ViewModels.Weapons;

public sealed class WeaponMountItemViewModel(MountedWeaponRecord mount, AmmoRecord? ammo = null)
{
    public MountedWeaponRecord Mount { get; } = mount;
    public string Header => $"槽位 {Mount.Index + 1} · AmmoBox {Mount.AmmoBoxIndex?.ToString() ?? "?"}";
    public string AmmoName => (ammo is null ? Mount.AmmoName : Localisation.UiText.Current.English ? ammo.DisplayName : ammo.ChineseName) + " · " + Mount.AmmoName;
    public string Detail => string.Join(" · ", new[] { Mount.WeaponAlternative, Mount.EffectTag }.Where(item => item.Length > 0));
}
