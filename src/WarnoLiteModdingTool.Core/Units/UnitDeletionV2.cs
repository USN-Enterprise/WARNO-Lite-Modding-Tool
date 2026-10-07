using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Units;

public sealed record UnitDeleteImpact(string Key, string File, string Owner, string Raw, string? Field,
    bool Automatic, bool CanReplace, string Description, int Offset, int Length,
    TextReplacement? Removal = null, int? SerializerId = null, bool Unverified = false);
public sealed record UnitDeleteAnalysis(IReadOnlyList<UnitDeleteImpact> Uses, string[] Coverage, string Review);
public sealed class UnitDeleteResult
{
    public List<string> Warnings { get; } = [];
    public HashSet<string> AllowedUnreachable { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int[]> RemovedIds { get; } = [];
}

// The old v1 planner is intentionally retained: old drafts never acquire force semantics.
public static class UnitDeletionV2
{
    public const string Keep = "!keep";
    public static bool IsNew(DraftOperation op) => op.TargetKind == DraftTargetKind.UnitDelete && UnitDeletion.Read(op).Version == 2;
    public static string GuidOf(string body)
    {
        var doc = new NdfSyntaxDocument(body); var constructors = doc.FindConstructors("TEntityDescriptor");
        if (constructors.Count != 1) throw new TransactionValidationException("单位声明不唯一 / Ambiguous unit declaration");
        var fields = doc.FindDirectAssignments(constructors[0], "DescriptorId");
        return fields.Count == 1 ? doc.Raw(fields[0]) : "";
    }
    public static UnitDeleteAnalysis Analyze(UnitProjectGraph graph, NdfObjectInfo unit)
    {
        var path = UnitProjectGraph.Normalize(unit.RelativeSourceFile);
        if (unit.TypeName != "TEntityDescriptor" || graph.UnverifiedFiles.Contains(path))
            throw new TransactionValidationException("目标单位结构未验证 / Target unit structure unverified");
        var uses = new List<UnitDeleteImpact>(); var counts = new Dictionary<string, int>();
        var tags = UnitCapabilities.ReadList(graph.Body(unit), "TTagsModuleDescriptor", "TagSet", true).Where(t => t.StartsWith("UNITE_", StringComparison.Ordinal)).ToHashSet();
        var registrations = graph.Files.Values.Where(f => !graph.UnverifiedFiles.Contains(f.Path))
            .SelectMany(f => f.Syntax.FindConstructors("TDeckSerializerEntries").SelectMany(c => f.Syntax.FindDirectAssignments(c, "UnitIds"))
                .SelectMany(map => f.Syntax.ReadMapEntries(map).Select(e => (File: f, Map: map, Entry: e)))).ToArray();
        foreach (var file in graph.Files.Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        foreach (var token in file.Syntax.Tokens)
        {
            if (file.Path == path && token.Start >= unit.CharacterOffset && token.End <= unit.CharacterOffset + unit.CharacterLength) continue;
            var quoted = token.Text.StartsWith('"') || token.Text.StartsWith('\'');
            if (quoted ? !tags.Contains(NdfSyntaxDocument.Unquote(token.Text)) : !Regex.IsMatch(token.Text, "(?<![A-Za-z0-9_])" + Regex.Escape(unit.Name) + "(?![A-Za-z0-9_])")) continue;
            // A declaration of the same leaf in another namespace is not a use.
            if (!quoted && file.Objects.Any(o => o.Name == token.Text && token.Start >= o.CharacterOffset && token.End <= o.CharacterOffset + o.CharacterLength && token.Start < o.CharacterOffset + o.Name.Length + 20))
            {
                var index = file.Syntax.Tokens.ToList().FindIndex(t => t.Start == token.Start);
                if (index + 1 < file.Syntax.Tokens.Count && file.Syntax.Tokens[index + 1].Text == "is") continue;
            }
            var owner = file.Objects.FirstOrDefault(o => token.Start >= o.CharacterOffset && token.End <= o.CharacterOffset + o.CharacterLength);
            var resolved = quoted ? null : graph.Resolve(file.Path, token.Text);
            if (resolved is not null && !UnitProjectGraph.Same(resolved, unit)) continue;
            var certain = !quoted && UnitProjectGraph.Same(resolved, unit) && !graph.UnverifiedFiles.Contains(file.Path);
            var reference = new UnitProjectGraph.Reference(file, token, owner);
            var context = certain ? UnitDeletion.Context(reference) : default;
            if (context.Field == "UnitRule" && context.Element is { } ruleElement)
            {
                var rules = file.Syntax.FindConstructors("TDeckUniteRule", ruleElement);
                if (rules.Count != 1 || ruleElement.StartTokenIndex != rules[0].TypeTokenIndex || ruleElement.EndTokenIndex != rules[0].CloseTokenIndex)
                    context = ("UnitRule", null, null);
            }
            TextReplacement? removal = context.Array is not null && context.Element is not null ? UnitProjectGraph.RemoveElement(file.Syntax, context.Array, context.Element, "清理引用 / Remove reference") : null;
            int? serializerId = null;
            var field = quoted ? "UNITE" : context.Field;
            var registration = registrations.Where(r => r.File.Path == file.Path && file.Syntax.StartOffset(r.Entry.Key) == token.Start).ToArray();
            if (registration.Length == 1)
            {
                field = "UnitIds"; var r = registration[0];
                var sameKeys = registrations.Count(x => UnitProjectGraph.Same(graph.Resolve(x.File.Path, x.File.Syntax.Raw(x.Entry.Key)), unit) || x.File.Syntax.Raw(x.Entry.Key) == unit.Name);
                var uniqueLeaf = graph.FindObjects(unit.Name).Count == 1;
                if ((certain || uniqueLeaf && token.Text == unit.Name) && sameKeys == 1 && int.TryParse(file.Syntax.Raw(r.Entry.Value), out var id) &&
                    registrations.Count(x => int.TryParse(x.File.Syntax.Raw(x.Entry.Value), out var n) && n == id) == 1)
                {
                    var element = file.Syntax.ReadArrayElements(r.Map).Single(e => file.Syntax.StartOffset(e) < token.Start && file.Syntax.StartOffset(e) + file.Syntax.Length(e) > file.Syntax.StartOffset(r.Entry.Value));
                    removal = UnitProjectGraph.RemoveElement(file.Syntax, r.Map, element, "移除注册 / Remove registration"); serializerId = id; certain = true;
                }
                else { certain = false; removal = null; }
            }
            var replace = certain && field is "Unit" or "Transport" or "UpgradeFromUnit";
            var stem = file.Path + "|" + (owner?.Name ?? "") + "|" + field + "|" + token.Text;
            var ordinal = counts.GetValueOrDefault(stem); counts[stem] = ordinal + 1;
            uses.Add(new(stem + "|" + ordinal, file.Path, owner?.Name ?? "", token.Text, field, removal is not null, replace,
                (owner?.Name ?? "UnitIds") + " · " + (field ?? "未知引用 / Unknown reference") + " · " + token.Text,
                token.Start, token.Length, removal, serializerId, !certain));
        }
        var coverage = graph.CoverageIssues.Order(StringComparer.Ordinal).ToArray();
        // Store readable evidence, including each owner once, to invalidate choices when its structure changes.
        var owners = uses.Select(u => (u.File, u.Owner)).Distinct().Select(p => new { p.File, p.Owner,
            Text = p.Owner.Length == 0 ? graph.Files[p.File].Text : graph.Body(graph.RequireObject(p.File, p.Owner)) }).ToArray();
        var review = JsonSerializer.Serialize(new { Uses = uses.Select(u => new { u.Key, u.Automatic, u.CanReplace, u.Unverified, u.SerializerId }), Coverage = coverage, Owners = owners });
        return new(uses, coverage, review);
    }
    public static DraftOperation Operation(UnitRecord unit, UnitProjectGraph graph, bool force = false, Dictionary<string, string>? choices = null)
    {
        var obj = graph.RequireObject(unit.Source.RelativeSourceFile, unit.Name); var analysis = Analyze(graph, obj);
        var body = UnitCreation.Source(unit);
        if (graph.Body(obj) != body) throw new TransactionValidationException("单位已变化，请重新加载 / Unit changed; reload");
        var state = new UnitDeleteState(2, GuidOf(body), choices ?? [], force, analysis.Review);
        var label = force ? "强行删除 / Force delete" : "删除单位 / Delete unit";
        return new(DraftOperation.CreateId(DraftTargetKind.UnitDelete, unit.Source.RelativeSourceFile, unit.Name, "unit.delete"), "delete:" + unit.Name,
            DraftTargetKind.UnitDelete, "units", unit.Source.RelativeSourceFile, unit.Name, unit.Source.TypeName, "unit.delete", "Unit/Delete", "UnitDelete",
            unit.Name, body, label, JsonSerializer.Serialize(state), label + " · " + unit.DisplayName + " · " + unit.Name, unit.NameToken, false, DateTimeOffset.UtcNow);
    }
    public static UnitDeleteResult Plan(string root, UnitWorkspaceData units, IReadOnlyList<DraftOperation> operations, List<PlannedFileChange> files)
    {
        var result = new UnitDeleteResult();
        foreach (var op in operations.Where(IsNew))
        {
            if (operations.Any(o => o.TargetKind == DraftTargetKind.UnitCreate && UnitCreation.Read(o).Mother == op.ObjectName))
                throw new TransactionValidationException("请先处理以此单位为母版的创建草稿 / Resolve dependent creation first");
            if (UnitCreationHistory.Load(root).DeletedUnits?.Any(d => d.Source == op.RelativeSourceFile && d.Name == op.ObjectName && d.DraftUtc == op.UpdatedUtc) == true)
                throw new TransactionValidationException("该删除草稿已提交，请清理旧草稿 / This deletion was already committed; clear the old draft");
            var state = UnitDeletion.Read(op); var original = new UnitProjectGraph(root, deletionScope: true);
            var originalUnit = original.RequireObject(op.RelativeSourceFile, op.ObjectName);
            if (original.Body(originalUnit) != op.BaselineRaw) throw new TransactionValidationException("删除基线已变化 / Deletion baseline changed");
            var originalAnalysis = Analyze(original, originalUnit);
            if (state.Review != originalAnalysis.Review) throw new TransactionValidationException("删除影响已变化，请撤销删除后重新预览 / Deletion impact changed; undo and review again");
            if (state.Replacements.Keys.Any(k => !originalAnalysis.Uses.Any(u => u.Key == k))) throw new TransactionValidationException("删除处置已失效 / Stale deletion choice");
            var graph = new UnitProjectGraph(root, files, deletionScope: true); var unit = graph.RequireObject(op.RelativeSourceFile, op.ObjectName);
            if (graph.Body(unit) != op.BaselineRaw) throw new TransactionValidationException("其他草稿改变了删除目标 / Another draft changes the deletion target");
            var analysis = Analyze(graph, unit);
            if (analysis.Uses.Any(u => !originalAnalysis.Uses.Any(old => old.Key == u.Key && old.Automatic == u.Automatic && old.CanReplace == u.CanReplace)))
                throw new TransactionValidationException("组合草稿改变了删除引用，请先应用关联修改 / Combined drafts change deletion references");
            if (!state.Force && analysis.Coverage.Length > 0) throw new TransactionValidationException(string.Join("\n", analysis.Coverage));
            result.Warnings.AddRange(analysis.Coverage.Select(s => "强行删除未验证 / Force deletion unverified: " + s));
            var patches = new Dictionary<string, List<TextReplacement>>(StringComparer.OrdinalIgnoreCase);
            void Add(string path, TextReplacement change) { if (!patches.TryGetValue(path, out var list)) patches[path] = list = []; list.Add(change); }
            Add(UnitProjectGraph.Normalize(unit.RelativeSourceFile), new(unit.CharacterOffset, unit.CharacterLength, graph.Body(unit), "", "删除单位 / Delete unit"));
            var removedIds = new List<int>();
            foreach (var use in analysis.Uses)
            {
                var choice = state.Replacements.GetValueOrDefault(use.Key);
                // A containing rule may already be removed. Do not replace a nested transport in it.
                if (analysis.Uses.Any(parent => parent.File == use.File && parent.Removal is { } r && state.Replacements.GetValueOrDefault(parent.Key) != Keep && r.Offset <= use.Offset && r.Offset + r.Length >= use.Offset + use.Length && parent.Key != use.Key)) continue;
                if (choice != Keep && use.Removal is { } removal)
                { Add(use.File, removal); if (use.SerializerId is { } id) removedIds.Add(id); continue; }
                if (choice is not null && choice != Keep && choice.Length > 0)
                {
                    if (!use.CanReplace) throw new TransactionValidationException("此引用不能自动替换 / This reference cannot be replaced: " + use.Description);
                    var target = graph.Resolve(use.File, choice);
                    if (target?.TypeName != "TEntityDescriptor" || UnitProjectGraph.Same(target, unit) || operations.Any(o => o.TargetKind == DraftTargetKind.UnitDelete && o.ObjectName == target.Name && o.RelativeSourceFile == target.RelativeSourceFile))
                        throw new TransactionValidationException("替代单位不存在、多义或待删除 / Invalid replacement: " + choice);
                    if (use.Field == "Transport" && !units.Units.Any(u => u.Name == target.Name && u.Source.RelativeSourceFile == target.RelativeSourceFile && u.HasUniqueTransporterModule))
                        throw new TransactionValidationException("替代运输必须有唯一运输模块 / Replacement needs a unique transport module");
                    Add(use.File, new(use.Offset, use.Length, use.Raw, choice, "替代单位引用 / Replace unit reference")); continue;
                }
                if (!state.Force) throw new TransactionValidationException("请先解除或替换引用 / Resolve reference first: " + use.File + " · " + use.Description);
                result.Warnings.Add((use.Unverified ? "强行删除未验证 / Force deletion unverified: " : "强行删除残留 / Force deletion remaining reference: ") + use.File + " · " + use.Description);
                result.AllowedUnreachable.Add(use.File + "|" + use.Raw);
            }
            foreach (var (path, changes) in patches)
            {
                if (graph.UnverifiedFiles.Contains(path)) throw new TransactionValidationException("不能修改未验证文件 / Cannot patch unverified file: " + path);
                var text = graph.Files[path].Text; var merged = new List<TextReplacement>();
                foreach (var r in changes.Where(c => c.Target.Length == 0).OrderBy(c => c.Offset))
                {
                    if (merged.LastOrDefault() is { } last && r.Offset <= last.Offset + last.Length)
                    { var end = Math.Max(r.Offset + r.Length, last.Offset + last.Length); merged[^1] = new(last.Offset, end - last.Offset, text.Substring(last.Offset, end - last.Offset), "", last.Description); }
                    else merged.Add(r);
                }
                merged.AddRange(changes.Where(c => c.Target.Length > 0 && !merged.Any(m => c.Offset >= m.Offset && c.Offset + c.Length <= m.Offset + m.Length)));
                UnitProjectGraph.Put(root, path, UnitProjectGraph.Patch(text, merged), files, op.Summary);
            }
            result.RemovedIds[op.Id] = removedIds.ToArray();
            // Provenance is optional for v2; only verified newly-created exclusive names can be removed.
            CreatedUnitIdentity? provenance = null;
            try { provenance = UnitCreationHistory.Require(original, op.RelativeSourceFile, op.ObjectName); } catch (Exception ex) when (ex is TransactionValidationException or InvalidDataException) { }
            var final = new UnitProjectGraph(root, files, deletionScope: true);
            if (final.FindObjects(op.ObjectName).Any(o => UnitProjectGraph.Normalize(o.RelativeSourceFile) == UnitProjectGraph.Normalize(op.RelativeSourceFile))) throw new TransactionValidationException("删除声明未完成 / Unit declaration still exists");
            if (provenance is not null && final.CoverageIssues.Count == 0 && units.Localisation.UniqueUnitsCsvPath is { } csv && !final.Files.Values.Any(f => f.Syntax.Tokens.Any(t => NdfSyntaxDocument.Unquote(t.Text) == provenance.Token)))
            {
                var path = Path.GetRelativePath(root, csv).Replace('\\', '/'); var prior = files.SingleOrDefault(f => f.RelativePath == path);
                var text = prior is null ? TextFileSnapshot.Load(root, path, FormalTextFileKind.Csv).Text : new StreamReader(new MemoryStream(prior.CandidateBytes), true).ReadToEnd();
                var rows = SemicolonCsvDocument.Parse(text).Rows.Where(r => r.Fields.Count >= 2 && r.Fields[0].Value == provenance.Token).ToArray();
                if (rows.Length == 1) { var row = rows[0]; UnitProjectGraph.Put(root, path, text.Remove(row.Offset, row.Length + row.LineEndLength), files, "移除独占新增名称行", FormalTextFileKind.Csv); }
            }
        }
        return result;
    }
}
