using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Weapons;

/// <summary>Typed professional Ammo contracts and source-preserving optional members.</summary>
public static class AmmoProfessional
{
    public static string Key(string field) => "ammo.pro." + field;
    private static WeaponFieldDefinition F(string field, string label, WeaponValueKind kind, string group,
        bool nonNegative = false, string? unit = null, string? constructor = null) =>
        new(Key(field), group, label, "仅修改所选原参数；未声明不等于关闭。", WeaponFieldOwner.Ammo, field, kind,
            nonNegative, unit, Section: group, Professional: true, CanInsert: true, Constructor: constructor);
    public static IReadOnlyList<WeaponFieldDefinition> Definitions { get; } =
    [
        F("AllowSuppressDamageWhenNoImpact", "未命中时允许压制", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("ComputeArmorFromImpactLocation", "按命中位置计算装甲", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("DisplaySalveAccuracy", "显示齐射精度", WeaponValueKind.Boolean, "表现与关联"),
        F("ForceHitTopArmorOnSuccess", "成功命中时使用顶部装甲", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("HasDeploymentTime", "使用部署时间", WeaponValueKind.Boolean, "高级射击与弹道"),
        F("IsHarmlessForAllies", "不伤害友军", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("PiercingWeapon", "穿甲武器标记", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("ReflexShotDisabledIfPriorityTarget", "优先目标存在时禁用反应射击", WeaponValueKind.Boolean, "高级射击与弹道"),
        F("SalvoShotsSorted", "齐射顺序标记", WeaponValueKind.Boolean, "高级射击与弹道"),
        F("ShowDamageInUI", "显示伤害", WeaponValueKind.Boolean, "表现与关联"),
        F("TandemCharge", "串联战斗部标记", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("TargetOnlyOneUnitInDistrict", "区域内仅针对单个单位", WeaponValueKind.Boolean, "高级射击与弹道"),
        F("AffecteParNombre", "AffecteParNombre", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("CorrectedShotDispersionMultiplier", "校射散布倍率", WeaponValueKind.Decimal, "高级射击与弹道", true),
        F("FireTriggeringProbability", "起火概率", WeaponValueKind.Decimal, "高级伤害与命中", true),
        F("ForceHitTopArmor", "强制使用顶部装甲", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("Guidance", "制导类别", WeaponValueKind.CatalogChoice, "高级射击与弹道"),
        F("DistanceToTarget", "命中规则距离修正", WeaponValueKind.Boolean, "高级伤害与命中", constructor: "TDiceHitRollRuleDescriptor"),
        F("IgnoreInflammabilityConditions", "忽略易燃条件", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("ImpactHappening", "命中表现", WeaponValueKind.CatalogChoice, "表现与关联"),
        F("InterfaceWeaponTexture", "武器面板图片", WeaponValueKind.CatalogChoice, "表现与关联"),
        F("IsSubAmmunition", "子弹药标记", WeaponValueKind.Boolean, "高级伤害与命中"),
        F("MaxSuccessiveHitCount", "连续命中次数", WeaponValueKind.Integer, "高级伤害与命中", true),
        F("MinMaxCategory", "射程显示类别", WeaponValueKind.CatalogChoice, "表现与关联"),
        F("MissileDescriptor", "导弹实体", WeaponValueKind.CatalogChoice, "表现与关联"),
        F("MissileTimeBetweenCorrections", "导弹修正间隔（原值）", WeaponValueKind.Decimal, "高级射击与弹道", true),
        F("NbSalvosShootOnPosition", "点地射击齐射次数", WeaponValueKind.Integer, "高级射击与弹道", true),
        F("NoiseDissimulationMalus", "开火隐蔽惩罚（原值）", WeaponValueKind.Decimal, "高级射击与弹道"),
        F("PitchForParabolic", "抛物弹道俯仰角", WeaponValueKind.Degrees, "高级射击与弹道", unit: " °"),
        F("WeaponDescriptionToken", "武器说明条目", WeaponValueKind.CatalogChoice, "表现与关联"),
        F("TraitsToken", "弹药说明标签", WeaponValueKind.Tags, "表现与关联") with { CanInsert = false,
            Hint = "射后不理修改后请核对F&F、manual、semiAuto标签；保留其他标签，不推断关闭后的制导方式。" }
    ];

    public static bool RequiresV3(string key) => WeaponFieldDefinitions.Ammo.Any(f => f.Key == key && (f.Professional || f.CanInsert));
    public static bool Visible(WeaponFieldValue field, bool professional) => professional || !field.Definition.Professional && field.State == WeaponFieldState.Declared;

    public static NdfConstructorSpan? Owner(NdfSyntaxDocument doc, NdfConstructorSpan root, WeaponFieldDefinition def)
    {
        if (def.Constructor is null) return root;
        var hit = doc.FindDirectAssignments(root, "HitRollRuleDescriptor");
        if (hit.Count != 1) return null;
        var owners = doc.FindConstructors(def.Constructor, hit[0]);
        return owners.Count == 1 ? owners[0] : null;
    }

    public static WeaponFieldValue Unavailable(NdfObjectInfo obj, WeaponFieldDefinition def, string raw, string reason) =>
        new(def, obj.Name, obj.TypeName, "", raw, new(obj.RelativeSourceFile, Path(def), -1, 0, obj.LineNumber), [], WeaponFieldState.Unavailable, reason);
    public static string Path(WeaponFieldDefinition def) => "TAmmunitionDescriptor." + (def.Constructor is null ? "" : "HitRollRuleDescriptor.") + def.FieldName;

    public static WeaponFieldValue Missing(NdfObjectInfo obj, string source, NdfSyntaxDocument doc, NdfConstructorSpan root, WeaponFieldDefinition def)
    {
        var owner = Owner(doc, root, def);
        if (owner is null) return Unavailable(obj, def, "", "缺少唯一命中规则，不能补建子字段");
        if (!def.CanInsert) return Unavailable(obj, def, "", "此字段尚不支持补建");
        string Raw(string name) { var values = doc.FindDirectAssignments(root, name); return values.Count == 1 ? doc.Raw(values[0]) : ""; }
        var projectile = Raw("ProjectileType");
        var missile = projectile == "EProjectileType/GuidedMissile";
        var fired = projectile is "EProjectileType/Obus" or "EProjectileType/Bombe" or "EProjectileType/Grenade" or "EProjectileType/Artillerie" || missile;
        if (!fired) return Unavailable(obj, def, "", "当前弹道类型不支持补建此参数");
        if (def.FieldName is "IsFireAndForget" or "MaxAccelerationGRU" or "MissileDescriptor" or "MissileTimeBetweenCorrections" or "ReflexShotDisabledIfPriorityTarget" && !missile)
            return Unavailable(obj, def, "", "仅适用于已有导弹结构");
        if (def.FieldName is "CorrectedShotDispersionMultiplier" or "PitchForParabolic" && projectile is not ("EProjectileType/Artillerie" or "EProjectileType/Obus"))
            return Unavailable(obj, def, "", "当前弹道类型不支持此抛物或校射参数");
        if (def.FieldName is "FireTriggeringProbability" or "IgnoreInflammabilityConditions" && Raw("FireDescriptor") is "" or "nil")
            return Unavailable(obj, def, "", "需要已有起火描述符引用");
        // The stock cluster bombs declare this on the fired Ammo itself, with no
        // separate parent/child member. Only expose that observed projectile shape.
        if (def.FieldName == "IsSubAmmunition" && projectile is not ("EProjectileType/Bombe" or "EProjectileType/Artillerie"))
            return Unavailable(obj, def, "", "仅支持炸弹或炮兵弹药的原标记；不创建子弹药系统");
        var close = doc.Tokens[owner.CloseTokenIndex].Start;
        var line = source.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
        var closingIndent = source[line..close];
        var nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var fields = doc.EnumerateDirectAssignments(owner);
        var first = fields.FirstOrDefault();
        var indent = closingIndent.All(c => c is ' ' or '\t') ? closingIndent + "    " : "    ";
        if (first is not null)
        {
            var pos = doc.StartOffset(first.Value);
            var start = source.LastIndexOf('\n', Math.Max(0, pos - 1)) + 1;
            var m = Regex.Match(source[start..pos], @"^[ \t]+");
            if (m.Success) indent = m.Value;
        }
        var at = closingIndent.All(c => c is ' ' or '\t') ? line : close;
        var prefix = (at == close && at != line ? nl : "") + indent + def.FieldName + " = ";
        return new(def, obj.Name, obj.TypeName, "", "", new(obj.RelativeSourceFile, Path(def), at, 0, obj.LineNumber + source.AsSpan(obj.CharacterOffset, at - obj.CharacterOffset).Count('\n')), [], WeaponFieldState.Missing, "未显式设置；选择值后新增", prefix, nl);
    }

    public static TextReplacement Replacement(WeaponFieldValue field, string raw, int offset = 0)
    {
        if (!field.CanEdit) throw new TransactionValidationException(field.Reason);
        return new(field.Location.CharacterOffset - offset, field.Location.CharacterLength, field.RawValue,
            field.IsMissing ? field.InsertPrefix + raw + field.InsertSuffix : raw, field.Definition.Label);
    }
    public static IReadOnlyList<TextReplacement> MergeInsertions(IEnumerable<TextReplacement> edits) => edits
        .GroupBy(e => (e.Offset, e.Length)).Select(g => g.Key.Length == 0
            ? new TextReplacement(g.Key.Offset, 0, "", string.Concat(g.OrderBy(e => e.Description, StringComparer.Ordinal).Select(e => e.Target)), string.Join("、", g.Select(e => e.Description)))
            : g.Count() == 1 ? g.Single() : throw new TransactionValidationException("重复字段补丁")).ToArray();

    public static string[] Tags(string raw)
    {
        var doc = new NdfSyntaxDocument("T is T(" + "V=" + raw + ")");
        var field = doc.FindDirectAssignments(doc.FindConstructors("T").Single(), "V").Single();
        if (doc.Tokens[field.StartTokenIndex].Text != "[" || doc.Tokens[field.EndTokenIndex].Text != "]") throw new FormatException("标签必须是列表");
        var values = doc.ReadArrayElements(field).Select(doc.Raw).ToArray();
        if (values.Any(v => v.Length < 2 || v[0] is not ('\'' or '"') || v[^1] != v[0])) throw new FormatException("标签必须是字符串");
        return values.Select(NdfSyntaxDocument.Unquote).ToArray();
    }
    public static string FormatTags(IEnumerable<string> values) => "[ " + string.Join(", ", values.Select(v => "'" + v.Replace("\\", "\\\\").Replace("'", "\\'") + "'")) + " ]";
}
