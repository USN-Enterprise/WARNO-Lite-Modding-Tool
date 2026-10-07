namespace WarnoLiteModdingTool.Core.SourceWorkspace;

public sealed record SourceSearchFile(string File, string Text, int Count);
public static class SourceSearch
{
    public static int Count(string text, string query, bool matchCase, CancellationToken token)
    {
        if (query.Length == 0) return 0; var count = 0; var offset = 0;
        while (offset <= text.Length - query.Length)
        {
            token.ThrowIfCancellationRequested(); offset = text.IndexOf(query, offset, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            if (offset < 0) break; count++; offset += query.Length;
        }
        return count;
    }
    public static IReadOnlyList<SourceHit> Page(IReadOnlyList<SourceSearchFile> files, string query, bool matchCase, int skip, int size, CancellationToken token)
    {
        var result = new List<SourceHit>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested(); if (skip >= file.Count) { skip -= file.Count; continue; }
            foreach (var hit in SourceLanguage.Enumerate(file.File, file.Text, query, matchCase, token).Skip(skip)) { result.Add(hit); if (result.Count == size) return result; }
            skip = 0;
        }
        return result;
    }
}
