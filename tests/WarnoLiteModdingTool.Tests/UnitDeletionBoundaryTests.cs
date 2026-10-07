using System.IO;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task DeletionBoundaries()
    {
        var root = DeletionFixture();
        try
        {
            var (_, units, _) = await LoadP4Async(root); var unit = units.Units.Last(); var other = units.Units.First();
            var path = Path.Combine(root, "CustomTransport.ndf");
            var text = "TransportDivision is TDeckDivisionRule(UnitRuleList = [TDeckUniteRule(UnitDescriptor = $/GFX/Unit/" + other.Name + ", AvailableWithoutTransport = False, AvailableTransportList = [$/GFX/Unit/" + unit.Name + "])])\n";
            File.WriteAllText(path, text);
            var graph = new UnitProjectGraph(root, deletionScope: true);
            var use = UnitDeletionV2.Analyze(graph, graph.RequireObject(Unit199Path, unit.Name)).Uses.Single(u => u.File == "CustomTransport.ndf");
            TestAssert.True(use.Field == "Transport" && !use.Automatic, "last required transport not auto removed");
            using var store = new DraftStore(root); await store.LoadAsync(); var service = new UnitTransactionService();
            await store.UpsertAsync(UnitDeletionV2.Operation(unit, graph, true, new() { [use.Key] = "$/GFX/Unit/" + other.Name }));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "force does not bypass invalid transport replacement");
            await store.UpsertAsync(UnitDeletionV2.Operation(unit, graph, true));
            var preview = await service.PrepareApplyAsync(root, store.Operations);
            var old = File.ReadAllBytes(Path.Combine(root, Unit199Path)); var operation = store.Operations.Single();
            var commit = await service.CommitApplyAsync(preview, store);
            TestAssert.Equal(text, File.ReadAllText(path), "required transport preserved as explicit residual");
            // Simulate a committed draft whose cleanup failed, followed by an external recreation.
            File.WriteAllBytes(Path.Combine(root, Unit199Path), old);
            await store.UpsertAsync(operation);
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "already committed deletion cannot delete recreated identical unit");
            await store.ClearAsync(); await service.CommitRestoreAsync(service.PrepareRestore(root, commit.BackupId));
            File.Delete(path);
            var registry = Path.Combine(root, UnitCreation.SerializerPath); var baseline = File.ReadAllText(registry);
            File.WriteAllText(Path.Combine(root, "DuplicateIds.ndf"), "Duplicate is TDeckSerializerEntries(UnitIds = MAP [($/GFX/Unit/" + unit.Name + ", 3)])");
            graph = new UnitProjectGraph(root, deletionScope: true);
            await store.UpsertAsync(UnitDeletionV2.Operation(unit, graph));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "strict refuses duplicate registration");
            await store.UpsertAsync(UnitDeletionV2.Operation(unit, graph, true)); preview = await service.PrepareApplyAsync(root, store.Operations);
            TestAssert.True(preview.DeletionWarnings.Count(w => w.Contains("UnitIds")) == 2, "both ambiguous registrations reported");
            await service.CommitApplyAsync(preview, store); TestAssert.Equal(baseline, File.ReadAllText(registry), "ambiguous ids untouched");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task DeletionOldReader(string oldCore)
    {
        var root = DeletionFixture();
        try
        {
            var (_, units, _) = await LoadP4Async(root); using var store = new DraftStore(root); await store.LoadAsync();
            await store.UpsertAsync(UnitDeletionV2.Operation(units.Units.Last(), new(root, deletionScope: true)));
            var before = File.ReadAllBytes(store.DraftPath); var context = new System.Runtime.Loader.AssemblyLoadContext("deletion-old-reader", true);
            try
            {
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(oldCore)); var type = assembly.GetType("WarnoLiteModdingTool.Core.Drafts.DraftStore")!;
                using var old = (IDisposable)Activator.CreateInstance(type, root)!; var task = (Task)type.GetMethod("LoadAsync")!.Invoke(old, [CancellationToken.None])!; await task;
                var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
                TestAssert.True((bool)result.GetType().GetProperty("IsBlocked")!.GetValue(result)!, "old reader refuses schema7");
                TestAssert.True(before.SequenceEqual(File.ReadAllBytes(store.DraftPath)), "old reader keeps draft bytes");
                var service = new UnitTransactionService(); await service.CommitApplyAsync(await service.PrepareApplyAsync(root, store.Operations), store);
                var history = assembly.GetType("WarnoLiteModdingTool.Core.Units.UnitCreationHistory")!; var refused = false;
                try { history.GetMethod("Load")!.Invoke(null, [root]); }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { refused = true; }
                TestAssert.True(refused, "old creation/deletion code refuses history v2 instead of reusing reserved ids");
            }
            finally { context.Unload(); }
            Console.WriteLine("Old draft reader and history writer protection PASS");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
