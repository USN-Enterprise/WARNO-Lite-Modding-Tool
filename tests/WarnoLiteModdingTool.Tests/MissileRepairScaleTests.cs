using System.Diagnostics;
using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private sealed class RepairProgress(Action<MissileRepairProgress> report) : IProgress<MissileRepairProgress>
    { public void Report(MissileRepairProgress value) => report(value); }
    private static async Task MissileRepairScale()
    {
        var root = BrokenMissileFixture();
        try
        {
            var path = Path.Combine(root, MotionFile);
            for (var i = 2; i <= 13; i++) File.AppendAllText(path, BrokenFlight("TestFlight_WLMT_" + i.ToString("x10"), "80000000-0000-0000-0000-" + (i + 1).ToString("x12")));
            var folder = Path.Combine(root, "Custom/Filler"); Directory.CreateDirectory(folder);
            for (var i = 0; i < 1500; i++) File.WriteAllText(Path.Combine(folder, $"F{i}.ndf"), $"private F{i} is TFixture(Value = {i})\n", new UTF8Encoding(false));
            var before = File.ReadAllBytes(path); var scope = new ProjectReadScope();
            using (scope.Enter())
            {
                var analysis = MissileDebugRepair.Analyze(root); var scan = analysis.Scan();
                Assert(scan.Rows.Count(r => r.CanRepair) == 13, "13个副本含无引用旧声明均识别");
                var count = analysis.ParsedFiles; var reads = scope.ReadFiles;
                var ops = analysis.Prepare(scan.Rows.Where(r => r.CanRepair).Select(r => r.Name).ToArray());
                foreach (var op in ops) { analysis.Validate(op); _ = analysis.Replacement(op); }
                Assert(ReferenceEquals(analysis, MissileDebugRepair.Analyze(root)) && analysis.ParsedFiles == count && scope.ReadFiles == reads && analysis.ReferencePasses == 1,
                    "同轮13项校验复用一次分析/引用遍历，不按修复条数反复全目录解析");
                using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync(ops, []);
                var preview = await new UnitApplyPlanner().PrepareAsync(root, ops);
                Assert(preview.Operations.Count == 13 && preview.FormalFileCount == 1 && preview.FullNdfReview, "独立批量修复只改一份正式文件并保留全目录复核");
                var ndf = preview.Files.Single(f => f.Kind == FormalTextFileKind.Ndf);
                analysis.ValidateFinal([ndf]);
                Assert(analysis.ParsedFiles == count + 1 && analysis.ReferencePasses == 1, "最终身份只解析变更文件，不再构建两份全目录图");
                MissileRepairPlanner.VerifyInputs(preview); await MissileRepairPlanner.VerifyDraftsAsync(preview);
                var extra = Path.Combine(folder, "new.ndf"); File.WriteAllText(extra, "NewReference is TFixture()\n");
                await TestAssert.ThrowsAsync<TransactionValidationException>(() => new UnitTransactionService().CommitApplyAsync(preview, store), "预览后新增文件必须拒绝");
                Assert(File.ReadAllBytes(path).SequenceEqual(before) && store.Operations.Count == 13, "拒绝后正式文件和草稿保持");
                File.Delete(extra);
                // A same-length edit with restored mtime must also invalidate the preview.
                var stamp = File.GetLastWriteTimeUtc(path); var text = File.ReadAllText(path);
                File.WriteAllText(path, text.Replace("AutoGyr = 1.5", "AutoGyr = 2.5")); File.SetLastWriteTimeUtc(path, stamp);
                try { MissileRepairPlanner.VerifyInputs(preview); throw new Exception("内容变化未检测"); }
                catch (TransactionValidationException) { }
            }
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task MissileRepairMixedDrafts()
    {
        var root = BrokenMissileFixture();
        try
        {
            var (_, _, data) = await LoadP4Async(root);
            var speed = MissileMotion.Prepare(data, [], MotionOp(data));
            var acceleration = MissileMotion.Prepare(data, [speed], MotionOp(data, "ammo.speed.acceleration", "150"));
            using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync([speed, acceleration], []);
            var preview = await new UnitApplyPlanner().PrepareRepairsAsync(root, [BrokenMissile]);
            Assert(preview.Operations.Count == 3 && preview.Operations.Count(MissileDebugRepair.IsRepair) == 1, "修复与相关两项运动草稿按原始基线组合");
            Assert(preview.Operations.Single(o => o.Id == speed.Id).MissileMotion!.SequenceEqual(speed.MissileMotion!) && preview.Operations.Single(o => o.Id == acceleration.Id).TargetRaw == acceleration.TargetRaw, "既有目标不重算或丢失");
            var repair = preview.Operations.Single(MissileDebugRepair.IsRepair); await store.UpsertAsync(repair);
            var service = new UnitTransactionService(); preview = await service.PrepareApplyAsync(root, [repair]);
            var result = await service.CommitApplyAsync(preview, store); Assert(result.Succeeded, "混合修复应用");
            Assert(!MissileDebugRepair.Scan(root).Rows.Any(r => r.CanRepair), "混合修复清除旧副本debug冲突");
            var (_, _, after) = await LoadP4Async(root); var values = MotionValues(after, after.Ammo(A1911)!);
            Assert(values.Speed == "2500" && values.Acceleration == "150", "共享运动目标保留，其他引用通过隔离保护");
            await service.CommitRestoreAsync(service.PrepareRestore(root, result.BackupId));
            Assert(MissileDebugRepair.Scan(root).Rows.Any(r => r.CanRepair), "恢复返回旧副本基线");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task MissileRepairCancellation()
    {
        var root = BrokenMissileFixture();
        try
        {
            var before = File.ReadAllBytes(Path.Combine(root, MotionFile)); using var work = new CancellationTokenSource();
            var watch = Stopwatch.StartNew();
            var progress = new RepairProgress(p => { if (p.Completed >= 2) work.Cancel(); });
            await TestAssert.ThrowsAsync<OperationCanceledException>(() => Task.Run(() => MissileDebugRepair.Analyze(root, work.Token, progress)), "扫描取消贯通实际循环");
            Assert(watch.Elapsed < TimeSpan.FromSeconds(2) && File.ReadAllBytes(Path.Combine(root, MotionFile)).SequenceEqual(before) && !File.Exists(Path.Combine(root, ".warno-editor/draft-v1.json")), "取消及时结束且没有草稿或正式写入");
            using var previewWork = new CancellationTokenSource();
            var previewProgress = new RepairProgress(p => { if (p.Stage.StartsWith("组合检查", StringComparison.Ordinal)) previewWork.Cancel(); });
            await TestAssert.ThrowsAsync<OperationCanceledException>(() => new UnitApplyPlanner().PrepareRepairsAsync(root, [BrokenMissile], previewWork.Token, previewProgress), "最终组合阶段可取消");
            Assert(File.ReadAllBytes(Path.Combine(root, MotionFile)).SequenceEqual(before), "取消组合预览无正式写入");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
