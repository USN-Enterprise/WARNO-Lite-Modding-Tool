using System.Text;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Units;

// Scoped NDF reference evidence for unit lifecycle and capability editing.
// Never resolves a qualified reference by its leaf alone.
public sealed class UnitProjectGraph
{
    public sealed record Source(string Path, string Text, NdfSyntaxDocument Syntax, IReadOnlyList<NdfObjectInfo> Objects);
    public sealed record Reference(Source File, NdfSyntaxDocument.NdfToken Token, NdfObjectInfo? Owner);
    public string Root { get; }
    public Dictionary<string, Source> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> Dependencies { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _namespaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NdfObjectInfo[]> _byName;
    public IEnumerable<NdfObjectInfo> Objects => Files.Values.SelectMany(f => f.Objects);
    public IReadOnlyList<NdfObjectInfo> FindObjects(string name) => _byName.GetValueOrDefault(name) ?? [];

    public static IEnumerable<string> InputFiles(string root)
    {
        foreach (var folder in new[] { "GameData", "CommonData" })
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*.ndf", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }).Order(StringComparer.OrdinalIgnoreCase))
                yield return Path.GetRelativePath(root, file).Replace('\\', '/');
        }
    }

    public UnitProjectGraph(string root, IReadOnlyList<PlannedFileChange>? candidates = null)
    {
        Root = Path.GetFullPath(root);
        foreach (var path in InputFiles(Root).Concat(candidates?.Where(f => f.Kind == FormalTextFileKind.Ndf).Select(f => f.RelativePath) ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var snapshot = TextFileSnapshot.Load(Root, path, FormalTextFileKind.Ndf, true);
            Dependencies[path] = snapshot.OriginalBytes;
            var candidate = candidates?.SingleOrDefault(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (candidate?.Action == PlannedFileAction.Delete) continue;
            var text = candidate is null ? snapshot.Text : Encoding.UTF8.GetString(candidate.CandidateBytes);
            var syntax = new NdfSyntaxDocument(text);
            var starts = syntax.Tokens.Select(t=>t.Start).ToHashSet();
            var objects = new NdfTopLevelScanner().Scan(text, snapshot.FullPath, "units", Root).Objects.Where(o=>starts.Contains(o.CharacterOffset)).ToArray();
            Files[path] = new(path, text, syntax, objects);
            _namespaces[path] = [];
        }
        // Existing exported references are the project's binding evidence. Multiple
        // prefixes for one source are ambiguous and are deliberately not guessed.
        _byName = Objects.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var unique = _byName.Where(g => g.Value.Length == 1).ToDictionary(g => g.Key, g => g.Value[0], StringComparer.Ordinal);
        foreach (var file in Files.Values)
        foreach (var token in file.Syntax.Tokens.Where(t => t.Text.StartsWith("$/", StringComparison.Ordinal)))
        {
            if (!unique.TryGetValue(NdfSyntaxDocument.Leaf(token.Text), out var obj) || !IsExported(obj)) continue;
            _namespaces[Normalize(obj.RelativeSourceFile)].Add(token.Text[..(token.Text.LastIndexOf('/') + 1)]);
        }
        // Relative cross-file references also carry scope evidence. Conditions,
        // for example, are private declarations in the same compilation scope.
        var relative = Files.Values.SelectMany(f => f.Syntax.Tokens.Where(t => t.Text.StartsWith("~/", StringComparison.Ordinal) && !t.Text[2..].Contains('/'))
            .Where(t => unique.ContainsKey(t.Text[2..])).Select(t => (Source: f.Path, Target: Normalize(unique[t.Text[2..]].RelativeSourceFile)))).Distinct().ToArray();
        bool changed;
        do
        {
            changed = false;
            foreach (var edge in relative)
                foreach (var prefix in _namespaces[edge.Source].ToArray()) changed |= _namespaces[edge.Target].Add(prefix);
        } while (changed);
    }

    public static string Normalize(string path) => path.Replace('\\', '/');
    public Source FileOf(NdfObjectInfo obj) => Files[Normalize(obj.RelativeSourceFile)];
    public string Body(NdfObjectInfo obj) => FileOf(obj).Text.Substring(obj.CharacterOffset, obj.CharacterLength);
    public bool IsExported(NdfObjectInfo obj) => new NdfSyntaxDocument(Body(obj)).Tokens.FirstOrDefault()?.Text == "export";
    public string? Prefix(NdfObjectInfo obj)
    {
        var evidence = _namespaces[Normalize(obj.RelativeSourceFile)];
        return evidence.Count == 1 ? evidence.Single() : null;
    }
    public NdfObjectInfo RequireObject(string file, string name) => Files.GetValueOrDefault(Normalize(file))?.Objects.SingleOrDefault(o => o.Name == name)
        ?? throw new TransactionValidationException("对象不存在或不唯一：" + file + " · " + name);

    public NdfObjectInfo? Resolve(string source, string raw)
    {
        if (raw.Length == 0 || raw[0] is '\'' or '"') return null;
        var leaf = NdfSyntaxDocument.Leaf(raw);
        var possible = _byName.GetValueOrDefault(leaf) ?? [];
        source = Normalize(source);
        if (!raw.Contains('/'))
        {
            var local = possible.Where(o => Normalize(o.RelativeSourceFile).Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (local.Length == 1) return local[0];
            var scope = _namespaces.GetValueOrDefault(source);
            var scoped = scope?.Count == 1 ? possible.Where(o => Prefix(o) == scope.Single()).ToArray() : [];
            return scoped.Length == 1 ? scoped[0] : null;
        }
        if (raw.StartsWith("~/", StringComparison.Ordinal))
        {
            if (!Files.ContainsKey(source)) return null;
            var local = possible.Where(o => Normalize(o.RelativeSourceFile).Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (local.Length == 1 && raw == "~/" + leaf) return local[0];
            var prefixes = _namespaces[source];
            var scoped = prefixes.Count == 1 ? possible.Where(o => Prefix(o) == prefixes.Single() && raw == "~/" + o.Name).ToArray() : [];
            return scoped.Length == 1 ? scoped[0] : null;
        }
        if (!raw.StartsWith("$/", StringComparison.Ordinal)) return null;
        var matches = possible.Where(o => IsExported(o) && Prefix(o) + o.Name == raw).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    public static bool Same(NdfObjectInfo? a, NdfObjectInfo b) => a is not null && a.Name == b.Name && Normalize(a.RelativeSourceFile).Equals(Normalize(b.RelativeSourceFile), StringComparison.OrdinalIgnoreCase);
    public string ReferenceTo(string source, NdfObjectInfo target)
    {
        if (Normalize(source).Equals(Normalize(target.RelativeSourceFile), StringComparison.OrdinalIgnoreCase)) return target.Name;
        if (!IsExported(target) || Prefix(target) is not { } prefix) throw new TransactionValidationException("无法确认导出路径：" + target.Name);
        return prefix + target.Name;
    }
    public static bool IsRegistration(Source file, NdfSyntaxDocument.NdfToken token) => file.Syntax.FindConstructors("TDeckSerializerEntries")
        .SelectMany(c=>file.Syntax.FindDirectAssignments(c,"UnitIds")).SelectMany(file.Syntax.ReadMapEntries)
        .Any(e=>file.Syntax.StartOffset(e.Key)==token.Start);
    public IReadOnlyList<Reference> References(NdfObjectInfo target, bool allowBrokenRegistration = false)
    {
        var result = new List<Reference>();
        foreach (var file in Files.Values)
        {
            var tokens = file.Syntax.Tokens;
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Text.StartsWith('"') || token.Text.StartsWith('\'')) continue;
                if (NdfSyntaxDocument.Leaf(token.Text) != target.Name)
                {
                    if(token.Text.Contains(target.Name,StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.IsMatch(token.Text,"(?<![A-Za-z0-9_])"+System.Text.RegularExpressions.Regex.Escape(target.Name)+"(?![A-Za-z0-9_])"))
                        throw new TransactionValidationException("单位引用表达式不支持："+file.Path+" · "+token.Text);
                    continue;
                }
                if (i + 1 < tokens.Count && tokens[i + 1].Text == "is") continue;
                var resolved = Resolve(file.Path, token.Text);
                if (Same(resolved, target)) result.Add(new(file, token, file.Objects.FirstOrDefault(o => token.Start >= o.CharacterOffset && token.End <= o.CharacterOffset + o.CharacterLength)));
                else if (resolved is null && !(allowBrokenRegistration && token.Text == target.Name && IsRegistration(file, token)))
                    throw new TransactionValidationException("引用作用域无法确认：" + file.Path + " · " + token.Text);
            }
        }
        return result;
    }
    public static void Put(string root, string path, string text, List<PlannedFileChange> files, string summary, FormalTextFileKind kind = FormalTextFileKind.Ndf)
    {
        path = Normalize(path);
        var snap = TextFileSnapshot.Load(root, path, kind, true);
        var prior = files.FirstOrDefault(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        files.RemoveAll(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (text == snap.Text) return;
        if (kind == FormalTextFileKind.Ndf && new NdfTopLevelScanner().Scan(text, snap.FullPath, "units", root).Diagnostics.Any(d => d.Severity == NdfDiagnosticSeverity.Error))
            throw new TransactionValidationException("候选NDF结构错误：" + path);
        files.Add(new(path, snap.FullPath, kind, PlannedFileAction.Write, snap.Existed, snap.OriginalBytes, snap.Encode(text), snap.LastWriteUtc, (prior?.Summaries ?? []).Append(summary).Distinct().ToArray()));
    }
    public static string Patch(string text, IEnumerable<TextReplacement> changes) => SemicolonCsvDocument.ApplyReplacements(text, changes.ToArray());
    public static TextReplacement RemoveElement(NdfSyntaxDocument doc, NdfValueSpan array, NdfValueSpan element, string label)
    {
        var start = doc.StartOffset(element); var end = start + doc.Length(element);
        var after = element.EndTokenIndex + 1;
        if (after < array.EndTokenIndex && doc.Tokens[after].Text == ",") end = doc.Tokens[after].End;
        else if (element.StartTokenIndex > array.StartTokenIndex + 1 && doc.Tokens[element.StartTokenIndex - 1].Text == ",") start = doc.Tokens[element.StartTokenIndex - 1].Start;
        // The original slice includes trivia only within the removed entry.
        var span = new NdfValueSpan(doc.Tokens.ToList().FindIndex(t => t.Start == start), doc.Tokens.ToList().FindIndex(t => t.End == end));
        return new(start, end - start, doc.Raw(span), "", label);
    }
}
