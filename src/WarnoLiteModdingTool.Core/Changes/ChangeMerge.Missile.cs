using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Changes;

public static partial class ChangeMerge
{
    // Config names are part of field identity; identical leaf names never collapse together.
    private static void MissileCells(NdfSyntaxDocument doc, Dictionary<string, Cell> cells)
    {
        var roots = doc.FindConstructors("TEntityDescriptor");
        if (roots.Count != 1) return;
        var lists = doc.FindDirectAssignments(roots[0], "ModulesDescriptors");
        if (lists.Count != 1) return;
        var modules = doc.FindConstructors("TGuidedMissileMovementModuleDescriptor", lists[0]);
        if (modules.Count != 1) return;
        foreach (var name in new[] { "DefaultConfig", "UncontrollableConfig" })
        {
            var configs = doc.FindDirectAssignments(modules[0], name);
            if (configs.Count != 1) continue;
            var inline = doc.FindConstructors("TGuidedMissileMovementConfigDescriptor", configs[0]);
            if (inline.Count != 1 || inline[0].TypeTokenIndex != configs[0].StartTokenIndex || inline[0].CloseTokenIndex != configs[0].EndTokenIndex) continue;
            foreach (var field in new[] { "MaxSpeedGRU", "MaxAccelerationGRU" })
            {
                var values = doc.FindDirectAssignments(inline[0], field);
                if (values.Count != 1) continue;
                var span = values[0];
                if (span.StartTokenIndex != span.EndTokenIndex) continue;
                var key = "TGuidedMissileMovementModuleDescriptor." + name + "." + field;
                cells.Add(key, new(key, doc.Raw(span), doc.StartOffset(span), doc.Length(span), true, false, true));
            }
        }
    }
}
