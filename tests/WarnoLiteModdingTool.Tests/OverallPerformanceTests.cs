using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    // Sample private commit in the complete benchmark process, including WPF.
    // Stage-end values alone can miss a transient preview peak.
    private sealed class PerformanceMemory : IDisposable
    {
        private readonly object _gate = new();
        private readonly System.Diagnostics.Process _process = System.Diagnostics.Process.GetCurrentProcess();
        private readonly Timer _timer;
        private long _peak;
        private bool _disposed;
        public PerformanceMemory() => _timer = new Timer(_ => Sample(), null, 0, 100);
        private void Sample()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _process.Refresh();
                _peak = Math.Max(_peak, _process.PrivateMemorySize64);
            }
        }
        public double PeakMiB { get { Sample(); lock (_gate) return _peak / 1048576d; } }
        public void Dispose()
        {
            _timer.Dispose();
            lock (_gate) { _disposed = true; _process.Dispose(); }
        }
    }

    private static async Task OverallPerformanceCore()
    {
        var nested = new NdfSyntaxDocument("Root is A(Child = A(Value = 1) Other = B(Value = 2)) Next is A(Value = 3)");
        Assert(nested.FindConstructors("A").Count == 2, "构造索引不把同类型内层重复算作外层");
        var outer = nested.FindConstructors("A")[0];
        Assert(nested.FindConstructors("A", nested.FindDirectAssignments(outer, "Child").Single()).Count == 1, "有界查询保留内层构造");
        using (var stop = new CancellationTokenSource())
        {
            var document = new NdfSyntaxDocument("X is A(Value = 1)");
            stop.Cancel();
            using (new ProjectReadScope(cancellation: stop.Token).Enter())
            {
                try { _ = document.FindConstructors("A"); throw new Exception("取消应打断索引准备"); }
                catch (OperationCanceledException) { }
            }
            Assert(document.FindConstructors("A").Count == 1, "取消不能发布半成品语法索引");
        }
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            Write198(root, "GameData/Extra.ndf", string.Join("\n", Enumerable.Range(0, 500).Select(i => $"export Extra{i} is TBuilding(Value = {i})")));
            using var metrics = new AnalysisMetrics();
            using (new ProjectReadScope().Enter())
            {
                var before = new UnitProjectGraph(root);
                var original = before.Files["GameData/Extra.ndf"];
                var path = Path.Combine(root, original.Path);
                var bytes = File.ReadAllBytes(path);
                var changed = new PlannedFileChange(original.Path, path, FormalTextFileKind.Ndf, PlannedFileAction.Write, true,
                    bytes, Encoding.UTF8.GetBytes(original.Text.Replace("Value = 499)", "Value = 501)")), File.GetLastWriteTimeUtc(path), []);
                var after = new UnitProjectGraph(root, [changed with { RelativePath = changed.RelativePath.Replace('/', '\\') }]);
                Assert(before.Files.Count == after.Files.Count && before.Objects.Count() == after.Objects.Count(), "混合路径分隔符不重复读取同一候选");
                Assert(!ReferenceEquals(original, after.Files[original.Path]), "候选变化文件独立解析");
                Assert(before.Files.Where(p => p.Key != original.Path).All(p => ReferenceEquals(p.Value, after.Files[p.Key])), "候选复用未变文件语法");
                Assert(metrics.GraphFiles == before.Files.Count + 1, "整轮每个未变文件只解析一次");
                var parseCount = metrics.SyntaxDocuments;
                foreach (var obj in before.Objects) { _ = before.IsExported(obj); _ = before.IsExported(obj); }
                Assert(metrics.SyntaxDocuments == parseCount, "导出身份查询不重新词法解析对象");
                var first = original.Objects[0];
                var view = before.Document(first);
                var direct = new NdfSyntaxDocument(original.Text, first.CharacterOffset, first.CharacterLength);
                Assert(view.Tokens.SequenceEqual(direct.Tokens), "共享语法视图保留文件绝对偏移和token");
                var span = view.FindConstructors(first.TypeName).Single();
                Assert(view.Raw(view.FindDirectAssignments(span, "Value").Single()) == "0", "共享语法视图正确读取字段");
            }
            var cachePath = Path.Combine(root, "isolated-cache.zip");
            var cache = ProjectLoadCache.Open(root, path: cachePath)!;
            var data = await ProjectWorkspaceSnapshot.LoadRootAsync(root, cache);
            Assert(cache.Save(), "紧凑缓存可保存");
            var reopened = await ProjectWorkspaceSnapshot.LoadRootAsync(root, ProjectLoadCache.Open(root, path: cachePath)!);
            Assert(data.Units.Units.Count == reopened.Units.Units.Count, "缓存单位数量保持");
            if (data.Weapons is { } weapons)
            {
                var restored = reopened.Weapons!;
                foreach (var ammo in weapons.Ammunition)
                foreach (var field in ammo.Fields)
                {
                    var actual = restored.Ammo(ammo.Name)!.Field(field.Key)!;
                    Assert(field.Definition == actual.Definition && field.RawValue == actual.RawValue && field.State == actual.State &&
                        field.Choices.SequenceEqual(actual.Choices), "紧凑缓存保留字段语义/完整有序候选");
                }
                var replacement = weapons with { Ammunition = [] };
                if (weapons.Ammunition.Count > 0)
                {
                    _ = weapons.Ammo(weapons.Ammunition[0].Name);
                    Assert(replacement.Ammo(weapons.Ammunition[0].Name) is null, "record候选替换不能复用旧查找索引");
                }
            }
        }
        finally { DeleteTemporaryFixture(root); }
        var motionRoot = MotionFixture();
        try
        {
            var cachePath = Path.Combine(motionRoot, "display-cache.zip");
            var cache = ProjectLoadCache.Open(motionRoot, path: cachePath)!;
            var first = await ProjectWorkspaceSnapshot.LoadRootAsync(motionRoot, cache);
            var expected = MissileMotion.Status(first.Weapons!, [], A1911, "ammo.speed.projectile", "");
            Assert(expected.Contains("1800"), "后台摘要保留实际实体数值");
            Assert(cache.Save(), "提示摘要随项目缓存保存");
            using (var metrics = new AnalysisMetrics())
            {
                var warm = await ProjectWorkspaceSnapshot.LoadRootAsync(motionRoot, ProjectLoadCache.Open(motionRoot, path: cachePath)!);
                Assert(MissileMotion.Status(warm.Weapons!, [], A1911, "ammo.speed.projectile", "") == expected, "缓存提示与冷读相同");
                Assert(MissileMotion.EntitySource(warm.Weapons!, [], A1911, "")?.Name == "TestFlight", "缓存源码定位保持");
                Assert(metrics.Graphs == 0, "缓存命中及首次提示不构建整图");
            }
            // An arbitrary new NDF outside the ordinary module directory must invalidate
            // the summary and preserve the resolver's ambiguity guard.
            Write198(motionRoot, "Custom/Duplicate.ndf", FlightBody());
            var changed = await ProjectWorkspaceSnapshot.LoadRootAsync(motionRoot, ProjectLoadCache.Open(motionRoot, path: cachePath)!);
            Assert(MissileMotion.Status(changed.Weapons!, [], A1911, "ammo.speed.projectile", "").Contains("unavailable"),
                "新目录多义实体使提示缓存失效，不能沿用旧通过结果");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await TestAssert.ThrowsAsync<OperationCanceledException>(
                () => ProjectWorkspaceSnapshot.LoadRootAsync(motionRoot, ProjectLoadCache.CreateSession(motionRoot), cancelled.Token),
                "取消贯穿项目摘要准备");
        }
        finally { DeleteTemporaryFixture(motionRoot); }
    }
}
