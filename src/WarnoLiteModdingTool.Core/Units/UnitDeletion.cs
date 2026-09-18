using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Units;

public sealed record UnitDeleteState(int Version, string Guid, Dictionary<string, string> Replacements);
public sealed record UnitDeleteUse(string Key, string File, string Description, string? Field, bool Automatic);

public static class UnitDeletion
{
    public static UnitDeleteState Read(DraftOperation op) => JsonSerializer.Deserialize<UnitDeleteState>(op.TargetRaw) is { Version: 1, Replacements: not null } state ? state : throw new InvalidDataException("删除草稿版本无效");
    public static string Key(UnitProjectGraph.Reference reference) => reference.File.Path + "|" + (reference.Owner?.Name ?? "") + "|" + reference.Token.Start;
    public static DraftOperation Operation(UnitRecord unit, Dictionary<string, string>? replacements = null)
    {
        var body = UnitCreation.Source(unit); var payload = JsonSerializer.Serialize(new UnitDeleteState(1, UnitIdentityEditing.GuidOf(body), replacements ?? []));
        return new(DraftOperation.CreateId(DraftTargetKind.UnitDelete, unit.Source.RelativeSourceFile, unit.Name, "unit.delete"), "delete:" + unit.Name, DraftTargetKind.UnitDelete, "units", unit.Source.RelativeSourceFile, unit.Name, unit.Source.TypeName, "unit.delete", "Unit/Delete", "UnitDelete", unit.Name, body, "删除", payload, "删除新建单位 · " + unit.DisplayName + " · " + unit.Name, unit.NameToken, false, DateTimeOffset.UtcNow);
    }
    public static ResolvedDraftOperation Resolve(UnitWorkspaceData units, DraftOperation op)
    {
        try
        {
            var state = Read(op); var unit = units.Units.Single(u => u.Name == op.ObjectName);
            if (UnitCreation.Source(unit) != op.BaselineRaw || UnitIdentityEditing.GuidOf(op.BaselineRaw) != state.Guid) throw new InvalidDataException("删除单位基线已变化");
            return new(op, DraftResolutionStatus.Active, "");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException) { return new(op, DraftResolutionStatus.Conflict, ex.Message); }
    }
    private static (string? Field, NdfValueSpan? Array, NdfValueSpan? Element) Context(UnitProjectGraph.Reference reference)
    {
        var doc = reference.File.Syntax;
        foreach (var constructor in doc.FindConstructors("TDeckDivisionRule"))
        foreach (var list in doc.FindDirectAssignments(constructor, "UnitRuleList"))
        foreach (var element in doc.ReadArrayElements(list))
        {
            if (reference.Token.Start < doc.StartOffset(element) || reference.Token.End > doc.StartOffset(element) + doc.Length(element)) continue;
            foreach (var rule in doc.FindConstructors("TDeckUniteRule", element))
            {
                foreach (var assignment in doc.EnumerateDirectAssignments(rule))
                {
                    if (reference.Token.Start < doc.StartOffset(assignment.Value) || reference.Token.End > doc.StartOffset(assignment.Value) + doc.Length(assignment.Value)) continue;
                    if (assignment.Name == "UnitDescriptor") return ("UnitRule", list, element);
                    if (assignment.Name == "AvailableTransportList")
                    {
                        var transport = doc.ReadArrayElements(assignment.Value).SingleOrDefault(e => doc.StartOffset(e) == reference.Token.Start);
                        var without = doc.FindDirectAssignments(rule, "AvailableWithoutTransport");
                        var canEmpty = without.Count == 1 && doc.Raw(without[0]) == "True";
                        if (transport is not null && (canEmpty || doc.ReadArrayElements(assignment.Value).Count > 1)) return ("AvailableTransportList", assignment.Value, transport);
                        return ("Transport", null, null);
                    }
                }
            }
        }
        var tokens = doc.Tokens; var index = tokens.ToList().FindIndex(t => t.Start == reference.Token.Start);
        if (index >= 2 && tokens[index - 1].Text == "=" && tokens[index - 2].Text is "Unit" or "Transport" or "UpgradeFromUnit") return (tokens[index - 2].Text, null, null);
        return (null, null, null);
    }
    public static IReadOnlyList<UnitDeleteUse> Uses(UnitProjectGraph graph, NdfObjectInfo unit)
    {
        _ = UnitCreationHistory.Require(graph, unit.RelativeSourceFile, unit.Name);
        return graph.References(unit, true).Where(r => !UnitProjectGraph.Same(r.Owner, unit)).Select(r =>
        {
            var context = Context(r); var registration = UnitProjectGraph.IsRegistration(r.File,r.Token);
            return new UnitDeleteUse(Key(r), r.File.Path, (r.Owner?.Name ?? "UnitIds") + " · " + (context.Field ?? r.Token.Text), context.Field, registration || context.Array is not null);
        }).ToArray();
    }
    public static void Plan(string root, UnitWorkspaceData units, IReadOnlyList<DraftOperation> operations, List<PlannedFileChange> files)
    {
        foreach (var op in operations.Where(o => o.TargetKind == DraftTargetKind.UnitDelete))
        {
            if (operations.Where(o => o.TargetKind == DraftTargetKind.UnitCreate).Any(o => UnitCreation.Read(o).Mother == op.ObjectName)) throw new TransactionValidationException("请先完成或调整以此单位为母版的创建草稿");
            var originalGraph = new UnitProjectGraph(root);
            var provenance = UnitCreationHistory.Require(originalGraph, op.RelativeSourceFile, op.ObjectName);
            var state = Read(op); var graph = new UnitProjectGraph(root, files); var unit = graph.RequireObject(op.RelativeSourceFile, op.ObjectName);
            if (UnitIdentityEditing.GuidOf(graph.Body(unit)) != state.Guid) throw new TransactionValidationException("删除身份变化");
            var patches = new Dictionary<string, List<TextReplacement>>(StringComparer.OrdinalIgnoreCase);
            void Add(string path, TextReplacement patch) { if (!patches.TryGetValue(path, out var changes)) patches[path] = changes = []; changes.Add(patch); }
            var unitFile = graph.FileOf(unit);
            Add(unitFile.Path, new(unit.CharacterOffset, unit.CharacterLength, graph.Body(unit), "", "删除单位声明"));
            var registrations = graph.Files.Values.SelectMany(f => f.Syntax.FindConstructors("TDeckSerializerEntries")
                .SelectMany(c => f.Syntax.FindDirectAssignments(c, "UnitIds")).SelectMany(f.Syntax.ReadMapEntries).Select(e => (File: f, Entry: e))).ToArray();
            var matching = registrations.Where(r => UnitProjectGraph.Same(graph.Resolve(r.File.Path, r.File.Syntax.Raw(r.Entry.Key)), unit) || r.File.Syntax.Raw(r.Entry.Key) == unit.Name).ToArray();
            if (matching.Length > 1 || matching.Length == 1 && registrations.Count(r => int.TryParse(r.File.Syntax.Raw(r.Entry.Value), out var number) && number == provenance.SerializerId) != 1)
                throw new TransactionValidationException("注册条目或编号跨文件重复，不能猜测删除");
            // Explicit registrations are removed even for legacy broken bare keys.
            foreach (var file in graph.Files.Values)
            foreach (var serializer in file.Syntax.FindConstructors("TDeckSerializerEntries"))
            foreach (var map in file.Syntax.FindDirectAssignments(serializer, "UnitIds"))
            {
                var entries = file.Syntax.ReadMapEntries(map);
                var found = entries.Where(e => UnitProjectGraph.Same(graph.Resolve(file.Path, file.Syntax.Raw(e.Key)), unit) || file.Syntax.Raw(e.Key) == unit.Name).ToArray();
                if (found.Length > 1) throw new TransactionValidationException("注册条目重复，不能猜测删除");
                if (found.Length == 1)
                {
                    var entry = found[0]; var number = file.Syntax.Raw(entry.Value);
                    if (number != provenance.SerializerId.ToString() || entries.Count(e => file.Syntax.Raw(e.Value) == number) != 1) throw new TransactionValidationException("注册编号与创建来源不一致或重复");
                    var element = file.Syntax.ReadArrayElements(map).Single(e => file.Syntax.StartOffset(e) < file.Syntax.StartOffset(entry.Key) && file.Syntax.StartOffset(e) + file.Syntax.Length(e) > file.Syntax.StartOffset(entry.Value));
                    Add(file.Path, UnitProjectGraph.RemoveElement(file.Syntax, map, element, "移除注册"));
                }
            }
            foreach (var reference in graph.References(unit, true).Where(r => !UnitProjectGraph.Same(r.Owner, unit)))
            {
                if (patches.GetValueOrDefault(reference.File.Path)?.Any(p => reference.Token.Start >= p.Offset && reference.Token.End <= p.Offset + p.Length) == true) continue;
                var context = Context(reference);
                if (context.Array is not null && context.Element is not null)
                {
                    Add(reference.File.Path, UnitProjectGraph.RemoveElement(reference.File.Syntax, context.Array, context.Element, "清理 " + context.Field)); continue;
                }
                // Offset keys belong to the original preview, so remap through the
                // owning object/field after preceding candidates changed its offset.
                var old = originalGraph.References(originalGraph.RequireObject(op.RelativeSourceFile, op.ObjectName), true)
                    .Where(r => r.File.Path == reference.File.Path && r.Owner?.Name == reference.Owner?.Name && Context(r).Field == context.Field).ToArray();
                string? replacement = old.Length == 1 ? state.Replacements.GetValueOrDefault(Key(old[0])) : state.Replacements.GetValueOrDefault(Key(reference));
                if (context.Field is null || string.IsNullOrWhiteSpace(replacement)) throw new TransactionValidationException("请先解除或替换引用：" + reference.File.Path + " · " + reference.Owner?.Name + " · " + reference.Token.Text);
                var target = graph.Resolve(reference.File.Path, replacement);
                if (target?.TypeName != "TEntityDescriptor" || UnitProjectGraph.Same(target, unit) || operations.Any(o => o.TargetKind == DraftTargetKind.UnitDelete && o.ObjectName == target.Name)) throw new TransactionValidationException("替代单位不存在、多义或待删除：" + replacement);
                if (context.Field == "Transport" && !units.Units.Any(u => u.Name == target.Name && u.HasUniqueTransporterModule)) throw new TransactionValidationException("替代运输必须有唯一运输模块");
                Add(reference.File.Path, new(reference.Token.Start, reference.Token.Length, reference.Token.Text, replacement, "替代单位引用"));
            }
            var identityTags = UnitCapabilities.ReadList(graph.Body(unit), "TTagsModuleDescriptor", "TagSet", true).Where(t => t.StartsWith("UNITE_", StringComparison.Ordinal)).ToHashSet();
            foreach (var file in graph.Files.Values)
            foreach (var token in file.Syntax.Tokens.Where(t => t.Text[0] is '\'' or '"' && identityTags.Contains(NdfSyntaxDocument.Unquote(t.Text))))
                if (!(file.Path == unitFile.Path && token.Start >= unit.CharacterOffset && token.End <= unit.CharacterOffset + unit.CharacterLength)) throw new TransactionValidationException("身份标签仍有使用者，请先处理：" + file.Path + " · " + token.Text);
            foreach (var (path, changes) in patches)
            {
                var text = graph.Files[path].Text;
                var ranges = changes.Where(p => p.Target.Length == 0).OrderBy(p => p.Offset).ToList(); var merged = new List<TextReplacement>();
                foreach (var range in ranges)
                {
                    if (merged.LastOrDefault() is { } last && range.Offset <= last.Offset + last.Length)
                    {
                        var end = Math.Max(last.Offset + last.Length, range.Offset + range.Length);
                        merged[^1] = new(last.Offset, end - last.Offset, text.Substring(last.Offset, end - last.Offset), "", last.Description);
                    }
                    else merged.Add(range);
                }
                merged.AddRange(changes.Where(c => c.Target.Length > 0 && !merged.Any(m => c.Offset >= m.Offset && c.Offset + c.Length <= m.Offset + m.Length)));
                UnitProjectGraph.Put(root, path, UnitProjectGraph.Patch(text, merged), files, op.Summary);
            }
            var final = new UnitProjectGraph(root, files);
            if (final.Objects.Any(o => o.Name == unit.Name && UnitProjectGraph.Normalize(o.RelativeSourceFile) == unitFile.Path)) throw new TransactionValidationException("删除声明未完成");
            var originalPrefix=graph.Prefix(unit);
            foreach (var file in final.Files.Values)
                if (file.Syntax.Tokens.Any(t => t.Text[0] is not ('\'' or '"') && NdfSyntaxDocument.Leaf(t.Text) == unit.Name
                    && (t.Text==originalPrefix+unit.Name || t.Text==unit.Name && file.Path==unitFile.Path || t.Text=="~/"+unit.Name && graph.Resolve(file.Path,t.Text) is {} resolved && UnitProjectGraph.Same(resolved,unit))))
                    throw new TransactionValidationException("删除后仍有单位引用：" + file.Path);
            // Only the creation token is eligible, and only if unused in all NDF.
            var csvPath = units.Localisation.UniqueUnitsCsvPath;
            if (csvPath is not null && !final.Files.Values.Any(f => f.Syntax.Tokens.Any(t => NdfSyntaxDocument.Unquote(t.Text) == provenance.Token)))
            {
                var relative = Path.GetRelativePath(root, csvPath).Replace('\\', '/');
                var prior = files.SingleOrDefault(f => f.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase));
                var snapshot = TextFileSnapshot.Load(root, relative, FormalTextFileKind.Csv, true);
                var text = prior is null ? snapshot.Text : new StreamReader(new MemoryStream(prior.CandidateBytes), true).ReadToEnd();
                var rows = SemicolonCsvDocument.Parse(text).Rows.Where(r => r.Fields.Count >= 2 && r.Fields[0].Value == provenance.Token).ToArray();
                if (rows.Length == 1)
                {
                    var row = rows[0]; UnitProjectGraph.Put(root, relative, text.Remove(row.Offset, row.Length + row.LineEndLength), files, "移除独占新增名称行", FormalTextFileKind.Csv);
                }
            }
        }
    }
}
