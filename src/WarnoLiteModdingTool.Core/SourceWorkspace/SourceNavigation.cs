using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.SourceWorkspace;

/// <summary>Resolved against the displayed text, never against a cached line number.</summary>
public sealed record SourceRange(int Offset, int Length, string Label);

public static class SourceNavigation
{
    public static IReadOnlyList<SourceRange> Object(string text, string name, string? type = null) =>
        Objects(text, name, type).Select(o => new SourceRange(o.CharacterOffset, o.CharacterLength, o.Name)).ToArray();

    private static IEnumerable<NdfObjectInfo> Objects(string text, string name, string? type) =>
        new NdfTopLevelScanner().Scan(text, "navigation.ndf", "source").Objects
            .Where(o => o.Name == name && (type is null || o.TypeName == type));

    public static IReadOnlyList<SourceRange> Fields(string text, string name, string? type,
        Func<NdfSyntaxDocument, NdfObjectInfo, IEnumerable<NdfValueSpan>> locate, string label)
    {
        var result = new List<SourceRange>();
        foreach (var obj in Objects(text, name, type))
        {
            var doc = new NdfSyntaxDocument(text, obj.CharacterOffset, obj.CharacterLength);
            foreach (var span in locate(doc, obj)) result.Add(new(doc.StartOffset(span), doc.Length(span), label));
        }
        return result.DistinctBy(r => (r.Offset, r.Length)).ToArray();
    }

    public static IReadOnlyList<SourceRange> UnitField(string text, string name, string type, NdfFieldSelector selector) =>
        Fields(text, name, type, (doc, _) => new NdfFieldLocator().Locate(doc, selector).Values, selector.DisplayPath);

    public static IReadOnlyList<SourceRange> Direct(string text, string name, string? type, string field) =>
        Fields(text, name, type, (doc, obj) => doc.FindConstructors(obj.TypeName).SelectMany(c => doc.FindDirectAssignments(c, field)), field);

