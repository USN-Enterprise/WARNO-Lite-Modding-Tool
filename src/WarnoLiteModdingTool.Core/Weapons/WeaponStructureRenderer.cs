using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public static class WeaponStructureRenderer
{
    /// <summary>Ordinary parameter candidates are composed first; anchors still refer to the unchanged original topology.</summary>
    public static string Render(WeaponStructureState state, string candidate, IReadOnlyDictionary<string, string>? ammoOverrides = null)
    {
        var original = new WeaponStructureSyntax(state.Weapon.Body); var current = new WeaponStructureSyntax(candidate);
        if (original.Boxes.Count != current.Boxes.Count || !original.Turrets.Select(t => (t.Type, t.Mounts.Count)).SequenceEqual(current.Turrets.Select(t => (t.Type, t.Mounts.Count))))
            throw WeaponStructureSyntax.Error("其他草稿改变了相同武器结构，请合并或撤销冲突计划");
        var rows = WeaponStructure.Rows(state).Where(r => !r.Removed).ToArray();
        var used = rows.Select(r => r.Box).ToHashSet(StringComparer.Ordinal);
        // Retain boxes that were already unused; this operation only removes boxes orphaned by its own deletions.
        var originallyUsed = original.Mounts.Select(m => "b:" + m.Box).ToHashSet(StringComparer.Ordinal);
        foreach (var key in state.Boxes.Keys.Where(k => k.StartsWith("b:", StringComparison.Ordinal) && !originallyUsed.Contains(k))) used.Add(key);
        var boxKeys = state.Boxes.Keys.Where(used.Contains).OrderBy(k => k.StartsWith("b:", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(k => k.StartsWith("b:", StringComparison.Ordinal) ? int.Parse(k[2..]) : 0).ToArray();
        var mapping = boxKeys.Select((key, i) => (key, i)).ToDictionary(x => x.key, x => x.i);
        var added = state.Added.ToDictionary(a => a.Id);
        string MountBody(string id, string body, string box)
        {
            if (state.Fields.TryGetValue(id, out var fields))
            {
                if (!added.ContainsKey(id))
                {
                    var baseline = WeaponStructureSyntax.Values(original.Mounts.Single(m => m.Id == id).Body, "TMountedWeaponDescriptor", fields.Keys);
                    var existing = WeaponStructureSyntax.Values(body, "TMountedWeaponDescriptor", fields.Keys);
                    foreach (var f in fields)
                        if (existing.GetValueOrDefault(f.Key) != baseline.GetValueOrDefault(f.Key) && existing.GetValueOrDefault(f.Key) != f.Value)
                            throw WeaponStructureSyntax.Error("结构与参数草稿目标冲突：" + id + " · " + f.Key);
                }
                body = WeaponStructureSyntax.Set(body, "TMountedWeaponDescriptor", fields);
            }
            var replacements = new Dictionary<string, string> { ["AmmoBoxIndex"] = WeaponStructureSyntax.Int(mapping[box]) };
            if (ammoOverrides?.TryGetValue(id, out var ammo) == true) replacements["Ammunition"] = ammo;
            return WeaponStructureSyntax.Set(body, "TMountedWeaponDescriptor", replacements);
        }
        var turretBodies = new Dictionary<int, string>(); var removedTurrets = new HashSet<int>();
        for (var i = 0; i < current.Turrets.Count; i++)
        {
            var t = current.Turrets[i]; var replacements = new Dictionary<int, string>(); var removed = new HashSet<int>();
            for (var m = 0; m < t.Mounts.Count; m++)
            {
                var mount = t.Mounts[m];
                if (state.Removed.Contains(mount.Id)) { removed.Add(m); continue; }
                var value = MountBody(mount.Id, mount.Body, "b:" + mount.Box);
                if (value != mount.Body) replacements[m] = value;
            }
            var append = state.Added.Where(a => a.Turret == t.Id).Select(a => MountBody(a.Id, a.Body, a.Box)).ToArray();
            if (removed.Count == t.Mounts.Count && append.Length == 0) { removedTurrets.Add(i); continue; }
            if (removed.Count == 0 && replacements.Count == 0 && append.Length == 0) continue;
            var td = new NdfSyntaxDocument(t.Body); var node = WeaponStructureSyntax.Constructor(td, new(0, td.Tokens.Count - 1), t.Type);
            turretBodies[i] = WeaponStructureSyntax.EditList(t.Body, WeaponStructureSyntax.Assignment(td, node, "MountedWeaponDescriptorList"), removed, replacements, append);
        }
        // A subordinate turret must not retain a master bone whose entire gameplay group was removed.
        var deletedBones = removedTurrets.SelectMany(i => WeaponStructureSyntax.Values(current.Turrets[i].Body, current.Turrets[i].Type, ["YulBoneOrdinal"]).Values).ToHashSet();
        foreach (var t in current.Turrets.Where((_, i) => !removedTurrets.Contains(i)))
            if (WeaponStructureSyntax.Values(t.Body, t.Type, ["MasterTurretYulBoneOrdinal"]).Values.Any(deletedBones.Contains))
                throw WeaponStructureSyntax.Error("其他炮塔仍依赖被删除炮塔的挂点");
        var newTurrets = state.Turrets.Select(t =>
        {
            var doc = new NdfSyntaxDocument(t.Body); var type = doc.Tokens[0].Text;
            var node = WeaponStructureSyntax.Constructor(doc, new(0, doc.Tokens.Count - 1), type);
            return WeaponStructureSyntax.EditList(t.Body, WeaponStructureSyntax.Assignment(doc, node, "MountedWeaponDescriptorList"), new HashSet<int>(), new Dictionary<int, string>(),
                state.Added.Where(a => a.Turret == t.Id).Select(a => MountBody(a.Id, a.Body, a.Box)).ToArray());
        }).ToArray();
        var text = WeaponStructureSyntax.EditList(candidate, current.TurretList, removedTurrets, turretBodies, newTurrets);
        var docAfter = new NdfSyntaxDocument(text); var root = WeaponStructureSyntax.Constructor(docAfter, new(0, docAfter.Tokens.Count - 1), "TWeaponManagerModuleDescriptor", true);
        var salves = WeaponStructureSyntax.Assignment(docAfter, root, "Salves");
        var removedBoxes = Enumerable.Range(0, current.Boxes.Count).Where(i => !used.Contains("b:" + i)).ToHashSet();
        var changedBoxes = new Dictionary<int, string>();
        foreach (var key in boxKeys.Where(k => k.StartsWith("b:", StringComparison.Ordinal)))
        {
            var i = int.Parse(key[2..]); var raw = current.Document.Raw(current.Boxes[i]); var baseline = original.Document.Raw(original.Boxes[i]);
            var target = WeaponStructureSyntax.Int(state.Boxes[key]);
            if (target != baseline && raw != baseline && raw != target) throw WeaponStructureSyntax.Error("弹药箱草稿目标冲突：" + key);
            if (target != baseline) changedBoxes[i] = target;
        }
        text = WeaponStructureSyntax.EditList(text, salves, removedBoxes, changedBoxes, boxKeys.Where(k => !k.StartsWith("b:", StringComparison.Ordinal)).Select(k => WeaponStructureSyntax.Int(state.Boxes[k])).ToArray());
        var final = new WeaponStructureSyntax(text);
        if (final.Mounts.Count() != rows.Length) throw WeaponStructureSyntax.Error("最终槽位数量校验失败");
        return text;
    }
}
