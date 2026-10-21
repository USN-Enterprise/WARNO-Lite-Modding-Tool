using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public sealed record MissileRepairProgress(string Stage, int Completed, int Total);

public static partial class MissileDebugRepair
{
    // One short-lived analysis per read scope. Whole-file syntax is released after each file;
    // only identity summaries, missile bodies and commit dependencies survive the pass.
    public sealed class Analysis
    {
        private readonly List<Entry> _entries = [];
        private readonly Dictionary<string, int> _names = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, int>> _guids = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NdfObjectInfo[]> _objects = new(StringComparer.OrdinalIgnoreCase);
        private MissileDebugRepairScan? _scan;
        private bool _users;
        public string Root { get; }
        public Dictionary<string, byte[]> Dependencies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings { get; } = [];
        public int ParsedFiles { get; private set; }
        public int ReferencePasses { get; private set; }
        public int ValidatedOperations { get; private set; }
        private Analysis(string root) { Root = Path.GetFullPath(root); }
        public static Analysis Load(string root, CancellationToken token = default, IProgress<MissileRepairProgress>? progress = null)
        {
            var result = new Analysis(root); var catalog = SourceFiles.Discover(root, token);
            result.Warnings.AddRange(catalog.Errors);
            var files = catalog.Files.Where(f => SourceFiles.IsNdf(f.Path) && !Excluded(f.Path)).ToArray();
            var completed = 0;
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(new("读取文件 / Reading files", completed++, files.Length));
                try
                {
                    var source = TextFileSnapshot.Load(root, file.Path, FormalTextFileKind.Ndf);
                    result.Dependencies[file.Path] = source.OriginalBytes;
                    var parsed = result.Parse(file.Path, source.Text, token);
                    result._entries.AddRange(parsed.Entries); result._objects[file.Path] = parsed.Objects; result._guids[file.Path] = parsed.Guids;
                    foreach (var obj in parsed.Objects) result._names[obj.Name] = result._names.GetValueOrDefault(obj.Name) + 1;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TransactionValidationException)
                { result.Warnings.Add(file.Path + " · " + ex.Message); }
            }
            token.ThrowIfCancellationRequested();
            progress?.Report(new("分析身份 / Checking identities", completed, files.Length));
            return result;
        }
        internal static Analysis FromGraph(UnitProjectGraph graph)
        {
            var result = new Analysis(graph.Root); result.Warnings.AddRange(graph.CoverageIssues);
            foreach (var file in graph.Files.Values)
            {
                result.Dependencies[file.Path] = graph.Dependencies[file.Path];
                result._objects[file.Path] = file.Objects.ToArray();
                result._guids[file.Path] = Guids(file.Syntax);
                foreach (var obj in file.Objects) result._names[obj.Name] = result._names.GetValueOrDefault(obj.Name) + 1;
                result._entries.AddRange(ReadEntries(file.Path, file.Text, file.Syntax, file.Objects));
            }
            return result;
        }
        internal static bool Excluded(string path) => path.StartsWith(".warno-editor/", StringComparison.OrdinalIgnoreCase) || path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
        private sealed record Parsed(Entry[] Entries, NdfObjectInfo[] Objects, Dictionary<string, int> Guids);
        private Parsed Parse(string path, string text, CancellationToken token)
        {
            var doc = new NdfSyntaxDocument(text, cancellationToken: token); ParsedFiles++;
            var scan = new NdfTopLevelScanner().Scan(text, Path.Combine(Root, path), "weapons", Root, token);
            if (scan.Diagnostics.Any(d => d.Severity == NdfDiagnosticSeverity.Error))
                throw new TransactionValidationException("NDF结构未验证 / NDF structure unverified: " + path);
            var starts = doc.Tokens.Select(t => t.Start).ToHashSet();
            var objects = scan.Objects.Where(o => starts.Contains(o.CharacterOffset)).ToArray();
            return new(ReadEntries(path, text, doc, objects, token), objects, Guids(doc));
        }
        private static int Lower(IReadOnlyList<NdfSyntaxDocument.NdfToken> tokens, int offset)
        {
            var lo = 0; var hi = tokens.Count;
            while (lo < hi) { var mid = (lo + hi) / 2; if (tokens[mid].Start < offset) lo = mid + 1; else hi = mid; }
            return lo;
        }
        private static Entry[] ReadEntries(string path, string text, NdfSyntaxDocument doc, IReadOnlyList<NdfObjectInfo> objects, CancellationToken token = default)
        {
            var entries = new List<Entry>();
            foreach (var obj in objects.Where(o => o.TypeName == "TEntityDescriptor"))
            {
                token.ThrowIfCancellationRequested();
                var start = Lower(doc.Tokens, obj.CharacterOffset); var end = Lower(doc.Tokens, obj.CharacterOffset + obj.CharacterLength) - 1;
                var roots = doc.FindConstructors("TEntityDescriptor", start, end);
                var debug = "";
                if (roots.Count == 1)
                {
                    var fields = doc.FindDirectAssignments(roots[0], "ClassNameForDebug");
                    if (fields.Count == 1 && fields[0].StartTokenIndex == fields[0].EndTokenIndex)
                    {
                        var raw = doc.Raw(fields[0]);
                        if (raw.Length >= 3 && raw[0] is '\'' or '"' && raw[^1] == raw[0]) debug = NdfSyntaxDocument.Unquote(raw);
                    }
                }
                var missile = doc.FindConstructors("TGuidedMissileMovementModuleDescriptor", start, end).Count > 0;
                entries.Add(new(obj, missile ? text.Substring(obj.CharacterOffset, obj.CharacterLength) : "", debug, missile));
            }
            return entries.ToArray();
        }
        private static Dictionary<string, int> Guids(NdfSyntaxDocument doc) => doc.Tokens.Where(t => t.Text.StartsWith("GUID:", StringComparison.Ordinal) && Guid.TryParse(t.Text[5..], out _))
            .GroupBy(t => Guid.Parse(t.Text[5..]).ToString("D"), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Reserved() => _entries.Where(e => e.Debug.Length > 0).Select(e => e.Debug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        public MissileDebugRepairScan Scan(bool users = true, CancellationToken token = default, IProgress<MissileRepairProgress>? progress = null)
        {
            if (_scan is null)
            {
                var entries = _entries.ToArray(); var reserved = Reserved(); var rows = new List<MissileDebugRepairRow>();
                var duplicates = entries.Where(e => e.Debug.Length > 0).GroupBy(e => e.Debug).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                foreach (var e in entries.Where(e => e.Missile && duplicates.Contains(e.Debug)).OrderBy(e => e.Source.Name, StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    var origin = ""; var after = ""; var reason = "原实体或非工具副本，保留 / Original or non-tool entity; retained";
                    try
                    {
                        if (_names.GetValueOrDefault(e.Source.Name) != 1) throw new TransactionValidationException("对象名称多义 / Ambiguous declaration name");
                        if (Origin(e, entries) is { } source) { origin = source.Source.Name; after = Allocate(e.Source.Name, reserved); reason = "已确认重名工具副本；仅修改debug名称 / Confirmed duplicate tool clone; debug name only"; }
                        else if (System.Text.RegularExpressions.Regex.IsMatch(e.Source.Name, @"_WLMT_[a-fA-F0-9]{10}$")) reason = "来源缺失或结构不同，不自动修复 / Missing origin or different structure; no automatic repair";
                    }
                    catch (Exception ex) when (ex is TransactionValidationException or InvalidOperationException) { reason = ex.Message; }
                    rows.Add(new(UnitProjectGraph.Normalize(e.Source.RelativeSourceFile), e.Source.Name, e.Debug, after, origin, after.Length > 0, reason, ""));
                }
                var warnings = Warnings.ToList();
                if (entries.Any(e => e.Missile && e.Debug.Length == 0)) warnings.Add("部分导弹debug名称缺失或不明确，未自动处理 / Some missile debug names are unsupported and were retained");
                _scan = new(rows, warnings);
            }
            if (users && !_users)
            {
                var names = _scan.Rows.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
                var refs = names.ToDictionary(n => n, _ => new HashSet<string>(), StringComparer.Ordinal); ReferencePasses++;
                var completed = 0;
                foreach (var file in Dependencies)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(new("分析引用 / Checking references", completed++, Dependencies.Count));
                    var text = Encoding.UTF8.GetString(file.Value);
                    if (!names.Any(n => text.Contains(n, StringComparison.Ordinal))) continue;
                    var doc = new NdfSyntaxDocument(text, cancellationToken: token);
                    var objects = _objects[file.Key]; var ownerIndex = 0;
                    foreach (var t in doc.Tokens)
                    {
                        token.ThrowIfCancellationRequested();
                        if (t.Text[0] is '\'' or '"') continue;
                        var name = NdfSyntaxDocument.Leaf(t.Text); if (!names.Contains(name)) continue;
                        while (ownerIndex < objects.Length && objects[ownerIndex].CharacterOffset + objects[ownerIndex].CharacterLength <= t.Start) ownerIndex++;
                        var owner = ownerIndex < objects.Length && objects[ownerIndex].CharacterOffset <= t.Start ? objects[ownerIndex] : null;
                        if (owner?.Name == name) continue;
                        refs[name].Add(file.Key + " · " + (owner?.Name ?? t.Text));
                    }
                }
                _scan = _scan with { Rows = _scan.Rows.Select(r => r with { Users = string.Join("\n", refs[r.Name].Order(StringComparer.Ordinal)) }).ToArray() }; _users = true;
            }
            return _scan;
        }
        private Entry Require(string file, string name) => _entries.SingleOrDefault(e => e.Source.Name == name && UnitProjectGraph.Normalize(e.Source.RelativeSourceFile).Equals(UnitProjectGraph.Normalize(file), StringComparison.OrdinalIgnoreCase))
            ?? throw new TransactionValidationException("导弹实体不存在或多义 / Missing or ambiguous missile: " + name);
        public IReadOnlyList<DraftOperation> Prepare(IReadOnlyList<string> selected, CancellationToken token = default)
        {
            if (Warnings.Count > 0) throw new TransactionValidationException("无法完整核对debug名称 / Incomplete debug scan:\n" + string.Join("\n", Warnings));
            var rows = Scan(false, token).Rows; var ops = new List<DraftOperation>();
            foreach (var name in selected.Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested(); var row = rows.SingleOrDefault(r => r.Name == name && r.CanRepair) ?? throw new TransactionValidationException("该副本不再可修复，请重新扫描 / Clone no longer repairable; scan again: " + name);
                var entity = Require(row.File, name); var origin = Require(row.File, row.Origin);
                var state = new MissileDebugRepairState(row.File, name, entity.Body, row.File, row.Origin, origin.Body, row.Before, row.After);
                ops.Add(new(DraftOperation.CreateId(DraftTargetKind.MissileDebugRepair, row.File, name, Key), "missile-debug-repair", DraftTargetKind.MissileDebugRepair,
                    "weapons", row.File, name, "TEntityDescriptor", Key, "TEntityDescriptor.ClassNameForDebug", "MissileDebugRepairV1", row.Before, state.Body,
                    row.After, System.Text.Json.JsonSerializer.Serialize(state), "修复导弹副本debug名称 / Repair missile clone debug name · " + name, null, false, DateTimeOffset.UtcNow, EditScope: DraftEditScope.AllReferences));
            }
            if (ops.Count == 0) throw new TransactionValidationException("请先勾选可修复的导弹副本 / Select repairable missile clones first");
            return ops;
        }
        public void Validate(DraftOperation op, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); ValidatedOperations++; var s = Read(op);
            if (!IsRepair(op) || op.FieldKey != Key || op.ObjectType != "TEntityDescriptor" || op.FieldPath != "TEntityDescriptor.ClassNameForDebug" || op.ValueType != "MissileDebugRepairV1" ||
                s.Name != op.ObjectName || s.File != UnitProjectGraph.Normalize(op.RelativeSourceFile) || s.Body != op.BaselineRaw || s.Before != op.BaselineValue || s.After != op.TargetValue || Require(s.File, s.Name).Body != s.Body || Require(s.OriginFile, s.Origin).Body != s.OriginBody)
                throw new TransactionValidationException("导弹修复基线已变化，请重新扫描 / Missile repair baseline changed; scan again");
            var row = Scan(false, token).Rows.SingleOrDefault(r => r.Name == s.Name && r.CanRepair);
            if (row is null || row.After != s.After || row.Origin != s.Origin || row.Before != s.Before) throw new TransactionValidationException("导弹修复目标或唯一性已变化 / Missile repair target or uniqueness changed");
        }
        public WeaponPlannedReplacement Replacement(DraftOperation op, CancellationToken token = default)
        {
            Validate(op, token); var s = Read(op); var entity = Require(s.File, s.Name); var doc = new NdfSyntaxDocument(s.Body, cancellationToken: token); var span = DebugSpan(doc);
            return new(s.File, new(entity.Source.CharacterOffset + doc.StartOffset(span), doc.Length(span), doc.Raw(span), "'" + s.After + "'", op.Summary), op.Summary);
        }
        public void ValidateFinal(IReadOnlyList<PlannedFileChange> files, CancellationToken token = default)
        {
            var changes = files.Where(f => f.Kind == FormalTextFileKind.Ndf).ToDictionary(f => UnitProjectGraph.Normalize(f.RelativePath), StringComparer.OrdinalIgnoreCase);
            var after = _entries.Where(e => !changes.ContainsKey(UnitProjectGraph.Normalize(e.Source.RelativeSourceFile))).ToList();
            var candidateNames = _names.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            var guids = _guids.Where(p => !changes.ContainsKey(p.Key)).SelectMany(p => p.Value).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Sum(p => p.Value), StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes.Values)
            {
                token.ThrowIfCancellationRequested();
                foreach (var old in _objects.GetValueOrDefault(change.RelativePath) ?? []) candidateNames[old.Name]--;
                if (change.Action == PlannedFileAction.Delete) continue;
                var parsed = Parse(change.RelativePath, Encoding.UTF8.GetString(change.CandidateBytes), token); after.AddRange(parsed.Entries);
                foreach (var obj in parsed.Objects) candidateNames[obj.Name] = candidateNames.GetValueOrDefault(obj.Name) + 1;
                foreach (var id in parsed.Guids) guids[id.Key] = guids.GetValueOrDefault(id.Key) + id.Value;
            }
            var debugCounts = after.Where(e => e.Debug.Length > 0).GroupBy(e => e.Debug).ToDictionary(g => g.Key, g => g.Count());
            foreach (var entry in after.Where(e => e.Missile && changes.ContainsKey(UnitProjectGraph.Normalize(e.Source.RelativeSourceFile))))
            {
                token.ThrowIfCancellationRequested();
                var old = _entries.SingleOrDefault(e => UnitProjectGraph.Same(e.Source, entry.Source));
                if (old?.Body == entry.Body) continue;
                if (entry.Debug.Length == 0 || debugCounts[entry.Debug] > 1) throw new TransactionValidationException("导弹debug名称缺失或重复 / Missing or duplicate missile debug name: " + entry.Source.Name);
                if (old is null)
                {
                    if (candidateNames[entry.Source.Name] > 1) throw new TransactionValidationException("导弹声明重名 / Duplicate missile declaration: " + entry.Source.Name);
                    var doc = new NdfSyntaxDocument(entry.Body, cancellationToken: token); var ids = doc.FindDirectAssignments(doc.FindConstructors("TEntityDescriptor").Single(), "DescriptorId");
                    if (ids.Count != 1 || !Guid.TryParse(doc.Raw(ids[0]).Replace("GUID:", "", StringComparison.Ordinal), out var id) || guids.GetValueOrDefault(id.ToString("D")) != 1)
                        throw new TransactionValidationException("导弹GUID缺失或重复 / Missing or duplicate missile GUID: " + entry.Source.Name);
                }
            }
        }
    }
    public static Analysis Analyze(string root, CancellationToken token = default, IProgress<MissileRepairProgress>? progress = null) =>
        ProjectReadScope.Memo("missile-repair:" + Path.GetFullPath(root), () => Analysis.Load(root, token, progress));
}
