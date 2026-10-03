using System.Globalization;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Rules;

namespace WarnoLiteModdingTool.Core.Changes;

public static partial class ChangeMerge
{
    private sealed record MapEntry(string Key, string Value, int Start, int End, int ValueStart, int ValueLength, int? Comma, Cell? Quantity);
    private sealed record KeyedMap(string Key, int Start, int Length, string Text, int Close, IReadOnlyList<MapEntry> Entries, bool Identity);
    private sealed record MapLine(int Start, int Length, string Text, string Indent);

    private static bool QuantityMapField(string type, string field) =>
        UnitFieldDefinitions.All.Any(d => d.Selector.ModuleType == type && d.Selector.FieldName == field && d.Selector.MapKey is not null && d.ValueKind is UnitValueKind.Integer or UnitValueKind.Decimal) ||
        type is "TAmmunitionDescriptor" or "TAmmunitionMissileDescriptor" && WeaponFieldDefinitions.Ammo.Any(d => d.FieldName == field && d.MapKey is not null && d.ValueKind is WeaponValueKind.Integer or WeaponValueKind.Decimal) ||
        RuleCatalog.All.Any(d => d.Constructor == type && d.Fields.Split(';').Contains(field));

    private static Dictionary<string, KeyedMap> Maps(string source, IReadOnlyCollection<Obj>? excluded = null)
    {
        if (!source.Contains("MAP", StringComparison.Ordinal)) return new(StringComparer.Ordinal);
        var doc = new NdfSyntaxDocument(source); var cells = Cells(source); var result = new Dictionary<string, KeyedMap>(StringComparer.Ordinal);
        var types = doc.Tokens.Zip(doc.Tokens.Skip(1)).Where(p => p.Second.Text == "(" && p.First.Text != "MAP").Select(p => p.First.Text).Distinct();
        foreach (var type in types)
        {
            var constructors = doc.FindConstructors(type).Where(c => excluded is null || !excluded.Any(o => doc.Tokens[c.TypeTokenIndex].Start >= o.Start && doc.Tokens[c.TypeTokenIndex].Start < o.Start + o.Length)).ToArray();
            if (constructors.Length != 1) continue;
            foreach (var group in doc.EnumerateDirectAssignments(constructors[0]).GroupBy(a => a.Name))
            {
                if (group.Count() != 1) continue;
                var a = group.Single(); var identity = type == "TDeckSerializerEntries" && a.Name is "UnitIds" or "DivisionIds";
                if (!identity && !QuantityMapField(type, a.Name)) continue;
                var value = a.Value; var tokens = doc.Tokens;
                if (tokens[value.StartTokenIndex].Text != "MAP" || value.EndTokenIndex < value.StartTokenIndex + 2 || tokens[value.StartTokenIndex + 1].Text != "[" || tokens[value.EndTokenIndex].Text != "]") continue;
                var key = type + "." + a.Name; var entries = new List<MapEntry>(); var seen = new HashSet<string>(StringComparer.Ordinal);
                var i = value.StartTokenIndex + 2; var valid = true;
                while (i < value.EndTokenIndex)
                {
                    if (i + 4 >= value.EndTokenIndex || tokens[i].Text != "(" || tokens[i + 2].Text != "," || tokens[i + 4].Text != ")")
                    { valid = false; break; }
                    var rawKey = tokens[i + 1].Text; var rawValue = tokens[i + 3];
                    if (rawKey is "(" or ")" or "[" or "]" or "," || !seen.Add(rawKey)) { valid = false; break; }
                    var cellKey = key + "[" + NdfSyntaxDocument.Unquote(rawKey) + "]";
                    var quantity = cells.GetValueOrDefault(cellKey);
                    if (quantity is not { Numeric: true } || quantity.Start != rawValue.Start) quantity = null;
                    var comma = i + 5 < value.EndTokenIndex && tokens[i + 5].Text == "," ? tokens[i + 5].Start : (int?)null;
                    entries.Add(new(rawKey, rawValue.Text, tokens[i].Start, tokens[i + 4].End, rawValue.Start, rawValue.Length, comma, quantity));
                    i += comma is null ? 5 : 6;
                    if (comma is null && i != value.EndTokenIndex) { valid = false; break; }
                }
                if (!valid) continue;
                result.Add(key, new(key, doc.StartOffset(value), doc.Length(value), doc.Raw(value), tokens[value.EndTokenIndex].Start, entries, identity));
            }
        }
        return result;
    }

