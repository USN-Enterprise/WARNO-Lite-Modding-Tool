using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Weapons;

public static partial class WeaponSafety
{
    // This caller is restricted to shared speed/acceleration fields plus debug
    // repairs. Mounts, salvo/supply/deployment fields and unit modules cannot change.
    // Validate the final touched numeric contracts; missile references and identity
    // are separately checked by the ordinary movement planner and final analysis.
    internal static Dictionary<string, byte[]> ValidateSharedMotion(string root, WeaponWorkspaceData data,
        IReadOnlyList<DraftOperation> operations, IReadOnlyList<Transactions.PlannedFileChange> files, ICollection<string> messages)
    {
        foreach (var group in operations.Where(o => o.TargetKind == DraftTargetKind.AmmoField).GroupBy(o => o.ObjectName))
        {
            var ammo = data.Ammo(group.Key)!;
            var file = files.Single(f => f.Kind == Transactions.FormalTextFileKind.Ndf && f.RelativePath.Equals(ammo.Source.RelativeSourceFile.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            var text = System.Text.Encoding.UTF8.GetString(file.CandidateBytes);
            var found = new NdfTopLevelScanner().Scan(text, Path.Combine(root, file.RelativePath), "ammo", root).Objects.Where(o => o.Name == ammo.Name).ToArray();
            if (found.Length != 1) throw new Transactions.TransactionValidationException("弹药候选不唯一 / Ambiguous final ammunition: " + ammo.Name);
            var doc = new NdfSyntaxDocument(text, found[0].CharacterOffset, found[0].CharacterLength);
            var owner = doc.FindConstructors("TAmmunitionDescriptor").Single(); var values = new Dictionary<string, string>();
            foreach (var op in group)
            {
                var field = ammo.Field(op.FieldKey)!; var spans = doc.FindDirectAssignments(owner, field.Definition.FieldName);
                if (spans.Count != 1 || doc.Raw(spans[0]) != op.TargetRaw) throw new Transactions.TransactionValidationException("弹药最终运动字段不同 / Final ammunition motion differs: " + ammo.Name);
                values[field.Definition.FieldName] = doc.Raw(spans[0]);
            }
            var issues = InspectAmmo(file.RelativePath, ammo.Name, values);
            if (issues.Any(i => i.Level is WeaponSafetyLevel.Error or WeaponSafetyLevel.Unverified))
                throw new Transactions.TransactionValidationException(string.Join("\n", issues), "weapon.safety");
        }
        messages.Add("已核对最终弹速与加速度、导弹实体及引用；所选Ammo全部使用者受影响 / Final speed, acceleration, missile entities and references checked; all users of selected Ammo are affected");
        return new(StringComparer.OrdinalIgnoreCase);
    }
    public static IReadOnlyDictionary<string, string> ReadValues(NdfSyntaxDocument doc, NdfConstructorSpan root) => doc.EnumerateDirectAssignments(root)
        .Where(a => a.Name == "ProjectileType" || AmmoRelations.Contains(a.Name)).GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.Count() == 1 ? doc.Raw(g.Single().Value) : "?");

    // Lightweight feedback only; the final candidate graph remains the authority at apply.
    public static IReadOnlyList<WeaponSafetyIssue> Preview(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> operations, string ammoName, string unit = "", string weapon = "", int mount = -1)
    {
        var cells = WeaponBatch.Cells(data, operations);
        Dictionary<string, string> Values(AmmoRecord a, string u, string w, int m)
        {
            var values = a.SafetyValues.ToDictionary(p => p.Key, p => p.Value);
            foreach (var field in a.Fields.Where(f => f.State == WeaponFieldState.Declared && f.Definition.Constructor is null && f.Definition.ArgumentName is null && f.Definition.MapKey is null)) values.TryAdd(field.Definition.FieldName, field.RawValue);
            foreach (var cell in cells.Where(c => c.Ammo == a.Name && c.Key.StartsWith("ammo.") && c.Unit.Length == 0)
                .Concat(cells.Where(c => c.Ammo == a.Name && c.Key.StartsWith("ammo.") && c.Unit == u && u.Length > 0 && c.Weapon == w && c.Mount == m)))
                if (a.Field(cell.Key) is { } field && field.Definition.Constructor is null && field.Definition.ArgumentName is null && field.Definition.MapKey is null) values[field.Definition.FieldName] = cell.Raw;
            return values;
        }
        var result = new List<WeaponSafetyIssue>();
        if (data.Ammo(ammoName) is { } current)
        {
            var values = Values(current, unit, weapon, mount);
            result.AddRange(InspectAmmo(current.Source.RelativeSourceFile, current.Name, values));
            if (Number(values.GetValueOrDefault("SupplyCost") ?? "", out var cost) && Number(values.GetValueOrDefault("ShotsCountPerSalvo") ?? "", out var shots) && shots > 0)
                result.Add(new("supply.info", WeaponSafetyLevel.Warning, current.Source.RelativeSourceFile, current.Name, "SupplyCost", "每发补给 / Supply per shot: " + (cost / shots).ToString("G8", System.Globalization.CultureInfo.InvariantCulture)));
        }
        foreach (var w in data.Weapons.Where(w => weapon.Length > 0 ? w.Name == weapon : w.Mounts.Any(m => m.AmmoName == ammoName)))
        {
            var contexts = unit.Length > 0 ? new[] { unit } : new[] { "" }.Concat(cells.Where(c => c.Weapon == w.Name).Select(c => c.Unit)).Distinct();
            foreach (var u in contexts)
            {
            var deployed = w.Mounts.Any(m => data.Ammo(WeaponBatch.AmmoAt(data, cells, u, w.Name, m.Index)) is { } a && Values(a, u, w.Name, m.Index).GetValueOrDefault("HasDeploymentTime") == "True");
            if (deployed)
            foreach (var target in data.Units.Where(n => n.Weapons.Contains(w.Name) && (u.Length == 0 || n.Name == u)))
            {
                var source = target.SourceSnapshot;
                if (source is null) continue;
                var doc = new NdfSyntaxDocument(source, target.Source.CharacterOffset, target.Source.CharacterLength);
                if (doc.FindConstructors("TWeaponDeploymentModuleDescriptor").Count == 0)
                    result.Add(new("deployment.preview", WeaponSafetyLevel.Warning, target.Source.RelativeSourceFile, target.Name, "HasDeploymentTime / Ammunition", "使用单位未发现内联部署模块，需核对引用及配套草稿 / No inline deployment module found; check references and related drafts"));
            }
            foreach (var group in w.Mounts.GroupBy(m => m.AmmoBoxIndex).Where(g => g.Count() > 1))
            {
                var counts = group.Select(m =>
                {
                    var name = WeaponBatch.AmmoAt(data, cells, u, w.Name, m.Index);
                    return data.Ammo(name) is { } a ? Values(a, u, w.Name, m.Index).GetValueOrDefault("ShotsCountPerSalvo") : null;
                }).ToArray();
                if (counts.All(v => Integer(v ?? "", out _)) && counts.Select(v => int.Parse(v!, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Count() > 1)
                    result.Add(new("box", WeaponSafetyLevel.Error, w.Source.RelativeSourceFile, w.Name, "ShotsCountPerSalvo / Ammunition", "同箱齐射数不同（" + string.Join(", ", counts) + "），需配套后应用 / Shared ammo-box salvo sizes differ; complete related drafts: " + string.Join(", ", counts) + " · " + u));
            }
            }
        }
        return result;
    }
}
