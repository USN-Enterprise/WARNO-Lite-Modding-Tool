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
    private byte[]? _persistedBytes; // SHA-256 of the persisted bytes, not a second copy of the JSON.

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
                _persistedBytes = null;
                _document = DraftDocument.Empty;
                _blocked = false;
                return new DraftLoadResult(_document, null, false);
            }

            try
            {
                await using var input = new FileStream(DraftPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                _persistedBytes = await System.Security.Cryptography.SHA256.HashDataAsync(input, cancellationToken);
                input.Position = 0;
                var prefix = new byte[3]; var prefixLength = await input.ReadAsync(prefix, cancellationToken);
                input.Position = prefixLength == 3 && prefix.AsSpan().SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
                var document = await JsonSerializer.DeserializeAsync<DraftDocument>(input, JsonOptions, cancellationToken)
                    ?? throw new JsonException("草稿内容为空。");
                if (document.SchemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7) || document.Operations.Any(o => !Enum.IsDefined(o.TargetKind) || o.TargetKind == DraftTargetKind.SourceFile && (o.Source is null || document.SchemaVersion < 6) || Units.UnitDeletionV2.IsNew(o) && document.SchemaVersion < 7))
                {
                    _blocked = true;
                    return new DraftLoadResult(
                        DraftDocument.Empty,
                        $"不支持的草稿版本：{document.SchemaVersion}。原文件已保留，请使用兼容版本打开。",
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
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException)
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
            var candidate = Candidate(operations);
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

            var candidate = Candidate(operations);
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

            var candidate = Candidate(operations);
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

    private DraftDocument Candidate(IReadOnlyList<DraftOperation> operations)
    {
        var advanced = operations.Any(o => o.TargetKind == DraftTargetKind.WeaponStructure || o.TargetKind == DraftTargetKind.UnitCreate && Units.UnitCreation.Read(o).WeaponStructures.Count > 0);
        var ammo = operations.Any(o => o.InsertAmmoField || Weapons.AmmoProfessional.RequiresV3(o.FieldKey)
            || o.TargetKind == DraftTargetKind.WeaponStructure && Weapons.WeaponStructure.Read(o).AmmoFields.Values.Any(f => f.Keys.Any(Weapons.AmmoProfessional.RequiresV3))
            || o.TargetKind == DraftTargetKind.UnitCreate && Units.UnitCreation.Read(o).WeaponStructures.Any(s => s.AmmoFields.Values.Any(f => f.Keys.Any(Weapons.AmmoProfessional.RequiresV3)))
            || o.TargetKind == DraftTargetKind.WeaponBatch && Weapons.WeaponBatch.ReadCells(o).Any(c => c.InsertAmmoField || Weapons.AmmoProfessional.RequiresV3(c.Key)));
        var aviation = operations.Any(o => Units.AviationMovement.IsField(o.FieldKey) || o.GroupId?.StartsWith("aviation-speed:", StringComparison.Ordinal) == true || o.TargetKind == DraftTargetKind.UnitCreate && Units.UnitCreation.Read(o).Fields.Keys.Any(k => Units.AviationMovement.IsField(k) || k == Units.AviationMovement.Speed));
        return new(Math.Max(_document.SchemaVersion, operations.Any(Units.UnitDeletionV2.IsNew) ? 7 : operations.Any(o => o.TargetKind == DraftTargetKind.SourceFile) ? 6 : aviation ? 5 : operations.Any(o => o.TargetKind is DraftTargetKind.DamageRule or DraftTargetKind.DamageDistance) ? 4 : ammo ? 3 : advanced ? 2 : 1), DateTimeOffset.UtcNow, operations);
    }

    private async Task SaveCandidateAsync(DraftDocument candidate, CancellationToken cancellationToken)
    {
        await Task.Run(() => VerifySourceDraftRevision(candidate.SchemaVersion >= 6),cancellationToken);
        await SourceWorkspace.SourceEditing.CheckCoexistence(ProjectRoot, candidate.Operations, cancellationToken);
        Directory.CreateDirectory(EditorDirectory);
        var temporary = Path.Combine(EditorDirectory, $"draft-v1.{Guid.NewGuid():N}.tmp");
        try
        {
            await Task.Run(async () =>
            {
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                await JsonSerializer.SerializeAsync(output, candidate, JsonOptions, cancellationToken);
                await output.FlushAsync(cancellationToken); output.Flush(true);
            }, cancellationToken);
            var savedFingerprint = await Task.Run(() => SourceWorkspace.SourceFingerprint.File(temporary),cancellationToken);
            await Task.Run(() => VerifySourceDraftRevision(candidate.SchemaVersion >= 6),cancellationToken);
            if (candidate.SchemaVersion > _document.SchemaVersion && File.Exists(DraftPath))
                File.Copy(DraftPath, Path.Combine(EditorDirectory, $"draft-before-structure-{Guid.NewGuid():N}.json"), false);
            File.Move(temporary, DraftPath, true);
            _persistedBytes = savedFingerprint;
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
        VerifySourceDraftRevision(_document.SchemaVersion >= 6);
        if (File.Exists(DraftPath))
        {
            File.Delete(DraftPath);
            _persistedBytes = null;
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
    private void VerifySourceDraftRevision(bool source)
    {
        var current = SourceWorkspace.SourceFingerprint.File(DraftPath);
        if (!source && current is not null)
        {
            try { using var input = File.OpenRead(DraftPath); using var json = JsonDocument.Parse(input); source = json.RootElement.TryGetProperty("schemaVersion", out var version) && version.GetInt32() >= 6; }
            catch (JsonException) { source = _document.SchemaVersion >= 6; }
        }
        if (source && !SourceWorkspace.SourceFiles.Equal(current, _persistedBytes)) throw new IOException("源码草稿已被其他会话修改，请重新打开项目 / Source drafts changed in another session; reopen the project");
    }
}
