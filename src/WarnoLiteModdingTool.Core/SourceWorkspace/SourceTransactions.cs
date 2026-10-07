using System.Text.Json;
using WarnoLiteModdingTool.Core.Changes;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.SourceWorkspace;

public sealed record SourceJournalFile(string Path, string? Before, string After);
public sealed record SourceJournal(int Version, string Id, string State, DateTimeOffset Created, SourceJournalFile[] Files, Dictionary<string, DateTimeOffset> Drafts, string? Error = null, string[]? Validation = null);
public static class SourceTransactions
{
    private const string Folder = ".warno-editor/source-transactions";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string DirectoryPath(string root, string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^source-[a-f0-9]{32}$")) throw new InvalidDataException("无效事务标识 / Invalid transaction ID");
        return ChangePaths.Resolve(root, Folder + "/" + id);
    }
    public static IReadOnlyList<SourceJournal> List(string root)
    {
        var path = ChangePaths.Resolve(root, Folder); if (!Directory.Exists(path)) return [];
        return Directory.GetDirectories(path).Where(d => File.Exists(Path.Combine(d, "journal.json"))).Select(d => Load(root, Path.GetFileName(d), verifyPayload: false)).OrderByDescending(j => j.Created).ToArray();
    }
    private static SourceJournal Load(string root, string id, bool verifyPayload = true)
    {
        var journal = JsonSerializer.Deserialize<SourceJournal>(File.ReadAllBytes(Path.Combine(DirectoryPath(root, id), "journal.json")), Json) ?? throw new InvalidDataException("事务记录损坏 / Invalid journal");
        if (journal.Version != 1 || journal.Id != id || journal.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Files.Length) throw new InvalidDataException("事务版本或路径无效 / Invalid journal version or paths");
        foreach (var file in journal.Files) { _ = SourceFiles.Resolve(root, file.Path); if (verifyPayload) { Payload(root, id, file.After); if (file.Before is not null) Payload(root, id, file.Before); } }
        return journal;
    }
    private static byte[] Payload(string root, string id, string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d+\.(before|after)\.bin$")) throw new InvalidDataException("无效载荷 / Invalid payload");
        return File.ReadAllBytes(Path.Combine(DirectoryPath(root, id), name));
    }
    private static void Flush(string path, byte[] bytes)
    { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(bytes); file.Flush(true); }
    private static void Save(string root, SourceJournal journal)
    {
        var dir = DirectoryPath(root, journal.Id); Directory.CreateDirectory(dir); var temporary = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
        Flush(temporary, JsonSerializer.SerializeToUtf8Bytes(journal, Json)); File.Move(temporary, Path.Combine(dir, "journal.json"), true);
    }
    public static void EnsureRecovered(string root, bool includeOtherTransactions = true)
    {
        if (includeOtherTransactions && (new TransactionBackupStore(root).List().Any(j => j.State is "Prepared" or "Committing" or "RecoveryRequired") || ChangeTransactions.List(root).Any(j => j.State is "Prepared" or "Committing" or "RecoveryRequired"))) throw new InvalidOperationException("存在未完成文件事务，请先恢复 / Recover the unfinished file transaction first");
        if (List(root).Any(j => j.State is "Prepared" or "Committing" or "RecoveryRequired" or "Restoring")) throw new InvalidOperationException("存在未完成源码事务，请先在工作台恢复 / Recover the unfinished source transaction first");
    }
    public static async Task<TransactionResult> Commit(ApplyPreview preview, DraftStore store, CancellationToken token = default, Action<int>? afterWrite = null)
    {
        if (!preview.SourceAcknowledged) throw new InvalidOperationException("请核对未验证范围 / Review unverified content before applying");
        using var gate = ProjectWriteGate.Enter(preview.ProjectRoot); EnsureRecovered(preview.ProjectRoot);
        await Verify(preview, store, token);
        var dir = DirectoryPath(preview.ProjectRoot, preview.BackupId); if (Directory.Exists(dir)) throw new IOException("事务已存在 / Transaction already exists");
        Directory.CreateDirectory(dir); var entries = new List<SourceJournalFile>();
        for (var i = 0; i < preview.Files.Count; i++)
        {
            token.ThrowIfCancellationRequested(); var file = preview.Files[i];
            var before = file.Existed ? i + ".before.bin" : null; if (before is not null) Flush(Path.Combine(dir, before), file.OriginalBytes);
            var after = i + ".after.bin"; Flush(Path.Combine(dir, after), file.CandidateBytes); entries.Add(new(file.RelativePath, before, after));
        }
        var journal = new SourceJournal(1, preview.BackupId, "Prepared", DateTimeOffset.UtcNow, entries.ToArray(), preview.Operations.ToDictionary(o => o.Id, o => o.UpdatedUtc), Validation: preview.ValidationMessages.ToArray());
        Save(preview.ProjectRoot, journal);
        try
        {
            await Verify(preview, store, token); journal = journal with { State = "Committing" }; Save(preview.ProjectRoot, journal);
            for (var i = 0; i < preview.Files.Count; i++)
            {
                token.ThrowIfCancellationRequested(); var file = preview.Files[i];
                if (!SourceFiles.Equal(SourceFiles.Read(preview.ProjectRoot, file.RelativePath), file.Existed ? file.OriginalBytes : null)) throw new IOException("提交期间文件变化 / File changed during commit: " + file.RelativePath);
                Write(preview.ProjectRoot, file.RelativePath, file.CandidateBytes); afterWrite?.Invoke(i);
            }
            foreach (var file in preview.Files)
                if (!SourceFiles.Equal(SourceFiles.Read(preview.ProjectRoot, file.RelativePath), file.CandidateBytes)) throw new IOException("提交结果已被外部修改 / Commit result changed externally: " + file.RelativePath);
            journal = journal with { State = "Completed" }; Save(preview.ProjectRoot, journal);
        }
        catch (Exception ex)
        {
            try { Rollback(preview.ProjectRoot, journal); }
            catch (Exception recovery) { Save(preview.ProjectRoot, journal with { State = "RecoveryRequired", Error = ex.Message + "\n" + recovery.Message }); throw new IOException("提交失败，需要恢复 / Commit failed; recovery required: " + journal.Id, recovery); }
            throw new IOException("提交失败，已回滚 / Commit failed and rolled back: " + ex.Message, ex);
        }
        var warnings = new List<string>();
        try { await Cleanup(journal, store); }
        catch (Exception ex) { warnings.Add("文件已应用，草稿收尾失败 / Files applied; draft cleanup failed: " + ex.Message); }
        return new(true, journal.Id, "源码已应用 / Source applied", Folder + "/" + journal.Id + "/journal.json", warnings);
    }
    private static async Task Verify(ApplyPreview preview, DraftStore store, CancellationToken token)
    {
        if (!Path.GetFullPath(store.ProjectRoot).Equals(Path.GetFullPath(preview.ProjectRoot), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("项目不一致 / Project mismatch");
        using var current = new DraftStore(preview.ProjectRoot); var loaded = await current.LoadAsync(token);
        if (loaded.IsBlocked || preview.DraftReview is null || SourceEditing.Review(current.Operations) != SourceEditing.Review(preview.DraftReview) || SourceEditing.Review(store.Operations) != SourceEditing.Review(preview.DraftReview)) throw new IOException("预览后草稿已变化 / Drafts changed after preview");
        var catalog = SourceFiles.Discover(preview.ProjectRoot, token); var snapshot = preview.SourceSnapshot ?? throw new InvalidDataException("缺少源码快照 / Missing source snapshot");
        if (!catalog.Errors.SequenceEqual(preview.SourceCatalogErrors)) throw new IOException("文件扫描状态已变化 / File scan state changed");
        if (!snapshot.Where(p => p.Value is not null).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(catalog.Files.Select(f => f.Path))) throw new IOException("项目文件清单已变化 / Project file list changed");
        foreach (var pair in snapshot) { token.ThrowIfCancellationRequested(); if (!SourceFiles.Equal(SourceFingerprint.File(SourceFiles.Resolve(preview.ProjectRoot, pair.Key)), pair.Value)) throw new IOException("预览后文件变化 / File changed after preview: " + pair.Key); }
    }
    private static void Write(string root, string path, byte[]? bytes)
    {
        var full = SourceFiles.Resolve(root, path);
        if (bytes is null) { if (File.Exists(full)) File.Delete(full); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { Flush(temporary, bytes); if (SourceFiles.Resolve(root, path) != full) throw new IOException("路径已变化 / Path changed"); File.Move(temporary, full, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Rollback(string root, SourceJournal journal)
    {
        // Actual bytes also identify a replacement made immediately before a process interruption.
        foreach (var file in journal.Files)
        {
            var before = file.Before is null ? null : Payload(root, journal.Id, file.Before); var after = Payload(root, journal.Id, file.After); var current = SourceFiles.Read(root, file.Path);
            if (!SourceFiles.Equal(current, before) && !SourceFiles.Equal(current, after)) throw new IOException("外部内容冲突，原文与候选均保留 / External content conflict; originals and candidates retained: " + file.Path);
        }
        Save(root, journal with { State = "Restoring" });
        foreach (var file in journal.Files.Reverse())
        {
            var before = file.Before is null ? null : Payload(root, journal.Id, file.Before); var current = SourceFiles.Read(root, file.Path);
            if (SourceFiles.Equal(current, before)) continue;
            if (!SourceFiles.Equal(current, Payload(root, journal.Id, file.After))) throw new IOException("恢复期间外部内容变化 / External change during recovery: " + file.Path);
            Write(root, file.Path, before);
        }
        Save(root, journal with { State = "Restored" });
    }
    public static void Restore(string root, string id)
    {
        using var gate = ProjectWriteGate.Enter(root); var journal = Load(root, id);
        if (journal.State == "Restored") return;
        if (journal.State == "Completed")
        {
            EnsureRecovered(root);
            using var drafts = new DraftStore(root); var loaded = Task.Run(() => drafts.LoadAsync()).GetAwaiter().GetResult();
            if (loaded.IsBlocked || drafts.Operations.Count != 0) throw new IOException("恢复已完成批次前，请先处理现有草稿 / Resolve existing drafts before restoring a completed transaction");
        }
        if (journal.State == "Completed" && List(root).Any(j => j.Created > journal.Created && j.State == "Completed" && j.Files.Any(f => journal.Files.Any(old => old.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase))))) throw new IOException("请先恢复较新的关联事务 / Restore newer overlapping transactions first");
        try { Rollback(root, journal); } catch (Exception ex) { Save(root, journal with { State = "RecoveryRequired", Error = ex.Message }); throw; }
    }
    public static async Task Cleanup(SourceJournal journal, DraftStore store)
    {
        if (journal.State != "Completed") return;
        await store.ApplyBatchAsync([], store.Operations.Where(o => journal.Drafts.TryGetValue(o.Id, out var stamp) && stamp == o.UpdatedUtc).Select(o => o.Id).ToArray());
    }
}
