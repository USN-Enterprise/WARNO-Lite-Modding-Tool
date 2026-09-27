using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static string RangeFixture(string newline = "\n")
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        var path = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert(text.Split("MinimumRangeAirplaneGRU = 0").Length == 2, "合成字段唯一");
        File.WriteAllText(path, text.Replace("MinimumRangeAirplaneGRU = 0", "MinimumRangeAirplaneGRU = 35").Replace("\n", newline), new UTF8Encoding(false));
        return root;
    }

    private static async Task AmmoRangeBatchCompatibility()
    {
        var language = WarnoLiteModdingTool.App.Localisation.UiText.Current.Language;
        try
        {
            WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("en");
            Assert(WarnoLiteModdingTool.App.Localisation.UiText.T("射程范围缺失或数值无效：Ammo / ammo.range.air") == "Range endpoints are missing or invalid: Ammo / ammo.range.air", "新射程诊断支持英文");
        }
        finally { WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(language); }
        var root = RangeFixture();
        try
        {
            var (_, units, data) = await LoadP4Async(root);
            var ammo = data.Ammo(A1911)!;
            var targets = new[] { AmmoBatchPlanner.Identity(ammo) };
            var p = P1912(units, data, [], "ammo.damage.suppress", "2.15", UnitBatchOperation.Multiply, targets: targets);
            Assert(p.CanSave && p.Rows.Single().Target == "107.5" && p.Upserts.Single().FieldKey == "ammo.damage.suppress", "35/0压制批改仅产生压制草稿");
            var noop = P1912(units, data, [], "ammo.damage.suppress", "1", UnitBatchOperation.Multiply, targets: targets);
            Assert(noop.Errors.Count == 0 && !noop.CanSave && noop.Upserts.Count == 0 && noop.Rows.Single().Status == "不变", "纯不变无射程误报或草稿");
            Assert(P1912(units, data, [], "ammo.range.ground.max", "2100", targets: targets).CanSave, "其他域不被35/0拦截");
            var mixed = P1912(units, data, [], "ammo.damage.physical", "10");
            Assert(mixed.CanSave && mixed.Upserts.Count == 1 && mixed.Upserts[0].ObjectName != A1911, "不变35/0对象不阻塞另一对象");
            Assert(P1911(data, [], "ammo.damage.suppress", "107.5").CanSave, "历史局部武器批改兼容35/0");
            Assert(P1911(data, [], "ammo.damage.suppress", "107.5", global: true).CanSave, "历史共享武器批改兼容35/0");

            foreach (var domain in new[] { "ground", "heli", "air", "projectile" })
            {
                var low = "ammo.range." + domain + ".min";
                var high = "ammo.range." + domain + ".max";
                var record = new AmmoRecord(ammo.Source, ammo.Fields.Select(f => f.Key == low ? f with { DisplayValue = "35", RawValue = "35" }
                    : f.Key == high ? f with { DisplayValue = "0", RawValue = "0" } : f).ToArray());
                var changed = data with { Ammunition = data.Ammunition.Select(a => a.Name == A1911 ? record : a).ToArray() };
                Assert(P1912(units, changed, [], low, "40", targets: targets).CanSave, "零最大射程保留正最小值：" + domain);
                Assert(P1912(units, changed, [], high, "100", targets: targets).CanSave, "启用有效正射程：" + domain);
                Assert(!P1912(units, changed, [], high, "20", targets: targets).CanSave, "拒绝新建正最大值倒置：" + domain);
                var positive = new AmmoRecord(record.Source, record.Fields.Select(f => f.Key == high ? f with { DisplayValue = "20", RawValue = "20" } : f).ToArray());
                var oldInvalid = changed with { Ammunition = changed.Ammunition.Select(a => a.Name == A1911 ? positive : a).ToArray() };
                Assert(P1912(units, oldInvalid, [], "ammo.damage.suppress", "60", targets: targets).CanSave, "无关编辑保留既有异常：" + domain);
                Assert(P1912(units, oldInvalid, [], high, "0", targets: targets).CanSave, "允许关闭目标域：" + domain);
                Assert(!P1912(units, oldInvalid, [], low, "36", targets: targets).CanSave, "修改该域仍须修正倒置：" + domain);
            }
            var missing = new AmmoRecord(ammo.Source, ammo.Fields.Where(f => f.Key != "ammo.range.air.max").ToArray());
            var incomplete = data with { Ammunition = data.Ammunition.Select(a => a.Name == A1911 ? missing : a).ToArray() };
            Assert(P1912(units, incomplete, [], "ammo.damage.suppress", "60", targets: targets).CanSave, "缺少无关端点不阻塞压制");
            Assert(!P1912(units, incomplete, [], "ammo.range.air.min", "40", targets: targets).CanSave, "修改射程不能猜缺失端点");
            foreach (var invalid in new[] { "-1", "NaN", "Infinity" })
                Assert(!P1912(units, data, [], "ammo.range.air.max", invalid, targets: targets).CanSave, "零射程例外不放开非法数值");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static async Task AmmoRangeApplyPreservation()
    {
        foreach (var newline in new[] { "\n", "\r\n" })
        foreach (var mode in new[] { "single-shared", "ammo-batch", "single-local", "weapon-batch" })
        {
            var root = RangeFixture(newline);
            try
            {
                var (_, units, data) = await LoadP4Async(root);
                var source = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
                var original = File.ReadAllBytes(source);
                var field = data.Ammo(A1911)!.Field("ammo.damage.suppress")!;
                var ops = mode switch
                {
                    "ammo-batch" => P1912(units, data, [], field.Key, "2.15", UnitBatchOperation.Multiply, targets: [AmmoBatchPlanner.Identity(data.Ammo(A1911)!)]).Upserts,
                    "weapon-batch" => P1911(data, [], field.Key, "107.5").Upserts,
                    _ => new[] { CreateWeaponDraft(field, "107.5", mode == "single-shared" ? DraftEditScope.AllReferences : DraftEditScope.CurrentUnit, [U1911], W1911) }
                };
                using var store = new DraftStore(root); await store.LoadAsync();
                await store.ApplyBatchAsync(ops, []);
                var service = new UnitTransactionService();
                var preview = await service.PrepareApplyAsync(root, store.Operations);
                await service.CommitApplyAsync(preview, store);
                var (_, _, after) = await LoadP4Async(root);
                if (mode is "single-shared" or "ammo-batch")
                {
                    Assert(File.ReadAllBytes(source).SequenceEqual(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("SuppressDamages = 50.0", "SuppressDamages = 107.5"))), "共享仅改压制原文，包括换行/射程/未改区段：" + mode);
                }
                else
                {
                    var local = after.Ammo(after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!.Mounts[0].AmmoName)!;
                    Assert(local.Name != A1911 && local.Field(field.Key)!.DisplayValue == "107.5", "局部副本压制生效：" + mode);
                    Assert(local.Field("ammo.range.air.min")!.RawValue == "35" && local.Field("ammo.range.air.max")!.RawValue == "0", "局部副本保留35/0");
                    Assert(after.Ammo(A1911)!.Field(field.Key)!.DisplayValue == "50" && File.ReadAllBytes(source).AsSpan(0, original.Length).SequenceEqual(original), "共享原对象逐字保留");
                }
                await service.CommitRestoreAsync(service.PrepareRestore(root, preview.BackupId));
                Assert(File.ReadAllBytes(source).SequenceEqual(original), "合成备份恢复字节一致");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }

    private static async Task AmmoRangeFinalComposition()
    {
        var root = RangeFixture();
        try
        {
            var (_, units, data) = await LoadP4Async(root);
            var ammo = data.Ammo(A1911)!;
            var service = new UnitTransactionService();
            DraftOperation Op(string key, string value, bool shared) => CreateWeaponDraft(ammo.Field(key)!, value,
                shared ? DraftEditScope.AllReferences : DraftEditScope.CurrentUnit, [U1911], W1911);
            foreach (var shared in new[] { true, false })
            {
                var minimum = Op("ammo.range.air.min", "200", shared);
                var maximum = Op("ammo.range.air.max", "100", shared);
                await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, [minimum, maximum]), "单条共享/局部最终倒置都拒绝");
                await service.PrepareApplyAsync(root, [minimum, maximum with { TargetValue = "300", TargetRaw = "300" }]);
                await service.PrepareApplyAsync(root, [minimum]); // Final maximum remains 0.
            }
            var sharedMax = Op("ammo.range.air.max", "100", true);
            var localMin = P1911(data, [sharedMax], "ammo.range.air.min", "200");
            Assert(!localMin.CanSave, "局部必须继承共享最大值后校验");
            var valid = P1911(data, [sharedMax], "ammo.range.air.min", "50");
            Assert(valid.CanSave, "共享与局部合法最终组合");
            using var store = new DraftStore(root); await store.LoadAsync();
            await store.ApplyBatchAsync(valid.Upserts.Append(sharedMax).ToArray(), []);
            var preview = await service.PrepareApplyAsync(root, valid.Upserts);
            Assert(preview.Operations.Count == 2, "部分应用扩展相关共享射程草稿");
            await service.CommitApplyAsync(preview, store);
            var (_, _, after) = await LoadP4Async(root);
            var variant = after.Ammo(after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!.Mounts[0].AmmoName)!;
            Assert(variant.Field("ammo.range.air.min")!.DisplayValue == "50" && variant.Field("ammo.range.air.max")!.DisplayValue == "100", "局部最终50/100实际写入");
            Assert(after.Ammo(A1911)!.Field("ammo.range.air.min")!.DisplayValue == "35" && after.Ammo(A1911)!.Field("ammo.range.air.max")!.DisplayValue == "100", "共享最终35/100实际写入");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
