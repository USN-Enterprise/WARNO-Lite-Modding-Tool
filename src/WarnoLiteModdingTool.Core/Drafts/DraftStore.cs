using System.Text;
using System.Text.Json;

namespace WarnoLiteModdingTool.Core.Drafts;

public sealed class DraftStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DraftDocument _document = DraftDocument.Empty;
    private bool _blocked;

    public DraftStore(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ProjectRoot = Path.GetFullPath(projectRoot);
        EditorDirectory = Path.Combine(ProjectRoot, ".warno-editor");
        DraftPath = Path.Combine(EditorDirectory, "draft-v1.json");
    }

    public string ProjectRoot { get; }

    public string EditorDirectory { get; }

    public string DraftPath { get; }

    public IReadOnlyList<DraftOperation> Operations => _document.Operations;

    public bool IsBlocked => _blocked;

    public async Task<DraftLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(DraftPath))
            {
                _document = DraftDocument.Empty;
                _blocked = false;
                return new DraftLoadResult(_document, null, false);
            }

            try
            {
                var json = await File.ReadAllTextAsync(DraftPath, cancellationToken);
                var document = JsonSerializer.Deserialize<DraftDocument>(json, JsonOptions)
                    ?? throw new JsonException("草稿内容为空。");
                if (document.SchemaVersion != 1)
                {
                    _blocked = true;
                    return new DraftLoadResult(
                        DraftDocument.Empty,
                        $"不支持的草稿版本：{document.SchemaVersion}。请先清空或使用兼容版本打开。",
                        true);
                }

                _document = document with
                {
                    Operations = document.Operations
                        .GroupBy(item => item.Id, StringComparer.Ordinal)
                        .Select(group => group.OrderByDescending(item => item.UpdatedUtc).First())
                        .OrderBy(item => item.UpdatedUtc)
                        .ToArray()
                };
                _blocked = false;
                return new DraftLoadResult(_document, null, false);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                _blocked = true;
                return new DraftLoadResult(
                    DraftDocument.Empty,
                    $"草稿无法解析，原文件已保留：{exception.Message}",
                    true);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(DraftOperation operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritableState();
            var operations = _document.Operations
                .Where(item => !string.Equals(item.Id, operation.Id, StringComparison.Ordinal))
                .Append(operation)
                .OrderBy(item => item.UpdatedUtc)
                .ToArray();
            var candidate = new DraftDocument(1, DateTimeOffset.UtcNow, operations);
            await SaveCandidateAsync(candidate, cancellationToken);
            _document = candidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyBatchAsync(
        IReadOnlyList<DraftOperation> upserts,
        IReadOnlyList<string> removeOperationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upserts);
        ArgumentNullException.ThrowIfNull(removeOperationIds);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritableState();
            var upsertById = upserts
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var removals = removeOperationIds.ToHashSet(StringComparer.Ordinal);
            var operations = _document.Operations
                .Where(item => !removals.Contains(item.Id) && !upsertById.ContainsKey(item.Id))
                .Concat(upsertById.Values)
                .OrderBy(item => item.UpdatedUtc)
                .ToArray();
            if (operations.Length == 0)
            {
                ClearOwnedFiles();
                _document = DraftDocument.Empty;
                return;
            }

            var candidate = new DraftDocument(1, DateTimeOffset.UtcNow, operations);
            await SaveCandidateAsync(candidate, cancellationToken);
            _document = candidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string operationId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWritableState();
            var operations = _document.Operations
                .Where(item => !string.Equals(item.Id, operationId, StringComparison.Ordinal))
                .ToArray();
            if (operations.Length == 0)
            {
                ClearOwnedFiles();
                _document = DraftDocument.Empty;
                return;
            }

            var candidate = new DraftDocument(1, DateTimeOffset.UtcNow, operations);
            await SaveCandidateAsync(candidate, cancellationToken);
            _document = candidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ClearOwnedFiles();
            _document = DraftDocument.Empty;
            _blocked = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task SaveCandidateAsync(DraftDocument candidate, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(EditorDirectory);
        var temporary = Path.Combine(EditorDirectory, $"draft-v1.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(candidate, JsonOptions);
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, DraftPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void ClearOwnedFiles()
    {
        if (File.Exists(DraftPath))
        {
            File.Delete(DraftPath);
        }

        if (Directory.Exists(EditorDirectory) && !Directory.EnumerateFileSystemEntries(EditorDirectory).Any())
        {
            Directory.Delete(EditorDirectory);
        }
    }

    private void EnsureWritableState()
    {
        if (_blocked)
        {
            throw new InvalidOperationException("现有草稿损坏或版本不兼容；请先清空草稿。");
        }
    }
}
