namespace WarnoLiteModdingTool.Core.Weapons;

public static class WeaponFieldDefinitions
{
    public static IReadOnlyList<WeaponFieldDefinition> Ammo { get; } =
    [
        I("ammo.shotsPerSalvo", "库存", "每次齐射射弹数", "与 Weapon 的 Salves 相乘得到推导总弹量。", "ShotsCountPerSalvo", true),
        I("ammo.displayPerSalvo", "库存", "界面每轮数量", "仅写 Ammo 的界面显示基数。", "AffichageMunitionParSalve", true),
        D("ammo.range.ground.min", "射程", "对地最小射程", "原始 GRU；当前没有可靠的统一界面换算。", "MinimumRangeGRU", true, " GRU"),
        D("ammo.range.ground.max", "射程", "对地最大射程", "原始 GRU；当前没有可靠的统一界面换算。", "MaximumRangeGRU", true, " GRU"),
        D("ammo.range.heli.min", "射程", "对直升机最小射程", "原始 GRU。", "MinimumRangeHelicopterGRU", true, " GRU"),
        D("ammo.range.heli.max", "射程", "对直升机最大射程", "原始 GRU。", "MaximumRangeHelicopterGRU", true, " GRU"),
        D("ammo.range.air.min", "射程", "对固定翼最小射程", "原始 GRU。", "MinimumRangeAirplaneGRU", true, " GRU"),
        D("ammo.range.air.max", "射程", "对固定翼最大射程", "原始 GRU。", "MaximumRangeAirplaneGRU", true, " GRU"),
        D("ammo.range.projectile.min", "射程", "对投射物最小射程", "原始 GRU。", "MinimumRangeProjectileGRU", true, " GRU"),
        D("ammo.range.projectile.max", "射程", "对投射物最大射程", "原始 GRU。", "MaximumRangeProjectileGRU", true, " GRU"),
        C("ammo.damage.family", "伤害", "伤害族", "选项来自当前 Mod 的 DamageResistance.ndf。", "Arme", "Family"),
        I("ammo.damage.index", "伤害", "伤害/穿深索引（原始）", "伤害族 Index 不保证等于游戏面板数值。", "Arme", true, argument: "Index"),
        D("ammo.damage.physical", "伤害", "物理伤害", "Ammo 原始伤害值。", "PhysicalDamages", true),
        D("ammo.damage.suppress", "伤害", "压制伤害", "Ammo 原始压制值。", "SuppressDamages", true),
        D("ammo.damage.splashPhysical", "伤害", "伤害溅射", "RadiusSplashPhysicalDamagesGRU。", "RadiusSplashPhysicalDamagesGRU", true, " GRU"),
        D("ammo.damage.splashSuppress", "伤害", "压制溅射", "RadiusSplashSuppressDamagesGRU。", "RadiusSplashSuppressDamagesGRU", true, " GRU"),
        I("ammo.accuracy.idle", "精度", "静止精度", "BaseHitValueModifiers/Idling 原始值。", "BaseHitValueModifiers", true, map: "EBaseHitValueModifier/Idling"),
        I("ammo.accuracy.moving", "精度", "移动精度", "BaseHitValueModifiers/Moving 原始值。", "BaseHitValueModifiers", true, map: "EBaseHitValueModifier/Moving"),
        D("ammo.dispersion.min", "散布", "最小距离散布", "实际弹道散布。", "DispersionAtMinRangeGRU", true, " GRU"),
        D("ammo.dispersion.max", "散布", "最大距离散布", "实际弹道散布。", "DispersionAtMaxRangeGRU", true, " GRU"),
        D("ammo.dispersion.angle", "散布", "角散布", "Ammo 原始角散布值。", "AngleDispersion", true),
        D("ammo.time.shot", "射击", "短装填时间", "TimeBetweenTwoShots。", "TimeBetweenTwoShots", true, " 秒"),
        D("ammo.time.salvo", "射击", "长装填时间", "TimeBetweenTwoSalvos。", "TimeBetweenTwoSalvos", true, " 秒"),
        D("ammo.time.aim", "射击", "瞄准时间", "AimingTime。", "AimingTime", true, " 秒"),
        D("ammo.speed.projectile", "弹道", "弹速", "ProjectileSpeedGRU 原始值。", "ProjectileSpeedGRU", true, " GRU"),
        D("ammo.speed.acceleration", "弹道", "最大加速度", "仅在当前 Ammo 已有字段时可编辑。", "MaxAccelerationGRU", true, " GRU"),
        D("ammo.supply", "补给", "补给消耗", "SupplyCost。", "SupplyCost", true),
        B("ammo.behavior.position", "行为", "允许指定地点射击", "CanShootOnPosition。", "CanShootOnPosition"),
        B("ammo.behavior.moving", "行为", "允许行进间射击", "CanShootWhileMoving。", "CanShootWhileMoving"),
        B("ammo.behavior.indirect", "行为", "使用间接射击", "TirIndirect。", "TirIndirect"),
        B("ammo.behavior.reflex", "行为", "反应射击", "TirReflexe。", "TirReflexe"),
        B("ammo.behavior.fireAndForget", "行为", "射后不理", "IsFireAndForget；字段不存在时不猜测。", "IsFireAndForget")
    ];

