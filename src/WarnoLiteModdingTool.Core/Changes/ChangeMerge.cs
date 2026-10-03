using System.Globalization;
using System.Text;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Rules;

namespace WarnoLiteModdingTool.Core.Changes;

// Semantic merging is deliberately conservative. Full byte payloads remain available
// even when a structure cannot be mapped to a newer baseline without guessing.
public static partial class ChangeMerge
{
    private sealed record Cell(string Key, string Raw, int Start, int Length, bool Numeric, bool Integer, bool NonNegative, bool Ecm = false, ScalarKind Literal = ScalarKind.None);
    public static RestoreFile Merge(ChangeFile file, byte[]? before, byte[]? after, byte[]? target, NumericPolicy policy,
        IReadOnlyList<ChangeTextDecision>? decisions = null)
    {
        var details = new List<ChangeDetail>();
        try
        {
            ChangePaths.Writable(file.Path);
            ChangeTextDecisions.Validate(decisions ?? []);
            if (decisions?.Any(d => !d.Conflict.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase)) == true ||
                decisions?.Count > 0 && (before is null || after is null || target is null))
                throw new InvalidDataException("冲突选择不对应可合并文件 / Conflict choices do not match a mergeable file");
            if (file.Before.Presence == FilePresence.Unknown) throw new InvalidDataException("缺少基础内容 / Missing baseline content");
            if (ChangePaths.Equal(before, target))
            {
                if (decisions?.Count > 0) throw new InvalidDataException("冲突依据已变化，请清除旧选择重新核对 / Conflict evidence changed; clear the previous choice and review again");
                ValidateBytes(file.Path, after);
                return new(file.Path, target, after, "精确还原 / Exact restore", Describe(file.Path, before, after));
            }
            // An equal value is not proof that a numeric multiplier has been applied.
            if (before is null && ChangePaths.Equal(after, target) || after is null && target is null)
                return new(file.Path, target, target, "已满足 / Already satisfied", []);
            if (ChangePaths.Equal(after, target) && !file.Path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("目标与旧修改结果相同，请核对是否已经应用 / Target matches old modified content; check whether it was already applied");
            if (before is null || after is null || target is null) throw new InvalidDataException("新增、删除或缺失目标冲突 / Added, deleted or missing target conflict");
            byte[] merged;
            if (file.Path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase))
                merged = Encoding.UTF8.GetBytes(MergeNdf(file.Path, DecodeNdf(before), DecodeNdf(after), DecodeNdf(target), policy, details, decisions ?? []));
            else if (file.Path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                merged = MergeCsv(file.Path, before, after, target, details, decisions ?? []);
            else throw new InvalidDataException("此文件新版也有变化，需人工核对 / This file also changed upstream; review required");
            ValidateBytes(file.Path, merged);
            return new(file.Path, target, merged, decisions?.Any(d => d.Choice == TextConflictChoice.KeepTarget) == true ? "可处理（保留新版项） / Ready with retained values" : "可还原 / Ready", details);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { return new(file.Path, target, target, "需核对 / Review required", details, ex.Message); }
    }
    public static IReadOnlyList<ChangeDetail> Describe(string path, byte[]? before, byte[]? after)
    {
        if (before is null || after is null) return [new("", path, before is null ? "不存在 / Missing" : before.Length + " bytes", after is null ? "不存在 / Missing" : after.Length + " bytes", "", "", "文件 / File")];
        var details = new List<ChangeDetail>();
        if (path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var b = Objects(DecodeNdf(before)); var m = Objects(DecodeNdf(after));
                var globalBefore = Maps(DecodeNdf(before), b.Values.ToArray()); var globalAfter = Maps(DecodeNdf(after), m.Values.ToArray());
                foreach (var key in globalBefore.Keys.Intersect(globalAfter.Keys)) DescribeMap("", globalBefore[key], globalAfter[key], null, details);
                foreach (var name in b.Keys.Union(m.Keys))
                {
                    if (!b.TryGetValue(name, out var old) || !m.TryGetValue(name, out var edited)) { details.Add(new(name, "对象 / Object", b.ContainsKey(name) ? "存在 / Present" : "", m.ContainsKey(name) ? "存在 / Present" : "", "", "", "结构 / Structure")); continue; }
                    var oldCells = Cells(old.Text); var newCells = Cells(edited.Text);
                    DescribeFieldPresence(name, oldCells, newCells, details);
                    var oldMaps = Maps(old.Text); var newMaps = Maps(edited.Text);
                    foreach (var key in oldMaps.Keys.Intersect(newMaps.Keys)) DescribeMap(name, oldMaps[key], newMaps[key], null, details);
                    foreach (var key in oldCells.Keys.Intersect(newCells.Keys))
                    {
                        var x = oldCells[key]; var y = newCells[key];
                        if (x.Raw != y.Raw && !details.Any(d => d.Object == name && d.Field == key)) details.Add(new(name, key, x.Raw, y.Raw, "", "", x.Numeric ? "统一数值方式 / Global numeric policy" : "原文 / Literal"));
                    }
                    if (old.Text != edited.Text && !details.Any(d => d.Object == name)) details.Add(new(name, "结构或原文 / Structure or text", old.Text, edited.Text, "", "", "完整保留 / Preserved"));
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { }
        }
        if (details.Count == 0) details.Add(new("", path, before.Length + " bytes", after.Length + " bytes", "", "", "完整文件内容 / Full file content"));
        return details;
    }
    private sealed record Obj(int Start, int Length, string Type, string Text);
    private static Dictionary<string, Obj> Objects(string text)
    {
        var scan = new NdfTopLevelScanner().Scan(text, "memory.ndf", "changes"); var result = new Dictionary<string, Obj>(StringComparer.Ordinal);
        foreach (var o in scan.Objects)
            if (!result.TryAdd(o.Name, new(o.CharacterOffset, o.CharacterLength, o.TypeName, text.Substring(o.CharacterOffset, o.CharacterLength))))
                throw new InvalidDataException("对象名称多义 / Ambiguous object: " + o.Name);
        return result;
    }
    private static string MergeNdf(string path, string before, string after, string target, NumericPolicy policy, List<ChangeDetail> details, IReadOnlyList<ChangeTextDecision> decisions)
    {
        var b = Objects(before); var m = Objects(after); var n = Objects(target);
        var review = new ScalarReview(path, decisions);
        var oldPatches = new List<TextReplacement>(); var newPatches = new List<TextReplacement>();
        var additions = new List<string>();
        MergeMaps("", before, after, target, policy, details, oldPatches, newPatches, b.Values.ToArray(), m.Values.ToArray(), n.Values.ToArray());
        var bg = GlobalCells(before); var mg = GlobalCells(after); var ng = GlobalCells(target);
        foreach (var key in bg.Keys.Intersect(mg.Keys))
        {
            var x = bg[key]; var y = mg[key]; if (x.Raw == y.Raw) continue;
            if (!ng.TryGetValue(key, out var z)) throw new InvalidDataException("全局数值缺失 / Global value missing: " + key);
            string value;
            try { value = Calculate(x, y, z, policy); }
            catch (Exception e) when (e is InvalidDataException or FormatException or OverflowException)
            { details.Add(new("", key, x.Raw, y.Raw, z.Raw, "", e.Message)); throw; }
            oldPatches.Add(new(x.Start, x.Length, x.Raw, y.Raw, key)); newPatches.Add(new(z.Start, z.Length, z.Raw, value, key));
            details.Add(new("", key, x.Raw, y.Raw, z.Raw, value, "统一数值方式 / Global numeric policy"));
        }
        foreach (var name in b.Keys.Union(m.Keys))
        {
            b.TryGetValue(name, out var x); m.TryGetValue(name, out var y); n.TryGetValue(name, out var z);
            if (x?.Text == y?.Text) continue;
            if (x is null)
            {
                if (z is not null) throw new InvalidDataException("新增对象已被占用 / Added object already exists: " + name);
                additions.Add(name); continue;
            }
            if (z is null || y is not null && (x.Type != y.Type || x.Type != z.Type)) throw new InvalidDataException("对象缺失或类型变化 / Object missing or type changed: " + name);
            string desired;
            if (y is null)
            {
                if (z.Text != x.Text) throw new InvalidDataException("删除对象在新版已改变 / Deleted object changed upstream: " + name);
                desired = "";
            }
            else desired = MergeObject(name, x.Text, y.Text, z.Text, policy, details, review);
            oldPatches.Add(new(x.Start, x.Length, x.Text, y?.Text ?? "", name));
            newPatches.Add(new(z.Start, z.Length, z.Text, desired, name));
        }
        var reconstructed = SemicolonCsvDocument.ApplyReplacements(before, oldPatches);
        if (additions.Count > 0)
        {
            // Appending a complete declaration suffix is unambiguous; arbitrary
            // interleaved insertions remain represented in the byte payload.
            if (!after.StartsWith(reconstructed, StringComparison.Ordinal)) throw new InvalidDataException("新增声明位置或其他原文变化需核对 / Declaration insertion or other text changes require review");
            var suffix = after[reconstructed.Length..];
            if (!Objects(suffix).Keys.ToHashSet().SetEquals(additions)) throw new InvalidDataException("新增声明范围不完整 / Incomplete appended declaration range");
            var separator = target.Length > 0 && target[^1] is not ('\r' or '\n') && suffix.Length > 0 && suffix[0] is not ('\r' or '\n') ? (target.Contains("\r\n") ? "\r\n" : "\n") : "";
            newPatches.Add(new(target.Length, 0, "", separator + suffix, "append")); reconstructed += suffix;
        }
        if (reconstructed != after) throw new InvalidDataException("还有常量、注释或未识别结构的变化，完整原文已保存 / Additional constants, comments or unknown structure changes are preserved for review");
        review.Complete();
        return SemicolonCsvDocument.ApplyReplacements(target, newPatches);
    }
    private static string MergeObject(string name, string before, string after, string target, NumericPolicy policy, List<ChangeDetail> details, ScalarReview review)
    {
        var b = Cells(before); var m = Cells(after); var n = Cells(target);
        if (target != before)
        {
            var fields = MergeFieldPresence(name, before, after, target, b, m, n, details);
            if (fields != (before, after, target))
            {
                (before, after, target) = fields;
                b = Cells(before); m = Cells(after); n = Cells(target);
            }
        }
        var oldPatches = new List<TextReplacement>(); var newPatches = new List<TextReplacement>();
        MergeMaps(name, before, after, target, policy, details, oldPatches, newPatches);
        foreach (var key in b.Keys.Intersect(m.Keys))
        {
            var x = b[key]; var y = m[key]; if (x.Raw == y.Raw) continue;
            if (oldPatches.Any(p => x.Start >= p.Offset && x.Start + x.Length <= p.Offset + p.Length)) continue;
            if (!n.TryGetValue(key, out var z)) throw new InvalidDataException("字段无法唯一定位 / Field cannot be uniquely located: " + name + " / " + key);
            string desired; ChangeTextConflict? conflict = null; var status = x.Numeric ? "统一数值方式 / Global numeric policy" : "原文 / Literal";
            try
            {
                if (x.Numeric && y.Numeric && z.Numeric) desired = Calculate(x, y, z, policy);
                else if (z.Raw == x.Raw) desired = y.Raw;
                else if (z.Raw == y.Raw && !decimal.TryParse(x.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) desired = z.Raw;
                else if (SupportedScalar(x, y, z)) desired = review.Resolve(name, x, y, z, out conflict, out status);
                else throw new InvalidDataException("双方修改或数值语义未知 / Concurrent edit or unknown numeric meaning: " + name + " / " + key);
            }
            catch (Exception e) when (e is InvalidDataException or FormatException or OverflowException)
            { details.Add(new(name, key, x.Raw, y.Raw, z.Raw, "", e.Message)); throw; }
            details.Add(new(name, key, x.Raw, y.Raw, z.Raw, conflict is not null && !review.Resolved(conflict) ? "" : desired, status) { Conflict = conflict });
            oldPatches.Add(new(x.Start, x.Length, x.Raw, y.Raw, key)); newPatches.Add(new(z.Start, z.Length, z.Raw, desired, key));
        }
        if (SemicolonCsvDocument.ApplyReplacements(before, oldPatches) != after)
        {
            if (target == before) return after;
            throw new InvalidDataException("对象结构或原文也有变化，需核对 / Object structure or original text also changed: " + name);
        }
        return SemicolonCsvDocument.ApplyReplacements(target, newPatches);
    }
    private static string Calculate(Cell b, Cell m, Cell n, NumericPolicy policy)
    {
        decimal Number(string value) => decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        var old = Number(b.Raw); var changed = Number(m.Raw); var current = Number(n.Raw);
        if (policy == NumericPolicy.Ratio && old == 0) throw new InvalidDataException("零基础值无法记录比例 / Ratio is undefined for a zero baseline: " + b.Key);
        var value = policy == NumericPolicy.Delta ? checked(current + (changed - old)) : checked(current * changed / old);
        if (b.Integer) value = decimal.Round(value, 0, MidpointRounding.AwayFromZero);
        if (b.NonNegative && value < 0 || b.Ecm && (value < -1 || value > 0)) throw new InvalidDataException("计算结果超出字段范围 / Result outside field limits: " + b.Key);
        return value.ToString("G29", CultureInfo.InvariantCulture);
    }
    private static Dictionary<string, Cell> Cells(string source)
    {
        var doc = new NdfSyntaxDocument(source); var result = new Dictionary<string, Cell>(StringComparer.Ordinal); var ambiguous = new HashSet<string>();
        var types = doc.Tokens.Zip(doc.Tokens.Skip(1)).Where(p => p.Second.Text == "(" && p.First.Text != "MAP").Select(p => p.First.Text).Distinct();
        foreach (var type in types)
        {
            var constructors = doc.FindConstructors(type); if (constructors.Count != 1) continue;
            foreach (var a in doc.EnumerateDirectAssignments(constructors[0]))
            {
                Add(type, a.Name, null, a.Value);
                foreach (var e in doc.ReadMapEntries(a.Value)) Add(type, a.Name, NdfSyntaxDocument.Unquote(doc.Raw(e.Key)), e.Value);
            }
        }
        foreach (var key in ambiguous) result.Remove(key);
        return result;
        void Add(string type, string field, string? map, NdfValueSpan span)
        {
            if (span.StartTokenIndex != span.EndTokenIndex) return;
            var token = doc.Tokens[span.StartTokenIndex]; var key = type + "." + field + (map is null ? "" : "[" + map + "]");
            var numeric = false; var integer = false; var nonNegative = false; var ecm = false;
            var unit = UnitFieldDefinitions.All.FirstOrDefault(d => d.Selector.ModuleTypes.Contains(type) && d.Selector.FieldName == field && d.Selector.MapKey == map &&
                d.Selector.ArgumentName is null && d.Selector.NestedField is null && d.ValueKind is UnitValueKind.Integer or UnitValueKind.Decimal or UnitValueKind.EcmPercent);
            if (unit is not null) { numeric = true; integer = unit.ValueKind == UnitValueKind.Integer; nonNegative = unit.NonNegative; ecm = unit.ValueKind == UnitValueKind.EcmPercent; }
            if (type is "TAmmunitionDescriptor" or "TAmmunitionMissileDescriptor")
            {
                var ammo = WeaponFieldDefinitions.Ammo.FirstOrDefault(d => d.FieldName == field && d.MapKey == map && d.ArgumentName is null && d.Constructor is null && d.ValueKind is WeaponValueKind.Integer or WeaponValueKind.Decimal);
                if (ammo is not null) { numeric = true; integer = ammo.ValueKind == WeaponValueKind.Integer; nonNegative = ammo.NonNegative; }
            }
            if (type == "TStairsDamageTypeEvolutionOverRangeDescriptor" && field is "DistanceGRU" or "AP") { numeric = true; nonNegative = true; }
            var rule = RuleCatalog.All.FirstOrDefault(r => r.Constructor == type && r.Fields.Split(';').Contains(field) && (r.MapKey is null || r.MapKey == map));
            if (rule is not null)
            {
                try
                {
                    var group = RuleWorkspace.Read(rule, source); var c = group.Cells.FirstOrDefault(c => c.Offset == token.Start && c.Length == token.Length);
                    if (c is not null && !c.Boolean) { numeric = true; integer = c.Integer; nonNegative = rule.Number != 16; }
                }
                catch (InvalidDataException) { }
            }
            if (numeric && !decimal.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) numeric = false;
            if (!result.TryAdd(key, new(key, token.Text, token.Start, token.Length, numeric, integer, nonNegative, ecm, ScalarType(type, field, map)))) ambiguous.Add(key);
        }
    }
    private static Dictionary<string, Cell> GlobalCells(string source)
    {
        var cells = new Dictionary<string, Cell>();
        foreach (var r in RuleCatalog.All.Where(r => r.Constructor.Length == 0))
        {
            try
            {
                var group = RuleWorkspace.Read(r, source);
                if (group.Cells.Count != 1 || group.Cells[0].Key != r.Fields || group.Cells[0].Boolean) continue;
                var c = group.Cells[0]; cells.Add(c.Key, new(c.Key, c.Raw, c.Offset, c.Length, true, c.Integer, r.Number != 16));
            }
            catch (InvalidDataException) { }
        }
        return cells;
    }
    private static byte[] MergeCsv(string path, byte[] before, byte[] after, byte[] target, List<ChangeDetail> details, IReadOnlyList<ChangeTextDecision> decisions)
    {
        var b = DecodeText(before); var m = DecodeText(after); var n = DecodeText(target);
        if (b.Encoding.CodePage != m.Encoding.CodePage || !b.Encoding.GetPreamble().SequenceEqual(m.Encoding.GetPreamble()))
            throw new InvalidDataException("记录包含词典编码变化，需人工核对 / Recorded dictionary encoding changed; review required");
        var bd = SemicolonCsvDocument.Parse(b.Text); var md = SemicolonCsvDocument.Parse(m.Text); var nd = SemicolonCsvDocument.Parse(n.Text);
        if (bd.Rows.Count == 0 || md.Rows.Count == 0 || nd.Rows.Count == 0 || bd.Rows[0].Fields[0].Value != "TOKEN" ||
            !bd.Rows[0].Fields.Select(f => f.Value).SequenceEqual(md.Rows[0].Fields.Select(f => f.Value)) ||
            !bd.Rows[0].Fields.Select(f => f.Value).SequenceEqual(nd.Rows[0].Fields.Select(f => f.Value)))
            throw new InvalidDataException("只自动合并同结构 TOKEN 词典 / Only matching TOKEN dictionaries merge automatically");
        var columns = bd.Rows[0].Fields.Select(f => f.Value).ToArray();
        if (columns.Any(string.IsNullOrWhiteSpace) || columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
            throw new InvalidDataException("词典列名称为空或重复 / Empty or duplicate dictionary columns");
        Dictionary<string, CsvRowSpan> Rows(SemicolonCsvDocument d)
        {
            if (d.Rows.Skip(1).Any(r => r.Fields.Count != columns.Length || string.IsNullOrEmpty(r.Fields[0].Value)))
                throw new InvalidDataException("词典行宽或 token 无效 / Invalid dictionary row width or token");
            return d.Rows.Skip(1).ToDictionary(r => r.Fields[0].Value, StringComparer.Ordinal);
        }
        var br = Rows(bd); var mr = Rows(md); var nr = Rows(nd); var patches = new List<TextReplacement>(); var bp = new List<TextReplacement>(); var append = "";
        var used = new HashSet<ChangeTextDecision>(); var unresolved = false;
        string Raw(string s, CsvRowSpan row) => s.Substring(row.Offset, row.Length + row.LineEndLength);
        foreach (var key in br.Keys.Union(mr.Keys))
        {
            br.TryGetValue(key, out var x); mr.TryGetValue(key, out var y); nr.TryGetValue(key, out var z);
            var old = x is null ? "" : Raw(b.Text, x); var changed = y is null ? "" : Raw(m.Text, y); if (old == changed) continue;
            var current = z is null ? "" : Raw(n.Text, z);
            if (x is not null) bp.Add(new(x.Offset, old.Length, old, changed, key)); else append += changed;
            if (x is not null && y is not null && z is not null)
            {
                var rowProof = new List<TextReplacement>();
                for (var i = 1; i < columns.Length; i++)
                {
                    var a = x.Fields[i]; var e = y.Fields[i]; var t = z.Fields[i]; if (a.Raw == e.Raw) continue;
                    rowProof.Add(new(a.Offset - x.Offset, a.Length, a.Raw, e.Raw, columns[i]));
                    var desired = e.Raw; var status = "词典文本 / Dictionary text"; ChangeTextConflict? conflict = null;
                    if (t.Value == e.Value) desired = t.Raw;
                    else if (t.Value != a.Value)
                    {
                        conflict = new(path, key, columns[i], a.Raw, e.Raw, t.Raw);
                        var decision = decisions.SingleOrDefault(d => d.Conflict.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && d.Conflict.Token == key && d.Conflict.Column == columns[i]);
                        if (decision is null) { desired = t.Raw; unresolved = true; status = "文本冲突：待选择 / Text conflict: choose an outcome"; }
                        else
                        {
                            if (decision.Conflict != conflict) throw new InvalidDataException("冲突依据已变化，请清除旧选择重新核对 / Conflict evidence changed; clear the previous choice and review again");
                            used.Add(decision);
                            if (decision.Choice == TextConflictChoice.KeepTarget) { desired = t.Raw; status = "保留新版（原修改未还原） / Keep target (recorded change omitted)"; }
                            else status = "使用记录内容 / Use recorded value";
                        }
                    }
                    details.Add(new(key, columns[i], a.Raw, e.Raw, t.Raw, conflict is not null && !used.Any(d => d.Conflict == conflict) ? "" : desired, status) { Conflict = conflict });
                    patches.Add(new(t.Offset, t.Length, t.Raw, desired, key + "/" + columns[i]));
                }
                if (SemicolonCsvDocument.ApplyReplacements(old, rowProof) != changed)
                    throw new InvalidDataException("词典行结构或换行变化需核对 / Dictionary row structure or line ending changed: " + key);
                continue;
            }
            if (current != old && current != changed) throw new InvalidDataException("词典新增、删除或缺失 token 冲突 / Added, deleted or missing dictionary token conflict: " + key);
            if (z is not null) patches.Add(new(z.Offset, current.Length, current, changed, key));
            else if (changed.Length > 0) patches.Add(new(n.Text.Length, 0, "", changed, key));
            details.Add(new(key, "CSV", old, changed, current, changed, "词典 / Dictionary"));
        }
        if (SemicolonCsvDocument.ApplyReplacements(b.Text, bp) + append != m.Text) throw new InvalidDataException("词典顺序或格式变化需核对 / Dictionary order or format change requires review");
        if (used.Count != decisions.Count) throw new InvalidDataException("冲突选择已不对应当前冲突，请清除后重新核对 / Conflict choices no longer match the current conflicts; clear and review again");
        if (unresolved) throw new InvalidDataException("词典文本双方均修改，请在明细中逐项选择 / Dictionary text changed on both sides; choose each outcome in the details");
        // Coalesce same-position appends to maintain order and avoid overlapping spans.
        var inserts = string.Concat(patches.Where(p => p.Length == 0).Select(p => p.Target));
        if (inserts.Length > 0 && n.Text.Length > 0 && n.Text[^1] is not ('\r' or '\n')) inserts = (n.Text.Contains("\r\n") ? "\r\n" : "\n") + inserts;
        patches.RemoveAll(p => p.Length == 0); if (inserts.Length > 0) patches.Add(new(n.Text.Length, 0, "", inserts, "append"));
        var text = SemicolonCsvDocument.ApplyReplacements(n.Text, patches);
        return n.Encoding.GetPreamble().Concat(n.Encoding.GetBytes(text)).ToArray();
    }
    private static (string Text, Encoding Encoding) DecodeText(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 254 })) return (new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2), new UnicodeEncoding(false, true, true));
        if (bytes.AsSpan().StartsWith(new byte[] { 254, 255 })) return (new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2), new UnicodeEncoding(true, true, true));
        var bom = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }); return (new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), new UTF8Encoding(bom, true));
    }
    public static string DecodeNdf(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) || bytes.AsSpan().StartsWith(new byte[] { 255, 254 }) || bytes.AsSpan().StartsWith(new byte[] { 254, 255 }))
            throw new InvalidDataException("NDF 必须为 UTF-8 无 BOM / NDF must be UTF-8 without BOM");
        return new UTF8Encoding(false, true).GetString(bytes);
    }
    public static void ValidateBytes(string path, byte[]? bytes)
    {
        if (bytes is null) return;
        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = SemicolonCsvDocument.Parse(DecodeText(bytes).Text);
            if (csv.Rows.Count > 0 && csv.Rows[0].Fields.FirstOrDefault()?.Value == "TOKEN" && csv.Rows.Skip(1).GroupBy(r => r.Fields[0].Value).Any(g => g.Count() > 1))
                throw new InvalidDataException("词典 token 重复 / Duplicate dictionary token: " + path);
            return;
        }
        if (!path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)) return;
        var text = DecodeNdf(bytes); var doc = new NdfSyntaxDocument(text); var stack = new Stack<string>();
        ValidateLexicalEnd(text);
        foreach (var t in doc.Tokens)
        {
            if (t.Text is "(" or "[") stack.Push(t.Text);
            if (t.Text is ")" or "]") if (!stack.TryPop(out var opening) || opening != (t.Text == ")" ? "(" : "[")) throw new InvalidDataException("NDF 括号不匹配 / NDF brackets do not match: " + path);
        }
        if (stack.Count != 0) throw new InvalidDataException("NDF 括号未闭合 / Unclosed NDF brackets: " + path);
        foreach (var c in Objects(text).Values.SelectMany(o => Cells(o.Text).Values).Concat(Cells(text).Values).Where(c => c.Numeric)) ValidateQuantity(c);
        ValidateMapKeys(doc, path);
        foreach (var rule in RuleCatalog.All.Where(r => r.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase) && r.Number != 54))
        {
            RuleGroup group;
            try { group = RuleWorkspace.Read(rule, text); } catch (InvalidDataException) { continue; }
            RuleWorkspace.Validate(group, group.Cells.ToDictionary(c => c.Key, c => c.Raw));
        }
    }
    private static void ValidateQuantity(Cell c)
    {
        var value = decimal.Parse(c.Raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        var aviation = AviationMovement.Fields.FirstOrDefault(f => f.Selector.ModuleTypes.Any(t => c.Key == t + "." + f.Selector.FieldName));
        if (aviation is not null && !AviationMovement.ValidTarget(aviation.Key, (double)value, out var error)) throw new InvalidDataException(error);
        if (c.NonNegative && value < 0 || c.Ecm && (value < -1 || value > 0) || c.Integer && decimal.Truncate(value) != value)
            throw new InvalidDataException("字段数值无效 / Invalid field value: " + c.Key);
    }
    private static void ValidateLexicalEnd(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/') { var end = text.IndexOf('\n', i + 2); if (end < 0) return; i = end; }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*') { var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal); if (end < 0) throw new InvalidDataException("NDF 注释未闭合 / Unclosed NDF comment"); i = end + 1; }
            else if (text[i] is '\'' or '"')
            {
                var quote = text[i]; var closed = false;
                while (++i < text.Length) { if (text[i] == '\\') { i++; continue; } if (text[i] == quote) { closed = true; break; } }
                if (!closed) throw new InvalidDataException("NDF 字符串未闭合 / Unclosed NDF string");
            }
        }
    }
}