    // Only a complete standalone entry line can be inserted/deleted. Its comment is
    // part of the recorded operation; unrelated lines and comments stay untouched.
    private static MapLine EntryLine(string source, KeyedMap map, MapEntry entry)
    {
        var start = source.LastIndexOf('\n', Math.Max(0, entry.Start - 1)) + 1;
        var end = source.IndexOf('\n', entry.End); if (end < 0) end = source.Length; else end++;
        var tailStart = entry.Comma is { } comma ? comma + 1 : entry.End;
        var tail = source[tailStart..end].Trim(); var indent = source[start..entry.Start];
        if (start <= map.Start || end > map.Close || !string.IsNullOrWhiteSpace(indent) && indent.Length != 0 ||
            source[entry.Start..entry.End].Contains('\n') || tail.Length > 0 && !tail.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("增删 MAP 项须为独立行；原文已保留 / Added or removed MAP entries must occupy standalone lines; original text retained: " + map.Key + " / " + entry.Key);
        return new(start, end - start, source[start..end], indent);
    }

    private static void MergeMaps(string name, string before, string after, string target, NumericPolicy policy, List<ChangeDetail> details,
        List<TextReplacement> oldPatches, List<TextReplacement> newPatches, IReadOnlyCollection<Obj>? beforeObjects = null,
        IReadOnlyCollection<Obj>? afterObjects = null, IReadOnlyCollection<Obj>? targetObjects = null)
    {
        var b = Maps(before, beforeObjects); var m = Maps(after, afterObjects); var n = Maps(target, targetObjects);
        foreach (var key in b.Keys.Intersect(m.Keys))
        {
            var x = b[key]; var y = m[key]; if (x.Text == y.Text) continue;
            // Existing named-object value-only edits keep the established cell path;
            // unrelated new target expressions must not turn them into structural edits.
            if (beforeObjects is null && !x.Identity && x.Entries.Select(e => e.Key).ToHashSet(StringComparer.Ordinal).SetEquals(y.Entries.Select(e => e.Key))) continue;
            if (!n.TryGetValue(key, out var z)) throw new InvalidDataException("MAP 无法唯一对应 / MAP cannot be uniquely matched: " + key);
            if (x.Identity)
            {
                DescribeMap(name, x, y, z, details);
                throw new InvalidDataException("注册编号属于身份，不使用比例或增减；跨版本注册变化需核对完整关联 / Registration numbers are identities, not quantities; cross-version changes require complete relationship review: " + key);
            }
            var desired = MergeMap(name, before, after, target, x, y, z, policy, details);
            oldPatches.Add(new(x.Start, x.Length, x.Text, y.Text, key)); newPatches.Add(new(z.Start, z.Length, z.Text, desired, key));
        }
    }

    private static string MergeMap(string name, string before, string after, string target, KeyedMap b, KeyedMap m, KeyedMap n, NumericPolicy policy, List<ChangeDetail> details)
    {
        var old = b.Entries.ToDictionary(e => e.Key); var edited = m.Entries.ToDictionary(e => e.Key); var current = n.Entries.ToDictionary(e => e.Key);
        var structural = !old.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(edited.Keys);
        var proof = new List<TextReplacement>(); var removeAdded = new List<TextReplacement>(); var patches = new List<TextReplacement>(); var additions = new List<MapLine>();
        TextReplacement Within(KeyedMap map, int start, int length, string raw, string value, string label) => new(start - map.Start, length, raw, value, label);
        foreach (var key in old.Keys.Union(edited.Keys))
        {
            old.TryGetValue(key, out var x); edited.TryGetValue(key, out var y); current.TryGetValue(key, out var z);
            if (x is not null && y is not null)
            {
                // Rebuild all surviving old entries; only their values and syntax commas
                // may differ. Reordering, comments and extra formatting remain conflicts.
                proof.Add(Within(b, x.ValueStart, x.ValueLength, x.Value, y.Value, key));
                if (structural && x.Comma is null && y.Comma is not null) proof.Add(Within(b, x.End, 0, "", ",", key));
                else if (structural && x.Comma is { } c && y.Comma is null) proof.Add(Within(b, c, 1, ",", "", key));
                if (x.Value == y.Value) continue;
            }
            var field = b.Key + "[" + key + "]"; var oldRaw = x?.Value ?? "不存在 / Missing"; var changedRaw = y?.Value ?? "不存在 / Missing"; var nowRaw = z?.Value ?? "不存在 / Missing";
            try
            {
                if (x is not null && x.Quantity is null || y is not null && y.Quantity is null || z is not null && z.Quantity is null)
                    throw new InvalidDataException("MAP 项数值语义无法确认 / Unknown numeric meaning for MAP entry: " + field);
                foreach (var quantity in new[] { x?.Quantity, y?.Quantity, z?.Quantity }.OfType<Cell>()) ValidateQuantity(quantity);
                string result; string status;
                if (x is null)
                {
                    var line = EntryLine(after, m, y!); removeAdded.Add(Within(m, line.Start, line.Length, line.Text, "", field));
                    if (z is not null && !SameNumber(z.Value, y!.Value)) throw new InvalidDataException("新增 MAP 键已被新版占用 / Added MAP key already exists with another value: " + field);
                    if (z is null) additions.Add(line);
                    result = z?.Value ?? y!.Value; status = z is null ? "新增项按记录原值 / New entry uses recorded value" : "新增项已满足 / Added entry already satisfied";
                }
                else if (y is null)
                {
                    var line = EntryLine(before, b, x); proof.Add(Within(b, line.Start, line.Length, line.Text, "", field));
                    if (z is not null)
                    {
                        var targetLine = EntryLine(target, n, z);
                        string Evidence(MapLine row, MapEntry entry) => (entry.Comma is { } c ? row.Text.Remove(c - row.Start, 1) : row.Text).Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
                        if (!SameNumber(x.Value, z.Value) || Evidence(line, x) != Evidence(targetLine, z))
                            throw new InvalidDataException("待删除 MAP 项在新版已变化 / Deleted MAP entry changed upstream: " + field);
                        patches.Add(Within(n, targetLine.Start, targetLine.Length, targetLine.Text, "", field));
                    }
                    result = "不存在 / Missing"; status = z is null ? "删除已满足 / Removal already satisfied" : "删除未变项 / Remove unchanged entry";
                }
                else
                {
                    if (z is null) throw new InvalidDataException("新版 MAP 项缺失 / MAP entry missing from target: " + field);
                    result = Calculate(x.Quantity!, y.Quantity!, z.Quantity!, policy); status = "统一数值方式 / Global numeric policy";
                    patches.Add(Within(n, z.ValueStart, z.ValueLength, z.Value, result, field));
                }
                details.Add(new(name, field, oldRaw, changedRaw, nowRaw, result, status));
            }
            catch (Exception e) when (e is InvalidDataException or FormatException or OverflowException)
            { details.Add(new(name, field, oldRaw, changedRaw, nowRaw, "", e.Message)); throw; }
        }
        if (SemicolonCsvDocument.ApplyReplacements(b.Text, proof) != SemicolonCsvDocument.ApplyReplacements(m.Text, removeAdded))
            throw new InvalidDataException("MAP 还有顺序、注释或格式变化，完整原文已保留 / Additional MAP order, comment or formatting changes are preserved for review: " + b.Key);
        if (additions.Count > 0)
        {
            var closeLine = target.LastIndexOf('\n', Math.Max(0, n.Close - 1)) + 1;
            var closingIndent = target[closeLine..n.Close];
            if (closeLine <= n.Start || !string.IsNullOrWhiteSpace(closingIndent) && closingIndent.Length != 0)
                throw new InvalidDataException("目标 MAP 末括号不在独立行，不能追加 / Target MAP closing bracket must occupy a standalone line: " + n.Key);
            var surviving = n.Entries.Where(e => !old.ContainsKey(e.Key) || edited.ContainsKey(e.Key)).ToArray();
            var newline = n.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var indent = closingIndent + "    ";
            if (surviving.LastOrDefault() is { } last)
            {
                indent = EntryLine(target, n, last).Indent;
                if (last.Comma is null) patches.Add(Within(n, last.End, 0, "", ",", n.Key));
            }
            var inserted = string.Concat(additions.Select(a => indent + a.Text[a.Indent.Length..].TrimEnd('\r', '\n').TrimEnd() + newline));
            // Reuse original lines, adding commas at tuple ends, before trailing comments.
            var insertPatches = new List<TextReplacement>();
            var insertDoc = new NdfSyntaxDocument(inserted);
            for (var i = 0; i < insertDoc.Tokens.Count; i++)
                if (insertDoc.Tokens[i].Text == ")" && (i + 1 == insertDoc.Tokens.Count || insertDoc.Tokens[i + 1].Text != ","))
                    insertPatches.Add(new(insertDoc.Tokens[i].End, 0, "", ",", n.Key));
            inserted = SemicolonCsvDocument.ApplyReplacements(inserted, insertPatches);
            patches.Add(Within(n, closeLine, 0, "", inserted, n.Key));
        }
        return SemicolonCsvDocument.ApplyReplacements(n.Text, patches);
    }
    private static bool SameNumber(string a, string b) => decimal.Parse(a, NumberStyles.Float, CultureInfo.InvariantCulture) == decimal.Parse(b, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static void ValidateMapKeys(NdfSyntaxDocument doc, string path)
    {
        var types = doc.Tokens.Zip(doc.Tokens.Skip(1)).Where(p => p.Second.Text == "(" && p.First.Text != "MAP").Select(p => p.First.Text).Distinct();
        foreach (var type in types)
        foreach (var constructor in doc.FindConstructors(type))
        foreach (var field in doc.EnumerateDirectAssignments(constructor))
        {
            var identity = type == "TDeckSerializerEntries" && field.Name is "UnitIds" or "DivisionIds";
            if (!identity && !QuantityMapField(type, field.Name) || doc.Tokens[field.Value.StartTokenIndex].Text != "MAP") continue;
            var entries = doc.ReadMapEntries(field.Value);
            if (entries.GroupBy(e => doc.Raw(e.Key), StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new InvalidDataException("MAP 键重复 / Duplicate MAP key: " + path + " / " + field.Name);
            if (!identity) continue;
            var ids = new HashSet<int>();
            foreach (var entry in entries)
                if (!int.TryParse(doc.Raw(entry.Value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id < 0 || !ids.Add(id))
                    throw new InvalidDataException("注册编号重复或无效 / Duplicate or invalid registration number: " + path + " / " + field.Name);
        }
    }
    private static void DescribeMap(string name, KeyedMap b, KeyedMap m, KeyedMap? n, List<ChangeDetail> details)
    {
        foreach (var key in b.Entries.Select(e => e.Key).Union(m.Entries.Select(e => e.Key)))
        {
            var x = b.Entries.SingleOrDefault(e => e.Key == key); var y = m.Entries.SingleOrDefault(e => e.Key == key); if (x?.Value == y?.Value) continue;
            details.Add(new(name, b.Key + "[" + key + "]", x?.Value ?? "不存在 / Missing", y?.Value ?? "不存在 / Missing",
                n?.Entries.SingleOrDefault(e => e.Key == key)?.Value ?? "", "", b.Identity ? "注册身份 / Registration identity" : x is null || y is null ? "MAP 增删 / MAP entry added or removed" : "统一数值方式 / Global numeric policy"));
        }
    }
}
