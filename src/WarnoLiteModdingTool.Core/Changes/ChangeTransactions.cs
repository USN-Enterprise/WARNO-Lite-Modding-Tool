using System.Text.Json;

namespace WarnoLiteModdingTool.Core.Changes;

public sealed record ChangeJournalFile(string Path, ContentStamp Before, ContentStamp After, string? BeforePayload, string? AfterPayload);
public sealed record ChangeJournal(int Version, string Id, string PackageIdentity, DateTimeOffset CreatedUtc, string State, IReadOnlyList<ChangeJournalFile> Files)
{
    public IReadOnlyList<string> RecordPaths { get; init; } = [];
    public IReadOnlyList<string> DeferredPaths { get; init; } = [];
    public IReadOnlyList<ChangeFileMapping> Mappings { get; init; } = [];
    public IReadOnlyList<ChangeTextDecision> Decisions { get; init; } = [];
    public IReadOnlyDictionary<string, ContentStamp>? TargetBaseline { get; init; }
}

public static class ChangeTransactions
{
    private const string Folder = ".warno-editor/change-transactions";
    internal static string StateKey(IReadOnlyList<ChangeJournal> journals) => string.Join("\n", journals.Select(j => j.Id + ":" + j.State));
    public static IReadOnlyList<ChangeJournal> List(string root)
    {
        var folder = ChangePaths.Resolve(root, Folder); if (!Directory.Exists(folder)) return [];
        var result = new List<ChangeJournal>();
        foreach (var dir in Directory.GetDirectories(folder).Order(StringComparer.Ordinal))
        {
            ChangePaths.NoLinks(dir); var path = Path.Combine(dir, "record.json"); if (!File.Exists(path)) continue;
            var record = JsonSerializer.Deserialize<ChangeJournal>(File.ReadAllBytes(path), ChangeJson.Options) ?? throw new InvalidDataException("事务记录为空 / Empty journal");
            Validate(record);
            if (record.Id != Path.GetFileName(dir)) throw new InvalidDataException("事务身份不符 / Journal identity mismatch");
            result.Add(record);
        }
        return result.OrderBy(r => r.CreatedUtc).ToArray();
    }
    public static ChangeJournal Commit(ChangePreview preview, CancellationToken cancel = default, Action<int>? afterWrite = null)
    {
        if (!preview.CanApply) throw new InvalidOperationException("预览不可应用 / Preview cannot be applied");
        var root = preview.Root; using var gate = Lock(root); Verify(preview, cancel);
        var previous = List(root);
        if (StateKey(previous) != preview.ReceiptState || previous.Any(j => j.State is "Prepared" or "Committing" or "RecoveryRequired" ||
            j.State == "Completed" && (j.PackageIdentity != preview.PackageIdentity || j.Version == 1 || j.RecordPaths.Intersect(preview.SelectedPaths, StringComparer.OrdinalIgnoreCase).Any())))
            throw new InvalidOperationException("事务状态已变化，请重新预览 / Transaction state changed; preview again");
        var id = Guid.NewGuid().ToString("N"); var folder = ChangePaths.Resolve(root, Folder + "/" + id); Directory.CreateDirectory(folder);
        var changes = preview.Files.Where(f => !ChangePaths.Equal(f.Before, f.After)).ToArray(); var entries = new List<ChangeJournalFile>();
        for (var i = 0; i < preview.Files.Count; i++)
        {
            cancel.ThrowIfCancellationRequested(); var file = preview.Files[i]; ChangePaths.Writable(file.Path); ChangeMerge.ValidateBytes(file.Path, file.After);
            string? Store(byte[]? bytes, string side)
            { if (bytes is null) return null; var name = i + "." + side; Flush(Path.Combine(folder, name), bytes); return name; }
            entries.Add(new(file.Path, ContentStamp.Of(file.Before), ContentStamp.Of(file.After), Store(file.Before, "before"), Store(file.After, "after")));
        }
        var journal = new ChangeJournal(ChangeTextDecisions.HasNdf(preview.Decisions) ? 4 : 3, id, preview.PackageIdentity, DateTimeOffset.UtcNow, "Prepared", entries)
        {
            RecordPaths = preview.SelectedPaths, DeferredPaths = preview.DeferredPaths,
            Mappings = preview.Mappings.Where(m => preview.SelectedPaths.Contains(m.SourcePath, StringComparer.OrdinalIgnoreCase)).ToArray(),
            Decisions = preview.Decisions.Where(d => preview.SelectedPaths.Contains(d.Conflict.Path, StringComparer.OrdinalIgnoreCase)).ToArray(), TargetBaseline = preview.TargetBaseline
        };
        Validate(journal); Save(root, journal);
        try
        {
            Verify(preview, cancel); journal = journal with { State = "Committing" }; Save(root, journal);
            for (var i = 0; i < changes.Length; i++)
            {
                cancel.ThrowIfCancellationRequested(); var file = changes[i];
                if (!ChangePaths.Equal(ChangeRestore.ReadCurrent(root, file.Path), file.Before)) throw new IOException("提交期间目标变化 / Target changed during commit: " + file.Path);
                WriteTarget(root, file.Path, file.After); afterWrite?.Invoke(i);
            }
            journal = journal with { State = "Completed" }; Save(root, journal); return journal;
        }
        catch (Exception failure)
        {
            try { RecoverLocked(root, journal); }
            catch (Exception recovery)
            {
                try { Save(root, journal with { State = "RecoveryRequired" }); } catch (IOException) { }
                throw new IOException("应用失败且需要恢复 / Apply failed; recovery required: " + id + "\n" + failure.Message + "\n" + recovery.Message, failure);
            }
            throw new IOException("应用失败，已恢复 / Apply failed and was rolled back: " + id + "\n" + failure.Message, failure);
        }
    }
    public static void Recover(string root, string id)
    {
        using var gate = Lock(root); var list = List(root); var journal = list.Single(j => j.Id == id);
        if (journal.State == "RolledBack") return;
        if (list.Last(j => j.State != "RolledBack").Id != id) throw new InvalidOperationException("请先恢复较新的批次，再恢复此批次 / Recover newer batches before this batch");
        RecoverLocked(root, journal);
    }
    private static void RecoverLocked(string root, ChangeJournal journal)
    {
        Validate(journal); var folder = ChangePaths.Resolve(root, Folder + "/" + journal.Id);
        var originals = new List<(ChangeJournalFile File, byte[]? Bytes)>();
        foreach (var file in journal.Files)
        {
            var current = ContentStamp.Of(ChangeRestore.ReadCurrent(root, file.Path));
            if (current != file.Before && current != file.After) throw new IOException("恢复前发现外部修改，未覆盖 / External changes found; recovery did not overwrite: " + file.Path);
            byte[]? Read(string? name, ContentStamp stamp)
            {
                var bytes = name is null ? null : File.ReadAllBytes(ChangePaths.Resolve(folder, name));
                if (ContentStamp.Of(bytes) != stamp) throw new IOException("备份内容损坏 / Backup integrity check failed"); return bytes;
            }
            var before = Read(file.BeforePayload, file.Before); _ = Read(file.AfterPayload, file.After); originals.Add((file, before));
        }
        Save(root, journal with { State = "RecoveryRequired" });
        foreach (var (file, bytes) in originals.AsEnumerable().Reverse())
        {
            var current = ContentStamp.Of(ChangeRestore.ReadCurrent(root, file.Path));
            if (current != file.Before && current != file.After) throw new IOException("恢复期间目标变化 / Target changed during recovery");
            if (current != file.Before) WriteTarget(root, file.Path, bytes);
        }
        Save(root, journal with { State = "RolledBack" });
    }
    internal static void Verify(ChangePreview preview, CancellationToken cancel)
    {
        var errors = new List<string>(); ChangeRestore.CheckDrafts(preview.Root, errors); if (errors.Count > 0) throw new IOException(string.Join("\n", errors));
        using var input = new ChangeCapture.Inputs(preview.Root, false);
        if (!input.Paths.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(preview.Dependencies.Keys)) throw new IOException("目标文件清单变化，请重新预览 / Target inventory changed; preview again");
        foreach (var pair in preview.Dependencies)
        { cancel.ThrowIfCancellationRequested(); if (ContentStamp.Of(input.Read(pair.Key)) != pair.Value) throw new IOException("目标文件变化，请重新预览 / Target content changed; preview again: " + pair.Key); }
    }
    internal static FileStream Lock(string root)
    {
        var path = ChangePaths.Resolve(root, ".warno-editor/change-restore.lock"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static void WriteTarget(string root, string relative, byte[]? bytes)
    {
        ChangePaths.Writable(relative); var path = ChangePaths.Resolve(root, relative);
        if (bytes is null) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { Flush(temporary, bytes); ChangePaths.Resolve(root, relative); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Save(string root, ChangeJournal journal)
    {
        var path = ChangePaths.Resolve(root, Folder + "/" + journal.Id + "/record.json"); var tmp = path + ".tmp";
        try { Flush(tmp, JsonSerializer.SerializeToUtf8Bytes(journal, ChangeJson.Options)); File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    private static void Flush(string path, byte[] bytes)
    { using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None); stream.Write(bytes); stream.Flush(true); }
    private static void Validate(ChangeJournal journal)
    {
        if (journal.Version is not (1 or 2 or 3 or 4) || !Guid.TryParseExact(journal.Id, "N", out _) || journal.Files is null || journal.Decisions is null ||
            journal.Version < 3 && journal.Decisions.Count > 0 ||
            journal.State is not ("Prepared" or "Committing" or "Completed" or "RecoveryRequired" or "RolledBack") ||
            journal.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Files.Count)
            throw new InvalidDataException("事务格式未知 / Invalid transaction format");
        if (journal.Version >= 2)
        {
            if (journal.RecordPaths is null || journal.DeferredPaths is null || journal.Mappings is null || journal.TargetBaseline is null ||
                journal.RecordPaths.Count == 0 || journal.RecordPaths.Count != journal.Files.Count ||
                journal.RecordPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.RecordPaths.Count ||
                journal.Mappings.Select(m => m.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Mappings.Count)
                throw new InvalidDataException("分组回执不完整 / Incomplete group receipt");
            foreach (var path in journal.RecordPaths.Concat(journal.DeferredPaths).Concat(journal.TargetBaseline.Keys)) ChangePaths.Normalize(path);
            foreach (var map in journal.Mappings)
            {
                ChangePaths.Writable(map.TargetPath);
                if (!journal.RecordPaths.Contains(map.SourcePath, StringComparer.OrdinalIgnoreCase) || map.Objects is null) throw new InvalidDataException("对应回执无效 / Invalid mapping receipt");
            }
            var targets = journal.RecordPaths.Select(p => journal.Mappings.SingleOrDefault(m => m.SourcePath.Equals(p, StringComparison.OrdinalIgnoreCase))?.TargetPath ?? p);
            if (!targets.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(journal.Files.Select(f => f.Path))) throw new InvalidDataException("回执文件不对应 / Receipt files do not match");
            ChangeTextDecisions.Validate(journal.Decisions);
            if (journal.Version < 4 && ChangeTextDecisions.HasNdf(journal.Decisions)) throw new InvalidDataException("此回执版本不支持 NDF 选择 / This receipt version does not support NDF choices");
            if (journal.Decisions.Any(d => !journal.RecordPaths.Contains(d.Conflict.Path, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException("冲突选择不在本批次中 / Conflict choice is not part of this batch");
        }
        foreach (var f in journal.Files)
        {
            ChangePaths.Writable(f.Path);
            foreach (var p in new[] { f.BeforePayload, f.AfterPayload }.Where(p => p is not null))
                if (!System.Text.RegularExpressions.Regex.IsMatch(p!, @"^\d+\.(before|after)$")) throw new InvalidDataException("备份路径无效 / Invalid backup path");
        }
    }
}
