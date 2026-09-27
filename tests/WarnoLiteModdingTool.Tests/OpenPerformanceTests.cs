using System.IO;
using System.Reflection;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Rules;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static Task ExperienceBaselineCompatibility()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            const string routeText = "export Route is TExperienceLevelsPackDescriptor(ExperienceLevelsDescriptors = [TExperienceLevelDescriptor(ThresholdAdditionalValue = 0 ThresholdPriceMultiplier = 1 LevelEffectsPacks = [~/Effect]), TExperienceLevelDescriptor(ThresholdAdditionalValue = 0 ThresholdPriceMultiplier = 2)])";
            const string effectText = "export Effect is TEffectsPackDescriptor(EffectsDescriptors = [TUnitEffectHealOverTimeDescriptor(NbUpdatePerSecond = 1 HealUnitsPerSecond = 1.9 DamageType = EDamageType/Suppress)])";
            Write198(root, "GameData/Effects.ndf", effectText);
            Write198(root, "GameData/Routes.ndf", routeText);
            Write198(root, "GameData/Users.ndf", "User is TBuilding(XP = ~/Route)");
            var data = ExperienceWorkspace.Load(root); var route = data.Routes.Single(); var level = route.Levels[0];
            var baselineField = typeof(ExperienceWorkspace).GetField("_baselines", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var baselines = (System.Collections.IDictionary)baselineField.GetValue(data)!;
            IEnumerable<Lazy<string>> Prepared(string name) => baselines.Values.Cast<object>().Select(v => (Lazy<string>)v.GetType().GetProperty(name)!.GetValue(v)!);
            Assert(Prepared("Legacy").Concat(Prepared("Compact")).All(b => !b.IsValueCreated), "只读打开不创建经验基线");
            // Explicit legacy wire payload: route text, effect source, reverse effect references, route references.
            var user = new { File = "GameData/Users.ndf", Owner = "User", Target = "Route", Raw = "~/Route" };
            var expected = JsonSerializer.Serialize(new[] { routeText, "GameData/Effects.ndf\n" + effectText,
                JsonSerializer.Serialize(new[] { new { File = "GameData/Routes.ndf", Owner = "Route", Target = "Effect", Raw = "~/Effect" }, user }),
                JsonSerializer.Serialize(new[] { user }) });
            var values = level.Cells.Where(c => c.Error.Length == 0).ToDictionary(c => c.Key, c => c.Raw); values["threshold.price"] = "3";
            var op = data.Operation(route, level, values);
            TestAssert.Equal("experience-v2:sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected))), op.BaselineRaw, "紧凑基线包含完全相同的旧格式证据");
            Assert(Prepared("Compact").Count(b => b.IsValueCreated) == 1 && Prepared("Legacy").All(b => !b.IsValueCreated), "只准备一个相关等级且不生成巨大旧基线");
            var legacy = op with { BaselineRaw = expected };
            Assert(data.Resolve(legacy).Status == DraftResolutionStatus.Active, "旧格式草稿可恢复");
            TestAssert.Equal(expected, data.Operation(route, level, values, expected).BaselineRaw, "继续编辑旧草稿保留原格式");
            Assert(data.Resolve(op with { BaselineRaw = "experience-v99:sha256:unknown" }).Status == DraftResolutionStatus.Conflict, "未知版本不猜测应用");
            Write198(root, "GameData/Added.ndf", "Added is TBuilding(XP = ~/Route)");
            TestAssert.Equal(expected, data.GetBaseline(route, level), "延迟基线只使用原快照，不隐式读盘");
            Assert(ExperienceWorkspace.Load(root).Resolve(op).Status == DraftResolutionStatus.Conflict, "新增外部使用者使旧基线冲突");
            Assert(ExperienceWorkspace.Load(root).Resolve(legacy).Status == DraftResolutionStatus.Conflict, "旧格式仍检查新增外部使用者");
            return Task.CompletedTask;
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static async Task CacheReaderLifetime()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            WriteExperience198(root);
            var path = Path.Combine(root, ".warno-editor/lifetime.zip");
            var first = await Read1915(root, ProjectLoadCache.Open(root, path: path)); Assert(first.Cache.Save(), "初次缓存保存");
            var cache = ProjectLoadCache.Open(root, path: path)!;
            var reader = typeof(ProjectLoadCache).GetField("_reader", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using (cache.BeginReadSession())
            {
                Assert(cache.Index is not null, "缓存读取"); var archive = reader.GetValue(cache); Assert(archive is not null, "读取器已建立");
                using (cache.BeginReadSession())
                {
                    var loaded = await Read1915(root, cache); Equal1915(loaded, first);
                    Assert(ReferenceEquals(archive, reader.GetValue(cache)), "跨分区和嵌套读取复用同一目录");
                }
                Assert(ReferenceEquals(archive, reader.GetValue(cache)), "内层释放不关闭外层读取器");
            }
            Assert(reader.GetValue(cache) is null, "最外层结束释放读取器");
            Assert(cache.Save(), "读取器释放后仍能后台保存");
            var cancelled = ProjectLoadCache.Open(root, path: path)!;
            try { await ProjectWorkspaceSnapshot.LoadRootAsync(root, cancelled, new CancellationToken(true)); throw new Exception("取消未生效"); }
            catch (OperationCanceledException) { }
            Assert(reader.GetValue(cancelled) is null, "取消释放读取器");
            var next = cache.Next(); Equal1915(await Read1915(root, next), first);
            Assert(!cache.Save(), "旧代不能覆盖新代缓存");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
