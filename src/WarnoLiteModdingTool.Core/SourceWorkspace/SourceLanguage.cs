using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.SourceWorkspace;

public sealed record SourceSymbol(string File, string Name, string Type, int Line, int Offset);
public sealed record SourceHit(string File, int Offset, int Length, int Line, string Context, string OriginalText);
public static class SourceLanguage
{
    public static IReadOnlyList<string> Diagnostics(string text)
    {
        var result = new List<string>(); var stack = new Stack<(char C, int Line)>(); var line = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i]; if (c == '\n') { line++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; line++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            { var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal); if (end < 0) { result.Add($"L{line}: 注释未闭合 / Unclosed comment"); break; } line += text.AsSpan(i, end + 2 - i).Count('\n'); i = end + 1; continue; }
            if (c is '\'' or '"')
            {
                var opening = line; var closed = false;
                while (++i < text.Length) { if (text[i] == '\n') line++; if (text[i] == '\\') { i++; continue; } if (text[i] == c) { closed = true; break; } }
                if (!closed) result.Add($"L{opening}: 字符串未闭合 / Unclosed string"); continue;
            }
            if (c is '(' or '[') stack.Push((c, line));
            if (c is ')' or ']') { if (!stack.TryPop(out var open) || open.C != (c == ')' ? '(' : '[')) result.Add($"L{line}: 括号不匹配 / Mismatched bracket"); }
        }
        foreach (var item in stack.Take(30)) result.Add($"L{item.Line}: {item.C} 未闭合 / Unclosed bracket");
        return result;
    }
    public static IReadOnlyList<SourceSymbol> Symbols(string file, string text, CancellationToken cancellationToken = default)
    {
        var symbols = new NdfTopLevelScanner().Scan(text, file, "source", cancellationToken:cancellationToken).Objects.Select(o => new SourceSymbol(file, o.Name, o.TypeName, o.LineNumber, o.CharacterOffset)).ToList();
        var tokens = new NdfSyntaxDocument(text,cancellationToken:cancellationToken).Tokens; var depth = 0; var names = symbols.Select(s=>s.Name).ToHashSet(); var line = 1; var scanned = 0;
        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            var token = tokens[i];
            cancellationToken.ThrowIfCancellationRequested();
            line += text.AsSpan(scanned,token.Start-scanned).Count('\n'); scanned = token.Start;
            if (token.Text is "(" or "[") { depth++; continue; }
            if (token.Text is ")" or "]") { depth = Math.Max(0, depth - 1); continue; }
            if (depth == 0 && tokens[i + 1].Text == "is" && !names.Contains(token.Text) && Regex.IsMatch(token.Text, @"^[\p{L}_][\p{L}\p{N}_]*$"))
            { names.Add(token.Text); symbols.Add(new(file, token.Text, "value", line, token.Start)); }
        }
        return symbols.OrderBy(s => s.Offset).ToArray();
    }
    public static IReadOnlyList<string> Words(string text, CancellationToken cancellationToken = default) => new NdfSyntaxDocument(text,cancellationToken:cancellationToken).Tokens.Select(t => { cancellationToken.ThrowIfCancellationRequested(); return t.Text; }).Where(t => Regex.IsMatch(t, @"^[\p{L}_][\p{L}\p{N}_./]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Distinct().ToArray();
    public static IReadOnlyList<SourceHit> Find(string path, string text, string query, bool matchCase, CancellationToken token = default) => Enumerate(path, text, query, matchCase, token).ToArray();
    public static IEnumerable<SourceHit> Enumerate(string path, string text, string query, bool matchCase, CancellationToken token = default)
    {
        if (query.Length == 0) yield break; var offset = 0; var line = 1; var scanned = 0; var start = 0; var end = text.IndexOf('\n'); if (end < 0) end = text.Length;
        while ((offset = text.IndexOf(query, offset, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            token.ThrowIfCancellationRequested();
            line += text.AsSpan(scanned, offset - scanned).Count('\n'); scanned = offset;
            if (offset > end) { start = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1; end = text.IndexOf('\n', offset); if (end < 0) end = text.Length; }
            yield return new(path, offset, query.Length, line, text[start..Math.Min(end, start + 240)], text); offset += query.Length;
        }
    }
    public static string Replace(string current, IEnumerable<SourceHit> hits, string replacement)
    {
        var selected = hits.OrderBy(h => h.Offset).ToArray();
        if (selected.Any(h => h.OriginalText != current)) throw new InvalidOperationException("搜索后文本已变化，请重新搜索 / Text changed; search again");
        var result = new System.Text.StringBuilder(); var offset = 0;
        foreach (var hit in selected)
        {
            if (hit.Offset < offset || hit.Offset < 0 || hit.Offset + hit.Length > current.Length) throw new InvalidOperationException("命中重叠或无效 / Overlapping or invalid matches");
            result.Append(current, offset, hit.Offset - offset).Append(replacement); offset = hit.Offset + hit.Length;
        }
        return result.Append(current, offset, current.Length - offset).ToString();
    }
}
