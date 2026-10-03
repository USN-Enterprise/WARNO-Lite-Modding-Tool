using System.Text.Json;

namespace WarnoLiteModdingTool.Core.Changes;

public sealed record ChangeReviewSession(int Version, string Id, DateTimeOffset CreatedUtc, string PackageIdentity, string TargetRoot,
    ChangeRestoreOptions Options, IReadOnlyDictionary<string, ContentStamp> Dependencies, string ReceiptState);

// Review snapshots are immutable local metadata. They never modify the portable record or formal Mod files.
public static class ChangeReviewSessions
{
    private const string Folder = ".warno-editor/change-review";
    public static ChangeReviewSession Save(ChangePreview preview, CancellationToken cancel = default)
    {
        using var gate = ChangeTransactions.Lock(preview.Root);
        ChangeTransactions.Verify(preview, cancel);
        if (ChangeTransactions.StateKey(ChangeTransactions.List(preview.Root)) != preview.ReceiptState)
            throw new InvalidDataException("回执已变化，请重新预览后保存进度 / Receipts changed; preview again before saving progress");
        var session = new ChangeReviewSession(ChangeTextDecisions.HasNdf(preview.Decisions) ? 2 : 1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, preview.PackageIdentity, Path.GetFullPath(preview.Root),
            new(preview.SelectedPaths, preview.Mappings, preview.Decisions), preview.Dependencies, preview.ReceiptState);
        var directory = ChangePaths.Resolve(preview.Root, Folder); Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session, ChangeJson.Options);
        if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("核对进度文件过大 / Review session is too large");
        var path = Path.Combine(directory, session.Id + ".json"); var temporary = path + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            File.Move(temporary, path); return session;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static ChangePreview Load(ChangePackage package, string root, CancellationToken cancel = default)
    {
        root = Path.GetFullPath(root); var directory = ChangePaths.Resolve(root, Folder);
        var sessions = new List<ChangeReviewSession>();
        foreach (var path in Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json") : [])
        {
            cancel.ThrowIfCancellationRequested(); ChangePaths.NoLinks(path);
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("核对进度文件过大 / Review session is too large");
            var session = JsonSerializer.Deserialize<ChangeReviewSession>(File.ReadAllBytes(path), ChangeJson.Options) ?? throw new InvalidDataException("核对进度为空 / Empty review session");
            if (session.PackageIdentity == package.Identity) sessions.Add(session);
        }
        var saved = sessions.OrderByDescending(s => s.CreatedUtc).FirstOrDefault() ?? throw new InvalidDataException("此记录及数值方式尚无已保存进度 / No saved progress for this record and numeric policy");
        if (saved.Version is not (1 or 2) || saved.Options is null || saved.Dependencies is null ||
            saved.Version == 1 && ChangeTextDecisions.HasNdf(saved.Options.Decisions ?? []) || !root.Equals(saved.TargetRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("核对进度版本或目标不匹配，原文件已保留 / Review session version or target mismatch; original retained");
        var preview = ChangeRestore.Prepare(package, root, cancel: cancel, options: saved.Options);
        if (saved.ReceiptState != preview.ReceiptState || saved.Dependencies.Count != preview.Dependencies.Count ||
            saved.Dependencies.Any(p => !preview.Dependencies.TryGetValue(p.Key, out var current) || p.Value != current))
            throw new InvalidDataException("目标或回执已变化，不能沿用已保存选择；请重新预览 / Target or receipts changed; saved choices cannot be reused; preview again");
        return preview;
    }
}

internal static class ChangeTextDecisions
{
    internal static bool HasNdf(IReadOnlyList<ChangeTextDecision> decisions) => decisions.Any(d => d?.Conflict?.Kind == ChangeConflictKind.NdfScalar);
    internal static void Validate(IReadOnlyList<ChangeTextDecision> decisions)
    {
        var keys = new HashSet<(string, string, string)>();
        foreach (var d in decisions)
        {
            if (d?.Conflict is not { } c || !Enum.IsDefined(d.Choice) || !Enum.IsDefined(c.Kind) || string.IsNullOrEmpty(c.Token) || string.IsNullOrEmpty(c.Column) ||
                c.Before is null || c.After is null || c.Target is null || c.Kind == ChangeConflictKind.DictionaryText && c.Column == "TOKEN")
                throw new InvalidDataException("文本冲突选择无效 / Invalid text conflict choice");
            ChangePaths.Writable(c.Path);
            if (!c.Path.EndsWith(c.Kind == ChangeConflictKind.NdfScalar ? ".ndf" : ".csv", StringComparison.OrdinalIgnoreCase) || !keys.Add((c.Path.ToUpperInvariant(), c.Token, c.Column)))
                throw new InvalidDataException("文本冲突选择重复或不受支持 / Duplicate or unsupported text conflict choice");
        }
    }
}
