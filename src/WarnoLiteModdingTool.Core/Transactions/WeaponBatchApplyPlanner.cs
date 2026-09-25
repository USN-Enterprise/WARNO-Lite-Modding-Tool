using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Transactions;

public static class WeaponBatchApplyPlanner
{
    private static bool Ammo(WeaponBatchCell c) => c.Key.StartsWith("ammo.", StringComparison.Ordinal);
    public static IReadOnlyList<WeaponBatchCell> ValidateCombination(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> operations)
    {
        var cells = WeaponBatch.Cells(data, operations);
        foreach (var c in cells)
        {
            if (c.Unit.Length > 0 && operations.Any(o => o.TargetKind == DraftTargetKind.UnitDelete && o.ObjectName == c.Unit))
                throw new TransactionValidationException("待删除单位不能应用武器批量草稿：" + c.Unit);
            if (c.Unit.Length == 0) continue;
            var shared = cells.FirstOrDefault(g => g.Unit.Length == 0 && g.Key == c.Key && (Ammo(c) ? g.Ammo == c.Ammo : g.Weapon == c.Weapon));
            if (shared is not null && shared.Raw != c.Raw) throw new TransactionValidationException("局部与全部引用草稿对同一字段有不同目标：" + c.Unit + " / " + c.Key);
            if (Ammo(c) && WeaponBatch.AmmoAt(data, cells, c.Unit, c.Weapon, c.Mount) != c.Ammo)
                throw new TransactionValidationException("弹药引用已被草稿替换，请移除旧弹药参数批次后按最终弹药重新预览：" + c.Unit + " / #" + c.Mount);
        }
        // Validate the complete final ammunition state, including inherited shared edits.
        foreach (var group in cells.Where(Ammo).GroupBy(c => (c.Unit, c.Weapon, c.Mount, c.Ammo)))
        {
            var record = data.Ammo(group.Key.Ammo)!;
            var final = record.Fields.ToDictionary(f => f.Key, f => f.DisplayValue);
            foreach (var c in cells.Where(c => Ammo(c) && c.Unit.Length == 0 && c.Ammo == record.Name).Concat(group)) final[c.Key] = c.Value;
            foreach (var prefix in new[] { "ammo.range.ground", "ammo.range.heli", "ammo.range.air", "ammo.range.projectile" })
                if (double.TryParse(final.GetValueOrDefault(prefix+".min"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var min) &&
                    double.TryParse(final.GetValueOrDefault(prefix+".max"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var max) && min > max)
                    throw new TransactionValidationException("最小值大于最大值：" + record.Name + " / " + prefix);
        }
        return cells;
    }
    public static WeaponApplyPlan Plan(UnitWorkspaceData units, WeaponWorkspaceData data, IReadOnlyList<DraftOperation> operations, Func<string,TextFileSnapshot> snapshot)
    {
        var cells = ValidateCombination(data, operations);
        var direct = new List<WeaponPlannedReplacement>(); var newNames = new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        var blocks = new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        var names = data.Weapons.Select(w => w.Name).Concat(data.Ammunition.Select(a => a.Name)).ToHashSet();
        var ammoCount = 0; var weaponCount = 0;
        Dictionary<string,WeaponBatchCell> Globals(string name, bool ammo) => cells.Where(c => c.Unit.Length == 0 && Ammo(c) == ammo && (ammo ? c.Ammo : c.Weapon) == name).DistinctBy(c => c.Key).ToDictionary(c => c.Key);
        string Signature(IEnumerable<KeyValuePair<string,string>> values) => string.Join("\n", values.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+p.Value));
        void Add(WeaponFieldValue field, string raw)
        {
            if (field.RawValue == raw) return;
            direct.Add(new(field.Location.RelativeSourceFile,new(field.Location.CharacterOffset,field.Location.CharacterLength,field.RawValue,raw,field.Definition.Label),field.Definition.Label));
        }
        string Clone(NdfObjectInfo source, IEnumerable<WeaponFieldValue> fields, Dictionary<string,string> values, bool ammo)
        {
            string name; do name = source.Name+"_WLMT_"+Guid.NewGuid().ToString("N")[..10]; while (!names.Add(name));
            var s = snapshot(source.RelativeSourceFile); var body = s.Text.Substring(source.CharacterOffset,source.CharacterLength);
            var declaration = Regex.Match(body,$@"^(\s*(?:export\s+)?){Regex.Escape(source.Name)}\s+is\s+");
            if (!declaration.Success) throw new TransactionValidationException("无法定位武器声明："+source.Name);
            var edits = new List<TextReplacement> { new(declaration.Groups[1].Length,source.Name.Length,source.Name,name,"隔离对象") };
            foreach (var f in fields.Where(f => values.TryGetValue(f.Key,out var v) && v != f.RawValue))
                edits.Add(new(f.Location.CharacterOffset-source.CharacterOffset,f.Location.CharacterLength,f.RawValue,values[f.Key],f.Definition.Label));
            if (ammo)
            {
                var doc = new NdfSyntaxDocument(body);
                // The ammo descriptor GUID must be unique, not a guessed replacement in an unrelated field.
                var idFields = doc.FindDirectAssignments(doc.FindConstructors(source.TypeName).Single(), "DescriptorId");
                var idField = idFields.Count == 1 ? idFields[0] : null;
                if (idField is null) throw new TransactionValidationException("弹药缺少 DescriptorId："+source.Name);
                var idRaw = doc.Raw(idField);
                if (!Regex.IsMatch(idRaw,@"^GUID:\{[0-9A-Fa-f-]{36}\}$")) throw new TransactionValidationException("弹药 DescriptorId 无法隔离："+source.Name);
                edits.Add(new(doc.StartOffset(idField),doc.Length(idField),idRaw,$"GUID:{{{Guid.NewGuid()}}}","新 DescriptorId"));
                ammoCount++;
            }
            else weaponCount++;
            var path = source.RelativeSourceFile.Replace('\\','/');
            if (!blocks.ContainsKey(path)) { blocks[path] = []; newNames[path] = []; }
            blocks[path].Add(SemicolonCsvDocument.ApplyReplacements(body,edits).TrimEnd('\r','\n')); newNames[path].Add(name);
            return name;
        }
        void ValidateAmmo(AmmoRecord record, Dictionary<string,string> values)
        {
            var familyRaw = values.GetValueOrDefault("ammo.damage.family") ?? record.Field("ammo.damage.family")?.RawValue;
            var indexRaw = values.GetValueOrDefault("ammo.damage.index") ?? record.Field("ammo.damage.index")?.RawValue;
            if (units.DamageResistance.DamageFamilies.FirstOrDefault(f => f.Name == NdfSyntaxDocument.Leaf(familyRaw ?? "")) is {} family &&
                (!int.TryParse(indexRaw,out var i) || i < family.MinimumIndex || i > family.MaximumIndex))
                throw new TransactionValidationException("伤害索引超出伤害族范围："+record.Name);
        }
        foreach (var a in data.Ammunition)
        {
            var global = Globals(a.Name,true); if (global.Count == 0) continue;
            ValidateAmmo(a,global.ToDictionary(p => p.Key,p => p.Value.Raw));
            foreach (var c in global.Values) Add(a.Field(c.Key)!,c.Raw);
        }
        foreach (var w in data.Weapons)
            foreach (var c in Globals(w.Name,false).Values) Add(WeaponBatch.Field(data,c)!,c.Raw);
        var ammoVariants = new Dictionary<string,string>();
        var weaponVariants = new Dictionary<string,string>();
        foreach (var group in cells.Where(c => c.Unit.Length > 0).GroupBy(c => (c.Unit,c.Weapon)))
        {
            var w = data.Weapon(group.Key.Weapon)!;
            var values = Globals(w.Name,false).ToDictionary(p => p.Key,p => p.Value.Raw);
            foreach (var c in group.Where(c => !Ammo(c))) values[c.Key] = c.Raw;
            foreach (var mount in group.Where(Ammo).GroupBy(c => (c.Mount,c.Ammo)))
            {
                var a = data.Ammo(mount.Key.Ammo)!;
                var edits = Globals(a.Name,true).ToDictionary(p => p.Key,p => p.Value.Raw);
                foreach (var c in mount) edits[c.Key] = c.Raw;
                ValidateAmmo(a,edits);
                var key = a.Name+"|"+Signature(edits);
                if (!ammoVariants.TryGetValue(key,out var name)) ammoVariants[key] = name = Clone(a.Source,a.Fields,edits,true);
                var field = w.Mounts.Single(m => m.Index == mount.Key.Mount).Fields.Single(f => f.Definition.FieldName == "Ammunition");
                values[field.Key] = ReplaceLeaf(field.RawValue,name);
            }
            var variant = w.Name+"|"+Signature(values);
            if (!weaponVariants.TryGetValue(variant,out var target)) weaponVariants[variant] = target = Clone(w.Source,w.Fields.Concat(w.Mounts.SelectMany(m => m.Fields)),values,false);
            var unit = units.Units.Single(u => u.Name == group.Key.Unit);
            var source = snapshot(unit.Source.RelativeSourceFile);
            var doc = new NdfSyntaxDocument(source.Text,unit.Source.CharacterOffset,unit.Source.CharacterLength);
            var refs = doc.FindReferences("WeaponDescriptor_").Where(r => r.Leaf == w.Name).ToArray();
            if (refs.Length == 0) throw new TransactionValidationException("单位武器引用已变化："+unit.Name);
            foreach (var r in refs) direct.Add(new(unit.Source.RelativeSourceFile,new(doc.StartOffset(r.Span),doc.Length(r.Span),r.Raw,ReplaceLeaf(r.Raw,target),"批量武器引用"),unit.Name+" → "+target));
        }
        foreach (var b in blocks)
        {
            var s = snapshot(b.Key); var nl = s.NewLine;
            direct.Add(new(b.Key,new(s.Text.Length,0,"",nl+string.Join(nl+nl,b.Value)+nl,"批量隔离对象"),"追加批量隔离对象"));
        }
        return new(direct,newNames.ToDictionary(p => p.Key,p => (IReadOnlyList<string>)p.Value),
            [$"批量武器：{weaponCount} 个 Weapon 副本、{ammoCount} 个 Ammo 副本", "按最终单位、挂载与字段组合隔离；同结果复用副本"]);
    }
    private static string ReplaceLeaf(string raw,string leaf) => raw[..(raw.LastIndexOf('/')+1)]+leaf;
}
