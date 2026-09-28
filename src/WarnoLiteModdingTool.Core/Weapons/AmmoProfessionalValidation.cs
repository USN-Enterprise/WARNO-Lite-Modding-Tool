using System.Globalization;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public static class AmmoProfessionalValidation
{
    // Validate only changed contracts. Native unusual values unrelated to this
    // edit must not prevent an otherwise valid transaction.
    public static void Candidate(AmmoRecord original, string body, IReadOnlyDictionary<string, string> changes,
        Func<UnitProjectGraph> graph, UnitLocalisationCatalog localisation)
    {
        var touched = changes.Where(p => AmmoProfessional.RequiresV3(p.Key)).ToArray();
        if (touched.Length == 0) return;
        var doc = new NdfSyntaxDocument(body);
        var roots = doc.FindConstructors(original.Source.TypeName);
        if (roots.Count != 1) throw new TransactionValidationException("弹药候选根节点不唯一：" + original.Name);
        string Raw(string name)
        {
            var values = doc.FindDirectAssignments(roots[0], name);
            return values.Count == 1 ? doc.Raw(values[0]) : "";
        }
        foreach (var (key, raw) in touched)
        {
            var field = original.Field(key) ?? throw new TransactionValidationException("未知弹药参数：" + key);
            var def = field.Definition;
            var owner = AmmoProfessional.Owner(doc, roots[0], def);
            var spans = owner is null ? [] : doc.FindDirectAssignments(owner, def.FieldName);
            if (spans.Count != 1 || doc.Raw(spans[0]) != raw || !WeaponValueConverter.TryRead(def, raw, out _))
                throw new TransactionValidationException("弹药候选字段未唯一写入：" + original.Name + " / " + def.FieldName);
            if (def.FieldName == "MissileDescriptor" || field.IsMissing && def.FieldName is "IsFireAndForget" or "MaxAccelerationGRU" or "MissileTimeBetweenCorrections")
            {
                if (Raw("ProjectileType") != "EProjectileType/GuidedMissile") throw new TransactionValidationException("需要已有 GuidedMissile 弹道：" + original.Name);
                var g = graph(); var target = MissileTarget(g, original.Source.RelativeSourceFile, Raw("MissileDescriptor"));
                if (target is null || target.TypeName != "TEntityDescriptor") throw new TransactionValidationException("导弹实体引用无法唯一解析：" + original.Name);
                var missile = new NdfSyntaxDocument(g.Body(target));
                if (missile.FindConstructors("TGuidedMissileModuleDescriptor").Count != 1 || missile.FindConstructors("TGuidedMissileMovementModuleDescriptor").Count != 1)
                    throw new TransactionValidationException("导弹实体缺少唯一制导或运动模块：" + target.Name);
            }
            if (def.FieldName is "FireTriggeringProbability" or "IgnoreInflammabilityConditions" && (raw == "True" || double.TryParse(raw, CultureInfo.InvariantCulture, out var p) && p > 0))
            {
                if (graph().Resolve(original.Source.RelativeSourceFile, Raw("FireDescriptor")) is null)
                    throw new TransactionValidationException("起火描述符引用无法唯一解析：" + original.Name);
            }
            if (def.FieldName == "NbSalvosShootOnPosition" && int.Parse(raw, CultureInfo.InvariantCulture) > 0 && Raw("CanShootOnPosition") != "True")
                throw new TransactionValidationException("设置点地齐射次数时，最终 CanShootOnPosition 必须为 True：" + original.Name);
            if (def.FieldName == "InterfaceWeaponTexture")
            {
                var matches = graph().FindObjects(NdfSyntaxDocument.Unquote(raw));
                if (matches.Count != 1 || matches[0].TypeName != "TUIResourceTexture_Common")
                    throw new TransactionValidationException("武器图片声明缺失或重复：" + raw);
                var tex = new NdfSyntaxDocument(graph().Body(matches[0]));
                if (tex.FindAssignmentsAnywhere("FileName").Count != 1) throw new TransactionValidationException("图片来源无法唯一解析：" + raw);
            }
            if (def.FieldName == "ImpactHappening" && ImpactKeys(graph()).Count(v => v == NdfSyntaxDocument.Unquote(raw)) != 1)
                throw new TransactionValidationException("命中表现注册键缺失或重复：" + raw);
            if (def.FieldName == "MinMaxCategory" && ScalarKeys(graph()).Count(v => v == raw) != 1)
                throw new TransactionValidationException("射程类别声明缺失或重复：" + raw);
            if (def.FieldName == "WeaponDescriptionToken")
            {
                var token = NdfSyntaxDocument.Unquote(raw);
                if (localisation.IsTokenAmbiguous(token) || !localisation.TryResolve(token, out _) && VanillaNames.Lookup("UNITS", token) is null)
                    throw new TransactionValidationException("说明 token 无法解析，请检查当前 Mod 字典或加载原版名称：" + token);
            }
        }
    }

    public static NdfObjectInfo? MissileTarget(UnitProjectGraph graph, string source, string raw)
    {
        if (graph.Resolve(source, raw) is { } bound) return bound;
        // Generated Ammo and MissileDescriptors are sibling files in the weapon
        // compilation scope. Stock missiles have only ~/ uses, so the general
        // graph has no exported namespace prefix to infer. Accept this precise
        // local reference shape only; qualified references never fall back by leaf.
        var leaf = NdfSyntaxDocument.Leaf(raw);
        if (raw != "~/" + leaf) return null;
        var matches = graph.FindObjects(leaf);
        if (matches.Count != 1 || !graph.IsExported(matches[0])) return null;
        var parent = System.IO.Path.GetDirectoryName(UnitProjectGraph.Normalize(source));
        return string.Equals(parent, System.IO.Path.GetDirectoryName(UnitProjectGraph.Normalize(matches[0].RelativeSourceFile)), StringComparison.OrdinalIgnoreCase) ? matches[0] : null;
    }

    public static IEnumerable<string> ImpactKeys(UnitProjectGraph graph) => graph.Files.Values.SelectMany(f => ImpactKeys(f.Syntax));
    public static IEnumerable<string> ImpactKeys(NdfSyntaxDocument doc)
    {
        foreach (var registration in doc.FindConstructors("TMimeticWorldHappeningRegistration"))
        foreach (var map in doc.FindDirectAssignments(registration, "Happenings"))
        foreach (var entry in doc.ReadMapEntries(map))
            if (doc.FindConstructors("TImpactHappening", entry.Value).Count == 1) yield return NdfSyntaxDocument.Unquote(doc.Raw(entry.Key));
    }
    public static IEnumerable<string> ScalarKeys(UnitProjectGraph graph) => graph.Files.Values.SelectMany(f => ScalarKeys(f.Syntax));
    public static IEnumerable<string> ScalarKeys(NdfSyntaxDocument doc)
    {
        for (var i = 0; i + 2 < doc.Tokens.Count; i++)
            if (doc.Tokens[i + 1].Text == "is" && int.TryParse(doc.Tokens[i + 2].Text, out _)) yield return doc.Tokens[i].Text;
    }
}
