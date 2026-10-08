using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Weapons;

public static partial class WeaponSafety
{
    public static IReadOnlyDictionary<string, string> ReadValues(NdfSyntaxDocument doc, NdfConstructorSpan root) => doc.EnumerateDirectAssignments(root)
        .Where(a => AmmoRelations.Contains(a.Name)).GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.Count() == 1 ? doc.Raw(g.Single().Value) : "?");

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
