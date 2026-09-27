using System.Globalization;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

/// <summary>Direct inline nodes, in source order. IDs are meaningful only with the saved object baseline.</summary>
public sealed class WeaponStructureSyntax
{
    public sealed record Mount(string Id, string TurretId, NdfValueSpan Span, string Body, int Box, string Ammo);
    public sealed record Turret(string Id, NdfValueSpan Span, string Body, string Type, NdfValueSpan List, IReadOnlyList<Mount> Mounts);
    public static readonly string[] Types = ["TTurretTwoAxisDescriptor", "TTurretUnitDescriptor", "TTurretInfanterieDescriptor", "TTurretBombardierDescriptor"];
    public string Text { get; }
    public NdfSyntaxDocument Document { get; }
    public NdfValueSpan Salves { get; }
    public NdfValueSpan TurretList { get; }
    public IReadOnlyList<NdfValueSpan> Boxes { get; }
    public IReadOnlyList<Turret> Turrets { get; }
    public IEnumerable<Mount> Mounts => Turrets.SelectMany(t => t.Mounts);

    public WeaponStructureSyntax(string text)
    {
        Text = text; Document = new(text);
        var root = Constructor(Document, new(0, Document.Tokens.Count - 1), "TWeaponManagerModuleDescriptor", declaration: true);
        Salves = Assignment(Document, root, "Salves"); TurretList = Assignment(Document, root, "TurretDescriptorList");
        Boxes = Array(Document, Salves);
        foreach (var box in Boxes)
            if (!int.TryParse(Document.Raw(box), out var n) || n < 0) throw Error("弹药箱库存不是非负整数");
        var turrets = new List<Turret>();
        foreach (var span in Array(Document, TurretList))
        {
            var type = Document.Tokens[span.StartTokenIndex].Text;
            if (!Types.Contains(type)) throw Error("暂不支持该炮塔结构：" + type);
            var node = Constructor(Document, span, type); var list = Assignment(Document, node, "MountedWeaponDescriptorList");
            var tid = "t:" + turrets.Count; var mounts = new List<Mount>();
            foreach (var item in Array(Document, list))
            {
                var mount = Constructor(Document, item, "TMountedWeaponDescriptor");
                var box = Document.Raw(Assignment(Document, mount, "AmmoBoxIndex"));
                if (!int.TryParse(box, out var ix) || ix < 0 || ix >= Boxes.Count) throw Error("挂载弹药箱索引超出范围");
                var ammo = Document.Raw(Assignment(Document, mount, "Ammunition"));
                mounts.Add(new(tid + "/m:" + mounts.Count, tid, item, Document.Raw(item), ix, ammo));
            }
            turrets.Add(new(tid, span, Document.Raw(span), type, list, mounts));
        }
        Turrets = turrets;
        if (Document.FindAssignmentsAnywhere("AmmoBoxIndex").Count != Mounts.Count())
            throw Error("存在无法归属到已知挂载的弹药箱索引，不能重编号");
    }

