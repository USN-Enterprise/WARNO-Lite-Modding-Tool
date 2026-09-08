using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;

namespace WarnoLiteModdingTool.Core.Transactions;

public sealed class TransactionBackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public TransactionBackupStore(string projectRoot)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
        BackupsRoot = Path.Combine(ProjectRoot, ".warno-editor", "backups");
    }

    public string ProjectRoot { get; }

    public string BackupsRoot { get; }

    public async Task<TransactionManifest> CreateAsync(
        string backupId,
        string kind,
        string? sourceBackupId,
        IReadOnlyList<PlannedFileChange> changes,
        IReadOnlyList<DraftOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ValidateBackupId(backupId);
        var backupDirectory = BackupDirectory(backupId);
        if (Directory.Exists(backupDirectory))
        {
            throw new IOException($"备份编号已存在：{backupId}");
        }

        Directory.CreateDirectory(backupDirectory);
        try
        {
            var files = new List<TransactionManifestFile>(changes.Count);
            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? backupRelativePath = null;
                if (change.Existed)
                {
                    backupRelativePath = Normalize(Path.Combine("original", change.RelativePath));
                    var backupPath = ResolveInsideBackup(backupDirectory, backupRelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    await WriteFlushedAsync(backupPath, change.OriginalBytes, cancellationToken);
                }

                files.Add(new TransactionManifestFile(
                    Normalize(change.RelativePath),
                    change.Kind.ToString(),
                    change.Existed,
                    change.OriginalBytes.LongLength,
                    change.OriginalLastWriteUtc,
                    backupRelativePath));
            }

            var manifest = new TransactionManifest(
                1,
                backupId,
                kind,
                sourceBackupId,
                DateTimeOffset.UtcNow,
                null,
                "Prepared",
                files,
                [],
                operations,
                null);
            await SaveAsync(manifest, cancellationToken);
            return manifest;
        }
        catch
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, true);
            }

            throw;
        }
    }

    public async Task SaveAsync(TransactionManifest manifest, CancellationToken cancellationToken = default)
    {
        ValidateBackupId(manifest.BackupId);
        var directory = BackupDirectory(manifest.BackupId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "manifest.json");
        var temporary = Path.Combine(directory, $"manifest.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            await WriteFlushedAsync(temporary, new UTF8Encoding(false).GetBytes(json), cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public TransactionManifest Load(string backupId)
    {
        ValidateBackupId(backupId);
        var path = Path.Combine(BackupDirectory(backupId), "manifest.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"备份清单不存在：{backupId}", path);
        }

        var manifest = JsonSerializer.Deserialize<TransactionManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"备份清单为空：{backupId}");
        if (manifest.SchemaVersion != 1 || !string.Equals(manifest.BackupId, backupId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"备份清单版本或编号不匹配：{backupId}");
        }

        return manifest;
    }

    public IReadOnlyList<BackupSummary> List()
    {
        if (!Directory.Exists(BackupsRoot))
        {
            return [];
        }

        var result = new List<BackupSummary>();
        foreach (var directory in Directory.EnumerateDirectories(BackupsRoot).OrderDescending())
        {
            var backupId = Path.GetFileName(directory);
            try
            {
                var manifest = Load(backupId);
                result.Add(new BackupSummary(
                    manifest.BackupId,
                    manifest.Kind,
                    manifest.State,
                    manifest.CreatedUtc,
                    manifest.Files.Select(item => item.RelativePath).ToArray(),
                    !string.Equals(manifest.State, "RolledBack", StringComparison.Ordinal),
                    manifest.Error));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                result.Add(new BackupSummary(
                    backupId,
                    "Unknown",
                    "Invalid",
                    Directory.GetCreationTimeUtc(directory),
                    [],
                    false,
                    exception.Message));
            }
        }

        return result.OrderByDescending(item => item.CreatedUtc).ToArray();
    }

    public byte[] ReadOriginal(string backupId, TransactionManifestFile file)
    {
        if (!file.Existed || file.OriginalBackupPath is null)
        {
            return [];
        }

        var path = ResolveInsideBackup(BackupDirectory(backupId), file.OriginalBackupPath);
        var bytes = File.ReadAllBytes(path);
        if (bytes.LongLength != file.OriginalLength)
        {
            throw new InvalidDataException($"备份原件长度不匹配：{file.RelativePath}");
        }

        return bytes;
    }

    public string BackupDirectory(string backupId)
    {
        ValidateBackupId(backupId);
        return Path.Combine(BackupsRoot, backupId);
    }

    private static string ResolveInsideBackup(string backupDirectory, string relativePath)
    {
        var root = Path.GetFullPath(backupDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"备份路径越界：{relativePath}");
        }

        return candidate;
    }

    private static void ValidateBackupId(string backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId) || backupId.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new InvalidDataException($"备份编号无效：{backupId}");
        }
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
