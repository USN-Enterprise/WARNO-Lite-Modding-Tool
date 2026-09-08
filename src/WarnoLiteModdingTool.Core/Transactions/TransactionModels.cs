using WarnoLiteModdingTool.Core.Drafts;

namespace WarnoLiteModdingTool.Core.Transactions;

public enum PlannedFileAction
{
    Write,
    Delete
}

public sealed record PlannedFileChange(
    string RelativePath,
    string FullPath,
    FormalTextFileKind Kind,
    PlannedFileAction Action,
    bool Existed,
    byte[] OriginalBytes,
    byte[] CandidateBytes,
    DateTime? OriginalLastWriteUtc,
    IReadOnlyList<string> Summaries);

public sealed record ApplyPreview(
    string ProjectRoot,
    string BackupId,
    DateTimeOffset PreparedUtc,
    IReadOnlyList<DraftOperation> Operations,
    IReadOnlyList<PlannedFileChange> Files,
    IReadOnlyList<string> ValidationMessages)
{
    public int FormalFileCount => Files.Count(item => item.Kind is FormalTextFileKind.Ndf or FormalTextFileKind.Csv);

    public string LogRelativePath => Files.Single(item => item.Kind == FormalTextFileKind.Log).RelativePath;
}

public sealed record RestorePreview(
    string ProjectRoot,
    string SourceBackupId,
    string RestoreBackupId,
    DateTimeOffset PreparedUtc,
    IReadOnlyList<PlannedFileChange> Files,
    IReadOnlyList<string> ValidationMessages);

public sealed record TransactionResult(
    bool Succeeded,
    string BackupId,
    string Message,
    string? LogRelativePath,
    IReadOnlyList<string> Warnings);

public sealed record TransactionManifest(
    int SchemaVersion,
    string BackupId,
    string Kind,
    string? SourceBackupId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? CompletedUtc,
    string State,
    IReadOnlyList<TransactionManifestFile> Files,
    IReadOnlyList<string> CommittedFiles,
    IReadOnlyList<DraftOperation> Operations,
    string? Error);

public sealed record TransactionManifestFile(
    string RelativePath,
    string Kind,
    bool Existed,
    long OriginalLength,
    DateTime? OriginalLastWriteUtc,
    string? OriginalBackupPath);

public sealed record BackupSummary(
    string BackupId,
    string Kind,
    string State,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<string> Files,
    bool CanRestore,
    string? Error);

public sealed class TransactionValidationException(string message) : InvalidOperationException(message);