    public static IReadOnlyList<SourceRange> WeaponField(string text, string name, string type, WeaponFieldDefinition field, MountedWeaponRecord? mount = null)
    {
        return Fields(text, name, type, (doc, obj) =>
        {
            if (field.Owner == WeaponFieldOwner.Ammo)
                return doc.FindConstructors(obj.TypeName).SelectMany(c => WeaponProjectLoader.LocateAmmoField(doc, c, field));
            bool Matches(NdfConstructorSpan c) => mount is null || doc.FindDirectAssignments(c, "Ammunition") is { Count: 1 } a && NdfSyntaxDocument.Leaf(doc.Raw(a[0])) == mount.AmmoName
                && (mount.AmmoBoxIndex is null || doc.FindDirectAssignments(c, "AmmoBoxIndex") is { Count: 1 } b && doc.Raw(b[0]).Trim() == mount.AmmoBoxIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (field.Owner == WeaponFieldOwner.MountedWeapon)
                return doc.FindConstructors("TMountedWeaponDescriptor").Where(Matches).SelectMany(c => doc.FindDirectAssignments(c, field.FieldName));
            if (field.Owner == WeaponFieldOwner.Turret)
                return WeaponStructureSyntax.Types.SelectMany(doc.FindConstructors).Where(c => mount is null || doc.FindConstructors("TMountedWeaponDescriptor", c.OpenTokenIndex + 1, c.CloseTokenIndex - 1).Any(Matches)).SelectMany(c => doc.FindDirectAssignments(c, field.FieldName));
            if (field.Key.StartsWith("weapon.salves.", StringComparison.Ordinal) && int.TryParse(field.Key.Split('.').Last(), out var box) && box >= 0)
                return doc.FindConstructors(obj.TypeName).SelectMany(c => doc.FindDirectAssignments(c, "Salves")).SelectMany(v => doc.ReadArrayElements(v).Skip(box).Take(1));
            return doc.FindConstructors(obj.TypeName).SelectMany(c => doc.FindDirectAssignments(c, field.FieldName));
        }, field.Key.StartsWith("weapon.salves.", StringComparison.Ordinal) ? "Salves[" + field.Key.Split('.').Last() + "]" : field.FieldName);
    }

    public static IReadOnlyList<SourceRange> Mount(string text, string name, string type, string ammo, int? box) =>
        Fields(text, name, type, (doc, _) => doc.FindConstructors("TMountedWeaponDescriptor")
            .Where(c => doc.FindDirectAssignments(c, "Ammunition") is { Count: 1 } a && NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(doc.Raw(a[0]))) == ammo
                && (box is null || doc.FindDirectAssignments(c, "AmmoBoxIndex") is { Count: 1 } b && doc.Raw(b[0]).Trim() == box.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Select(c => new NdfValueSpan(c.TypeTokenIndex, c.CloseTokenIndex)), "TMountedWeaponDescriptor");

    public static IReadOnlyList<SourceRange> Rule(string text, RuleDefinition definition, string? key)
    {
        if (definition.Number == AirLayout.Number) return [];
        var doc = new NdfSyntaxDocument(text);
        var result = new List<SourceRange>();
        foreach (var field in definition.Fields.Split(';'))
        {
            IEnumerable<NdfValueSpan> spans = definition.Constructor.Length == 0 ? doc.FindNamedValues(field)
                : doc.FindConstructors(definition.Constructor).SelectMany(c => doc.FindDirectAssignments(c, field));
            if (definition.MapKey is {} mapKey) spans = spans.SelectMany(doc.ReadMapEntries).Where(e => doc.Raw(e.Key) == mapKey).Select(e => e.Value);
            foreach (var span in spans) Visit(span, field);
        }
        return result;
        void Visit(NdfValueSpan span, string path)
        {
            if (key is null || path == key) { result.Add(new(doc.StartOffset(span), doc.Length(span), path)); return; }
            var raw = doc.Raw(span).TrimStart();
            if (raw.StartsWith("MAP", StringComparison.Ordinal))
                foreach (var entry in doc.ReadMapEntries(span)) Visit(entry.Value, path + "/" + doc.Raw(entry.Key));
            else if (raw.StartsWith('['))
            { var entries = doc.ReadArrayElements(span); for (var i = 0; i < entries.Count; i++) Visit(entries[i], path + "/" + i); }
        }
    }

    public static IReadOnlyList<SourceRange> DivisionMember(string text, string name, string unit) =>
        Fields(text, name, null, (doc, _) => doc.FindConstructors("TDeckUniteRule")
            .Where(c => doc.FindDirectAssignments(c, "UnitDescriptor").Any(v => NdfSyntaxDocument.Leaf(doc.Raw(v)) == unit))
            .Select(c => new NdfValueSpan(c.TypeTokenIndex, c.CloseTokenIndex)), unit);

    public static IReadOnlyList<SourceRange> Terrain(string text, string name, string? key)
    {
        if (key is not null) return TerrainWorkspace.Read("terrain.ndf", text).Where(t => t.Name == name).SelectMany(t => t.Cells).Where(c => c.Key == key)
            .Select(c => new SourceRange(c.Offset, c.Length, key)).ToArray();
        var doc = new NdfSyntaxDocument(text);
        return doc.FindConstructors("TGameplayTerrain").Where(c => doc.FindDirectAssignments(c, "Name") is { Count: 1 } names && NdfSyntaxDocument.Unquote(doc.Raw(names[0])) == name)
            .Select(c => new SourceRange(doc.StartOffset(new(c.TypeTokenIndex, c.CloseTokenIndex)), doc.Length(new(c.TypeTokenIndex, c.CloseTokenIndex)), name)).ToArray();
    }

    public static IReadOnlyList<SourceRange> Experience(string text, ExperienceRoute route, ExperienceLevel level, ExperienceCell cell) =>
        Fields(text, cell.Source, null, (doc, _) => cell.Source == route.Name
            ? doc.FindConstructors("TExperienceLevelsPackDescriptor").SelectMany(c => doc.FindDirectAssignments(c, "ExperienceLevelsDescriptors"))
                .SelectMany(v => doc.ReadArrayElements(v).Skip(level.Index).Take(1)).SelectMany(v => doc.FindConstructors("TExperienceLevelDescriptor", v))
                .SelectMany(c => doc.FindDirectAssignments(c, cell.Field))
            : doc.FindAssignmentsAnywhere(cell.Field), cell.Field);

    // Exact token matching preserves identity after whitespace/comment edits. Changed structures are not guessed.
    public static IReadOnlyList<SourceRange> Fragment(string text, string name, string fragment, string label)
    {
        var expected = new NdfSyntaxDocument(fragment).Tokens;
        if (expected.Count == 0) return [];
        return Fields(text, name, null, (doc, _) =>
        {
            var hits = new List<NdfValueSpan>();
            for (var i = 0; i + expected.Count <= doc.Tokens.Count; i++)
                if (Enumerable.Range(0, expected.Count).All(j => doc.Tokens[i + j].Text == expected[j].Text)) hits.Add(new(i, i + expected.Count - 1));
            return hits;
        }, label);
    }

    public static IReadOnlyList<SourceRange> Csv(string text, string token) =>
        Transactions.SemicolonCsvDocument.Parse(text).Rows.Where(r => r.Fields.Count > 0 && r.Fields[0].Value == token)
            .Select(r => new SourceRange(r.Offset, r.Length, token + " · CSV")).ToArray();

    public static IReadOnlyList<SourceRange> Damage(string text, string name, DamageCell cell) =>
        Fields(text, name, null, (doc, obj) =>
        {
            var result = new List<NdfValueSpan>();
            foreach (var root in doc.FindConstructors(obj.TypeName))
            {
                int Position(string field, DamageSlot slot)
                {
                    var spans = doc.FindDirectAssignments(root, field);
                    if (spans.Count != 1) return -1;
                    var entries = doc.ReadMapEntries(spans[0]); var offset = 0; var found = -1;
                    foreach (var entry in entries)
                    {
                        if (!int.TryParse(doc.Raw(entry.Value), out var count) || count < 1) return -1;
                        if (doc.Raw(entry.Key) == slot.Family) { if (found >= 0 || slot.Index < 1 || slot.Index > count) return -1; found = offset + slot.Index - 1; }
                        offset += count;
                    }
                    return found;
                }
                var r = Position("DamageFamilyCounts", cell.Attack); var c = Position("ResistanceFamilyCounts", cell.Resistance);
                if (r < 0 || c < 0) continue;
                foreach (var values in doc.FindDirectAssignments(root, "Values"))
                foreach (var row in doc.ReadArrayElements(values).Skip(r).Take(1)) result.AddRange(doc.ReadArrayElements(row).Skip(c).Take(1));
            }
            return result;
        }, cell.Key);
}
