using System.Globalization;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Rules;

public sealed record DamageSlot(string Family, int Index)
{
    public string Key => Family + "/" + Index;
}
public sealed record DamageCell(DamageSlot Attack, DamageSlot Resistance, string Raw, int Offset, int Length)
{
    public string Key => Attack.Key + "/" + Resistance.Key;
}
public sealed record DamageMatrix(string File, string Name, string Shape, IReadOnlyList<DamageSlot> Rows,
    IReadOnlyList<DamageSlot> Columns, IReadOnlyList<DamageCell> Cells);
public sealed record DamageStair(NdfObjectInfo Source, string Body, string Distance, string AP);
public sealed record DamageChange(string Key, string Before, string After);
public sealed record DamageRuleState(int Version, string Shape, IReadOnlyList<DamageChange> Changes);

/// <summary>Semantic matrix coordinates and native staircase definitions; no fixed stock family list.</summary>
public sealed class DamageWorkspace
{
    public UnitProjectGraph Graph { get; }
    public DamageMatrix? Matrix { get; private set; }
    public List<DamageStair> Stairs { get; } = [];
    public List<string> Diagnostics { get; } = [];
    public string MatrixError { get; private set; } = "";
    public DamageWorkspace(string root, IReadOnlyList<PlannedFileChange>? candidates = null)
    {
        Graph = new(root, candidates);
        try { Matrix = ReadMatrix(Graph); }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException)
        { MatrixError = e.Message; Diagnostics.Add(e.Message); }
        foreach (var obj in Graph.Objects.Where(o => o.TypeName == "TStairsDamageTypeEvolutionOverRangeDescriptor"))
        {
            try
            {
                if (Graph.FindObjects(obj.Name).Count != 1) throw new InvalidDataException("距离规则重名：" + obj.Name);
                var body = Graph.Body(obj); var doc = new NdfSyntaxDocument(body);
                var owner = doc.FindConstructors(obj.TypeName).Single();
                var distance = doc.Raw(Required(doc, owner, "DistanceGRU"));
                var ap = doc.Raw(Required(doc, owner, "AP"));
                Positive(distance); Positive(ap);
                Stairs.Add(new(obj, body, distance, ap));
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException)
            { Diagnostics.Add(obj.Name + ": " + e.Message); }
        }
    }
    public static decimal Number(string raw)
    {
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidDataException("请输入有限非负数值");
        if(value==0 && raw.Split('e','E')[0].Any(c=>c is >= '1' and <= '9'))
            throw new InvalidDataException("数值小于当前编辑精度，请保留原值");
        return value;
    }
    public static decimal Positive(string raw)
    {
        var value = Number(raw);
        if (value <= 0) throw new InvalidDataException("距离间隔与每阶变化量必须为正数；特殊规则暂不支持");
        return value;
    }
    public static string Format(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    public static NdfValueSpan Required(NdfSyntaxDocument doc, NdfConstructorSpan root, string field)
    {
        var values = doc.FindDirectAssignments(root, field);
        return values.Count == 1 ? values[0] : throw new InvalidDataException("缺少唯一参数：" + field);
    }
    private static DamageMatrix ReadMatrix(UnitProjectGraph graph)
    {
        var containers = graph.Objects.Where(o => o.TypeName == "TGameplayDamageResistanceContainer").ToArray();
        if (containers.Length != 1) throw new InvalidDataException("伤害矩阵缺失或不唯一");
        var obj = containers[0]; var source = graph.FileOf(obj);
        var doc = new NdfSyntaxDocument(source.Text, obj.CharacterOffset, obj.CharacterLength);
        var root = doc.FindConstructors(obj.TypeName).Single();
        List<(string Family, int Count)> ReadCounts(string field)
        {
            var span = Required(doc, root, field);
            var entries = doc.ReadMapEntries(span);
            if (doc.Tokens[span.StartTokenIndex].Text != "[" || entries.Count == 0 || entries.Count != doc.ReadArrayElements(span).Count)
                throw new InvalidDataException("伤害家族计数表格式不支持：" + field);
            var values = entries.Select(e => (Family: doc.Raw(e.Key), Count: int.TryParse(doc.Raw(e.Value), out var n) ? n : 0)).ToList();
            if (values.Any(v => v.Count < 1) || values.Select(v => v.Family).Distinct().Count() != values.Count)
                throw new InvalidDataException("伤害家族重复或档位数无效");
            return values;
        }
        var damage = ReadCounts("DamageFamilyCounts"); var resistance = ReadCounts("ResistanceFamilyCounts");
        var names = damage.Concat(resistance).Select(f => f.Family).ToHashSet(StringComparer.Ordinal);
        var declarations = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in graph.Files.Values)
        {
            var tokens = file.Syntax.Tokens; var depth = 0;
            for (var i = 0; i + 2 < tokens.Count; i++)
            {
                if (tokens[i].Text is "(" or "[") { depth++; continue; }
                if (tokens[i].Text is ")" or "]") { depth--; continue; }
                if (depth != 0 || !names.Contains(tokens[i].Text) || tokens[i + 1].Text != "is") continue;
                if (!declarations.TryGetValue(tokens[i].Text, out var list)) declarations[tokens[i].Text] = list = [];
                var next=i+3;
                if(next<tokens.Count && tokens[next].Text is "export" or "private")next++;
                var literal=next>=tokens.Count || next+1<tokens.Count && tokens[next+1].Text=="is";
                list.Add(literal?tokens[i + 2].Text:"(unsupported expression)");
            }
        }
        // Validate numeric identity and engine name registration before interpreting positional Values.
        void Registration(List<(string Family, int Count)> families, string type)
        {
            var registrations = graph.Objects.Where(o => o.TypeName == type).ToArray();
            if (registrations.Length != 1) throw new InvalidDataException("家族名称注册缺失或不唯一：" + type);
            var reg = new NdfSyntaxDocument(graph.Body(registrations[0]));
            var values = reg.ReadArrayElements(Required(reg, reg.FindConstructors(type).Single(), "Values"))
                .Select(v => NdfSyntaxDocument.Unquote(reg.Raw(v))).ToArray();
            if (!values.SequenceEqual(families.Select(f => f.Family))) throw new InvalidDataException("家族注册与矩阵顺序不一致");
            for (var i = 0; i < families.Count; i++)
            {
                var matches = declarations.GetValueOrDefault(families[i].Family, []);
                if (matches.Count != 1 || !int.TryParse(matches[0], out var id) || id != i)
                    throw new InvalidDataException("家族ID与矩阵顺序不一致：" + families[i].Family);
            }
        }
        Registration(damage, "TDamageFamilyList"); Registration(resistance, "TResistanceFamilyList");
        var rows = damage.SelectMany(f => Enumerable.Range(1, f.Count).Select(i => new DamageSlot(f.Family, i))).ToArray();
        var cols = resistance.SelectMany(f => Enumerable.Range(1, f.Count).Select(i => new DamageSlot(f.Family, i))).ToArray();
        var valuesSpan = Required(doc, root, "Values");
        if (doc.Tokens[valuesSpan.StartTokenIndex].Text != "[") throw new InvalidDataException("伤害矩阵不是直接数组");
        var matrixRows = doc.ReadArrayElements(valuesSpan);
        if (matrixRows.Count != rows.Length) throw new InvalidDataException("伤害矩阵行数与档位数不一致");
        var cells = new List<DamageCell>();
        for (var r = 0; r < rows.Length; r++)
        {
            var values = doc.ReadArrayElements(matrixRows[r]);
            if (doc.Tokens[matrixRows[r].StartTokenIndex].Text != "[" || values.Count != cols.Length)
                throw new InvalidDataException("伤害矩阵列数与抗性档位数不一致");
            for (var c = 0; c < cols.Length; c++)
                cells.Add(new(rows[r], cols[c], doc.Raw(values[c]), doc.StartOffset(values[c]), doc.Length(values[c])));
        }
        var shape = JsonSerializer.Serialize(new { Damage = damage.Select(f => new { f.Family, f.Count }), Resistance = resistance.Select(f => new { f.Family, f.Count }) });
        return new(source.Path, obj.Name, shape, rows, cols, cells);
    }
    public DamageStair? ResolveStair(string file, string raw)
    {
        var obj = Graph.Resolve(file, raw);
        if (obj is null && raw.StartsWith("~/", StringComparison.Ordinal) && !raw[2..].Contains('/'))
        {
            var matches = Graph.FindObjects(raw[2..]);
            if (matches.Count == 1 && Graph.IsExported(matches[0]) &&
                string.Equals(Path.GetDirectoryName(file.Replace('\\', '/')), Path.GetDirectoryName(matches[0].RelativeSourceFile.Replace('\\', '/')), StringComparison.OrdinalIgnoreCase)) obj = matches[0];
        }
        return obj is null ? null : Stairs.SingleOrDefault(s => UnitProjectGraph.Same(obj, s.Source));
    }
    public string Reference(string file, DamageStair stair)
    {
        var relative = "~/" + stair.Source.Name;
        if (ResolveStair(file, relative) == stair) return relative;
        var raw = Graph.ReferenceTo(file, stair.Source);
        return ResolveStair(file, raw) == stair ? raw : throw new InvalidDataException("距离规则作用域不兼容");
    }
    public IReadOnlyList<string> References(string name) => ReferenceChain(false,name);
    public IReadOnlyList<string> FullReferences(params string[] names) => ReferenceChain(true,names);
    private Dictionary<string,List<(string Location,string? Owner)>>? _referenceIndex;
    private IReadOnlyList<string> ReferenceChain(bool expand,params string[] names)
    {
        if(_referenceIndex is null)
        {
            _referenceIndex=new(StringComparer.Ordinal);
            var relevant=Graph.Objects.Select(o=>o.Name).Concat(Matrix?.Rows.Select(r=>r.Family)??[]).Concat(Matrix?.Columns.Select(c=>c.Family)??[]).ToHashSet(StringComparer.Ordinal);
            foreach(var file in Graph.Files.Values)
            {
                var owners=file.Objects.OrderBy(o=>o.CharacterOffset).ToArray();var ownerIndex=0;var tokens=file.Syntax.Tokens;
                for(var i=0;i<tokens.Count;i++)
                {
                    var t=tokens[i];if(t.Text.Length==0||t.Text[0] is '\'' or '"'||i+1<tokens.Count&&tokens[i+1].Text=="is")continue;
                    while(ownerIndex<owners.Length && t.Start>=owners[ownerIndex].CharacterOffset+owners[ownerIndex].CharacterLength)ownerIndex++;
                    var owner=ownerIndex<owners.Length&&t.Start>=owners[ownerIndex].CharacterOffset?owners[ownerIndex]:null;
                    var key=NdfSyntaxDocument.Leaf(t.Text);
                    if(!relevant.Contains(key))continue;
                    if(!_referenceIndex.TryGetValue(key,out var refs))_referenceIndex[key]=refs=[];
                    refs.Add((file.Path+" · "+(owner?.Name??"(global)"),owner?.Name));
                }
            }
        }
        var pending=new Queue<string>(names);var visited=new HashSet<string>(StringComparer.Ordinal);var result=new HashSet<string>(StringComparer.Ordinal);
        while(pending.TryDequeue(out var name))
        {
            if(!visited.Add(name))continue;
            foreach(var reference in _referenceIndex.GetValueOrDefault(name,[]))
            {
                result.Add(reference.Location);
                if(expand && reference.Owner is {} owner && owner!=name)pending.Enqueue(owner);
            }
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }
    public static DamageRuleState Read(DraftOperation op)
    {
        try
        {
            var state = JsonSerializer.Deserialize<DamageRuleState>(op.TargetRaw);
            if (state is null || state.Version != 1 || string.IsNullOrEmpty(state.Shape) || state.Changes is null || state.Changes.Count == 0 ||
                state.Changes.Any(c=>c is null || string.IsNullOrEmpty(c.Key) || c.Before is null || c.After is null) ||
                state.Changes.Select(c => c.Key).Distinct().Count() != state.Changes.Count) throw new JsonException();
            return state;
        }
        catch (JsonException) { throw new InvalidDataException("伤害规则草稿格式无效"); }
    }
    public DraftOperation MatrixOperation(IReadOnlyList<DamageChange> changes)
    {
        var m = Matrix ?? throw new InvalidDataException(MatrixError);
        var op = Operation(m.File, m.Name, "matrix", new(1, m.Shape, changes), "伤害与抗性矩阵", changes.Count);
        Validate(op); return op;
    }
    public DraftOperation StairOperation(DamageStair stair, string distance, string ap)
    {
        Positive(distance); Positive(ap);
        var op = Operation(stair.Source.RelativeSourceFile, stair.Source.Name, "stair", new(1, stair.Body,
            [new("DistanceGRU", stair.Distance, distance), new("AP", stair.AP, ap)]), "共享距离规则", References(stair.Source.Name).Count);
        Validate(op); return op;
    }
    private static DraftOperation Operation(string file, string name, string key, DamageRuleState state, string title, int count) =>
        new(DraftOperation.CreateId(DraftTargetKind.DamageRule, file, name, key), null, DraftTargetKind.DamageRule,
            "rules", file, name, "DamageRuleV1", key, key, "DamageRuleV1", "", "", count.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(state), title + " · " + name + " · " + count, null, false, DateTimeOffset.UtcNow);
    public void Validate(DraftOperation op)
    {
        var state = Read(op);
        if (op.FieldKey == "matrix")
        {
            var m = Matrix ?? throw new InvalidDataException(MatrixError);
            if (op.ObjectName != m.Name || op.RelativeSourceFile != m.File || state.Shape != m.Shape) throw new InvalidDataException("伤害矩阵结构已变化");
            var cells = m.Cells.ToDictionary(c => c.Key);
            foreach (var change in state.Changes)
            {
                if (!cells.TryGetValue(change.Key, out var cell) || cell.Raw != change.Before) throw new InvalidDataException("伤害矩阵字段基线已变化：" + change.Key);
                Number(change.Before); Number(change.After);
            }
        }
        else if (op.FieldKey == "stair")
        {
            var stair = Stairs.Single(s => s.Source.Name == op.ObjectName && s.Source.RelativeSourceFile == op.RelativeSourceFile);
            if (stair.Body != state.Shape || !state.Changes.Select(c => c.Key).Order().SequenceEqual(new[] { "AP", "DistanceGRU" })) throw new InvalidDataException("距离规则基线已变化");
            foreach (var c in state.Changes)
            {
                if (c.Before != (c.Key == "AP" ? stair.AP : stair.Distance)) throw new InvalidDataException("距离规则字段基线已变化");
                Positive(c.After);
            }
        }
        else throw new InvalidDataException("未知伤害规则操作");
    }
    public ResolvedDraftOperation Resolve(DraftOperation op)
    {
        try { Validate(op); return new(op, DraftResolutionStatus.Active, ""); }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException)
        { return new(op, DraftResolutionStatus.Conflict, e.Message); }
    }
    public void Plan(IReadOnlyList<DraftOperation> operations, List<PlannedFileChange> files)
    {
        var ops = operations.Where(o => o.TargetKind == DraftTargetKind.DamageRule).ToArray();
        if (ops.Length == 0) return;
        var final = new DamageWorkspace(Graph.Root, files); var candidates = new CandidateTextFiles(Graph.Root, files);
        var patches = new Dictionary<string, List<TextReplacement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in ops)
        {
            Validate(op); final.Validate(op);
            if (!patches.TryGetValue(op.RelativeSourceFile, out var list)) patches[op.RelativeSourceFile] = list = [];
            foreach (var change in Read(op).Changes)
            {
                if (change.After == change.Before) continue;
                if (op.FieldKey == "matrix")
                {
                    var cell = final.Matrix!.Cells.Single(c => c.Key == change.Key);
                    list.Add(new(cell.Offset, cell.Length, cell.Raw, change.After, op.Summary));
                }
                else
                {
                    var stair = final.Stairs.Single(s => s.Source.Name == op.ObjectName);
                    var doc = new NdfSyntaxDocument(final.Graph.FileOf(stair.Source).Text, stair.Source.CharacterOffset, stair.Source.CharacterLength);
                    var span = Required(doc, doc.FindConstructors(stair.Source.TypeName).Single(), change.Key);
                    list.Add(new(doc.StartOffset(span), doc.Length(span), change.Before, change.After, op.Summary));
                }
            }
        }
        foreach (var (path, patch) in patches) candidates.Set(path, SemicolonCsvDocument.ApplyReplacements(candidates.Get(path), patch));
        candidates.Complete("伤害与抗性共享规则；所有匹配对象（双方）适用");
        var check = new DamageWorkspace(Graph.Root, files);
        foreach (var op in ops)
            foreach (var c in Read(op).Changes)
            {
                var actual = op.FieldKey == "matrix" ? check.Matrix?.Cells.Single(v => v.Key == c.Key).Raw :
                    c.Key == "AP" ? check.Stairs.Single(s => s.Source.Name == op.ObjectName).AP : check.Stairs.Single(s => s.Source.Name == op.ObjectName).Distance;
                if (actual != c.After) throw new TransactionValidationException("伤害规则候选回读不一致");
            }
    }
}
