using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.App.Settings;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Drafts;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static async Task<(UnitWorkspaceData Units, WeaponWorkspaceData Weapons)> Load193(string root, ProjectLoadCache? cache)
    {
        var context = new ModProjectDetector().Detect(root);
        var index = await new ProjectIndexer().IndexAsync(context, cache: cache);
        var units = await new UnitProjectLoader().LoadAsync(context, index, cache: cache);
        var weapons = await new WeaponProjectLoader().LoadAsync(context, index, units, cache: cache);
        return (units, weapons);
    }

    private static async Task ProjectCache193()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        var cachePath = Path.Combine(root, ".warno-editor", "test-cache.gz");
        try
        {
            var before = Directory.GetFiles(Path.Combine(root, "GameData"), "*", SearchOption.AllDirectories)
                .ToDictionary(p => p, File.ReadAllBytes);
            var cold = ProjectLoadCache.Open(root, path: cachePath)!;
            TestAssert.False(cold.IsHit, "首次未命中");
            var baseline = await Load193(root, cold);
            TestAssert.True(cold.Save(), "保存完整快照");
            var warm = ProjectLoadCache.Open(root, path: cachePath)!;
            TestAssert.True(warm.IsHit, "跨实例磁盘命中");
            var restored = await Load193(root, warm);
            TestAssert.Equal(baseline.Units.Units.Count, warm.RestoredUnits, "单位字段/引用实际复用");
            TestAssert.Equal(JsonSerializer.Serialize(baseline.Units.Units), JsonSerializer.Serialize(restored.Units.Units), "字段、位置、选择器及名称逐项等价");
            TestAssert.Equal(JsonSerializer.Serialize(baseline.Weapons), JsonSerializer.Serialize(restored.Weapons), "武器弹药引用及显示逐项等价");
            foreach (var pair in before) TestAssert.True(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)), "无损只读");

            var draftPath = Path.Combine(root, ".warno-editor", "unrelated-draft.json");
            File.WriteAllText(draftPath, "{}");
            TestAssert.True(ProjectLoadCache.Open(root, path: cachePath)!.IsHit, "草稿不使正式数据快照失效");
            TestAssert.True(ProjectLoadCache.Open(root, false, cachePath) is null, "关闭不读取缓存");
            var settings = new UiSettings(Path.Combine(root, ".warno-editor", "settings.json"));
            TestAssert.True(settings.Load().CacheLastMod, "缺省启用");
            settings.Save(settings.Load() with { CacheLastMod = false });
            TestAssert.False(settings.Load().CacheLastMod, "关闭持久化");

            var added = Path.Combine(root, "GameData", "new.csv");
            File.WriteAllText(added, "NEWNAME001;New");
            TestAssert.False(ProjectLoadCache.Open(root, path: cachePath)!.IsHit, "新增依赖失效");
            File.Delete(added);
            var ndf = before.Keys.First(p => p.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase));
            File.AppendAllText(ndf, "\n// external change\n", new UTF8Encoding(false));
            TestAssert.False(ProjectLoadCache.Open(root, path: cachePath)!.IsHit, "外部修改失效");
            TestAssert.False(warm.Save(), "读取期间改变不保存");
            File.Delete(ndf);
            TestAssert.False(ProjectLoadCache.Open(root, path: cachePath)!.IsHit, "删除依赖失效");
            File.WriteAllText(cachePath, "broken cache");
            TestAssert.False(ProjectLoadCache.Open(root, path: cachePath)!.IsHit, "损坏回退");
            TestAssert.True(ProjectLoadCache.Open(root + "-missing", path: cachePath) is null, "不存在目录不复用");
            ProjectLoadCache.Clear(cachePath);
            TestAssert.False(File.Exists(cachePath), "清除入口");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static async Task ProjectCacheWeapons193()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        var cachePath = Path.Combine(root, ".warno-editor", "cache.gz");
        try
        {
            var cache = ProjectLoadCache.Open(root, path: cachePath)!;
            var baseline = await Load193(root, cache);
            TestAssert.True(cache.Save(), "保存武器快照");
            var restoredCache = ProjectLoadCache.Open(root, path: cachePath)!;
            var restored = await Load193(root, restoredCache);
            TestAssert.True(restoredCache.RestoredWeapons > 0, "实际复用武器对象");
            TestAssert.Equal(JsonSerializer.Serialize(baseline.Weapons), JsonSerializer.Serialize(restored.Weapons), "全部武器/弹药/共享影响等价");
            var other = CreateTemporaryFixtureCopy("p2-unit-complete");
            try { TestAssert.False(ProjectLoadCache.Open(other, path: cachePath)!.IsHit, "不同Mod隔离"); }
            finally { DeleteTemporaryFixture(other); }
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            bool rejected = false;
            try { cache.Save(canceled.Token); } catch (OperationCanceledException) { rejected = true; }
            TestAssert.True(rejected && ProjectLoadCache.Open(root, path: cachePath)!.IsHit, "取消保留原缓存");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static async Task ProjectCacheTransaction193()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        var path = Path.Combine(root, ".warno-editor", "cache.gz");
        try
        {
            var cache = ProjectLoadCache.Open(root, path: path)!;
            var original = await Load193(root, cache);
            TestAssert.True(cache.Save(), "基线缓存");
            var tank = original.Units.Units.Single(u => u.Name == "Descriptor_Unit_Test_Tank_US");
            var health = tank.Field("survival.health")!;
            using var drafts = new DraftStore(root);
            await drafts.LoadAsync();
            await drafts.UpsertAsync(CreateFieldDraft(tank, health, "12", "12"));
            var stamp = File.GetLastWriteTimeUtc(tank.Source.SourceFile);
            var text = File.ReadAllText(tank.Source.SourceFile);
            var replacement = health.RawValue[..^1] + (health.RawValue[^1] == '1' ? '2' : '1');
            File.WriteAllText(tank.Source.SourceFile, text.Remove(health.Location!.CharacterOffset, health.Location.CharacterLength)
                .Insert(health.Location.CharacterOffset, replacement), new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(tank.Source.SourceFile, stamp);
            TestAssert.True(ProjectLoadCache.Open(root, path: path)!.IsHit, "长度与时间戳相同的外部替换可能仍命中打开缓存");
            bool rejected = false;
            try { await new UnitTransactionService().PrepareApplyAsync(root, drafts.Operations); }
            catch (TransactionValidationException) { rejected = true; }
            TestAssert.True(rejected, "正式预览重新读盘拒绝旧字段基线");

            var blockedParent = Path.Combine(root, ".warno-editor", "not-a-directory");
            File.WriteAllText(blockedParent, "file");
            var unwritable = ProjectLoadCache.Open(root, path: Path.Combine(blockedParent, "cache.gz"))!;
            await Load193(root, unwritable);
            TestAssert.False(unwritable.Save(), "缓存写入失败不影响工作区加载");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    // Explicit read-only benchmark: never opens a DraftStore or writes the selected Mod.
    private static async Task BenchmarkCache193(string root, string path)
    {
        foreach (var mode in new[] { "disabled", "cold", "warm" })
        {
            var timer = Stopwatch.StartNew();
            var cache = mode == "disabled" ? null : ProjectLoadCache.Open(root, path: path);
            var lookup = timer.ElapsedMilliseconds;
            var context = new ModProjectDetector().Detect(root);
            var index = await new ProjectIndexer().IndexAsync(context, cache: cache);
            var indexed = timer.ElapsedMilliseconds;
            var units = await new UnitProjectLoader().LoadAsync(context, index, cache: cache);
            var unitTime = timer.ElapsedMilliseconds;
            var weapons = await new WeaponProjectLoader().LoadAsync(context, index, units, cache: cache);
            var loaded = timer.ElapsedMilliseconds;
            var division = await new Core.Divisions.DivisionProjectLoader().LoadAsync(context, index, units);
            var divisionTime = timer.ElapsedMilliseconds;
            var strategic = await new Core.Strategic.StrategicLoader().LoadAsync(context, index, units);
            var strategicTime = timer.ElapsedMilliseconds;
            cache?.Save();
            Console.WriteLine($"{mode}: lookup={lookup}ms index={indexed}ms unit={unitTime}ms weapon={loaded}ms division={divisionTime}ms strategic={strategicTime}ms total={timer.ElapsedMilliseconds}ms hit={cache?.IsHit} units={units.Units.Count} restored={cache?.RestoredUnits} weapons={cache?.RestoredWeapons} bytes={(File.Exists(path) ? new FileInfo(path).Length : 0)}");
        }
    }
}
