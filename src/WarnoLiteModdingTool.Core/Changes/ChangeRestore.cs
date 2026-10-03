using System.Text.Json;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Changes;

public static class ChangeRestore
{
    public static ChangePreview Prepare(ChangePackage package, string targetRoot, IProgress<ChangeProgress>? progress = null, CancellationToken cancel = default,
        ChangeRestoreOptions? options = null)
    {
        ChangePackageStore.Validate(package); var root = Path.GetFullPath(targetRoot); ChangePaths.NoLinks(root);
        var errors = new List<string>(); var all = new List<RestoreFile>();
        if (!package.Manifest.Complete) errors.Add("记录基础不完整，先补齐基线重新导出 / Incomplete baseline; complete it and export again");
        CheckDrafts(root, errors);
        var journals = ChangeTransactions.List(root);
        if (journals.Any(j => j.State is "Prepared" or "Committing" or "RecoveryRequired")) errors.Add("此目标有未完成还原，请先恢复 / This target has an unfinished restore; recover first");
        var completed = journals.Where(j => j.State == "Completed").ToArray();
        if (completed.Any(j => j.PackageIdentity != package.Identity))
            errors.Add("目标已应用其他记录或另一数值方式，请先恢复或选择干净基础 / Target contains another applied record or numeric policy; recover first or select a clean baseline");
        var previous = completed.Where(j => j.PackageIdentity == package.Identity).ToArray();
        var applied = previous.SelectMany(j => j.Version == 1 ? package.Manifest.Files.Select(f => f.Path) : j.RecordPaths).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var decisions = previous.SelectMany(j => j.Decisions).ToList();
        ChangeTextDecisions.Validate(options?.Decisions ?? []);
        foreach (var decision in options?.Decisions ?? [])
        {
            var c = decision.Conflict;
            if (!package.Manifest.Files.Any(f => f.Path.Equals(c.Path, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("冲突选择来源不在记录中 / Conflict choice source is not in the record");
            var saved = decisions.SingleOrDefault(d => d.Conflict.Path.Equals(c.Path, StringComparison.OrdinalIgnoreCase) && d.Conflict.Token == c.Token && d.Conflict.Column == c.Column);
            if (applied.Contains(c.Path) && saved != decision) errors.Add("已处理的冲突选择不可更改，请先恢复 / Recover before changing a completed conflict choice");
            else { if (saved is not null) decisions.Remove(saved); decisions.Add(decision); }
        }
        var mappings = previous.SelectMany(j => j.Mappings).ToDictionary(m => m.SourcePath, StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in options?.Mappings ?? [])
        {
            ChangePaths.Writable(mapping.TargetPath); ChangePaths.Normalize(mapping.SourcePath);
            if (!package.Manifest.Files.Any(f => f.Path.Equals(mapping.SourcePath, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("对应来源不在记录中 / Mapping source is not in the record");
            if (applied.Contains(mapping.SourcePath) && (!mappings.TryGetValue(mapping.SourcePath, out var old) || !SameMapping(old, mapping)))
                errors.Add("已应用组的对应关系不可更改，请先恢复 / Recover before changing an applied mapping");
            else mappings[mapping.SourcePath] = mapping;
        }
        var targetPaths = package.Manifest.Files.Select(f => mappings.TryGetValue(f.Path, out var map) ? map.TargetPath : f.Path).ToArray();
        if (targetPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targetPaths.Length)
            errors.Add("多个记录文件指向同一目标文件，需合并核对 / Multiple recorded files map to one target file; combined review required");
        using var input = new ChangeCapture.Inputs(root, false);
        var deps = new Dictionary<string, ContentStamp>(StringComparer.OrdinalIgnoreCase);
        var content = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var changedPaths = targetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in input.Paths)
        {
            cancel.ThrowIfCancellationRequested(); var bytes = input.Read(path)!; deps.Add(path, ContentStamp.Of(bytes));
            if (changedPaths.Contains(path) || path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)) content.Add(path, bytes);
        }
        var baseline = previous.FirstOrDefault()?.TargetBaseline ?? deps;
        if (previous.Length > 0)
        {
            var expected = new Dictionary<string, ContentStamp>(baseline, StringComparer.OrdinalIgnoreCase);
            foreach (var journal in previous)
            foreach (var file in journal.Files)
                if (file.After.Presence == FilePresence.Missing) expected.Remove(file.Path); else expected[file.Path] = file.After;
            if (previous.Any(j => j.Version == 1))
            {
                if (previous.SelectMany(j => j.Files).Any(f => ContentStamp.Of(ReadCurrent(root, f.Path)) != f.After))
                    errors.Add("已应用文件后来变化，请先核对 / Applied files changed later; review first");
            }
            else if (expected.Count != deps.Count || expected.Any(p => !deps.TryGetValue(p.Key, out var actual) || actual != p.Value))
                errors.Add("部分还原后目标有外部变化，不能沿用原基础继续 / Target changed externally after partial restore; continuation is blocked");
        }
        for (var i = 0; i < package.Manifest.Files.Count; i++)
        {
            cancel.ThrowIfCancellationRequested(); var file = package.Manifest.Files[i]; progress?.Report(new("还原预览 / Restore preview", i, package.Manifest.Files.Count, file.Path));
            var path = mappings.TryGetValue(file.Path, out var mapping) ? mapping.TargetPath : file.Path;
            var current = content.GetValueOrDefault(path);
            var fileDecisions = decisions.Where(d => d.Conflict.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (applied.Contains(file.Path))
            {
                all.Add(new(path, current, current, fileDecisions.Any(d => d.Choice == TextConflictChoice.KeepTarget) ? "已处理（保留新版项） / Processed with retained values" : "已应用 / Applied",
                    fileDecisions.Select(d => new ChangeDetail(d.Conflict.Token, d.Conflict.Column, d.Conflict.Before, d.Conflict.After, d.Conflict.Target,
                        d.Choice == TextConflictChoice.KeepTarget ? d.Conflict.Target : d.Conflict.After,
                        d.Choice == TextConflictChoice.KeepTarget ? "保留新版（原修改未还原） / Keep target (recorded change omitted)" : "使用记录内容 / Use recorded value")).ToArray()) { SourcePath = file.Path }); continue;
            }
            var merged = mapping is null ? ChangeMerge.Merge(file, package.Before(file), package.After(file), current, package.Manifest.NumericPolicy, fileDecisions)
                : ChangeMerge.MergeMapped(package, file, mapping, current, package.Manifest.NumericPolicy);
            if (mapping is not null && fileDecisions.Length > 0)
                merged = merged with { After = current, Error = "对象对应不支持冲突选择，请清除对应或旧选择 / Object mapping does not support conflict choices; clear the mapping or previous choices" };
            all.Add(merged);
        }
        var rawGroups = ChangeGrouping.Build(package, content, mappings.Values.ToArray(), cancel);
        var requested = options?.SelectedPaths?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested is not null && requested.Any(p => !package.Manifest.Files.Any(f => f.Path.Equals(p, StringComparison.OrdinalIgnoreCase))))
            errors.Add("选择包含未知记录文件 / Selection contains unknown record files");
        var groups = rawGroups.Select((g, i) => new ChangeRestoreGroup((i + 1).ToString(), g.Paths,
            g.Paths.Any(p => !applied.Contains(p)) && (requested is null || g.Paths.Any(requested.Contains)), g.Paths.All(applied.Contains), g.Reason)).ToArray();
        foreach (var group in groups)
        {
            if (group.Paths.Any(applied.Contains) && !group.Applied) errors.Add("依赖组与已应用回执不一致，请先恢复 / Dependency group differs from completed receipts; recover first");
            if (requested is not null && group.Selected && group.Paths.Any(p => !applied.Contains(p) && !requested.Contains(p)))
                errors.Add("不能拆开依赖组 / A dependency group cannot be split: " + group.Id);
        }
        var selected = groups.Where(g => g.Selected).SelectMany(g => g.Paths).Where(p => !applied.Contains(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = all.Where(f => selected.Contains(f.RecordPath)).ToList();
        errors.AddRange(files.Where(f => f.Error is not null).Select(f => f.RecordPath + ": " + f.Error));
        if (errors.Count == 0) ValidateReferences(content, files, errors);
        if (errors.Count == 0)
        {
            try { Units.AviationMovement.ValidateRestored(root, files); }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or ArgumentException) { errors.Add(ex.Message); }
        }
        input.Verify(cancel);
        var allProcessed = errors.Count == 0 && package.Manifest.Files.Count > 0 && applied.Count == package.Manifest.Files.Count;
        return new(root, package.Identity, package.Manifest.NumericPolicy, files, errors, deps, allProcessed && !decisions.Any(d => d.Choice == TextConflictChoice.KeepTarget))
        {
            AllFiles = all, Groups = groups, SelectedPaths = selected.Order(StringComparer.OrdinalIgnoreCase).ToArray(), AppliedPaths = applied.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            DeferredPaths = package.Manifest.Files.Select(f => f.Path).Where(p => !applied.Contains(p) && !selected.Contains(p)).ToArray(),
            Mappings = mappings.Values.ToArray(), Decisions = decisions, AllProcessed = allProcessed, TargetBaseline = baseline, ReceiptState = ChangeTransactions.StateKey(journals), Impacts = Impacts(content, files, cancel)
        };
    }
    private static bool SameMapping(ChangeFileMapping a, ChangeFileMapping b) => a.TargetPath.Equals(b.TargetPath, StringComparison.OrdinalIgnoreCase) &&
        a.Objects.Count == b.Objects.Count && a.Objects.All(p => b.Objects.TryGetValue(p.Key, out var value) && p.Value == value);
    internal static byte[]? ReadCurrent(string root, string path)
    {
        var full = ChangePaths.Resolve(root, path); if (!File.Exists(full)) return null;
        if (new FileInfo(full).Length > ChangePackageStore.MaxFileBytes) throw new IOException("文件过大 / File too large: " + path);
        return File.ReadAllBytes(full);
    }
    private static IReadOnlyList<string> Impacts(Dictionary<string, byte[]> content, IReadOnlyList<RestoreFile> files, CancellationToken cancel)
    {
        var affected = files.SelectMany(f => f.Details).Select(d => d.Object).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
        var reverse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); var locations = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var f in content.Where(f => f.Key.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)))
        {
            cancel.ThrowIfCancellationRequested(); string text;
            try { text = ChangeMerge.DecodeNdf(f.Value); } catch (InvalidDataException) { continue; }
            foreach (var o in new NdfTopLevelScanner().Scan(text, f.Key, "changes").Objects)
            {
                if (!locations.TryGetValue(o.Name, out var paths)) locations[o.Name] = paths = [];
                paths.Add(f.Key);
                var doc = new NdfSyntaxDocument(text, o.CharacterOffset, o.CharacterLength);
                foreach (var t in doc.Tokens.Where(t => t.Text.StartsWith("~/", StringComparison.Ordinal) || t.Text.StartsWith("$/", StringComparison.Ordinal)))
                {
                    var name = NdfSyntaxDocument.Leaf(t.Text); if (name == o.Name) continue;
                    if (!reverse.TryGetValue(name, out var owners)) reverse[name] = owners = [];
                    owners.Add(o.Name);
                }
            }
        }
        var queue = new Queue<string>(affected); var rows = new HashSet<string>();
        while (queue.TryDequeue(out var name))
        foreach (var owner in reverse.GetValueOrDefault(name) ?? [])
        {
            rows.Add(name + " ← " + owner + " · " + string.Join(", ", locations[owner].Order()));
            if (affected.Add(owner)) queue.Enqueue(owner);
        }
        return rows.Order(StringComparer.Ordinal).ToArray();
    }
    internal static void CheckDrafts(string root, List<string> errors)
    {
        foreach (var relative in new[] { ".warno-editor/draft-v1.json", ".warno-editor/v2/session.json" })
        {
            var path = ChangePaths.Resolve(root, relative); if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                if (!doc.RootElement.TryGetProperty("operations", out var ops) || ops.ValueKind != JsonValueKind.Array || ops.GetArrayLength() > 0)
                    errors.Add("目标有未应用或未知草稿，请保留并使用新基础 / Target has pending or unknown drafts; preserve them and use a new baseline");
            }
            catch (JsonException) { errors.Add("目标草稿不可识别 / Target draft is unreadable"); }
        }
    }
    // Validate changed direct references and every reference to removed declarations.
    // Namespace ambiguity is a conflict rather than a leaf-name guess.
    private static void ValidateReferences(Dictionary<string, byte[]> original, List<RestoreFile> files, List<string> errors)
    {
        var final = new Dictionary<string, byte[]>(original, StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) { if (f.After is null) final.Remove(f.Path); else final[f.Path] = f.After; }
        Dictionary<string, HashSet<string>> Definitions(Dictionary<string, byte[]> set)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var f in set.Where(f => f.Key.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)))
            {
                var doc = new NdfSyntaxDocument(ChangeMerge.DecodeNdf(f.Value));
                for (var i = 0; i + 1 < doc.Tokens.Count; i++)
                    if (doc.Tokens[i + 1].Text == "is")
                    {
                        var name = doc.Tokens[i].Text;
                        if (!result.TryGetValue(name, out var paths)) result[name] = paths = new(StringComparer.OrdinalIgnoreCase);
                        paths.Add(f.Key);
                    }
            }
            return result;
        }
        try
        {
            var beforeDefs = Definitions(original); var afterDefs = Definitions(final);
            var oldQualifiedReferences = new HashSet<string>(StringComparer.Ordinal); var exportedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in original.Where(f => f.Key.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)))
            {
                var syntax = new NdfSyntaxDocument(ChangeMerge.DecodeNdf(source.Value));
                foreach (var t in syntax.Tokens) { if (t.Text == "export") exportedPaths.Add(source.Key); if (t.Text.StartsWith("$/", StringComparison.Ordinal)) oldQualifiedReferences.Add(t.Text); }
            }
            Dictionary<string, int> Guids(Dictionary<string, byte[]> set) => set.Where(f => f.Key.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase))
                .SelectMany(f => new NdfSyntaxDocument(ChangeMerge.DecodeNdf(f.Value)).Tokens.Where(t => t.Text.Trim('\'', '"').StartsWith("GUID:{", StringComparison.OrdinalIgnoreCase)).Select(t => t.Text.Trim('\'', '"')))
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var oldGuids = Guids(original);
            foreach (var guid in Guids(final).Where(g => g.Value > 1 && g.Value > oldGuids.GetValueOrDefault(g.Key))) errors.Add("新增 GUID 重复 / Added GUID collision: " + guid.Key);
            foreach (var pair in final.Where(f => f.Key.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)))
            {
                var changed = files.FirstOrDefault(f => f.Path.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                var oldTokens = original.TryGetValue(pair.Key, out var old) ? new NdfSyntaxDocument(ChangeMerge.DecodeNdf(old)).Tokens.Select(t => t.Text).ToHashSet() : [];
                var doc = new NdfSyntaxDocument(ChangeMerge.DecodeNdf(pair.Value));
                foreach (var token in doc.Tokens.Where(t => t.Text.StartsWith("~/", StringComparison.Ordinal) || t.Text.StartsWith("$/", StringComparison.Ordinal)))
                {
                    var name = NdfSyntaxDocument.Leaf(token.Text);
                    var removed = beforeDefs.ContainsKey(name) && !afterDefs.ContainsKey(name);
                    var addedReference = changed is not null && !oldTokens.Contains(token.Text);
                    if (!removed && !addedReference) continue;
                    if (!afterDefs.TryGetValue(name, out var targets)) { errors.Add("引用不可达 / Unresolved reference: " + pair.Key + " → " + token.Text); continue; }
                    if (token.Text.StartsWith("$/", StringComparison.Ordinal))
                    {
                        var evidence = targets.Any(exportedPaths.Contains);
                        // Existing exact qualified references elsewhere establish the binding;
                        // entirely new qualified scopes need an explicit namespace adapter.
                        var binding = oldQualifiedReferences.Contains(token.Text);
                        if (!evidence || !binding) errors.Add("新增限定引用需核对命名空间 / New qualified reference requires namespace review: " + token.Text);
                    }
                    else if (token.Text[2..].Contains('/') || targets.Count != 1 && !targets.Contains(pair.Key))
                        errors.Add("引用范围多义 / Ambiguous reference scope: " + token.Text);
                }
            }
            foreach (var f in files.Where(f => f.After is not null && f.Path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)))
            {
                var doc = new NdfSyntaxDocument(ChangeMerge.DecodeNdf(f.After!));
                foreach (var type in new[] { "TAmmunitionDescriptor", "TAmmunitionMissileDescriptor" })
                foreach (var ammo in doc.FindConstructors(type))
                foreach (var suffix in new[] { "GRU", "HelicopterGRU", "AirplaneGRU", "ProjectileGRU" })
                {
                    var min = doc.FindDirectAssignments(ammo, "MinimumRange" + suffix); var max = doc.FindDirectAssignments(ammo, "MaximumRange" + suffix);
                    if (min.Count == 1 && max.Count == 1 && decimal.TryParse(doc.Raw(min[0]), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) &&
                        decimal.TryParse(doc.Raw(max[0]), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) && (a < 0 || b < 0 || b > 0 && a > b))
                        errors.Add("射程组合无效 / Invalid range combination: " + f.Path);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { errors.Add(ex.Message); }
    }
}
