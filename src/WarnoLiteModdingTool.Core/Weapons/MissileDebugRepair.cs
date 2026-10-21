using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public sealed record MissileDebugRepairRow(string File, string Name, string Before, string After, string Origin,
    bool CanRepair, string Reason, string Users);
public sealed record MissileDebugRepairScan(IReadOnlyList<MissileDebugRepairRow> Rows, IReadOnlyList<string> Warnings);
public sealed record MissileDebugRepairState(string File, string Name, string Body, string OriginFile, string Origin,
    string OriginBody, string Before, string After);

// Debug identity is independent of NDF declaration identity and DescriptorId.
public static partial class MissileDebugRepair
{
    public const string Key = "missile.debug.repair";
    public static bool IsRepair(DraftOperation op) => op.TargetKind == DraftTargetKind.MissileDebugRepair;
    private sealed record Entry(NdfObjectInfo Source, string Body, string Debug, bool Missile);
    public static NdfValueSpan DebugSpan(NdfSyntaxDocument doc)
    {
        var roots = doc.FindConstructors("TEntityDescriptor");
        if (roots.Count != 1) throw new TransactionValidationException("导弹实体根节点不唯一 / Ambiguous missile root");
        var fields = doc.FindDirectAssignments(roots[0], "ClassNameForDebug");
        if (fields.Count != 1 || fields[0].StartTokenIndex != fields[0].EndTokenIndex)
            throw new TransactionValidationException("导弹debug名称缺失或多义 / Missing or ambiguous missile debug name");
        var raw = doc.Raw(fields[0]);
        if (raw.Length < 3 || raw[0] is not ('\'' or '"') || raw[^1] != raw[0])
            throw new TransactionValidationException("导弹debug名称需非空字符串 / Missile debug name must be a non-empty string");
        return fields[0];
    }
    public static HashSet<string> Reserved(UnitProjectGraph graph) => Analysis.FromGraph(graph).Reserved();
    public static string Allocate(string declaration, HashSet<string> reserved)
    {
        var stem = declaration.StartsWith("Descriptor_", StringComparison.Ordinal) ? declaration[11..] : declaration;
        if (!Regex.IsMatch(stem, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new TransactionValidationException("导弹debug名称无法从声明派生 / Cannot derive missile debug name");
        var value = stem; var n = 1;
        while (!reserved.Add(value)) value = stem + "_" + n++;
        return value;
    }
    private static string Shape(Entry entry)
    {
        var doc = new NdfSyntaxDocument(entry.Body); var root = doc.FindConstructors("TEntityDescriptor").Single();
        if (doc.FindConstructors("TGuidedMissileModuleDescriptor").Count != 1) throw new TransactionValidationException("导弹制导模块不唯一 / Ambiguous guidance module");
        var ids = doc.FindDirectAssignments(root, "DescriptorId");
        if (ids.Count != 1 || !Guid.TryParse(doc.Raw(ids[0]).Replace("GUID:", "", StringComparison.Ordinal), out _))
            throw new TransactionValidationException("导弹GUID无法确认 / Unsupported missile GUID");
        var ignored = new List<NdfValueSpan> { ids[0], DebugSpan(doc) };
        foreach (var config in new[] { "DefaultConfig", "UncontrollableConfig" })
        foreach (var field in new[] { "MaxSpeedGRU", "MaxAccelerationGRU" }) ignored.Add(MissileMotion.Field(doc, config, field));
        return string.Join("\n", doc.Tokens.Select((t, i) => (t, i)).Where(p => !ignored.Any(s => p.i >= s.StartTokenIndex && p.i <= s.EndTokenIndex))
            .Select(p => p.t.Text[0] is not ('\'' or '"') && NdfSyntaxDocument.Leaf(p.t.Text) == entry.Source.Name
                ? p.t.Text[..^entry.Source.Name.Length] + "$SELF" : p.t.Text));
    }
    private static Entry? Origin(Entry copy, Entry[] entries)
    {
        var name = copy.Source.Name;
        while (Regex.IsMatch(name, @"_WLMT_[a-fA-F0-9]{10}$"))
        {
            name = Regex.Replace(name, @"_WLMT_[a-fA-F0-9]{10}$", "");
            var possible = entries.Where(e => e.Missile && e.Source.Name == name && e.Debug == copy.Debug &&
                UnitProjectGraph.Normalize(e.Source.RelativeSourceFile).Equals(UnitProjectGraph.Normalize(copy.Source.RelativeSourceFile), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (possible.Length == 1 && Shape(possible[0]) == Shape(copy)) return possible[0];
        }
        return null;
    }
    public static MissileDebugRepairScan Scan(string root) => Analyze(root).Scan();
    public static MissileDebugRepairScan Scan(UnitProjectGraph graph) => Analysis.FromGraph(graph).Scan();
    public static IReadOnlyList<DraftOperation> Prepare(string root, IReadOnlyList<string> selected) => Analyze(root).Prepare(selected);
    public static MissileDebugRepairState Read(DraftOperation op)
    {
        try { return JsonSerializer.Deserialize<MissileDebugRepairState>(op.TargetRaw) ?? throw new JsonException(); }
        catch (JsonException) { throw new TransactionValidationException("导弹修复草稿无效 / Invalid missile repair draft"); }
    }
    public static void Validate(UnitProjectGraph graph, DraftOperation op) => Analysis.FromGraph(graph).Validate(op);
    public static ResolvedDraftOperation Resolve(Analysis analysis, DraftOperation op)
    {
        try { analysis.Validate(op); return new(op, DraftResolutionStatus.Active, ""); }
        catch (Exception ex) when (ex is IOException or TransactionValidationException or InvalidOperationException or ArgumentException)
        { return new(op, DraftResolutionStatus.Conflict, ex.Message); }
    }
    public static WeaponPlannedReplacement Replacement(UnitProjectGraph graph, DraftOperation op) => Analysis.FromGraph(graph).Replacement(op);
    public static void ValidateFinal(string root, IReadOnlyList<PlannedFileChange> files, CancellationToken token = default)
    {
        if (!files.Any(f => f.Kind == FormalTextFileKind.Ndf && (System.Text.Encoding.UTF8.GetString(f.CandidateBytes).Contains("TGuidedMissileMovementModuleDescriptor", StringComparison.Ordinal) ||
            System.Text.Encoding.UTF8.GetString(f.OriginalBytes).Contains("TGuidedMissileMovementModuleDescriptor", StringComparison.Ordinal)))) return;
        Analyze(root, token).ValidateFinal(files, token);
    }
}
