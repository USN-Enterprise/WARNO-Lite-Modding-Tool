using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Units;

public sealed record UnitIdentityEdit(int Version, string Guid, string NewName);

public static class UnitIdentityEditing
{
    public static bool IsKind(DraftTargetKind kind) => kind is DraftTargetKind.UnitRename or DraftTargetKind.UnitRegistration;
    public static string GuidOf(string body)
    {
        var doc = new NdfSyntaxDocument(body);
        var top = doc.FindConstructors("TEntityDescriptor");
        if (top.Count != 1) throw new InvalidDataException("单位声明不唯一");
        var guid = doc.FindDirectAssignments(top[0], "DescriptorId");
        return guid.Count == 1 ? doc.Raw(guid[0]) : throw new InvalidDataException("单位GUID不唯一");
    }
    public static UnitIdentityEdit Read(DraftOperation op) => JsonSerializer.Deserialize<UnitIdentityEdit>(op.TargetRaw) is { Version: 1 } value ? value : throw new InvalidDataException("单位身份草稿版本无效");
    public static void ValidateName(string name)
    {
        if (!Regex.IsMatch(name, "^Descriptor_Unit_[A-Za-z0-9_]+$")) throw new TransactionValidationException("单位变量名须以Descriptor_Unit_开头，后接英文字母、数字或下划线");
    }
    public static void RequireAvailable(string name, UnitProjectGraph graph, IEnumerable<DraftOperation> operations, string? except = null)
    {
        ValidateName(name);
        if (name != except && graph.Objects.Any(o => o.Name == name)) throw new TransactionValidationException("单位变量名已占用：" + name);
        if (operations.Any(o => o.ObjectName != except && (o.TargetKind == DraftTargetKind.UnitCreate && o.ObjectName == name || o.TargetKind == DraftTargetKind.UnitRename && Read(o).NewName == name)))
            throw new TransactionValidationException("待提交名称已占用：" + name);
    }
    public static string Suggest(string mother, UnitProjectGraph graph, IEnumerable<DraftOperation> drafts)
    {
        var pattern = "^" + Regex.Escape(mother) + "_mod_([0-9]+)$";
        var names = graph.Objects.Select(o => o.Name).Concat(drafts.Where(o => o.TargetKind == DraftTargetKind.UnitCreate).Select(o => o.ObjectName))
            .Concat(drafts.Where(o => o.TargetKind == DraftTargetKind.UnitRename).Select(o => Read(o).NewName));
        long maximum = 0;
        foreach (var name in names)
            if (Regex.Match(name, pattern) is { Success: true } match && long.TryParse(match.Groups[1].Value, out var n)) maximum = Math.Max(maximum, n);
        return mother + "_mod_" + checked(maximum + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
    }
    public static DraftOperation Operation(UnitRecord unit, string name, bool registration = false)
    {
        if (!registration) ValidateName(name);
        var body = UnitCreation.Source(unit);
        var kind = registration ? DraftTargetKind.UnitRegistration : DraftTargetKind.UnitRename;
        var key = registration ? "unit.registration" : "unit.rename";
        var payload = JsonSerializer.Serialize(new UnitIdentityEdit(1, GuidOf(body), name));
        return new(DraftOperation.CreateId(kind, unit.Source.RelativeSourceFile, unit.Name, key), "identity:" + unit.Name, kind, "units", unit.Source.RelativeSourceFile, unit.Name, unit.Source.TypeName, key, key, "UnitIdentity", unit.Name, body, name, payload,
            registration ? "修复单位注册 · " + unit.Name : "单位变量名 · " + unit.Name + " → " + name, unit.NameToken, false, DateTimeOffset.UtcNow);
    }
    public static ResolvedDraftOperation Resolve(UnitWorkspaceData units, DraftOperation op)
    {
        try
        {
            var edit = Read(op);
            var unit = units.Units.Single(u => u.Name == op.ObjectName && UnitProjectGraph.Normalize(u.Source.RelativeSourceFile) == UnitProjectGraph.Normalize(op.RelativeSourceFile));
            if (UnitCreation.Source(unit) != op.BaselineRaw || GuidOf(op.BaselineRaw) != edit.Guid) throw new InvalidDataException("单位身份基线已变化");
            if (op.TargetKind == DraftTargetKind.UnitRename) ValidateName(edit.NewName);
            return new(op, DraftResolutionStatus.Active, "");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or ArgumentException) { return new(op, DraftResolutionStatus.Conflict, ex.Message); }
    }
    public static (UnitProjectGraph.Source File, NdfValueSpan Map, NdfMapEntry Entry) Registration(UnitProjectGraph graph, NdfObjectInfo unit)
    {
        var maps = graph.Files.Values.SelectMany(f => f.Syntax.FindConstructors("TDeckSerializerEntries").SelectMany(c => f.Syntax.FindDirectAssignments(c, "UnitIds")).Select(a => (File: f, Map: a))).ToArray();
        if (maps.Length != 1) throw new TransactionValidationException("UnitIds无法唯一定位");
        var (file, map) = maps[0];
        var entries = file.Syntax.ReadMapEntries(map);
        var found = entries.Where(e => UnitProjectGraph.Same(graph.Resolve(file.Path, file.Syntax.Raw(e.Key)), unit)
            || file.Syntax.Raw(e.Key) == unit.Name).ToArray();
        if (found.Length != 1) throw new TransactionValidationException("单位注册缺失或不唯一：" + unit.Name);
        var id = file.Syntax.Raw(found[0].Value);
        if (!int.TryParse(id, out var number) || number < 0 || entries.Count(e => file.Syntax.Raw(e.Value) == id) != 1) throw new TransactionValidationException("单位注册编号无效或重复：" + id);
        return (file, map, found[0]);
    }
    public static string NewRegistration(UnitProjectGraph graph, UnitRecord mother, string name)
    {
        var target = graph.RequireObject(mother.Source.RelativeSourceFile, mother.Name);
        var (file, _, entry) = Registration(graph, target);
        var raw = file.Syntax.Raw(entry.Key);
        if (!UnitProjectGraph.Same(graph.Resolve(file.Path, raw), target)) throw new TransactionValidationException("母版注册路径不可达，请先修复：" + raw);
        return raw[..(raw.Length - mother.Name.Length)] + name;
    }
    public static void Plan(string root, IReadOnlyList<DraftOperation> operations, List<PlannedFileChange> files)
    {
        foreach (var op in operations.Where(o => o.TargetKind == DraftTargetKind.UnitRegistration))
        {
            var graph = new UnitProjectGraph(root, files); var unit = graph.RequireObject(op.RelativeSourceFile, op.ObjectName);
            var (file, _, entry) = Registration(graph, unit);
            var target = graph.ReferenceTo(file.Path, unit);
            var raw = file.Syntax.Raw(entry.Key);
            if (raw == target) continue;
            UnitProjectGraph.Put(root, file.Path, UnitProjectGraph.Patch(file.Text, [new(file.Syntax.StartOffset(entry.Key), file.Syntax.Length(entry.Key), raw, target, "注册路径")]), files, "修复注册 · " + op.ObjectName);
        }
        foreach (var op in operations.Where(o => o.TargetKind == DraftTargetKind.UnitRename))
        {
            var edit = Read(op); var graph = new UnitProjectGraph(root, files);
            RequireAvailable(edit.NewName, graph, operations, op.ObjectName);
            var unit = graph.RequireObject(op.RelativeSourceFile, op.ObjectName);
            if (GuidOf(graph.Body(unit)) != edit.Guid) throw new TransactionValidationException("单位身份已变化");
            var references = graph.References(unit);
            foreach (var file in graph.Files.Values)
            {
                var changes = references.Where(r => r.File.Path == file.Path).Select(r => new TextReplacement(r.Token.Start, r.Token.Length, r.Token.Text, r.Token.Text[..(r.Token.Text.Length - unit.Name.Length)] + edit.NewName, "单位引用")).ToList();
                if (file.Path == UnitProjectGraph.Normalize(unit.RelativeSourceFile))
                {
                    var doc = new NdfSyntaxDocument(file.Text, unit.CharacterOffset, unit.CharacterLength);
                    var name = doc.Tokens.Zip(doc.Tokens.Skip(1)).Single(p => p.First.Text == unit.Name && p.Second.Text == "is").First;
                    changes.Add(new(name.Start, name.Length, name.Text, edit.NewName, "单位声明"));
                    var debug = doc.FindAssignmentsAnywhere("ClassNameForDebug");
                    if (debug.Count == 1 && NdfSyntaxDocument.Unquote(doc.Raw(debug[0])) == unit.Name["Descriptor_".Length..])
                        changes.Add(new(doc.StartOffset(debug[0]), doc.Length(debug[0]), doc.Raw(debug[0]), "'" + edit.NewName["Descriptor_".Length..] + "'", "调试名"));
                }
                if (changes.Count > 0) UnitProjectGraph.Put(root, file.Path, UnitProjectGraph.Patch(file.Text, changes), files, op.Summary + " · " + changes.Count + "处");
            }
            var final = new UnitProjectGraph(root, files);
            var renamed = final.RequireObject(op.RelativeSourceFile, edit.NewName);
            _ = final.References(renamed);
            if (final.Objects.Any(o => o.Name == op.ObjectName && o.RelativeSourceFile == unit.RelativeSourceFile)) throw new TransactionValidationException("旧声明仍存在");
        }
    }
}
