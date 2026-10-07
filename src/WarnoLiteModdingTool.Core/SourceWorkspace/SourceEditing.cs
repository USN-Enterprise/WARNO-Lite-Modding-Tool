using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.SourceWorkspace;

public static class SourceEditing
{
    public const string Group = "source-workspace";
    public static DraftOperation Operation(string path, SourceFileDraft data) => new(
        "source|" + path.ToUpperInvariant(), Group, DraftTargetKind.SourceFile, "source", path,
        Path.GetFileName(path), "NDF/CSV", "source", path, "Source", data.Baseline is null ? "新文件 / New file" : "正式文件 / Formal file",
        "", "源码草稿 / Source draft", "", "源码 / Source · " + path, null, false, DateTimeOffset.UtcNow, Source: data);
    public static ResolvedDraftOperation Resolve(string root, DraftOperation op)
    {
        try
        {
            if (op.Source is null) throw new InvalidDataException("源码载荷缺失 / Missing source payload");
            var matches = SourceFiles.Equal(SourceFiles.Read(root, op.RelativeSourceFile), op.Source.Baseline);
            return new(op, matches ? DraftResolutionStatus.Active : DraftResolutionStatus.Conflict, matches ? "源码草稿；语义待检查 / Source draft; semantics unchecked" : "磁盘与源码基线不一致 / Disk differs from source baseline");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return new(op, DraftResolutionStatus.Conflict, ex.Message); }
    }
    public static async Task CheckCoexistence(string root, IReadOnlyList<DraftOperation> operations, CancellationToken token)
    {
        var source = operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile).ToArray();
        var business = operations.Where(o => o.TargetKind != DraftTargetKind.SourceFile).ToArray();
        if (source.Length == 0 || business.Length == 0) return;
        ApplyPreview preview;
        try { preview = await new UnitApplyPlanner().PrepareAsync(root, business, token); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        { throw new InvalidOperationException("无法确定业务草稿完整影响；请先处理业务草稿 / Resolve business drafts before source editing: " + ex.Message, ex); }
        var footprint = preview.ObservedFiles.Concat(preview.Files.Select(f => f.FullPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlaps = source.Where(o => footprint.Contains(SourceFiles.Resolve(root, o.RelativeSourceFile))).Select(o => o.RelativeSourceFile).ToArray();
        if (overlaps.Length > 0) throw new InvalidOperationException("源码与表单草稿存在读写依赖冲突，请先处理相关业务草稿 / Source and form drafts overlap:\n" + string.Join("\n", overlaps.Select(p => p + (preview.Files.Any(f => f.RelativePath.Equals(p,StringComparison.OrdinalIgnoreCase)) ? " · 业务写入 / Business write" : " · 业务读取依赖 / Business read dependency"))) + "\n" + string.Join("\n", business.Select(o => o.Summary + " · " + o.RelativeSourceFile)));
    }
    public static async Task<ApplyPreview> Prepare(string root, IReadOnlyList<DraftOperation> requested, CancellationToken token)
    {
        using var store = new DraftStore(root); var load = await store.LoadAsync(token);
        if (load.IsBlocked) throw new InvalidDataException(load.Error);
        var operations = store.Operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile).ToArray();
        if (operations.Length == 0 || requested.Any(o => o.TargetKind != DraftTargetKind.SourceFile)) throw new InvalidOperationException("源码必须独立整组应用 / Apply source drafts as one separate group");
        await CheckCoexistence(root, store.Operations, token);
        var catalog = SourceFiles.Discover(root, token); var snapshot = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in catalog.Files) { token.ThrowIfCancellationRequested(); snapshot[file.Path] = SourceFingerprint.File(SourceFiles.Resolve(root, file.Path)); }
        var files = new List<PlannedFileChange>(); var messages = new List<string>(catalog.Errors);
        foreach (var op in operations)
        {
            token.ThrowIfCancellationRequested(); var data = op.Source ?? throw new InvalidDataException("源码载荷缺失 / Missing source payload");
            var current = SourceFiles.Read(root, op.RelativeSourceFile);
            if (!SourceFiles.Equal(current, data.Baseline)) throw new InvalidOperationException("基线已变化，请三方核对 / Baseline changed; compare three versions: " + op.RelativeSourceFile);
            var after = SourceFiles.Encode(op.RelativeSourceFile, data);
            snapshot[op.RelativeSourceFile] = SourceFingerprint.Bytes(current);
            var oldText = data.Baseline is null ? "" : SourceFiles.Open(root, op.RelativeSourceFile, data.CodePage).Text;
            var beforeSupported = true;
            try { Changes.ChangeMerge.ValidateBytes(op.RelativeSourceFile, SourceFiles.IsNdf(op.RelativeSourceFile) ? System.Text.Encoding.UTF8.GetBytes(oldText) : data.Baseline); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException) { beforeSupported = false; messages.Add(op.RelativeSourceFile + " · 既有内容未通过已知规则检查 / Existing content outside known validation: " + ex.Message); }
            if (beforeSupported) Changes.ChangeMerge.ValidateBytes(op.RelativeSourceFile, after);
            else
            {
                IReadOnlyDictionary<string,string>? oldIssues = null, newIssues = null;
                try { oldIssues = Changes.ChangeMerge.SourceKnownIssues(op.RelativeSourceFile,oldText); newIssues = Changes.ChangeMerge.SourceKnownIssues(op.RelativeSourceFile,data.Text); }
                catch(Exception ex) when(ex is InvalidDataException or ArgumentException or OverflowException) { messages.Add(op.RelativeSourceFile + " · 无法完整对应既有与候选规则 / Rule correspondence incomplete: " + ex.Message); }
                if(oldIssues is not null && newIssues is not null)
                {
                    var introduced = newIssues.Where(p => !oldIssues.TryGetValue(p.Key,out var previous) || previous != p.Value).ToArray();
                    if(introduced.Length>0)throw new InvalidDataException(op.RelativeSourceFile+"\n"+string.Join("\n",introduced.Select(p=>p.Key+" · "+p.Value)));
                }
            }
            if (SourceFiles.IsNdf(op.RelativeSourceFile))
            {
                var beforeIssues = SourceLanguage.Diagnostics(oldText);
                var afterIssues = SourceLanguage.Diagnostics(data.Text);
                SourceValidation.CheckLexicalDelta(op.RelativeSourceFile, oldText, data.Text);
                messages.Add(op.RelativeSourceFile + " · 语义未完整验证 / Semantics not fully verified" + (afterIssues.Count == 0 ? "" : "\n" + string.Join("\n", afterIssues)));
            }
            else messages.Add(op.RelativeSourceFile + " · CSV 原文编辑，列与 token 关系未验证 / Raw CSV; columns and token relations not verified");
            if (!SourceFiles.Equal(current, after)) files.Add(new(op.RelativeSourceFile, SourceFiles.Resolve(root, op.RelativeSourceFile), SourceFiles.IsNdf(op.RelativeSourceFile) ? FormalTextFileKind.Ndf : FormalTextFileKind.Csv, PlannedFileAction.Write, current is not null, current ?? [], after, current is null ? null : File.GetLastWriteTimeUtc(SourceFiles.Resolve(root, op.RelativeSourceFile)), [op.Summary]));
        }
        SourceValidation.KnownCombinations(root,files,operations,messages,token);
        return new(root, "source-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, operations, files, messages) { SourceSnapshot = snapshot, SourceCatalogErrors = catalog.Errors.ToArray(), DraftReview = store.Operations.ToArray() };
    }
    public static string Review(IReadOnlyList<DraftOperation> ops) => SourceFingerprint.Json(ops);
}
