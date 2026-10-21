using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Transactions;

// A debug-only repair does not edit weapon gameplay or require a unit workspace.
// Mixed operations still use the ordinary business planners and dependency expansion.
public static class MissileRepairPlanner
{
    internal static ApplyPreview Prepare(string root, IReadOnlyList<DraftOperation> operations,
        IReadOnlyList<DraftOperation> saved, CancellationToken token, IProgress<MissileRepairProgress>? progress)
    {
        UnitApplyPlanner.ValidateOperationIdentity(operations);
        var analysis = MissileDebugRepair.Analyze(root, token, progress);
        if (analysis.Warnings.Count > 0) throw new TransactionValidationException(string.Join("\n", analysis.Warnings));
        progress?.Report(new("核对草稿 / Checking drafts", 0, operations.Count));
        var patches = operations.Select(o => analysis.Replacement(o, token)).ToArray();
        var files = new List<PlannedFileChange>();
        foreach (var group in patches.GroupBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested(); var source = TextFileSnapshot.Load(root, group.Key, FormalTextFileKind.Ndf);
            files.Add(UnitApplyPlanner.ToWriteChange(source, UnitProjectGraph.Patch(source.Text, group.Select(p => p.Replacement)), group.Select(p => p.Summary).ToArray()));
        }
        progress?.Report(new("组合检查 / Checking final result", operations.Count, operations.Count));
        analysis.ValidateFinal(files, token);
        var messages = new[] { $"已验证{operations.Count}项导弹debug修复 / Validated {operations.Count} missile debug repairs", "仅修改已确认副本的ClassNameForDebug；原实体、GUID、引用和运动参数保留 / Only confirmed clone debug names change; original entities, GUIDs, references and movement values are preserved", "提交前复核完整NDF内容、文件集及草稿 / Full NDF contents, file set and drafts are checked again before commit" };
        var backup = UnitApplyPlanner.CreateBackupId("apply"); var now = DateTimeOffset.UtcNow;
        var log = TextFileSnapshot.Load(root, $"logs/WARNO Lite Modding Tool-{backup}.md", FormalTextFileKind.Log, true);
        if (log.Existed) throw new TransactionValidationException("应用日志已存在 / Apply log already exists");
        files.Add(UnitApplyPlanner.ToWriteChange(log, UnitApplyPlanner.BuildApplyLog(backup, now, operations, files, messages), ["写入本次应用记录"]));
        var dependencies = new Dictionary<string, byte[]>(analysis.Dependencies, StringComparer.OrdinalIgnoreCase);
        var history = Path.Combine(root, UnitCreationHistory.LedgerPath);
        dependencies[UnitCreationHistory.LedgerPath] = File.Exists(history) ? File.ReadAllBytes(history) : [];
        var preview = new ApplyPreview(root, backup, now, operations, files, messages) { FullNdfReview = true, UnitReadDependencies = dependencies, DraftReview = saved.ToArray() };
        VerifyInputs(preview, token);
        return preview;
    }
    public static void VerifyInputs(ApplyPreview preview, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!preview.FullNdfReview || preview.UnitReadDependencies is not { } dependencies) throw new TransactionValidationException("修复预览缺少依赖 / Repair preview dependencies missing");
        var catalog = SourceFiles.Discover(preview.ProjectRoot, token);
        var paths = catalog.Files.Where(f => SourceFiles.IsNdf(f.Path) && !MissileDebugRepair.Analysis.Excluded(f.Path)).Select(f => f.Path).Append(UnitCreationHistory.LedgerPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (catalog.Errors.Count > 0 || !paths.SetEquals(dependencies.Keys)) throw new TransactionValidationException("扫描文件集已变化，请重新预览 / File set changed; preview again");
        foreach (var pair in dependencies)
        {
            token.ThrowIfCancellationRequested();
            var path = TextFileSnapshot.ResolveInsideRoot(preview.ProjectRoot, pair.Key);
            if (!(File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>()).AsSpan().SequenceEqual(pair.Value))
                throw new TransactionValidationException("预览后内容已变化，请重新预览 / Content changed; preview again: " + pair.Key);
        }
    }
    public static async Task VerifyDraftsAsync(ApplyPreview preview, CancellationToken token = default)
    {
        using var store = new DraftStore(preview.ProjectRoot); var load = await store.LoadAsync(token);
        if (load.IsBlocked || JsonSerializer.Serialize(store.Operations) != JsonSerializer.Serialize(preview.DraftReview))
            throw new TransactionValidationException("预览后草稿已变化，请重新预览 / Drafts changed; preview again");
    }
}
