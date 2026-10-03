using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Changes;

internal static class ChangeGrouping
{
    internal static IReadOnlyList<(string[] Paths, string Reason)> Build(ChangePackage package, Dictionary<string, byte[]> target,
        IReadOnlyList<ChangeFileMapping> mappings, CancellationToken cancel)
    {
        var paths = package.Manifest.Files.Select(f => f.Path).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return [];
        // Only quantity-only records can be split. Structural/resource/identity
        // relationships are not proven by the generic NDF token graph.
        if (package.Manifest.Files.Any(f => !ChangeMerge.QuantityOnly(f.Path, package.Before(f), package.After(f))))
            return [(paths, "包含结构、资源或未知关系，保持整体 / Structural, resource or unknown relationships are kept together")];
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Root(string path)
        {
            if (!parent.TryGetValue(path, out var next)) return parent[path] = path;
            return next.Equals(path, StringComparison.OrdinalIgnoreCase) ? path : parent[path] = Root(next);
        }
        void Join(string a, string b) { a = Root(a); b = Root(b); if (!a.Equals(b, StringComparison.OrdinalIgnoreCase)) parent[b] = a; }
        var docs = new List<(string Path, NdfSyntaxDocument Doc)>();
        foreach (var file in target.Where(f => f.Key.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)))
        {
            cancel.ThrowIfCancellationRequested();
            try { docs.Add((file.Key, new(ChangeMerge.DecodeNdf(file.Value)))); }
            catch (InvalidDataException) { return [(paths, "目标存在无法分析的关系，保持整体 / Unreadable target relationships are kept together")]; }
        }
        foreach (var file in package.Manifest.Files)
        {
            docs.Add((file.Path, new(ChangeMerge.DecodeNdf(package.Before(file)!))));
            docs.Add((file.Path, new(ChangeMerge.DecodeNdf(package.After(file)!)))); Root(file.Path);
        }
        foreach (var map in mappings) Join(map.SourcePath, map.TargetPath);
        var definitions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (path, doc) in docs)
        for (var i = 0; i + 1 < doc.Tokens.Count; i++)
            if (doc.Tokens[i + 1].Text == "is")
            {
                if (!definitions.TryGetValue(doc.Tokens[i].Text, out var locations)) definitions[doc.Tokens[i].Text] = locations = new(StringComparer.OrdinalIgnoreCase);
                locations.Add(path);
            }
        foreach (var (path, doc) in docs)
        {
            cancel.ThrowIfCancellationRequested();
            for (var i = 0; i < doc.Tokens.Count; i++)
            {
                var token = doc.Tokens[i];
                if (i + 1 < doc.Tokens.Count && doc.Tokens[i + 1].Text == "=") continue;
                var name = token.Text.StartsWith("~/", StringComparison.Ordinal) || token.Text.StartsWith("$/", StringComparison.Ordinal)
                    ? NdfSyntaxDocument.Leaf(token.Text) : token.Text;
                if (definitions.TryGetValue(name, out var locations)) foreach (var location in locations) Join(path, location);
            }
        }
        return paths.GroupBy(Root, StringComparer.OrdinalIgnoreCase).Select(g => (g.ToArray(), "同文件及声明引用关联 / Same file and declaration-reference relationships")).ToArray();
    }
}
