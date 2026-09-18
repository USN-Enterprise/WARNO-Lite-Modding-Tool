using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Units;

public sealed record CreatedUnitIdentity(string Guid, string Name, string Mother, string Token, int SerializerId, string Source, string Evidence, bool Deleted = false, string? NamingRoot = null);
public sealed record UnitCreationLedger(int Version, IReadOnlyList<CreatedUnitIdentity> Units, IReadOnlyList<int> ReservedIds);

public static class UnitCreationHistory
{
    public const string LedgerPath = ".warno-editor/unit-history-v1.json";
    public static UnitCreationLedger Load(string root)
    {
        var path = TextFileSnapshot.ResolveInsideRoot(root, LedgerPath);
        if (!File.Exists(path)) return new(1, [], []);
        return JsonSerializer.Deserialize<UnitCreationLedger>(File.ReadAllText(path)) is { Version: 1, Units: not null, ReservedIds: not null } ledger ? ledger : throw new InvalidDataException("新建单位来源记录无效");
    }
    public static CreatedUnitIdentity Require(UnitProjectGraph graph, string file, string name)
    {
        var unit = graph.RequireObject(file, name); var guid = UnitIdentityEditing.GuidOf(graph.Body(unit));
        if (graph.Objects.Where(o => o.TypeName == "TEntityDescriptor").Count(o => UnitIdentityEditing.GuidOf(graph.Body(o)) == guid) != 1) throw new TransactionValidationException("单位GUID不唯一，不能删除");
        var ledger = Load(graph.Root);
        var known = ledger.Units.Where(u => !u.Deleted && u.Guid == guid && u.Name == name && UnitProjectGraph.Normalize(u.Source) == UnitProjectGraph.Normalize(file)).ToArray();
        var backups = new TransactionBackupStore(graph.Root);
        var manifests = backups.List().Where(b => b.Kind == "Apply" && b.State is "Completed" or "CompletedWithWarning" or "Committed").ToArray();
        foreach (var backup in manifests)
        {
            var manifest = backups.Load(backup.BackupId);
            foreach (var op in manifest.Operations.Where(o => o.TargetKind == DraftTargetKind.UnitCreate))
            {
                var creation = UnitCreation.Read(op);
                if ("GUID:{" + creation.Guid + "}" != guid || UnitProjectGraph.Normalize(op.RelativeSourceFile) != UnitProjectGraph.Normalize(file)) continue;
                if (creation.Id != name && !known.Any(u => u.Evidence == backup.BackupId)) continue;
                var oldFile = manifest.Files.SingleOrDefault(f => UnitProjectGraph.Normalize(f.RelativePath) == UnitProjectGraph.Normalize(file));
                if (oldFile is null || !oldFile.Existed) continue;
                var original = System.Text.Encoding.UTF8.GetString(backups.ReadOriginal(backup.BackupId, oldFile));
                if (new Ndf.NdfSyntaxDocument(original).Tokens.Any(t => t.Text == guid)) continue;
                return known.SingleOrDefault(u => u.Evidence == backup.BackupId) ?? new(guid, name, creation.Mother, creation.Token, creation.SerializerId, file, backup.BackupId, NamingRoot: creation.NamingRoot);
            }
        }
        throw new TransactionValidationException("无法核对本项目成功创建记录，请保留或提供对应创建备份：" + name);
    }
    public static void Plan(string root, IReadOnlyList<DraftOperation> ops, List<PlannedFileChange> files, string backupId)
    {
        if (!ops.Any(o => o.TargetKind is DraftTargetKind.UnitCreate or DraftTargetKind.UnitRename or DraftTargetKind.UnitDelete)) return;
        var ledger = Load(root); var units = ledger.Units.ToList(); var ids = ledger.ReservedIds.ToHashSet();
        var graph = new UnitProjectGraph(root);
        foreach (var op in ops)
        {
            if (op.TargetKind == DraftTargetKind.UnitCreate)
            {
                var s = UnitCreation.Read(op); units.RemoveAll(u => u.Guid == "GUID:{" + s.Guid + "}");
                units.Add(new("GUID:{" + s.Guid + "}", s.Id, s.Mother, s.Token, s.SerializerId, op.RelativeSourceFile, backupId, NamingRoot: s.NamingRoot));
            }
            if (op.TargetKind is DraftTargetKind.UnitRename or DraftTargetKind.UnitDelete)
            {
                CreatedUnitIdentity? entry = null;
                try { entry = Require(graph, op.RelativeSourceFile, op.ObjectName); }
                catch (TransactionValidationException) when (op.TargetKind == DraftTargetKind.UnitRename) { }
                if (entry is null) continue;
                units.RemoveAll(u => u.Guid == entry.Guid);
                if (op.TargetKind == DraftTargetKind.UnitDelete) { units.Add(entry with { Deleted = true }); ids.Add(entry.SerializerId); }
                else units.Add(entry with { Name = UnitIdentityEditing.Read(op).NewName });
            }
        }
        UnitProjectGraph.Put(root, LedgerPath, JsonSerializer.Serialize(new UnitCreationLedger(1, units, ids.Order().ToArray()), new JsonSerializerOptions { WriteIndented = true }), files, "维护新建单位来源与保留编号", FormalTextFileKind.Metadata);
    }
}
