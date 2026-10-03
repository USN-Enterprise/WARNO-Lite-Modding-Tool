using System.Text;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Changes;

public static partial class ChangeMerge
{
    // Only an exact reconstruction by known quantity cells proves that no identity,
    // reference, list, comment or other structural change is hidden in this file.
    public static bool QuantityOnly(string path, byte[]? before, byte[]? after)
    {
        if (!path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase) || before is null || after is null) return false;
        try { return QuantityObjects(DecodeNdf(before), DecodeNdf(after), allowGlobals: true) is not null; }
        catch (Exception e) when (e is InvalidDataException or ArgumentException) { return false; }
    }
    private static Dictionary<string, string>? QuantityObjects(string before, string after, bool allowGlobals)
    {
        var b = Objects(before); var m = Objects(after); var patches = new List<TextReplacement>(); var changed = new Dictionary<string, string>();
        if (!b.Keys.ToHashSet().SetEquals(m.Keys)) return null;
        foreach (var name in b.Keys)
        {
            var x = b[name]; var y = m[name]; if (x.Text == y.Text) continue;
            if (x.Type != y.Type) return null;
            var bc = Cells(x.Text); var mc = Cells(y.Text); var local = new List<TextReplacement>();
            foreach (var key in bc.Keys.Intersect(mc.Keys))
            {
                var old = bc[key]; var value = mc[key]; if (old.Raw == value.Raw) continue;
                if (!old.Numeric || !value.Numeric) return null;
                local.Add(new(old.Start, old.Length, old.Raw, value.Raw, key));
            }
            if (local.Count == 0 || SemicolonCsvDocument.ApplyReplacements(x.Text, local) != y.Text) return null;
            patches.Add(new(x.Start, x.Length, x.Text, y.Text, name)); changed.Add(name, x.Type);
        }
        if (allowGlobals)
        {
            var bg = GlobalCells(before); var mg = GlobalCells(after);
            foreach (var key in bg.Keys.Intersect(mg.Keys))
                if (bg[key].Raw != mg[key].Raw) patches.Add(new(bg[key].Start, bg[key].Length, bg[key].Raw, mg[key].Raw, key));
        }
        return patches.Count > 0 && SemicolonCsvDocument.ApplyReplacements(before, patches) == after ? changed : null;
    }
    public static IReadOnlyList<ChangeObjectTarget> MappingSources(ChangePackage package, string path)
    {
        var f = package.Manifest.Files.Single(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (!path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase) || package.Before(f) is not { } before || package.After(f) is not { } after)
            throw new InvalidDataException("只能对应已有对象的已知数值修改 / Only known quantity changes of existing objects can be mapped");
        var changed = QuantityObjects(DecodeNdf(before), DecodeNdf(after), allowGlobals: false);
        if (changed is null || changed.Count == 0) throw new InvalidDataException("含结构、身份、引用或其他原文修改，不能仅靠对象对应处理 / Structural, identity, reference or other text changes cannot be resolved by object mapping");
        return changed.Select(c => new ChangeObjectTarget(path, c.Key, c.Value)).ToArray();
    }
    public static IReadOnlyList<ChangeObjectTarget> MappingTargets(string root, CancellationToken cancel = default)
    {
        using var input = new ChangeCapture.Inputs(root, false); var rows = new List<ChangeObjectTarget>();
        foreach (var path in input.Paths.Where(p => p.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)))
        {
            cancel.ThrowIfCancellationRequested();
            try { rows.AddRange(Objects(DecodeNdf(input.Read(path)!)).Select(o => new ChangeObjectTarget(path, o.Key, o.Value.Type))); }
            catch (Exception e) when (e is InvalidDataException or ArgumentException) { }
        }
        return rows.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.Ordinal).ToArray();
    }
    internal static RestoreFile MergeMapped(ChangePackage package, ChangeFile file, ChangeFileMapping mapping, byte[]? target, NumericPolicy policy)
    {
        var details = new List<ChangeDetail>();
        try
        {
            ChangePaths.Writable(mapping.TargetPath);
            var sources = MappingSources(package, file.Path);
            if (!mapping.TargetPath.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase) || target is null || mapping.Objects is null ||
                !mapping.Objects.Keys.ToHashSet().SetEquals(sources.Select(s => s.Name)) || mapping.Objects.Values.Distinct(StringComparer.Ordinal).Count() != mapping.Objects.Count)
                throw new InvalidDataException("每个修改对象须对应一个不同的现有同类型对象 / Map every changed object to a distinct existing object of the same type");
            var targetText = DecodeNdf(target); var targets = Objects(targetText); var b = Objects(DecodeNdf(package.Before(file)!)); var m = Objects(DecodeNdf(package.After(file)!));
            var patches = new List<TextReplacement>();
            foreach (var source in sources)
            {
                var name = mapping.Objects[source.Name];
                if (!targets.TryGetValue(name, out var current) || current.Type != source.Type) throw new InvalidDataException("对应对象缺失或类型不符 / Mapped object missing or type differs: " + name);
                // MergeObject works only on field spans; the new declaration name and
                // all target references/identity values are left untouched.
                var old = b[source.Name]; var edited = m[source.Name];
                var desired = MergeObject(name, old.Text, edited.Text, current.Text, policy, details, new ScalarReview(file.Path, []));
                patches.Add(new(current.Start, current.Length, current.Text, desired, name));
            }
            var bytes = Encoding.UTF8.GetBytes(SemicolonCsvDocument.ApplyReplacements(targetText, patches)); ValidateBytes(mapping.TargetPath, bytes);
            return new(mapping.TargetPath, target, bytes, "已对应，可还原 / Mapped, ready", details) { SourcePath = file.Path };
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        { return new(mapping.TargetPath, target, target, "对应需核对 / Mapping requires review", details, e.Message) { SourcePath = file.Path }; }
    }
}
