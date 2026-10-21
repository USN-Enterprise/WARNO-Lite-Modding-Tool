using System.IO;
using System.Text;
using System.Runtime.Loader;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Changes;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private const string MotionFile = "GameData/Generated/Gameplay/Gfx/FlightEntities.ndf";
    private static string MotionFixture(string nl = "\n", bool shared = true, bool missingAcceleration = false)
    {
        var root = StructureFixture(nl); var ammoPath = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
        var text = File.ReadAllText(ammoPath).Replace("\r\n", "\n");
        text = text.Replace("    ProjectileSpeedGRU = 2000", "    // speed comment retained\n    ProjectileType = EProjectileType/GuidedMissile\n    MissileDescriptor = ~/TestFlight\n" +
            (missingAcceleration ? "" : "    MaxAccelerationGRU = 100\n") + "    ProjectileSpeedGRU = 2000");
        if (shared) text = text.Replace("    PhysicalDamages = 3.0", "    ProjectileType = EProjectileType/GuidedMissile\n    MissileDescriptor = ~/TestFlight\n    ProjectileSpeedGRU = 500\n    MaxAccelerationGRU = 50\n    PhysicalDamages = 3.0");
        File.WriteAllText(ammoPath, text.Replace("\n", nl), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, MotionFile), FlightBody().Replace("\n", nl), new UTF8Encoding(false));
        foreach (var file in Directory.GetFiles(Path.Combine(root, "GameData"), "*.ndf", SearchOption.AllDirectories))
            File.WriteAllText(file, File.ReadAllText(file).Replace("\r\n", "\n").Replace("\n", nl), new UTF8Encoding(false));
        return root;
    }
    private static string FlightBody() => """
export TestFlight is TEntityDescriptor
(
    DescriptorId = GUID:{80000000-0000-0000-0000-000000000001}
    ClassNameForDebug = 'keep flight visual'
    ModulesDescriptors = [
        TGuidedMissileModuleDescriptor(),
        TGuidedMissileMovementModuleDescriptor
        (
            DefaultConfig = TGuidedMissileMovementConfigDescriptor
            (
                MaxSpeedGRU = 1800 // keep default comment
                MaxAccelerationGRU = 90
                AutoGyr = 1.5
            )
            UncontrollableConfig = TGuidedMissileMovementConfigDescriptor
            (
                MaxSpeedGRU = 1600
                MaxAccelerationGRU = 80
                AutoGyr = 1.2
            )
            CruiseAltitudeGRU = 7.0
        ),
    ]
)
""" + "\n";
    private static DraftOperation MotionOp(WeaponWorkspaceData data, string key = "ammo.speed.projectile", string value = "2500", bool local = false) =>
        ProOp(data.Ammo(A1911)!.Field(key)!, value, local);
    private static (string Speed, string Lost, string Acceleration, string LostAcceleration) MotionValues(WeaponWorkspaceData data, AmmoRecord ammo)
    {
        var graph = MissileMotion.Graph(data); var entity = AmmoProfessionalValidation.MissileTarget(graph, ammo.Source.RelativeSourceFile, ammo.Field(MissileMotion.ReferenceKey)!.RawValue)!;
        var doc = new NdfSyntaxDocument(graph.Body(entity));
        string R(string config, string field) => doc.Raw(MissileMotion.Field(doc, config, field));
        return (R("DefaultConfig", "MaxSpeedGRU"), R("UncontrollableConfig", "MaxSpeedGRU"), R("DefaultConfig", "MaxAccelerationGRU"), R("UncontrollableConfig", "MaxAccelerationGRU"));
    }
    private static async Task MissileMotionTransactions()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        foreach (var local in new[] { false, true })
        foreach (var shared in new[] { false, true })
        {
            var root = MotionFixture(nl, shared);
            try
            {
                var (_, units, data) = await LoadP4Async(root);
                Assert(MissileMotion.Guided(data.Ammo(A1911)!), "真实弹道识别");
                var before = Directory.GetFiles(Path.Combine(root, "GameData"), "*.ndf", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
                var speed = MissileMotion.Prepare(data, [], MotionOp(data, local: local));
                var accel = MissileMotion.Prepare(data, [speed], MotionOp(data, "ammo.speed.acceleration", "150", local));
                Assert(speed.MissileMotion?.Count > 0 && accel.MissileMotion?.Count > 0, "两参数独立配套");
                using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync([speed, accel], []);
                Assert(File.ReadAllText(store.DraftPath).Contains("\"schemaVersion\": 9"), "schema9");
                Assert(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "保存不改正式文件");
                using (var reopen = new DraftStore(root)) { Assert(!(await reopen.LoadAsync()).IsBlocked && reopen.Operations.All(MissileMotion.Has), "重开完整联动"); }
                var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, [speed]);
                Assert(preview.Operations.Count == 2, "局部应用展开完整相关参数");
                var entityText = Encoding.UTF8.GetString(preview.Files.Single(f => f.RelativePath == MotionFile).CandidateBytes);
                Assert(entityText.Contains("AutoGyr = 1.5") && entityText.Contains("keep default comment") && entityText.Contains("CruiseAltitudeGRU = 7.0"), "表现、注释与无关运动保留");
                var entityCount = new NdfTopLevelScanner().Scan(entityText, MotionFile, "ammo", root).Objects.Count;
                Assert(entityCount == (local || shared ? 2 : 1), $"只在必要时隔离实体且速度加速度复用同一副本: local={local}, shared={shared}, count={entityCount}\n{entityText}");
                var commit = await service.CommitApplyAsync(preview, store); Assert(commit.Succeeded, "运动整组提交");
                var (_, _, after) = await LoadP4Async(root);
                var actual = local ? after.Ammo(after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!.Mounts[0].AmmoName)! : after.Ammo(A1911)!;
                Assert(actual.Field("ammo.speed.projectile")!.RawValue == "2500" && actual.Field("ammo.speed.acceleration")!.RawValue == "150", "Ammo两值");
                Assert(MotionValues(after, actual) == ("2500", "2500", "150", "150"), "实体两配置同时生效");
                if (local) Assert(after.Ammo(A1911)!.Field("ammo.speed.projectile")!.RawValue == "2000", "范围外Ammo不变");
                if (shared) Assert(MotionValues(after, after.Ammo("Ammo_P4_Alt")!) == ("1800", "1600", "90", "80"), "共享实体其他Ammo不受影响");
                foreach (var f in preview.Files.Where(f => f.Kind == FormalTextFileKind.Ndf))
                {
                    Assert(!f.CandidateBytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "无BOM");
                    var s = Encoding.UTF8.GetString(f.CandidateBytes);
                    Assert(nl == "\n" ? !s.Contains('\r') : !s.Replace("\r\n", "").Contains('\n'), "原换行保持");
                }
                await service.CommitRestoreAsync(service.PrepareRestore(root, commit.BackupId));
                Assert(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "全部文件恢复独立基线和引用");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task MissileMotionBatches()
    {
        foreach (var weapon in new[] { false, true })
        {
            var root = MotionFixture();
            try
            {
                var (_, units, data) = await LoadP4Async(root); IReadOnlyList<DraftOperation> ops;
                if (weapon)
                {
                    var p = WeaponBatch.Preview(data, [], new([new(U1911, W1911, 0)], "ammo.speed.projectile", UnitBatchOperation.IncreasePercent, "50"));
                    Assert(p.CanSave, string.Join(";", p.Errors)); ops = p.Upserts;
                }
                else
                {
                    var p = AmmoBatchPlanner.Preview(units, data, [], new([AmmoBatchPlanner.Identity(data.Ammo(A1911)!)], "ammo.speed.projectile", UnitBatchOperation.IncreasePercent, "50"));
                    Assert(p.CanSave, string.Join(";", p.Errors)); ops = p.Upserts;
                }
                var b = ops.SelectMany(o => o.MissileMotion ?? []).Single();
                Assert(b.DefaultAfter == "2700" && b.UncontrollableAfter == "2400", "百分比用各自基值");
                using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync(ops, []);
                var service = new UnitTransactionService(); var result = await service.CommitApplyAsync(await service.PrepareApplyAsync(root, ops), store);
                Assert(result.Succeeded, "两入口批量运动提交");
                var (_, _, after) = await LoadP4Async(root);
                var ammo = weapon ? after.Ammo(after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!.Mounts[0].AmmoName)! : after.Ammo(A1911)!;
                Assert(ammo.Field("ammo.speed.projectile")!.RawValue == "3000" && MotionValues(after, ammo) == ("2700", "2400", "90", "80"), "三处独立百分比、加速度不变");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task MissileMotionGuards()
    {
        foreach (var replacement in new[] { ("MaxSpeedGRU = 1800", "MaxSpeedGRU = Foo"), ("MaxSpeedGRU = 1800", "MaxSpeedGRU = 1800 MaxSpeedGRU = 1700"),
            ("DefaultConfig = TGuidedMissileMovementConfigDescriptor", "DefaultConfig = ~/SharedConfig //"), ("export TestFlight is", "export TestFlightBad is") })
        {
            var root = MotionFixture();
            try
            {
                var file = Path.Combine(root, MotionFile); File.WriteAllText(file, File.ReadAllText(file).Replace(replacement.Item1, replacement.Item2));
                var (_, _, data) = await LoadP4Async(root);
                await TestAssert.ThrowsAsync<Exception>(() => Task.Run(() => MissileMotion.Prepare(data, [], MotionOp(data))), "未知/多义/断引用拒绝");
            }
            finally { DeleteTemporaryFixture(root); }
        }
        var valid = MotionFixture(missingAcceleration: true);
        try
        {
            var (_, units, data) = await LoadP4Async(valid); var op = MissileMotion.Prepare(data, [], MotionOp(data));
            var acceleration = MissileMotion.Prepare(data, [op], MotionOp(data, "ammo.speed.acceleration", "150"));
            Assert(acceleration.InsertAmmoField && MissileMotion.Has(acceleration), "Ammo缺加速度沿既有受控补建");
            using var store = new DraftStore(valid); await store.LoadAsync(); await store.UpsertAsync(MotionOp(data));
            using (var reopen = new DraftStore(valid)) { await reopen.LoadAsync(); Assert(!MissileMotion.Has(reopen.Operations.Single()), "旧稿不暗加联动"); }
            await store.UpsertAsync(op); await store.RemoveAsync(op.Id); Assert(store.Operations.Count == 0, "撤销无实体残留草稿");
            var service = new UnitTransactionService(); await store.ApplyBatchAsync([op, acceleration], []);
            var preview = await service.PrepareApplyAsync(valid, [op]); var before = preview.Files.Where(f => f.Existed).ToDictionary(f => f.FullPath, f => f.OriginalBytes);
            var blocked = Path.Combine(valid, MotionFile); File.SetAttributes(blocked, FileAttributes.ReadOnly);
            try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "只读失败"); }
            finally { File.SetAttributes(blocked, FileAttributes.Normal); }
            Assert(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)) && store.Operations.Count == 2, "失败整组恢复且留草稿");
            File.WriteAllText(blocked, File.ReadAllText(blocked).Replace("AutoGyr = 1.5", "AutoGyr = 1.6"));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(valid, [op]), "实体外改拒绝");
        }
        finally { DeleteTemporaryFixture(valid); }
        var custom = MotionFixture(shared: false);
        try
        {
            Directory.CreateDirectory(Path.Combine(custom, "Custom"));
            File.WriteAllText(Path.Combine(custom, "Custom/Other.ndf"), "Other is TCustomDescriptor(Value=7)\n");
            var (_, _, data) = await LoadP4Async(custom); var op = MissileMotion.Prepare(data, [], MotionOp(data));
            using var store = new DraftStore(custom); await store.LoadAsync(); await store.UpsertAsync(op);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(custom, [op]);
            var added = Path.Combine(custom, "Custom/Incoming.ndf"); File.WriteAllText(added, "Incoming is TCustomDescriptor(Ref=$/Custom/TestFlight)\n");
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "预览后新增自建目录引用文件必须重算影响");
            File.Delete(added);
            Assert((await service.CommitApplyAsync(await service.PrepareApplyAsync(custom, [op]), store)).Succeeded, "自建目录完整依赖集合可正常提交");
        }
        finally { DeleteTemporaryFixture(custom); }
    }
    private static Task MissileMotionChangeMerge()
    {
        var before = FlightBody(); var after = before.Replace("MaxSpeedGRU = 1800", "MaxSpeedGRU = 2700").Replace("MaxSpeedGRU = 1600", "MaxSpeedGRU = 2000");
        var target = before.Replace("MaxSpeedGRU = 1800", "MaxSpeedGRU = 3600").Replace("MaxSpeedGRU = 1600", "MaxSpeedGRU = 4000");
        var b = Encoding.UTF8.GetBytes(before); var m = Encoding.UTF8.GetBytes(after); var n = Encoding.UTF8.GetBytes(target);
        var f = new ChangeFile(MotionFile, ContentStamp.Of(b), ContentStamp.Of(m), "b", "m");
        var result = ChangeMerge.Merge(f, b, m, n, NumericPolicy.Ratio);
        Assert(result.Error is null, result.Error ?? "numeric merge");
        var doc = new NdfSyntaxDocument(Encoding.UTF8.GetString(result.After!));
        Assert(doc.Raw(MissileMotion.Field(doc, "DefaultConfig", "MaxSpeedGRU")) == "5400" && doc.Raw(MissileMotion.Field(doc, "UncontrollableConfig", "MaxSpeedGRU")) == "5000", "两配置独立比例还原不串写");
        Assert(result.Details.Count(d => d.Field.Contains("Config.MaxSpeedGRU")) == 2, "完整路径明细");
        var issues = ChangeMerge.SourceKnownIssues(MotionFile, before.Replace("MaxSpeedGRU = 1600", "MaxSpeedGRU = -1"));
        Assert(issues.Keys.Any(k => k.Contains("UncontrollableConfig.MaxSpeedGRU")), "源码负速度按路径拦截");
        return Task.CompletedTask;
    }
    private static async Task MissileMotionCombinations()
    {
        var root = MotionFixture();
        try
        {
            var (_, units, data) = await LoadP4Async(root);
            var batch = AmmoBatchPlanner.Preview(units, data, [], new(data.Ammunition.Where(a => a.Name is A1911 or "Ammo_P4_Alt").Select(AmmoBatchPlanner.Identity).ToArray(),
                "ammo.speed.projectile", UnitBatchOperation.Set, "2500"));
            Assert(batch.CanSave, string.Join(";", batch.Errors));
            using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync(batch.Upserts, []);
            var service = new UnitTransactionService(); var apply = await service.PrepareApplyAsync(root, batch.Upserts);
            var entityText = Encoding.UTF8.GetString(apply.Files.Single(f => f.RelativePath == MotionFile).CandidateBytes);
            Assert(new NdfTopLevelScanner().Scan(entityText, MotionFile, "ammo", root).Objects.Count == 2, "相同最终实体结果只克隆一次");
            Assert((await service.CommitApplyAsync(apply, store)).Succeeded, "两个Ammo共同修改提交");
            var (_, _, after) = await LoadP4Async(root);
            Assert(after.Ammo(A1911)!.Field(MissileMotion.ReferenceKey)!.RawValue == after.Ammo("Ammo_P4_Alt")!.Field(MissileMotion.ReferenceKey)!.RawValue, "同结果复用实体");
            var different = MissileMotion.Prepare(after, [], ProOp(after.Ammo("Ammo_P4_Alt")!.Field("ammo.speed.projectile")!, "3000"));
            await store.UpsertAsync(different);
            Assert((await service.CommitApplyAsync(await service.PrepareApplyAsync(root, [different]), store)).Succeeded, "后续不同结果再次隔离");
            var (_, _, final) = await LoadP4Async(root);
            Assert(MotionValues(final, final.Ammo(A1911)!).Speed == "2500" && MotionValues(final, final.Ammo("Ammo_P4_Alt")!).Speed == "3000", "同实体不同目标不按最后一项覆盖");
        }
        finally { DeleteTemporaryFixture(root); }

        root = MotionFixture();
        try
        {
            var file = Path.Combine(root, MotionFile);
            File.AppendAllText(file, FlightBody().Replace("TestFlight", "Flight2").Replace("keep flight visual", "flight2 debug").Replace("80000000-0000-0000-0000-000000000001", "80000000-0000-0000-0000-000000000002"));
            var (_, _, data) = await LoadP4Async(root);
            var reference = ProOp(data.Ammo(A1911)!.Field(MissileMotion.ReferenceKey)!, "~/Flight2");
            var oldSpeed = MissileMotion.Prepare(data, [], MotionOp(data));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => Task.Run(() => MissileMotion.ValidateCombination(data, [reference, oldSpeed])), "后改实体引用必须重新编辑运动，旧计划不误写");
            var speed = MissileMotion.Prepare(data, [reference], MotionOp(data));
            Assert(speed.MissileMotion!.All(b => b.Entity == "Flight2"), "最终引用为运动目标");
            using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync([reference, speed], []);
            var service = new UnitTransactionService(); Assert((await service.CommitApplyAsync(await service.PrepareApplyAsync(root, [speed]), store)).Succeeded, "引用切换与运动整组提交");
            var (_, _, after) = await LoadP4Async(root);
            Assert(MotionValues(after, after.Ammo(A1911)!).Speed == "2500" && MotionValues(after, after.Ammo("Ammo_P4_Alt")!).Speed == "1800", "新实体修改、旧实体使用者保持");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task MissileMotionOldReader(string oldCore)
    {
        var root = MotionFixture(); var load = new AssemblyLoadContext("motion-old-reader", true);
        try
        {
            var (_, _, data) = await LoadP4Async(root); using var store = new DraftStore(root); await store.LoadAsync();
            await store.UpsertAsync(MotionOp(data)); var before = File.ReadAllBytes(store.DraftPath);
            await store.UpsertAsync(MissileMotion.Prepare(data, store.Operations, MotionOp(data)));
            Assert(Directory.GetFiles(store.EditorDirectory, "draft-before-structure-*.json").Any(p => File.ReadAllBytes(p).SequenceEqual(before)), "升级前草稿保留");
            var bytes = File.ReadAllBytes(store.DraftPath); var assembly = load.LoadFromAssemblyPath(Path.GetFullPath(oldCore));
            var type = assembly.GetType("WarnoLiteModdingTool.Core.Drafts.DraftStore")!;
            using var old = (IDisposable)Activator.CreateInstance(type, root)!;
            var task = (Task)type.GetMethod("LoadAsync")!.Invoke(old, [CancellationToken.None])!; await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert((bool)result.GetType().GetProperty("IsBlocked")!.GetValue(result)! && File.ReadAllBytes(store.DraftPath).SequenceEqual(bytes), "2.11.5拒读schema9且不改稿");
            Console.WriteLine("PASS schema9 old-reader rejection and preserved draft");
        }
        finally { load.Unload(); DeleteTemporaryFixture(root); }
    }
}
