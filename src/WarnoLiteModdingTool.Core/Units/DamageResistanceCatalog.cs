using System.Globalization;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.Core.Units;

public sealed record DamageFamilyDefinition(string Name, int Count)
{
    public int MinimumIndex => 1;

    public int MaximumIndex => Math.Max(MinimumIndex, Count);
}

public sealed record DamageResistanceCatalog(
    IReadOnlyList<DamageFamilyDefinition> ResistanceFamilies,
    IReadOnlyList<DamageFamilyDefinition> DamageFamilies,
    IReadOnlyList<string> Diagnostics)
{
    public static DamageResistanceCatalog Load(ModProjectContext context)
    {
        var path = Path.Combine(context.Layout.GameplayPath, "Gfx", "DamageResistance.ndf");
        if (!File.Exists(path))
        {
            return new([], [], [$"缺少 {Path.GetRelativePath(context.Layout.RootPath, path)}；护甲族与伤害族仅保留原值。"]);
        }

        try
        {
            var source = File.ReadAllText(path);
            var document = new NdfSyntaxDocument(source);
            var roots = document.FindConstructors("TGameplayDamageResistanceContainer");
            if (roots.Count != 1)
            {
                return new([], [], ["DamageResistance 容器无法唯一定位；护甲族与伤害族仅保留原值。"]);
            }

            return new(
                ReadCounts(document, roots[0], "ResistanceFamilyCounts"),
                ReadCounts(document, roots[0], "DamageFamilyCounts"),
                []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new([], [], [$"无法读取 DamageResistance.ndf：{exception.Message}"]);
        }
    }

    private static IReadOnlyList<DamageFamilyDefinition> ReadCounts(
        NdfSyntaxDocument document,
        NdfConstructorSpan root,
        string fieldName)
    {
        var values = document.FindDirectAssignments(root, fieldName);
        if (values.Count != 1)
        {
            return [];
        }

        return document.ReadMapEntries(values[0])
            .Select(entry => new
            {
                Name = NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(document.Raw(entry.Key))),
                Count = int.TryParse(document.Raw(entry.Value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : -1
            })
            .Where(item => item.Name.Length > 0 && item.Count >= 0)
            .Select(item => new DamageFamilyDefinition(item.Name, item.Count))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
