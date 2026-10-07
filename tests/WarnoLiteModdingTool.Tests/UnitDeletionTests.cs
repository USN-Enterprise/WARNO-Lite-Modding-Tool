using System.IO;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static string DeletionFixture(string nl = "\n")
    {
        var root = Fixture199(nl);
        var path = Path.Combine(root, "GameData/Generated/Gameplay/Decks/DivisionRules.ndf");
        File.WriteAllText(path, File.ReadAllText(path).Replace("UnitRules =", "UnitRuleList ="));
        return root;
    }
    private static async Task DeletionFocused()
    {
        await DeletionOriginalRestore(); Console.WriteLine("deletion original PASS");
        await DeletionForceReferences(); Console.WriteLine("deletion force PASS");
        await DeletionCustomAndStale(); Console.WriteLine("deletion custom PASS");
        await DeletionInvalidScope(); Console.WriteLine("deletion unverified PASS");
        await DeletionRollback(); Console.WriteLine("deletion rollback PASS");
        await DeletionDraftCoexistence(); await DeletionBoundaries(); Console.WriteLine("Deletion 7/7 passed");
    }
    private static async Task DeletionOriginalRestore()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        {
            var root = DeletionFixture(nl);
            try
            {
                var (_, units, _) = await LoadP4Async(root); var unit = units.Units.Single(u => u.Name.EndsWith("Recon_SOV"));
                var originals = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
                using var store = new DraftStore(root); await store.LoadAsync();
                await store.UpsertAsync(UnitDeletionV2.Operation(unit, new(root, deletionScope: true)));
                TestAssert.True(File.ReadAllText(store.DraftPath).Contains("\"schemaVersion\": 7"), "new deletion schema7");
                var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
                TestAssert.True(preview.DeletionWarnings.Length == 0, "strict clean deletion");
                var result = await service.CommitApplyAsync(preview, store);
                TestAssert.True(result.Succeeded && result.Warnings.Count == 0, "delete original without creation backup");
                TestAssert.True(!File.ReadAllText(Path.Combine(root, Unit199Path)).Contains("export " + unit.Name), "declaration removed");
                TestAssert.True(UnitCreationHistory.Load(root).ReservedIds.Contains(3), "serializer id reserved");
                TestAssert.True(UnitCreationHistory.Load(root).Units.Count == 0, "no fake creation provenance");
                TestAssert.True(!preview.Files.Any(f => f.Kind == FormalTextFileKind.Csv), "original name retained");
                await service.CommitRestoreAsync(service.PrepareRestore(root, result.BackupId));
                foreach (var (path, bytes) in originals) TestAssert.True(bytes.SequenceEqual(File.ReadAllBytes(path)), "restore exact original " + path);
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task DeletionForceReferences()
    {
        var root = DeletionFixture();
        try
        {
            var (_, units, _) = await LoadP4Async(root); var unit = units.Units.First();
            var file = Path.Combine(root, "Unknown.ndf"); var raw = "Unknown is TFuture(Ref = $/GFX/Unit/" + unit.Name + ")\nTags is TFuture(Tag = 'UNITE_Test_Tank_US')\n"; File.WriteAllText(file, raw);
            using var store = new DraftStore(root); await store.LoadAsync(); var service = new UnitTransactionService();
            await store.UpsertAsync(UnitDeletionV2.Operation(unit, new(root, deletionScope: true)));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "strict refuses unresolved refs");
            var graph = new UnitProjectGraph(root, deletionScope: true); var uses = UnitDeletionV2.Analyze(graph, graph.RequireObject(Unit199Path, unit.Name)).Uses;
            var keepRule = uses.First(u => u.Field == "UnitRule");
            await store.UpsertAsync(UnitDeletionV2.Operation(unit, graph, true, new() { [keepRule.Key] = UnitDeletionV2.Keep }));
            var preview = await service.PrepareApplyAsync(root, store.Operations);
            TestAssert.True(preview.DeletionWarnings.Length >= 3, "upgrade, unknown and tag reported");
            var result = await service.CommitApplyAsync(preview, store);
            TestAssert.True(result.Succeeded && result.Warnings.Count >= 3, "force commits with warnings");
            TestAssert.Equal(raw, File.ReadAllText(file), "unknown refs preserved");
            TestAssert.True(File.ReadAllText(Path.Combine(root, Unit199Path)).Contains("UpgradeFromUnit = " + unit.Name), "dangling upgrade retained");
            TestAssert.Equal("CompletedWithWarning", new TransactionBackupStore(root).Load(result.BackupId).State, "warning persisted");
            TestAssert.True(File.ReadAllText(Path.Combine(root, result.LogRelativePath!)).Contains("Force deletion"), "formal log residuals");
            TestAssert.True(UnitCreationHistory.Load(root).ReservedIds.Contains(17), "highest id reserved");
            await service.CommitRestoreAsync(service.PrepareRestore(root, result.BackupId));
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task DeletionCustomAndStale()
    {
        var root = DeletionFixture();
        try
        {
            var (_, units, _) = await LoadP4Async(root); var unit = units.Units.Last(); var other = units.Units.First();
            var relative = "Custom/Nested/Users.NDF"; Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, relative))!);
            var raw = "CustomUser is TFuture(Unit = $/GFX/Unit/" + unit.Name + ")\n"; File.WriteAllText(Path.Combine(root, relative), raw);
            var graph = new UnitProjectGraph(root, deletionScope: true); var use = UnitDeletionV2.Analyze(graph, graph.RequireObject(Unit199Path, unit.Name)).Uses.Single(u => u.File == relative);
            var op = UnitDeletionV2.Operation(unit, graph, choices: new() { [use.Key] = graph.ReferenceTo(relative, graph.RequireObject(Unit199Path, other.Name)) });
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(op); var service = new UnitTransactionService();
            var preview = await service.PrepareApplyAsync(root, store.Operations);
            File.WriteAllText(Path.Combine(root, "NewUse.ndf"), "Added is TFuture(Ref = $/GFX/Unit/" + unit.Name + ")");
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "new input invalidates preview");
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "new ref invalidates draft review");
            File.Delete(Path.Combine(root, "NewUse.ndf"));
            preview = await service.PrepareApplyAsync(root, store.Operations); var result = await service.CommitApplyAsync(preview, store);
            TestAssert.True(File.ReadAllText(Path.Combine(root, relative)).Contains(other.Name), "custom path written");
            await service.CommitRestoreAsync(service.PrepareRestore(root, result.BackupId)); TestAssert.Equal(raw, File.ReadAllText(Path.Combine(root, relative)), "custom restored");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task DeletionInvalidScope()
    {
        var root = DeletionFixture();
        try
        {
            var unitPath = Path.Combine(root, Unit199Path); var raw = File.ReadAllText(unitPath);
            var match = System.Text.RegularExpressions.Regex.Matches(raw, @"(?m)^.*DescriptorId.*(?:\r?\n|$)").Last();
            File.WriteAllText(unitPath, raw.Remove(match.Index, match.Length));
            var (_, units, _) = await LoadP4Async(root); var unit = units.Units.Last();
            File.WriteAllBytes(Path.Combine(root, "Broken.NDF"), [0xff, 0x01, 0xff]);
            using var store = new DraftStore(root); await store.LoadAsync(); var service = new UnitTransactionService();
            var graph = new UnitProjectGraph(root, deletionScope: true); TestAssert.True(graph.CoverageIssues.Count > 0, "invalid encoding reported");
            await store.UpsertAsync(UnitDeletionV2.Operation(unit, graph));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "strict blocks incomplete scan");
            await store.UpsertAsync(UnitDeletionV2.Operation(unit, graph, true)); var preview = await service.PrepareApplyAsync(root, store.Operations);
            File.WriteAllBytes(Path.Combine(root, "Broken.NDF"), [0xff, 0x02, 0xff]);
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "invalid bytes tracked");
            preview = await service.PrepareApplyAsync(root, store.Operations); var result = await service.CommitApplyAsync(preview, store);
            TestAssert.True(result.Succeeded && result.Warnings.Count > 0, "missing guid and incomplete scope force accepted");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task DeletionRollback()
    {
        var root = DeletionFixture();
        try
        {
            var (_, units, _) = await LoadP4Async(root); using var store = new DraftStore(root); await store.LoadAsync();
            await store.UpsertAsync(UnitDeletionV2.Operation(units.Units.Last(), new(root, deletionScope: true)));
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
            foreach (var index in new[] { 0, 1, 2 })
            {
                var changed = preview.Files.Where(f => f.Existed).ToArray(); var locked = changed[Math.Min(index, changed.Length - 1)].FullPath;
                File.SetAttributes(locked, FileAttributes.ReadOnly);
                try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "injected readonly failure"); }
                finally { File.SetAttributes(locked, FileAttributes.Normal); }
                foreach (var file in changed) TestAssert.True(file.OriginalBytes.SequenceEqual(File.ReadAllBytes(file.FullPath)), "rollback exact bytes");
                TestAssert.Equal(1, store.Operations.Count, "failed deletion retains draft"); preview = await service.PrepareApplyAsync(root, store.Operations);
            }
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task DeletionDraftCoexistence()
    {
        var root = DeletionFixture();
        try
        {
            var (_, units, _) = await LoadP4Async(root); var unit = units.Units.Last();
            using var store = new DraftStore(root); await store.LoadAsync(); var service = new UnitTransactionService();
            await store.UpsertAsync(UnitDeletion.Operation(unit));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "legacy provenance guard");
            var op = UnitDeletionV2.Operation(unit, new(root, deletionScope: true)); await store.UpsertAsync(op);
            using (var reopened = new DraftStore(root)) { var load = await reopened.LoadAsync(); TestAssert.True(!load.IsBlocked && UnitDeletion.Read(reopened.Operations.Single()).Version == 2, "new draft reopen"); }
            var source = Core.SourceWorkspace.SourceEditing.Operation(Unit199Path, Core.SourceWorkspace.SourceFiles.Open(root, Unit199Path) with { Text = File.ReadAllText(Path.Combine(root, Unit199Path)) + "\n// source draft\n" });
            await TestAssert.ThrowsAsync<InvalidOperationException>(() => store.UpsertAsync(source), "source overlap blocked before saving");
            TestAssert.Equal(1, store.Operations.Count, "rejected source leaves deletion draft intact");
            await store.RemoveAsync(op.Id); TestAssert.Equal(0, store.Operations.Count, "undo deletion");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