    public static TransactionValidationException Error(string message) => new(message);
    public static NdfConstructorSpan Constructor(NdfSyntaxDocument doc, NdfValueSpan span, string type, bool declaration = false)
    {
        var nodes = doc.FindConstructors(type, span);
        if (nodes.Count != 1 || (!declaration && (nodes[0].TypeTokenIndex != span.StartTokenIndex || nodes[0].CloseTokenIndex != span.EndTokenIndex)))
            throw Error("结构无法唯一定位：" + type);
        return nodes[0];
    }
    public static NdfValueSpan Assignment(NdfSyntaxDocument doc, NdfConstructorSpan node, string field)
    {
        var values = doc.FindDirectAssignments(node, field);
        return values.Count == 1 ? values[0] : throw Error("字段无法唯一定位：" + field);
    }
    public static IReadOnlyList<NdfValueSpan> Array(NdfSyntaxDocument doc, NdfValueSpan span)
    {
        if (doc.Tokens[span.StartTokenIndex].Text != "[" || doc.Tokens[span.EndTokenIndex].Text != "]") throw Error("只支持直接数组");
        return doc.ReadArrayElements(span);
    }
    public static Dictionary<string, string> Values(string body, string type, IEnumerable<string> fields)
    {
        var doc = new NdfSyntaxDocument(body); var node = Constructor(doc, new(0, doc.Tokens.Count - 1), type, true);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var matches = doc.FindDirectAssignments(node, field);
            if (matches.Count > 1) throw Error("字段重复：" + field);
            if (matches.Count == 1) result[field] = doc.Raw(matches[0]);
        }
        return result;
    }
    public static string Set(string body, string type, IReadOnlyDictionary<string, string> values)
    {
        var doc = new NdfSyntaxDocument(body); var node = Constructor(doc, new(0, doc.Tokens.Count - 1), type, true);
        var edits = values.Select(p =>
        {
            var span = Assignment(doc, node, p.Key);
            return new TextReplacement(doc.StartOffset(span), doc.Length(span), doc.Raw(span), p.Value, p.Key);
        }).ToArray();
        return UnitProjectGraph.Patch(body, edits);
    }
    public static string Rename(string body, string name)
    {
        var doc = new NdfSyntaxDocument(body); var tokens = doc.Tokens;
        var i = tokens[0].Text == "export" ? 1 : 0;
        if (tokens.Count <= i + 2 || tokens[i + 1].Text != "is") throw Error("对象声明无法定位");
        return UnitProjectGraph.Patch(body, [new(tokens[i].Start, tokens[i].Text.Length, tokens[i].Text, name, "独立对象")]);
    }
    public static string EditList(string body, NdfValueSpan span, IReadOnlySet<int> removed, IReadOnlyDictionary<int, string> replaced, IReadOnlyList<string> appended)
    {
        var doc = new NdfSyntaxDocument(body); var elements = Array(doc, span); var edits = new List<TextReplacement>();
        // Delete element tokens and trailing separators independently: adjacent removals never overlap.
        for (var i = 0; i < elements.Count; i++)
        {
            var el = elements[i];
            if (removed.Contains(i))
            {
                edits.Add(new(doc.StartOffset(el), doc.Length(el), doc.Raw(el), "", "删除列表元素"));
                var next = el.EndTokenIndex + 1;
                if (next < span.EndTokenIndex && doc.Tokens[next].Text == ",") edits.Add(new(doc.Tokens[next].Start, 1, ",", "", "删除分隔符"));
            }
            else if (replaced.TryGetValue(i, out var value)) edits.Add(new(doc.StartOffset(el), doc.Length(el), doc.Raw(el), value, "修改列表元素"));
        }
        if (appended.Count > 0)
        {
            var nl = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var last = Enumerable.Range(0, elements.Count).Where(i => !removed.Contains(i)).DefaultIfEmpty(-1).Last();
            if (last >= 0 && doc.Tokens[elements[last].EndTokenIndex + 1].Text != ",")
                edits.Add(new(doc.Tokens[elements[last].EndTokenIndex].End, 0, "", ",", "补分隔符"));
            var at = doc.Tokens[span.EndTokenIndex].Start;
            var indent = Indent(body, elements.Count > 0 ? doc.StartOffset(elements[0]) : at) + (elements.Count == 0 ? "    " : "");
            var blocks = appended.Select(b => IndentBlock(b, indent, nl));
            edits.Add(new(at, 0, "", nl + string.Join("," + nl, blocks) + "," + nl + Indent(body, at), "追加列表元素"));
        }
        return UnitProjectGraph.Patch(body, edits);
    }
    public static string Indent(string text, int at)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, at - 1)); start = start < 0 ? 0 : start + 1;
        var end = start; while (end < at && text[end] is ' ' or '\t') end++;
        return text[start..end];
    }
    private static string IndentBlock(string body, string indent, string nl)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var pad = lines.Skip(1).Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return indent + lines[0].TrimStart() + (lines.Length == 1 ? "" : nl + string.Join(nl, lines.Skip(1).Select(l => indent + l[Math.Min(pad, l.Length)..])));
    }
    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
