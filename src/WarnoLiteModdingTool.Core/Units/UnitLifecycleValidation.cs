using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Units;

public static class UnitLifecycleValidation
{
    public static void Validate(string root, IReadOnlyList<PlannedFileChange> files)
    {
        var graph = new UnitProjectGraph(root, files);
        var touched = files.Where(f => f.Kind == FormalTextFileKind.Ndf).Select(f => UnitProjectGraph.Normalize(f.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var upgrade = new Dictionary<string, string>(StringComparer.Ordinal);
        string Id(NdfObjectInfo o) => UnitProjectGraph.Normalize(o.RelativeSourceFile) + "|" + o.Name;
        foreach (var obj in graph.Objects.Where(o => o.TypeName == "TEntityDescriptor"))
        {
            var doc = new NdfSyntaxDocument(graph.Body(obj));
            foreach (var field in doc.FindAssignmentsAnywhere("UpgradeFromUnit"))
            {
                var raw = doc.Raw(field); var target = graph.Resolve(obj.RelativeSourceFile, raw);
                if (target?.TypeName == "TEntityDescriptor") upgrade[Id(obj)] = Id(target);
                else if (touched.Contains(UnitProjectGraph.Normalize(obj.RelativeSourceFile)) && raw is not ("nil" or "null")) throw new TransactionValidationException("升级引用不可达：" + obj.Name + " → " + raw);
            }
        }
        foreach (var start in upgrade.Keys)
        {
            var seen = new HashSet<string>(); var at = start;
            while (upgrade.TryGetValue(at, out var next))
            {
                if (!seen.Add(at)) throw new TransactionValidationException("单位升级链形成循环：" + start);
                at = next;
            }
        }
        foreach (var file in graph.Files.Values.Where(f => touched.Contains(f.Path)))
        {
            if (file.Objects.GroupBy(o => o.Name).Any(g => g.Count() > 1)) throw new TransactionValidationException("候选声明重名：" + file.Path);
            foreach (var rule in file.Syntax.FindConstructors("TDeckUniteRule"))
            {
                foreach (var unit in file.Syntax.FindDirectAssignments(rule, "UnitDescriptor"))
                    if (graph.Resolve(file.Path, file.Syntax.Raw(unit))?.TypeName != "TEntityDescriptor") throw new TransactionValidationException("师单位引用不可达：" + file.Path + " · " + file.Syntax.Raw(unit));
                var transport = file.Syntax.FindDirectAssignments(rule, "AvailableTransportList");
                var without = file.Syntax.FindDirectAssignments(rule, "AvailableWithoutTransport");
                if (transport.Count == 1 && without.Count == 1 && file.Syntax.Raw(without[0]) == "False" && file.Syntax.ReadArrayElements(transport[0]).Count == 0)
                    throw new TransactionValidationException("移除后需要运输的师规则没有可用运输：" + file.Path);
            }
        }
    }
}
