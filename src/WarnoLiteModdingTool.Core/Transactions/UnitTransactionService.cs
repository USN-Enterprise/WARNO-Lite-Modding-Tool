using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Transactions;

public sealed class UnitTransactionService(UnitApplyPlanner? planner = null)
{
    private readonly UnitApplyPlanner _planner = planner ?? new UnitApplyPlanner();

    public Task<ApplyPreview> PrepareApplyAsync(
        string projectRoot,
        IReadOnlyList<DraftOperation> operations,
        CancellationToken cancellationToken = default) =>
        _planner.PrepareAsync(projectRoot, operations, cancellationToken);

    public IReadOnlyList<BackupSummary> ListBackups(string projectRoot) =>
        new TransactionBackupStore(projectRoot).List();

    public async Task<TransactionResult> CommitApplyAsync(
        ApplyPreview preview,
        DraftStore draftStore,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetFullPath(draftStore.ProjectRoot), Path.GetFullPath(preview.ProjectRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new TransactionValidationException("草稿存储与应用项目不一致。");
        }

        if (!preview.Operations.All(operation => draftStore.Operations.Contains(operation)))
        {
            throw new TransactionValidationException("预览后草稿已变化，请重新预览。");
        }

        var backupStore = new TransactionBackupStore(preview.ProjectRoot);
        var manifest = await ExecuteAsync(
            backupStore,
            preview.BackupId,
            "Apply",
            null,
            preview.Files,
            preview.Operations,
            cancellationToken);
        var warnings = new List<string>();
        try
        {
            await draftStore.ApplyBatchAsync([], preview.Operations.Select(o => o.Id).ToArray(), CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            warnings.Add($"正式文件已应用，但草稿清理失败：{exception.Message}");
        }

        manifest = manifest with
        {
            State = warnings.Count == 0 ? "Completed" : "CompletedWithWarning",
            CompletedUtc = DateTimeOffset.UtcNow,
            Error = warnings.Count == 0 ? null : string.Join("；", warnings)
        };
        try
        {
            await backupStore.SaveAsync(manifest, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"事务已提交，但备份状态收尾失败：{exception.Message}");
        }

        return new TransactionResult(
            true,
            preview.BackupId,
            warnings.Count == 0 ? "应用完成" : "应用完成，但有收尾警告",
            preview.LogRelativePath,
            warnings);
    }

    public RestorePreview PrepareRestore(string projectRoot, string sourceBackupId)
    {
        var root = Path.GetFullPath(projectRoot);
        var backupStore = new TransactionBackupStore(root);
        var sourceManifest = backupStore.Load(sourceBackupId);
        var files = new List<PlannedFileChange>();
        foreach (var file in sourceManifest.Files.Where(item => !string.Equals(item.Kind, FormalTextFileKind.Log.ToString(), StringComparison.Ordinal)))
        {
            var fullPath = TextFileSnapshot.ResolveInsideRoot(root, file.RelativePath);
            var currentExists = File.Exists(fullPath);
            var currentBytes = currentExists ? File.ReadAllBytes(fullPath) : [];
            var candidate = file.Existed ? backupStore.ReadOriginal(sourceBackupId, file) : [];
            var kind = Enum.TryParse<FormalTextFileKind>(file.Kind, out var parsedKind)
                ? parsedKind
                : throw new InvalidDataException($"备份文件类型无效：{file.Kind}");
            if (file.Existed)
            {
                ValidateRestoredContent(file.RelativePath, kind, candidate);
            }

            files.Add(new PlannedFileChange(
                file.RelativePath,
                fullPath,
                kind,
                file.Existed ? PlannedFileAction.Write : PlannedFileAction.Delete,
                currentExists,
                currentBytes,
                candidate,
                currentExists ? File.GetLastWriteTimeUtc(fullPath) : null,
                [$"恢复 `{file.RelativePath}` 到备份 {sourceBackupId} 的应用前状态"]));
        }

        if (files.Count == 0)
        {
            throw new TransactionValidationException($"备份没有可恢复的正式文件：{sourceBackupId}");
        }

        var restoreId = UnitApplyPlanner.CreateBackupId("restore");
        var preparedUtc = DateTimeOffset.UtcNow;
        var validation = new[]
        {
            $"已验证源备份 {sourceBackupId} 的原件和路径",
            $"将恢复 {files.Count} 个正式文件",
            "恢复前会先备份这些文件的当前状态"
        };
        var logRelative = Normalize(Path.Combine("logs", $"WARNO Lite Modding Tool-{restoreId}.md"));
        var logSnapshot = TextFileSnapshot.Load(root, logRelative, FormalTextFileKind.Log, allowMissing: true);
        if (logSnapshot.Existed)
        {
            throw new TransactionValidationException($"恢复日志已存在：{logRelative}");
        }

        var log = BuildRestoreLog(restoreId, sourceBackupId, preparedUtc, files, validation);
        files.Add(UnitApplyPlanner.ToWriteChange(logSnapshot, log, ["写入本次恢复记录"]));
        return new RestorePreview(root, sourceBackupId, restoreId, preparedUtc, files, validation);
    }

    public async Task<TransactionResult> CommitRestoreAsync(
        RestorePreview preview,
        CancellationToken cancellationToken = default)
    {
        var backupStore = new TransactionBackupStore(preview.ProjectRoot);
        var manifest = await ExecuteAsync(
            backupStore,
            preview.RestoreBackupId,
            "Restore",
            preview.SourceBackupId,
            preview.Files,
            [],
            cancellationToken);
        manifest = manifest with { State = "Completed", CompletedUtc = DateTimeOffset.UtcNow };
        var warnings = new List<string>();
        try
        {
            await backupStore.SaveAsync(manifest, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"恢复已提交，但备份状态收尾失败：{exception.Message}");
        }

        return new TransactionResult(
            true,
            preview.RestoreBackupId,
            warnings.Count == 0 ? "恢复完成" : "恢复完成，但有收尾警告",
            preview.Files.Single(item => item.Kind == FormalTextFileKind.Log).RelativePath,
            warnings);
    }

    private static async Task<TransactionManifest> ExecuteAsync(
        TransactionBackupStore backupStore,
        string backupId,
        string kind,
        string? sourceBackupId,
        IReadOnlyList<PlannedFileChange> changes,
        IReadOnlyList<DraftOperation> operations,
        CancellationToken cancellationToken)
    {
        ValidatePlannedFiles(backupStore.ProjectRoot, changes);
        var manifest = await backupStore.CreateAsync(
            backupId,
            kind,
            sourceBackupId,
            changes,
            operations,
            cancellationToken);
        var temporaryFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var committed = new List<string>();
        try
        {
            VerifyCurrentFiles(changes);
            foreach (var change in changes.Where(item => item.Action == PlannedFileAction.Write))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = Path.GetDirectoryName(change.FullPath)!;
                Directory.CreateDirectory(directory);
                var temporary = Path.Combine(directory, $".{Path.GetFileName(change.FullPath)}.{backupId}.tmp");
                if (File.Exists(temporary))
                {
                    throw new IOException($"事务临时文件已存在：{temporary}");
                }

                await WriteFlushedAsync(temporary, change.CandidateBytes, cancellationToken);
                temporaryFiles[change.RelativePath] = temporary;
            }

            manifest = manifest with { State = "Committing" };
            await backupStore.SaveAsync(manifest, cancellationToken);
            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifyCurrentFile(change);
                if (change.Action == PlannedFileAction.Write)
                {
                    File.Move(temporaryFiles[change.RelativePath], change.FullPath, true);
                }
                else if (File.Exists(change.FullPath))
                {
                    File.Delete(change.FullPath);
                }

                committed.Add(change.RelativePath);
                manifest = manifest with { CommittedFiles = committed.ToArray() };
                await backupStore.SaveAsync(manifest, cancellationToken);
            }

            manifest = manifest with
            {
                State = "Committed",
                CompletedUtc = DateTimeOffset.UtcNow,
                CommittedFiles = committed.ToArray()
            };
            await backupStore.SaveAsync(manifest, cancellationToken);
            return manifest;
        }
        catch (Exception exception)
        {
            var rollbackErrors = await RollBackAsync(backupStore, manifest, changes, committed);
            manifest = manifest with
            {
                State = rollbackErrors.Count == 0 ? "RolledBack" : "RecoveryRequired",
                CompletedUtc = DateTimeOffset.UtcNow,
                CommittedFiles = committed.ToArray(),
                Error = rollbackErrors.Count == 0
                    ? exception.Message
                    : $"{exception.Message}；回滚失败：{string.Join("；", rollbackErrors)}"
            };
            try
            {
                await backupStore.SaveAsync(manifest, CancellationToken.None);
            }
            catch
            {
                // The original exception plus the durable backup directory are more useful than masking the failure.
            }

            throw new IOException(
                rollbackErrors.Count == 0
                    ? $"事务失败，已回滚。备份编号：{backupId}。{exception.Message}"
                    : $"事务失败且需要人工恢复。备份编号：{backupId}。{string.Join("；", rollbackErrors)}",
                exception);
        }
        finally
        {
            foreach (var temporary in temporaryFiles.Values)
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    private static void VerifyCurrentFiles(IReadOnlyList<PlannedFileChange> changes)
    {
        foreach (var change in changes)
        {
            VerifyCurrentFile(change);
        }
    }

    private static void ValidatePlannedFiles(string projectRoot, IReadOnlyList<PlannedFileChange> changes)
    {
        if (changes.Count == 0 || changes.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Count)
        {
            throw new TransactionValidationException("事务文件清单为空或包含重复路径。");
        }

        foreach (var change in changes)
        {
            var relative = Normalize(change.RelativePath);
            var expected = TextFileSnapshot.ResolveInsideRoot(projectRoot, relative);
            if (!string.Equals(expected, Path.GetFullPath(change.FullPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new TransactionValidationException($"事务文件身份不一致：{relative}");
            }

            var allowed = change.Kind switch
            {
                FormalTextFileKind.Ndf or FormalTextFileKind.Csv => relative.StartsWith("GameData/", StringComparison.OrdinalIgnoreCase),
                FormalTextFileKind.Log => relative.StartsWith("logs/", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
            if (!allowed)
            {
                throw new TransactionValidationException($"事务文件不在允许目录：{relative}");
            }
        }
    }

    private static void VerifyCurrentFile(PlannedFileChange change)
    {
        var exists = File.Exists(change.FullPath);
        if (exists != change.Existed || (exists && !File.ReadAllBytes(change.FullPath).SequenceEqual(change.OriginalBytes)))
        {
            throw new TransactionValidationException($"预览后文件已变化：{change.RelativePath}");
        }
    }

    private static async Task<IReadOnlyList<string>> RollBackAsync(
        TransactionBackupStore backupStore,
        TransactionManifest manifest,
        IReadOnlyList<PlannedFileChange> changes,
        IReadOnlyList<string> committed)
    {
        var errors = new List<string>();
        foreach (var relativePath in committed.Reverse())
        {
            try
            {
                var change = changes.Single(item => string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
                var file = manifest.Files.Single(item => string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
                if (file.Existed)
                {
                    var original = backupStore.ReadOriginal(manifest.BackupId, file);
                    var directory = Path.GetDirectoryName(change.FullPath)!;
                    Directory.CreateDirectory(directory);
                    var temporary = Path.Combine(directory, $".{Path.GetFileName(change.FullPath)}.{manifest.BackupId}.rollback.tmp");
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }

                    await WriteFlushedAsync(temporary, original, CancellationToken.None);
                    File.Move(temporary, change.FullPath, true);
                }
                else if (File.Exists(change.FullPath))
                {
                    File.Delete(change.FullPath);
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{relativePath}: {exception.Message}");
            }
        }

        return errors;
    }

    private static void ValidateRestoredContent(string relativePath, FormalTextFileKind kind, byte[] bytes)
    {
        if (kind == FormalTextFileKind.Ndf)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                throw new InvalidDataException($"备份 NDF 含 BOM：{relativePath}");
            }

            var text = new UTF8Encoding(false, true).GetString(bytes);
            var scan = new NdfTopLevelScanner().Scan(text, relativePath, "units");
            if (scan.Diagnostics.Any(item => item.Severity == NdfDiagnosticSeverity.Error))
            {
                throw new InvalidDataException($"备份 NDF 结构无效：{relativePath}");
            }
        }
    }

    private static string BuildRestoreLog(
        string restoreId,
        string sourceBackupId,
        DateTimeOffset preparedUtc,
        IReadOnlyList<PlannedFileChange> files,
        IReadOnlyList<string> validation)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# WARNO Lite Modding Tool 恢复记录");
        builder.AppendLine();
        builder.AppendLine($"- 恢复时间：{preparedUtc:O}");
        builder.AppendLine($"- 恢复事务备份编号：`{restoreId}`");
        builder.AppendLine($"- 来源备份编号：`{sourceBackupId}`");
        builder.AppendLine();
        builder.AppendLine("## 文件");
        builder.AppendLine();
        foreach (var file in files)
        {
            builder.AppendLine($"- `{file.RelativePath}`：恢复到来源备份记录的应用前状态");
        }

        builder.AppendLine();
        builder.AppendLine("## 校验");
        builder.AppendLine();
        foreach (var message in validation)
        {
            builder.AppendLine($"- {message}");
        }

        return builder.ToString();
    }

    private static async Task WriteFlushedAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(true);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