    public static WeaponFieldDefinition Salves(int ammoBox) =>
        new($"weapon.salves.{ammoBox}", "库存", $"AmmoBox {ammoBox} 齐射次数", "Weapon.Salves 中与 AmmoBoxIndex 对应的项。", WeaponFieldOwner.Weapon, "Salves", WeaponValueKind.Integer, true, Section: "挂载与库存");

    public static WeaponFieldDefinition MountedAmmo(int mount) =>
        new($"mount.{mount}.ammo", "挂载", "使用的 Ammo", "只能替换为当前项目中已有 Ammo；不新增槽位。", WeaponFieldOwner.MountedWeapon, "Ammunition", WeaponValueKind.Reference, Section: "挂载与库存");

    public static WeaponFieldDefinition MountedHidden(int mount) =>
        new($"mount.{mount}.hidden", "挂载", "在界面隐藏", "仅编辑已存在的 HideInInterface。", WeaponFieldOwner.MountedWeapon, "HideInInterface", WeaponValueKind.Boolean, Section: "挂载与库存");

    public static WeaponFieldDefinition Turret(int turret, string field, string label) =>
        new($"turret.{turret}.{field}", "炮塔", label, "界面以角度显示，写回弧度。", WeaponFieldOwner.Turret, field, WeaponValueKind.Degrees, Section: "射界");

    private static WeaponFieldDefinition I(string key, string group, string label, string hint, string field, bool nonNegative, string? suffix = null, string? argument = null, string? map = null) =>
        new(key, group, label, hint, WeaponFieldOwner.Ammo, field, WeaponValueKind.Integer, nonNegative, suffix, argument, map, AmmoSection(group));

    private static WeaponFieldDefinition D(string key, string group, string label, string hint, string field, bool nonNegative, string? suffix = null) =>
        new(key, group, label, hint, WeaponFieldOwner.Ammo, field, WeaponValueKind.Decimal, nonNegative, suffix, Section: AmmoSection(group));

    private static WeaponFieldDefinition B(string key, string group, string label, string hint, string field) =>
        new(key, group, label, hint, WeaponFieldOwner.Ammo, field, WeaponValueKind.Boolean, Section: AmmoSection(group));

    private static WeaponFieldDefinition C(string key, string group, string label, string hint, string field, string argument) =>
        new(key, group, label, hint, WeaponFieldOwner.Ammo, field, WeaponValueKind.Choice, ArgumentName: argument, Section: AmmoSection(group));

    private static string AmmoSection(string group) => group switch
    {
        "库存" or "补给" => "库存与消耗",
        "射程" or "伤害" or "精度" or "散布" => "作战性能",
        "射击" or "弹道" => "射击与弹道",
        "行为" => "行为设置",
        _ => "其他"
    };
}
