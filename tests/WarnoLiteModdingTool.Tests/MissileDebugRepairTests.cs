using System.IO;
using System.Text;
using System.Runtime.Loader;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private const string BrokenMissile = "TestFlight_WLMT_1111111111";
    private static string BrokenFlight(string name = BrokenMissile, string guid = "80000000-0000-0000-0000-000000000002") =>
        FlightBody().Replace("export TestFlight is", "export " + name + " is")
            .Replace("80000000-0000-0000-0000-000000000001", guid).Replace("MaxSpeedGRU = 1800", "MaxSpeedGRU = 2500");
    private static string BrokenMissileFixture(string nl = "\n", bool independent = false)
    {
        var root = MotionFixture(nl);
        File.AppendAllText(Path.Combine(root, MotionFile), BrokenFlight().Replace("\n", nl), new UTF8Encoding(false));
        var path = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
        File.WriteAllText(path, File.ReadAllText(path).Replace("~/TestFlight", "~/" + BrokenMissile), new UTF8Encoding(false));
        if (independent)
        {
            var outside = Path.Combine(root, "Custom/ClonedFlights.ndf"); Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
            File.Move(Path.Combine(root, MotionFile), outside);
            File.Delete(Path.Combine(root, "GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf"));
            File.Delete(Path.Combine(root, "GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf"));
        }
        return root;
    }
    private static async Task MissileDebugNewClones()
    {
        var root = MotionFixture();
        try
        {
            var (_, _, data) = await LoadP4Async(root); var op = MissileMotion.Prepare(data, [], MotionOp(data));
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(op);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, [op]);
            var graph = new UnitProjectGraph(root, preview.Files, deletionScope: true);
            var entities = graph.Objects.Where(o => new NdfSyntaxDocument(graph.Body(o)).FindConstructors("TGuidedMissileMovementModuleDescriptor").Count > 0).ToArray();
            var debugNames = entities.Select(e => { var doc = new NdfSyntaxDocument(graph.Body(e)); return doc.Raw(MissileDebugRepair.DebugSpan(doc)); }).ToArray();
            Assert(debugNames.Length == 2 && debugNames.Distinct().Count() == 2, "源实体和新副本debug名独立，重现截图条件");
            var identifiers = entities.Select(e => { var d = new NdfSyntaxDocument(graph.Body(e)); return d.Raw(d.FindDirectAssignments(d.FindConstructors("TEntityDescriptor").Single(), "DescriptorId").Single()); }).ToArray();
            Assert(identifiers.Distinct().Count() == 2 && entities.Select(e => e.Name).Distinct().Count() == 2, "NDF名、GUID、debug名三种身份都独立");
            Assert(graph.Body(graph.RequireObject(MotionFile, "TestFlight")).Contains("ClassNameForDebug = 'keep flight visual'"), "原实体自定义debug不变");
            Assert((await service.CommitApplyAsync(preview, store)).Succeeded, "唯一身份候选提交");
        }
        finally { DeleteTemporaryFixture(root); }
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Missile_X_WLMT_1111111111", "Missile_X_WLMT_1111111111_1" };
        Assert(MissileDebugRepair.Allocate("Descriptor_Missile_X_WLMT_1111111111", reserved) == "Missile_X_WLMT_1111111111_2", "按当前Mod保留值避让重名");
    }
    private static async Task MissileDebugRepairTransactions()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        foreach (var independent in new[] { false, true })
        {
            var root = BrokenMissileFixture(nl, independent);
            try
            {
                var originals = Directory.GetFiles(root, "*.ndf", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
                var scan = MissileDebugRepair.Scan(root);
                Assert(scan.Rows.Count == 2 && scan.Rows.Single(r => r.CanRepair).Name == BrokenMissile, "只处理已确认工具副本，原实体保留");
                var ops = MissileDebugRepair.Prepare(root, [BrokenMissile]); using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync(ops, []);
                Assert(File.ReadAllText(store.DraftPath).Contains("\"schemaVersion\": 10"), "修复草稿schema10");
                Assert(originals.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "扫描/预览/保存不修改正式NDF");
                using (var reopened = new DraftStore(root)) { Assert(!(await reopened.LoadAsync()).IsBlocked && reopened.Operations.Count == 1, "修复草稿重开"); }
                var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, ops);
                var ndf = preview.Files.Single(f => f.Kind == FormalTextFileKind.Ndf);
                var after = Encoding.UTF8.GetString(ndf.CandidateBytes); var before = Encoding.UTF8.GetString(ndf.OriginalBytes);
                var graph = new UnitProjectGraph(root, deletionScope: true); var state = MissileDebugRepair.Read(ops[0]); var doc = new NdfSyntaxDocument(state.Body);
                var offset = graph.RequireObject(state.File, state.Name).CharacterOffset + doc.StartOffset(MissileDebugRepair.DebugSpan(doc));
                var expected = before[..offset] + "'" + state.After + "'" + before[(offset + doc.Length(MissileDebugRepair.DebugSpan(doc)))..];
                Assert(after == expected, "精确只改一个副本debug字符串，所有数值/GUID/引用和原文保持");
                Assert(!ndf.CandidateBytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "UTF8无BOM");
                var result = await service.CommitApplyAsync(preview, store); Assert(result.Succeeded, "独立于Unit/Weapon文件的修复提交");
                Assert(MissileDebugRepair.Scan(root).Rows.All(r => !r.CanRepair), "重复扫描幂等，不再生成修复");
                await service.CommitRestoreAsync(service.PrepareRestore(root, result.BackupId));
                Assert(originals.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "备份恢复逐字返回原debug与完整引用");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task MissileDebugRepairGuards()
    {
        var root = BrokenMissileFixture();
        try
        {
            var path = Path.Combine(root, MotionFile);
            File.AppendAllText(path, BrokenFlight("TestFlight_WLMT_2222222222", "80000000-0000-0000-0000-000000000003"));
            File.AppendAllText(path, BrokenFlight("AuthorFlight", "80000000-0000-0000-0000-000000000004"));
            File.AppendAllText(path, BrokenFlight("TestFlight_WLMT_3333333333", "80000000-0000-0000-0000-000000000005").Replace("AutoGyr = 1.5", "AutoGyr = 2.5"));
            var scan = MissileDebugRepair.Scan(root); Assert(scan.Rows.Count(r => r.CanRepair) == 2, "作者自建和结构不同对象不猜测修复");
            var ops = MissileDebugRepair.Prepare(root, scan.Rows.Where(r => r.CanRepair).Select(r => r.Name).ToArray());
            Assert(ops.Select(o => o.TargetValue).Distinct().Count() == 2, "多个副本独立分配debug名");
            using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync(ops, []);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, [ops[0]]);
            Assert(preview.Operations.Count == 2, "已保存修复组局部应用依赖闭合");
            var bytes = File.ReadAllBytes(path); File.SetAttributes(path, FileAttributes.ReadOnly);
            try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "只读提交失败"); }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
            Assert(File.ReadAllBytes(path).SequenceEqual(bytes) && store.Operations.Count == 2, "失败保留正式文件与草稿");
            File.WriteAllText(path, File.ReadAllText(path).Replace("MaxSpeedGRU = 2500", "MaxSpeedGRU = 2600"));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, ops), "修复基线外改拒绝");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task MissileDebugOldReader(string oldCore)
    {
        var root = BrokenMissileFixture(); var load = new AssemblyLoadContext("debug-repair-old-reader", true);
        try
        {
            using var store = new DraftStore(root); await store.LoadAsync();
            await store.ApplyBatchAsync(MissileDebugRepair.Prepare(root, [BrokenMissile]), []);
            var bytes = File.ReadAllBytes(store.DraftPath); var assembly = load.LoadFromAssemblyPath(Path.GetFullPath(oldCore));
            var type = assembly.GetType("WarnoLiteModdingTool.Core.Drafts.DraftStore")!;
            using var old = (IDisposable)Activator.CreateInstance(type, root)!;
            var task = (Task)type.GetMethod("LoadAsync")!.Invoke(old, [CancellationToken.None])!; await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert((bool)result.GetType().GetProperty("IsBlocked")!.GetValue(result)! && File.ReadAllBytes(store.DraftPath).SequenceEqual(bytes), "2.11.6拒读新修复草稿且不改原稿");
            Console.WriteLine("PASS schema10 old-reader rejection and unchanged repair draft");
        }
        finally { load.Unload(); DeleteTemporaryFixture(root); }
    }
}
