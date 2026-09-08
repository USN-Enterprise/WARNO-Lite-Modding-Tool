using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Units;

public static class UnitFieldDefinitions
{
    public static IReadOnlyList<UnitFieldDefinition> All { get; } =
    [
        MapInteger("economy.commandPoints", "费用与部署", "费用", "指挥点", "部署价格", "TProductionModuleDescriptor", "ProductionRessourcesNeeded", "Resource_CommandPoints"),
        MapInteger("economy.tickets", "费用与部署", "费用", "将军模式价格", "单位在将军模式中的价格", "TProductionModuleDescriptor", "ProductionRessourcesNeeded", "Resource_Tickets"),

        ScalarInteger("survival.health", "生存与防护", "耐久", "生命值", "最大物理伤害承受", "TBaseDamageModuleDescriptor", "MaxPhysicalDamages"),
        ScalarInteger("survival.suppression", "生存与防护", "耐久", "压制承受", "最大压制伤害承受", "TBaseDamageModuleDescriptor", "MaxSuppressionDamages"),
        ScalarInteger("survival.stun", "生存与防护", "耐久", "眩晕承受", "最大眩晕伤害承受", "TBaseDamageModuleDescriptor", "MaxStunDamages"),

        ArmorFamily("armor.front.family", "正面护甲类型", "ResistanceFront"),
        Armor("armor.front", "正面装甲", "ResistanceFront"),
        ArmorFamily("armor.side.family", "侧面护甲类型", "ResistanceSides"),
        Armor("armor.side", "侧面装甲", "ResistanceSides"),
        ArmorFamily("armor.rear.family", "后方护甲类型", "ResistanceRear"),
        Armor("armor.rear", "后方装甲", "ResistanceRear"),
        ArmorFamily("armor.top.family", "顶部护甲类型", "ResistanceTop"),
        Armor("armor.top", "顶部装甲", "ResistanceTop"),
        new UnitFieldDefinition(
            "armor.ecm",
            "防护",
            "ECM",
            "界面显示正百分比；保存时自动换回代码层负值",
            new NdfFieldSelector("TDamageModuleDescriptor", "HitRollECM"),
            UnitValueKind.EcmPercent,
            UnitEditorKind.Text,
            true,
            "%",
            "界面百分比 = -HitRollECM × 100；只接受 0 到 100%",
            "生存与防护"),

        ScalarDecimal("movement.maxSpeed", "机动与续航", "速度与操纵", "最大速度", "通用移动模块速度", "TGenericMovementModuleDescriptor", "MaxSpeedInKmph", "km/h"),
        ScalarDecimal("movement.roadSpeed", "机动与续航", "速度与操纵", "最大公路速度", "单位在公路上的最高速度", "TUnitUIModuleDescriptor", "DisplayRoadSpeedInKmph", "km/h"),
        ScalarDecimal("movement.acceleration", "机动与续航", "速度与操纵", "加速度", "陆地移动模块加速度系数", "TLandMovementModuleDescriptor", "MaxAccelerationGRU", "GRU"),
        ScalarDecimal("movement.deceleration", "机动与续航", "速度与操纵", "减速度", "陆地移动模块减速度系数", "TLandMovementModuleDescriptor", "MaxDecelerationGRU", "GRU"),
        ScalarDecimal("movement.turnTime", "机动与续航", "速度与操纵", "转向时间", "陆地移动模块转向时间系数", "TLandMovementModuleDescriptor", "TempsDemiTour", "秒"),

        ScalarDecimal("fuel.capacity", "机动与续航", "燃油", "燃油容量", "显示燃油量", "TFuelModuleDescriptor", "FuelCapacity", null),
        ScalarDecimal("fuel.duration", "机动与续航", "燃油", "可持续移动时间", "实际可运行时间", "TFuelModuleDescriptor", "FuelMoveDuration", "秒"),

        ScalarDecimal("recon.concealment", "侦察与感知", "隐蔽", "隐蔽修正", "单位可见性修正", "TVisibilityModuleDescriptor", "UnitConcealmentBonus", null, false),
        MapDecimal("recon.vision.standard", "侦察与感知", "视野上限", "标准视野上限", "视野扫描距离上限（GRU）；基础模式按原始比例联动低空和高空", "TScannerConfigurationDescriptor", "VisionRangesGRU", "EVisionRange/Standard", "GRU"),
        MapDecimal("recon.vision.low", "侦察与感知", "视野上限", "低空视野上限", "视野扫描距离上限（GRU）；基础模式按原始比例联动低空和高空", "TScannerConfigurationDescriptor", "VisionRangesGRU", "EVisionRange/LowAltitude", "GRU"),
        MapDecimal("recon.vision.high", "侦察与感知", "视野上限", "高空视野上限", "视野扫描距离上限（GRU）；基础模式按原始比例联动低空和高空", "TScannerConfigurationDescriptor", "VisionRangesGRU", "EVisionRange/HighAltitude", "GRU"),
        MapDecimal("recon.optics.standard", "侦察与感知", "观察强度", "标准观察强度", "侦察显示评级门槛参考；实际探测还受隐蔽和视野上限影响", "TScannerConfigurationDescriptor", "OpticalStrengths", "EOpticalStrength/Standard", null),
        MapDecimal("recon.optics.low", "侦察与感知", "观察强度", "低空观察强度", "低空观察强度", "TScannerConfigurationDescriptor", "OpticalStrengths", "EOpticalStrength/LowAltitude", null),
        MapDecimal("recon.optics.high", "侦察与感知", "观察强度", "高空观察强度", "高空观察强度", "TScannerConfigurationDescriptor", "OpticalStrengths", "EOpticalStrength/HighAltitude", null),
        new UnitFieldDefinition(
            "deployment.shift", "部署", "前置部署", "部署偏移距离",
            new NdfFieldSelector("TDeploymentShiftModuleDescriptor", "DeploymentShiftGRU"),
            UnitValueKind.Decimal, UnitEditorKind.Text, true, "GRU", Section: "费用与部署", CanInsertWhenMissing: true),

        ScalarInteger("strategic.attack", "战略数值", "自动结算", "战略攻击值", "将军模式战略数值，用于自动结算", "TStrategicDataModuleDescriptor", "UnitAttackValue"),
        ScalarInteger("strategic.defense", "战略数值", "自动结算", "战略防御值", "将军模式战略数值，用于自动结算", "TStrategicDataModuleDescriptor", "UnitDefenseValue"),
        ScalarInteger("strategic.xpBonus", "战略数值", "自动结算", "经验加成值", "将军模式战略数值，用于自动结算", "TStrategicDataModuleDescriptor", "UnitBonusXpPerLevelValue"),

        Choice("structure.coalition", "基本信息", "身份", "阵营", "不会自动替换模型、语言或卡组", "TTypeUnitModuleDescriptor", "Coalition"),
        new UnitFieldDefinition(
            "structure.country",
            "身份",
            "国家",
            "不会自动替换模型、贴图、语言或卡组",
            new NdfFieldSelector("TTypeUnitModuleDescriptor", "MotherCountry"),
            UnitValueKind.QuotedString,
            UnitEditorKind.Choice,
            Section: "基本信息"),
        new UnitFieldDefinition(
            "structure.category",
            "分类",
            "单位类别",
            "AcknowUnitTypes；选项来自当前项目",
            new NdfFieldSelector("TTypeUnitModuleDescriptor", "AcknowUnitTypes"),
            UnitValueKind.PathList,
            UnitEditorKind.Choice,
            Section: "基本信息"),
        Choice("structure.factory", "基本信息", "分类", "生产栏位", "FactoryType；选项来自当前项目", "TProductionModuleDescriptor", "FactoryType"),
        new UnitFieldDefinition(
            "structure.role",
            "分类",
            "角色",
            "UnitRole；选项来自当前项目",
            new NdfFieldSelector("TUnitUIModuleDescriptor", "UnitRole"),
            UnitValueKind.QuotedString,
            UnitEditorKind.Choice,
            Section: "基本信息"),
        new UnitFieldDefinition("structure.specialties", "关系与标签", "单位特性",
            "只修改 SpecialtiesList；不会自动修改武器、烟幕、运输或 TagSet",
            new NdfFieldSelector("TUnitUIModuleDescriptor", "SpecialtiesList"),
            UnitValueKind.StringList, UnitEditorKind.Text, Section: "基本信息", CanInsertWhenMissing: true),
        new UnitFieldDefinition(
            "structure.tags",
            "关系与标签",
            "标签",
            "每个标签独立选择；只修改本 Unit 的 TagSet 草稿",
            new NdfFieldSelector("TTagsModuleDescriptor", "TagSet"),
            UnitValueKind.StringList,
            UnitEditorKind.Text,
            Section: "基本信息"),
        new UnitFieldDefinition(
            "structure.upgradeFrom",
            "关系与标签",
            "升级来源",
            "只能选择当前项目中的其他 Unit",
            new NdfFieldSelector("TUnitUIModuleDescriptor", "UpgradeFromUnit"),
            UnitValueKind.UnitReference,
            UnitEditorKind.Choice,
            Section: "基本信息")
    ];

