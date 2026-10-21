using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public static partial class WeaponSafety
{
    private static string ObjectKey(string file, string name) => UnitProjectGraph.Normalize(file).ToUpperInvariant() + "|" + name;

    // Reuses the two evaluated graphs. Only explicit business-created objects are visited here;
    // source edits never supply this operation-local provenance.
    private static HashSet<string> InheritedCreationIssues(UnitProjectGraph before, UnitProjectGraph after,
        Dictionary<string, Check> old, Dictionary<string, Check> current, IReadOnlyList<UnitCreationOrigin> origins,
        ICollection<string> messages)
    {
        var inherited = new HashSet<string>(StringComparer.Ordinal);
        var weapons = origins.Where(o => o.Type == "TWeaponManagerModuleDescriptor")
            .GroupBy(o => o.Name, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var referenceEvidence = new Dictionary<(string File, string Raw), bool>();

        bool ValidOrigin(UnitCreationOrigin origin)
        {
            bool Matches(IReadOnlyList<NdfObjectInfo> objects) => objects.Count == 1 && objects[0].TypeName == origin.Type &&
                UnitProjectGraph.Normalize(objects[0].RelativeSourceFile).Equals(UnitProjectGraph.Normalize(origin.RelativeFile), StringComparison.OrdinalIgnoreCase);
            return before.FindObjects(origin.Name).Count == 0 && Matches(after.FindObjects(origin.Name)) &&
                Matches(before.FindObjects(origin.Mother)) && Matches(after.FindObjects(origin.Mother)) && before.HasSameScope(origin.RelativeFile, after);
        }

        IEnumerable<(string Key, Check Check)> Modules(Dictionary<string, Check> checks, string key)
        {
            for (var i = 0; checks.TryGetValue(key + "|module.ref." + i, out var check); i++)
                yield return (key + "|module.ref." + i, check);
        }

        Dictionary<string, int> UnknownCounts(Dictionary<string, Check> checks, string key) => Modules(checks, key)
            .Where(p => p.Check.Issue?.Level == WeaponSafetyLevel.Unverified)
            .GroupBy(p => p.Check.Stamp, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        bool UnchangedDeclarations(string file, string raw)
        {
            var key = (UnitProjectGraph.Normalize(file).ToUpperInvariant(), raw);
            if (referenceEvidence.TryGetValue(key, out var cached)) return cached;
            var prior = before.FindObjects(NdfSyntaxDocument.Leaf(raw));
            var next = after.FindObjects(NdfSyntaxDocument.Leaf(raw));
            // An unresolved reference may still have declarations. Do not hide a same-transaction
            // deletion/edit of those declarations just because resolution still returns null.
            var unchanged = prior.Count == next.Count && prior.All(p => next.Any(n => UnitProjectGraph.Same(p, n) && p.TypeName == n.TypeName &&
                before.FileOf(p).Text.AsSpan(p.CharacterOffset, p.CharacterLength).SequenceEqual(after.FileOf(n).Text.AsSpan(n.CharacterOffset, n.CharacterLength))));
            referenceEvidence[key] = unchanged;
            return unchanged;
        }

        foreach (var origin in origins.Where(o => o.Type == "TEntityDescriptor"))
        {
            if (!ValidOrigin(origin)) continue;
            var key = ObjectKey(origin.RelativeFile, origin.Name);
            var mother = ObjectKey(origin.RelativeFile, origin.Mother);
            var oldCounts = UnknownCounts(old, mother);
            var finalCounts = UnknownCounts(current, mother);
            var details = new List<string>();
            foreach (var (ruleKey, check) in Modules(current, key))
            {
                if (check.Issue?.Level != WeaponSafetyLevel.Unverified || oldCounts.GetValueOrDefault(check.Stamp) == 0 || finalCounts.GetValueOrDefault(check.Stamp) == 0) continue;
                var raw = check.Stamp[..check.Stamp.LastIndexOf('|')];
                if (!UnchangedDeclarations(origin.RelativeFile, raw)) continue;
                oldCounts[check.Stamp]--; finalCounts[check.Stamp]--;
                inherited.Add(ruleKey); details.Add(raw);
            }
            // Only the existing *unverified* deployment state may be inherited, and only when
            // every recorded input is identical in the mother before/after and the new unit.
            // Known errors, changed deployment requirements and unknown module structures stay fatal.
            foreach (var (_, module) in Modules(current, key))
            {
                if (!module.Stamp.EndsWith("|TWeaponManagerModuleDescriptor", StringComparison.Ordinal)) continue;
                var weapon = after.Resolve(origin.RelativeFile, module.Stamp[..module.Stamp.LastIndexOf('|')]);
                if (weapon is null) continue;
                var originalWeapon = weapon.Name;
                if (weapons.TryGetValue(weapon.Name, out var weaponOrigin))
                {
                    if (!ValidOrigin(weaponOrigin)) continue;
                    originalWeapon = weaponOrigin.Mother;
                }
                var ruleKey = key + "|deployment." + weapon.Name;
                var originalKey = mother + "|deployment." + originalWeapon;
                if (current.TryGetValue(ruleKey, out var check) && check.Issue?.Level == WeaponSafetyLevel.Unverified &&
                    old.TryGetValue(originalKey, out var prior) && prior.Issue?.Level == WeaponSafetyLevel.Unverified && prior.Stamp == check.Stamp &&
                    current.TryGetValue(originalKey, out var final) && final.Issue?.Level == WeaponSafetyLevel.Unverified && final.Stamp == check.Stamp &&
                    Modules(current, key).Where(p => p.Check.Issue is not null).All(p => inherited.Contains(p.Key)))
                {
                    inherited.Add(ruleKey); details.Add("HasDeploymentTime / " + weapon.Name);
                }
            }
            if (details.Count > 0)
                messages.Add($"继承母版的未验证模块关联（{details.Count}项）；未确认有效 / Inherited unverified module dependencies ({details.Count}); validity unconfirmed: {origin.Name} ← {origin.Mother}\n" + string.Join("\n", details));
        }
        return inherited;
    }
}
