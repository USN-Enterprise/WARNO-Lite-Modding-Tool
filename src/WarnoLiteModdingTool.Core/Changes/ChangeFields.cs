using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Changes;

public static partial class ChangeMerge
{
    private sealed record FieldSlot(string Name, int Start, int End, int? Comma, Cell? Cell);
    private sealed record FieldBlock(int Open, int Close, IReadOnlyList<FieldSlot> Fields);
    private sealed record FieldLine(int Start, int Length, string Text, string Indent);

    private static bool KnownField(Cell? cell) => cell is not null && (cell.Numeric || SupportedScalar(cell, cell, cell));
    private static Dictionary<string, FieldBlock> FieldBlocks(string source, Dictionary<string, Cell> cells)
    {
        var doc = new NdfSyntaxDocument(source); var result = new Dictionary<string, FieldBlock>();
        var types = doc.Tokens.Zip(doc.Tokens.Skip(1)).Where(p => p.Second.Text == "(").Select(p => p.First.Text).Distinct();
        foreach (var type in types)
        {
            var constructors = doc.FindConstructors(type); if (constructors.Count != 1) continue;
            var block = constructors[0]; var fields = new List<FieldSlot>();
            foreach (var assignment in doc.EnumerateDirectAssignments(block))
            {
                var value = assignment.Value; var key = type + "." + assignment.Name;
                var cell = cells.GetValueOrDefault(key);
                if (cell?.Start != doc.StartOffset(value) || value.StartTokenIndex != value.EndTokenIndex) cell = null;
                var comma = value.EndTokenIndex + 1 < block.CloseTokenIndex && doc.Tokens[value.EndTokenIndex + 1].Text == "," ? doc.Tokens[value.EndTokenIndex + 1].Start : (int?)null;
                fields.Add(new(assignment.Name, doc.Tokens[value.StartTokenIndex - 2].Start, doc.Tokens[value.EndTokenIndex].End, comma, cell));
            }
            result.Add(type, new(doc.Tokens[block.OpenTokenIndex].Start, doc.Tokens[block.CloseTokenIndex].Start, fields));
        }
        return result;
    }
    private static FieldLine FieldRow(string source, FieldBlock block, FieldSlot field)
    {
        var start = source.LastIndexOf('\n', Math.Max(0, field.Start - 1)) + 1;
        var end = source.IndexOf('\n', field.End); if (end < 0) end = source.Length; else end++;
        var indent = source[start..field.Start]; var tail = source[(field.Comma is { } c ? c + 1 : field.End)..end].Trim();
        if (start <= block.Open || end > block.Close || indent.Any(c => c is not (' ' or '\t')) ||
            source[field.Start..field.End].Contains('\n') || tail.Length > 0 && !tail.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("增删字段须为独立行，原文已保存 / Added or removed fields must occupy standalone lines; original retained: " + field.Name);
        return new(start, end - start, source[start..end], indent);
    }

    // Remove only the recorded presence changes from B/M for the existing full-text
    // proof, and apply them to N. The regular merge then handles surviving fields.
    private static (string Before, string After, string Target) MergeFieldPresence(string name, string before, string after, string target,
        Dictionary<string, Cell> bc, Dictionary<string, Cell> mc, Dictionary<string, Cell> nc, List<ChangeDetail> details)
    {
        if (!bc.Keys.Except(mc.Keys).Concat(mc.Keys.Except(bc.Keys)).Any(k => !k.Contains('['))) return (before, after, target);
        var b = FieldBlocks(before, bc); var m = FieldBlocks(after, mc); var n = FieldBlocks(target, nc);
        var bp = new List<TextReplacement>(); var mp = new List<TextReplacement>(); var np = new List<TextReplacement>();
        foreach (var type in b.Keys.Intersect(m.Keys))
        {
            var x = b[type]; var y = m[type];
            var added = y.Fields.Where(f => !x.Fields.Any(o => o.Name == f.Name) && KnownField(f.Cell)).ToArray();
            var removed = x.Fields.Where(f => !y.Fields.Any(o => o.Name == f.Name) && KnownField(f.Cell)).ToArray();
            if (added.Length + removed.Length == 0) continue;
            if (!n.TryGetValue(type, out var z)) throw new InvalidDataException("字段所在构造块缺失或多义 / Field constructor missing or ambiguous: " + type);
            var insertions = new List<(FieldSlot Field, FieldLine Row)>(); var deleted = new HashSet<string>();
            foreach (var field in removed.Concat(added))
            {
                var adding = added.Contains(field); var key = type + "." + field.Name;
                var matches = z.Fields.Where(f => f.Name == field.Name).ToArray(); var current = matches.Length == 1 ? matches[0] : null;
                var oldRaw = adding ? "不存在 / Missing" : field.Cell!.Raw;
                var newRaw = adding ? field.Cell!.Raw : "不存在 / Missing";
                var currentRaw = matches.Length > 1 ? "多义 / Ambiguous" : current?.Cell?.Raw ?? (current is null ? "不存在 / Missing" : target[current.Start..current.End]);
                try
                {
                    if (matches.Length > 1 || current is not null && !KnownField(current.Cell)) throw new InvalidDataException("目标字段重复或值不受支持 / Duplicate or unsupported target field: " + key);
                    if (field.Cell!.Numeric) ValidateQuantity(field.Cell);
                    if (current?.Cell?.Numeric == true) ValidateQuantity(current.Cell);
                    var row = FieldRow(adding ? after : before, adding ? y : x, field);
                    (adding ? mp : bp).Add(new(row.Start, row.Length, row.Text, "", key));
                    string status;
                    if (adding)
                    {
                        if (current is not null && !(field.Cell.Numeric && current.Cell!.Numeric ? SameNumber(field.Cell.Raw, current.Cell.Raw) : field.Cell.Raw == current.Cell!.Raw))
                            throw new InvalidDataException("新增字段已被新版占用 / Added field already exists with a different value: " + key);
                        if (current is null) insertions.Add((field, row));
                        status = current is null ? "新增字段按记录原值 / New field uses recorded value" : "新增字段已满足 / Added field already satisfied";
                    }
                    else
                    {
                        if (current is not null)
                        {
                            var targetRow = FieldRow(target, z, current);
                            string Evidence(FieldLine line, FieldSlot slot) => (slot.Comma is { } c ? line.Text.Remove(c - line.Start, 1) : line.Text).Trim();
                            if (Evidence(row, field) != Evidence(targetRow, current)) throw new InvalidDataException("待删除字段在新版已变化 / Deleted field changed upstream: " + key);
                            np.Add(new(targetRow.Start, targetRow.Length, targetRow.Text, "", key)); deleted.Add(field.Name);
                        }
                        status = current is null ? "字段删除已满足 / Field removal already satisfied" : "删除未变字段 / Remove unchanged field";
                    }
                    details.Add(new(name, key, oldRaw, newRaw, currentRaw, adding ? current?.Cell?.Raw ?? newRaw : newRaw, status));
                }
                catch (InvalidDataException ex) { details.Add(new(name, key, oldRaw, newRaw, currentRaw, "", ex.Message)); throw; }
            }
            if (insertions.Count == 0) continue;
            var closeLine = target.LastIndexOf('\n', Math.Max(0, z.Close - 1)) + 1; var closingIndent = target[closeLine..z.Close];
            if (closeLine <= z.Open || closingIndent.Any(c => c is not (' ' or '\t'))) throw new InvalidDataException("目标构造块末括号须独占一行 / Target constructor closing bracket must occupy a standalone line: " + type);
            var surviving = z.Fields.Where(f => !deleted.Contains(f.Name)).ToArray(); var last = surviving.LastOrDefault();
            var indent = closingIndent + "    ";
            if (last is not null)
            {
                var lineStart = target.LastIndexOf('\n', Math.Max(0, last.Start - 1)) + 1;
                var prefix = target[lineStart..last.Start];
                if (prefix.All(c => c is ' ' or '\t')) indent = prefix;
            }
            var commaStyle = z.Fields.Count == 0 ? insertions[0].Field.Comma is not null : z.Fields.Any(f => f.Comma is not null);
            if (commaStyle && last is { Comma: null }) np.Add(new(last.End, 0, "", ",", type));
            var newline = target.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var inserted = string.Concat(insertions.Select(item =>
            {
                var line = item.Row.Text;
                if (commaStyle && item.Field.Comma is null) line = line.Insert(item.Field.End - item.Row.Start, ",");
                else if (!commaStyle && item.Field.Comma is { } c) line = line.Remove(c - item.Row.Start, 1);
                return indent + line[item.Row.Indent.Length..].TrimEnd('\r', '\n') + newline;
            }));
            np.Add(new(closeLine, 0, "", inserted, type));
        }
        return (SemicolonCsvDocument.ApplyReplacements(before, bp), SemicolonCsvDocument.ApplyReplacements(after, mp), SemicolonCsvDocument.ApplyReplacements(target, np));
    }
    private static void DescribeFieldPresence(string name, Dictionary<string, Cell> b, Dictionary<string, Cell> m, List<ChangeDetail> details)
    {
        foreach (var key in b.Keys.Except(m.Keys).Concat(m.Keys.Except(b.Keys)).Where(k => !k.Contains('[')))
        {
            var x = b.GetValueOrDefault(key); var y = m.GetValueOrDefault(key);
            if (KnownField(x ?? y)) details.Add(new(name, key, x?.Raw ?? "不存在 / Missing", y?.Raw ?? "不存在 / Missing", "", "", "字段增删 / Field added or removed"));
        }
    }
}
