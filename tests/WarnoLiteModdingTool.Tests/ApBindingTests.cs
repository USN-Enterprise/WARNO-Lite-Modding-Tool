using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static string ApFixture(string nl = "\n", bool missing = false, bool noStairs = false)
    {
        var root = DamageFixture(nl);
        var path = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
        var body = File.ReadAllText(path).Replace("\r\n", "\n").Replace("\n", nl).Replace("Family=DamageFamily_ap Index=8", "Family=DamageFamily_he Index=3")
            .Replace("DamageTypeEvolutionOverRangeDescriptor = ~/TestStair", "DamageTypeEvolutionOverRangeDescriptor = nil")
            .Replace("PiercingWeapon = True", "PiercingWeapon = False");
        if (missing) body = body.Replace("    DamageTypeEvolutionOverRangeDescriptor = nil" + nl, "").Replace("    PiercingWeapon = False" + nl, "");
        File.WriteAllText(path, body, new UTF8Encoding(false));
        if (noStairs) File.WriteAllText(Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Stairs.ndf"), "// no rules" + nl, new UTF8Encoding(false));
        foreach (var file in Directory.GetFiles(Path.Combine(root, "GameData"), "*.ndf", SearchOption.AllDirectories))
            File.WriteAllText(file, File.ReadAllText(file).Replace("\r\n", "\n").Replace("\n", nl), new UTF8Encoding(false));
        return root;
    }
    private static DraftOperation ApFamily(WeaponWorkspaceData data, bool local = false)
    {
        var ammo = data.Ammo(A1911)!; var f = ammo.Field(ApBinding.FamilyKey)!;
        return new(DraftOperation.CreateId(DraftTargetKind.AmmoField, f.Location.RelativeSourceFile, ammo.Name, f.Key), null,
            DraftTargetKind.AmmoField, "ammo", f.Location.RelativeSourceFile, ammo.Name, ammo.Source.TypeName, f.Key,
            f.Location.FieldPath, "Choice", f.DisplayValue, f.RawValue, "DamageFamily_ap", "DamageFamily_ap", "AP conversion", null, false,
            DateTimeOffset.UtcNow, EditScope: local ? DraftEditScope.CurrentUnit : DraftEditScope.AllReferences, SelectedUnitNames: local ? [U1911] : []);
    }
    private static async Task ApBindingTransactions()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        foreach (var local in new[] { false, true })
        foreach (var missing in new[] { false, true })
        {
            var root = ApFixture(nl, missing, noStairs: missing);
            try
            {
                if (local && missing)
                {
                    var privatePath = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
                    File.WriteAllText(privatePath, File.ReadAllText(privatePath).Replace("export " + A1911 + " is", "private " + A1911 + " is"), new UTF8Encoding(false));
                }
                var (_, _, data) = await LoadP4Async(root); var damage = new DamageWorkspace(root);
                var originals = Directory.GetFiles(Path.Combine(root, "GameData"), "*.ndf", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
                var op = ApBinding.Prepare(data, [], ApFamily(data, local));
                Assert(op.ApBindings?.Count == 1 && op.ApBindings[0].Distance.CreateRule == missing, "默认阶梯复用或当前Mod内创建");
                using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(op);
                Assert(File.ReadAllText(store.DraftPath).Contains("\"schemaVersion\": 8"), "联动草稿schema8");
                using (var reopened = new DraftStore(root))
                {
                    Assert(!(await reopened.LoadAsync()).IsBlocked && reopened.Operations.Single().ApBindings?.Count == 1, "联动重开完整");
                }
                Assert(originals.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "保存家族及内嵌配套不改正式文件");
                var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, [op]);
                var result = await service.CommitApplyAsync(preview, store); Assert(result.Succeeded && store.Operations.Count == 0, "整组提交及清理");
                var (_, _, after) = await LoadP4Async(root);
                var actual = local ? after.Ammo(after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!.Mounts[0].AmmoName)! : after.Ammo(A1911)!;
                Assert(actual.Field(ApBinding.FamilyKey)?.RawValue == "DamageFamily_ap" && actual.Field(ApBinding.PiercingKey)?.RawValue == "True", "家族与穿甲标记落在同一Ammo");
                Assert(actual.Field("ammo.damage.index")?.RawValue == "3", "保留Index");
                var stair = new DamageWorkspace(root).ResolveStair(actual.Source.RelativeSourceFile, actual.Field(DamageDistance.ReferenceKey)!.RawValue);
                Assert(stair is not null && DamageWorkspace.Number(stair.Distance) == 175 && DamageWorkspace.Number(stair.AP) == 1, "最终有效175/1引用");
                if (local) Assert(after.Ammo(A1911)!.Field(ApBinding.FamilyKey)?.RawValue == "DamageFamily_he", "范围外Ammo逐字保留");
                foreach (var file in preview.Files.Where(f => f.Kind == FormalTextFileKind.Ndf))
                {
                    Assert(!file.CandidateBytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "NDF无BOM");
                    var text = Encoding.UTF8.GetString(file.CandidateBytes);
                    Assert(nl == "\n" ? !text.Contains('\r') : !text.Replace("\r\n", "").Contains('\n'), "换行保持");
                }
                await service.CommitRestoreAsync(service.PrepareRestore(root, result.BackupId));
                Assert(originals.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "恢复原对象、缺失字段和新建阶梯");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task ApBindingGuards()
    {
        var root = ApFixture();
        try
        {
            var (_, _, data) = await LoadP4Async(root); var op = ApBinding.Prepare(data, [], ApFamily(data));
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(op);
            await store.RemoveAsync(op.Id); Assert(store.Operations.Count == 0, "撤销自动配套不留独立草稿");
            var nonAp = ApBinding.Prepare(data, [op], op with { TargetRaw = "DamageFamily_he", TargetValue = "DamageFamily_he" });
            Assert(nonAp.ApBindings is null, "切出AP清理仅自动配套");
            var field = data.Ammo(A1911)!.Field(ApBinding.PiercingKey)!;
            var noPiercing = op with { Id = "piercing", FieldKey = field.Key, FieldPath = field.Location.FieldPath, BaselineRaw = field.RawValue,
                BaselineValue = field.DisplayValue, TargetValue = "否", TargetRaw = "False", ValueType = "Boolean", ApBindings = null };
            await TestAssert.ThrowsAsync<InvalidDataException>(() => Task.Run(() => ApBinding.Prepare(data, [noPiercing], ApFamily(data))), "相反显式草稿拒绝且不覆盖");
            var stair = data.DamageSource!().Stairs[1];
            var explicitRule = DamageDistance.Operation(data.DamageSource(), data.Ammo(A1911)!, stair, stair.Distance, stair.AP, DraftEditScope.AllReferences, [], true);
            var linked = ApBinding.Prepare(data, [explicitRule], ApFamily(data));
            var expanded = ApBinding.Expand(data, data.DamageSource(), [linked, explicitRule]);
            Assert(expanded.Count(o => o.TargetKind == DraftTargetKind.DamageDistance) == 1, "用户独立距离规则优先且不重复");
            await store.ApplyBatchAsync([linked, explicitRule], []); await store.RemoveAsync(linked.Id);
            Assert(store.Operations.Single().Id == explicitRule.Id, "撤销家族保留用户距离草稿");
            var path = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
            File.WriteAllText(path, File.ReadAllText(path).Replace("DamageTypeEvolutionOverRangeDescriptor = nil", "DamageTypeEvolutionOverRangeDescriptor = ~/MissingRule"));
            var (_, _, changed) = await LoadP4Async(root);
            await TestAssert.ThrowsAsync<InvalidDataException>(() => Task.Run(() => ApBinding.Prepare(changed, [], ApFamily(changed))), "损坏引用不覆盖");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task ApBindingBatches()
    {
        foreach (var weaponBatch in new[] { false, true })
        {
            var root = ApFixture(missing: true, noStairs: true);
            try
            {
                var (_, units, data) = await LoadP4Async(root);
                IReadOnlyList<DraftOperation> ops;
                if (weaponBatch)
                {
                    var preview = WeaponBatch.Preview(data, [], new([new(U1911, W1911, 0)], ApBinding.FamilyKey,
                        UnitBatchOperation.Set, "DamageFamily_ap"));
                    Assert(preview.CanSave, string.Join(";", preview.Errors)); ops = preview.Upserts;
                }
                else
                {
                    var preview = AmmoBatchPlanner.Preview(units, data, [], new([AmmoBatchPlanner.Identity(data.Ammo(A1911)!)], ApBinding.FamilyKey,
                        UnitBatchOperation.Set, "DamageFamily_ap"));
                    Assert(preview.CanSave, string.Join(";", preview.Errors)); ops = preview.Upserts;
                }
                Assert(ops.Any(ApBinding.Has), "批量与单项共用联动");
                using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync(ops, []);
                var service = new UnitTransactionService(); var apply = await service.PrepareApplyAsync(root, ops);
                Assert((await service.CommitApplyAsync(apply, store)).Succeeded, "批量完整组合提交");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task ApBindingRecovery()
    {
        var root = ApFixture(missing: true, noStairs: true);
        try
        {
            var (_, _, data) = await LoadP4Async(root); var op = ApBinding.Prepare(data, [], ApFamily(data, true));
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(op);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, [op]);
            var files = preview.Files.Where(f => f.Existed).ToDictionary(f => f.FullPath, f => f.OriginalBytes);
            var path = preview.Files.First(f => f.Kind == FormalTextFileKind.Ndf && f.Existed).FullPath;
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "只读失败须报告失败"); }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
            Assert(files.All(f => File.ReadAllBytes(f.Key).SequenceEqual(f.Value)) && store.Operations.Count == 1, "失败不留半套AP字段或阶梯，保留草稿");
            var ammo = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
            File.WriteAllText(ammo, File.ReadAllText(ammo).Replace("PhysicalDamages = 10.0", "PhysicalDamages = 11.0"));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, [op]), "外部变化拒绝过期联动");
        }
        finally { DeleteTemporaryFixture(root); }
        var preserved = ApFixture();
        try
        {
            var path = Path.Combine(preserved, "GameData/Generated/Gameplay/Gfx/Stairs.ndf");
            File.WriteAllText(path, File.ReadAllText(path).Replace("175.0", "700.0"));
            var ammoPath = Path.Combine(preserved, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
            File.WriteAllText(ammoPath, File.ReadAllText(ammoPath).Replace("DamageTypeEvolutionOverRangeDescriptor = nil", "DamageTypeEvolutionOverRangeDescriptor = ~/TestStair"));
            var (_, _, data) = await LoadP4Async(preserved);
            var op = ApBinding.Prepare(data, [], ApFamily(data));
            Assert(op.ApBindings![0].Distance.Distance == "700.0" && !op.ApBindings[0].Distance.CreateRule, "有效自定义规则不归一化");
            var legacy = ApFamily(data) with { ApBindings = null };
            using var store = new DraftStore(preserved); await store.LoadAsync(); await store.UpsertAsync(legacy);
            using var reopen = new DraftStore(preserved); await reopen.LoadAsync();
            Assert(reopen.Operations.Single().ApBindings is null, "旧草稿加载不补写联动");
        }
        finally { DeleteTemporaryFixture(preserved); }
    }
    private static async Task ApBindingOldReader(string oldCore)
    {
        var root = ApFixture(missing: true, noStairs: true);
        var load = new System.Runtime.Loader.AssemblyLoadContext("ap-old-reader", true);
        try
        {
            var (_, _, data) = await LoadP4Async(root); using var store = new DraftStore(root); await store.LoadAsync();
            await store.UpsertAsync(ApFamily(data)); var before = File.ReadAllBytes(store.DraftPath);
            await store.UpsertAsync(ApBinding.Prepare(data, store.Operations, ApFamily(data)));
            Assert(Directory.GetFiles(store.EditorDirectory, "draft-before-structure-*.json").Any(p => File.ReadAllBytes(p).SequenceEqual(before)), "升级前保留原草稿");
            var bytes = File.ReadAllBytes(store.DraftPath);
            var assembly = load.LoadFromAssemblyPath(Path.GetFullPath(oldCore)); var type = assembly.GetType(typeof(DraftStore).FullName!)!;
            using var old = (IDisposable)Activator.CreateInstance(type, root)!;
            var task = (Task)type.GetMethod("LoadAsync")!.Invoke(old, [CancellationToken.None])!; await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert((bool)result.GetType().GetProperty("IsBlocked")!.GetValue(result)! && File.ReadAllBytes(store.DraftPath).SequenceEqual(bytes), "旧2.11.4拒读schema8且保留字节");
            Console.WriteLine("PASS previous release rejects AP schema8; upgrade copy and original draft preserved");
        }
        finally { load.Unload(); DeleteTemporaryFixture(root); }
    }
}