    private static UnitFieldDefinition MapInteger(
        string key, string section, string group, string label, string hint,
        string module, string field, string mapKey) =>
        new(key, group, label, hint, new NdfFieldSelector(module, field, mapKey), UnitValueKind.Integer, UnitEditorKind.Text, true, Section: section);

    private static UnitFieldDefinition MapDecimal(
        string key, string section, string group, string label, string hint,
        string module, string field, string mapKey, string? suffix) =>
        new(key, group, label, hint, new NdfFieldSelector(module, field, mapKey), UnitValueKind.Decimal, UnitEditorKind.Text, true, suffix, Section: section);

    private static UnitFieldDefinition ScalarInteger(
        string key, string section, string group, string label, string hint,
        string module, string field) =>
        new(key, group, label, hint, new NdfFieldSelector(module, field), UnitValueKind.Integer, UnitEditorKind.Text, true, Section: section);

    private static UnitFieldDefinition ScalarDecimal(
        string key, string section, string group, string label, string hint,
        string module, string field, string? suffix, bool nonNegative = true) =>
        new(key, group, label, hint, new NdfFieldSelector(module, field), UnitValueKind.Decimal, UnitEditorKind.Text, nonNegative, suffix, Section: section);

    private static UnitFieldDefinition Choice(
        string key, string section, string group, string label, string hint,
        string module, string field) =>
        new(key, group, label, hint, new NdfFieldSelector(module, field), UnitValueKind.Choice, UnitEditorKind.Choice, Section: section);

    private static UnitFieldDefinition Armor(string key, string label, string nestedField) =>
        new(
            key,
            "防护",
            label,
            "装甲 ResistanceFamily 索引",
            new NdfFieldSelector(
                "TDamageModuleDescriptor",
                "BlindageProperties",
                NestedType: "TBlindageProperties",
                NestedField: nestedField,
                ArgumentName: "Index"),
            UnitValueKind.Integer,
            UnitEditorKind.Text,
            true,
            null,
            "显示 TResistanceTypeRTTI 的 Index；不改 Family",
            "生存与防护");

    private static UnitFieldDefinition ArmorFamily(string key, string label, string nestedField) =>
        new(
            key,
            "护甲类型",
            label,
            "ResistanceFamily；步兵护甲类型的 Index 固定为 1",
            new NdfFieldSelector(
                "TDamageModuleDescriptor",
                "BlindageProperties",
                NestedType: "TBlindageProperties",
                NestedField: nestedField,
                ArgumentName: "Family"),
            UnitValueKind.Choice,
            UnitEditorKind.Choice,
            false,
            null,
            "选项来自当前 Mod 的 DamageResistance.ndf",
            "生存与防护");
}
