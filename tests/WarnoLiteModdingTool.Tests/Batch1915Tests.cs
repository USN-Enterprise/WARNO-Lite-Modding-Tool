using System.IO;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Strategic;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static Task<ProjectWorkspaceSnapshot> Read1915(string root, ProjectLoadCache? cache = null) =>
        ProjectWorkspaceSnapshot.LoadRootAsync(root, cache ?? ProjectLoadCache.CreateSession(root));

    private static void Equal1915(ProjectWorkspaceSnapshot actual, ProjectWorkspaceSnapshot expected)
    {
        string Json(object? value) => JsonSerializer.Serialize(value);
        TestAssert.Equal(Json(expected.Index), Json(actual.Index), "增量顶层索引/位置与完整读取一致");
        TestAssert.Equal(Json(expected.Units), Json(actual.Units), "单位/名称/候选/引用/规则一致");
        TestAssert.Equal(Json(expected.Weapons), Json(actual.Weapons), "武器/弹药/共享使用者一致");
        TestAssert.Equal(Json(expected.Divisions), Json(actual.Divisions), "师与单位数据一致");
        TestAssert.Equal(Json(expected.Strategic), Json(actual.Strategic), "战略数据与单位数据一致");
        TestAssert.Equal(Json(expected.PictureTextures), Json(actual.PictureTextures), "单位图片引用目录一致");
        TestAssert.Equal(expected.PictureDiagnostic, actual.PictureDiagnostic, "图片诊断一致");
    }
    private static async Task Cache1915Partitions()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            WriteExperience198(root);
            var path = Path.Combine(root, ".warno-editor/cache1915.zip");
            var first = await Read1915(root, ProjectLoadCache.Open(root, path: path));
            Assert(first.Cache.Save(), "保存分区快照");
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
            {
                var entry = zip.Entries.First(e => e.FullName.StartsWith("units/")); var name = entry.FullName; entry.Delete();
                using var corrupt = zip.CreateEntry(name).Open(); corrupt.WriteByte(255);
            }
            var recovered = await Read1915(root, ProjectLoadCache.Open(root, path: path));
            Equal1915(recovered, first);
            Assert(!recovered.Cache.IsHit && recovered.Cache.Save(), "损坏单位分区自动重建");
            var source = first.Units.Units[0].Source.SourceFile;
            using (first.Reads.Enter())
            {
                var before = first.Reads.ReadFiles;
                Assert(ReferenceEquals(ProjectReadScope.ReadAllText(source), ProjectReadScope.ReadAllText(source)), "共享同一源文本");
                Assert(before == first.Reads.ReadFiles, "已读文件不再次读盘");
            }
            var csv = first.Units.Localisation.UnitsCsvPaths.Single();
            File.AppendAllText(csv, "\nNEWTEST001;New name\n");
            var partial = await Read1915(root, ProjectLoadCache.Open(root, path: path));
            Assert(!partial.Cache.IsHit && partial.Cache.RestoredUnits == first.Units.Units.Count, "CSV变化仍复用单位语法分区");
            Assert(partial.Cache.RestoredIndexFiles > 0, "CSV变化不重扫无关NDF");
            Equal1915(partial, await Read1915(root));
            Assert(partial.Cache.Save(), "保存局部更新后的完整有效缓存");
            var warm = await Read1915(root, ProjectLoadCache.Open(root, path: path));
            Equal1915(warm, await Read1915(root));
            using (var drafts = new DraftStore(root))
            {
                await drafts.LoadAsync();
                await Apply1915(warm, drafts, Edit198(warm.Units.Rules!.Experience));
            }
            var invalidated = ProjectLoadCache.Open(root, path: path)!;
            await Read1915(root, invalidated);
            ProjectLoadCache.Clear(path);
            Assert(!invalidated.Save() && !File.Exists(path), "清除后旧任务不得重新发布");
        }
        finally { DeleteTemporaryFixture(root); }
        root = Fixture199();
        try
        {
            var data = await Read1915(root); using var drafts = new DraftStore(root); await drafts.LoadAsync();
            var mother = data.Units.Units.First(); var creation = UnitCreation.New(mother, data.Units, []);
            data = await Apply1915(data, drafts, UnitCreation.Operation(mother, creation));
            var created = data.Units.Units.Single(u => u.Name == creation.Id);
            data = await Apply1915(data, drafts, UnitIdentityEditing.Operation(created, "Descriptor_Unit_Refresh_Renamed"));
            created = data.Units.Units.Single(u => u.Name == "Descriptor_Unit_Refresh_Renamed");
            data = await Apply1915(data, drafts, UnitDeletion.Operation(created));
        }
        finally { DeleteTemporaryFixture(root); }
        root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            WriteStrategicFixture(root, "\n");
            var data = await Read1915(root); using var drafts = new DraftStore(root); await drafts.LoadAsync();
            var record = data.Strategic!.Records.First();
            data = await Apply1915(data, drafts, StrategicCodec.Operation(record, record.Baseline with { Name = "刷新战略名称" }));
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task<ProjectWorkspaceSnapshot> Apply1915(ProjectWorkspaceSnapshot before, DraftStore drafts, params DraftOperation[] operations)
    {
        foreach (var operation in operations) await drafts.UpsertAsync(operation);
        var service = new UnitTransactionService();
        var preview = await service.PrepareApplyAsync(drafts.ProjectRoot, operations);
        await service.CommitApplyAsync(preview, drafts);
        var next = await ProjectWorkspaceSnapshot.LoadRootAsync(drafts.ProjectRoot, before.Cache.Next(), previous: before, committed: preview.Files);
        Equal1915(next, await Read1915(drafts.ProjectRoot));
        var changed = preview.Files.Count(f => f.Kind != FormalTextFileKind.Log && f.Action == PlannedFileAction.Write);
        Assert(next.Reads.ReadFiles <= changed, "提交后只读取变更文件，复用未变源文本");
        return next;
    }
    private static async Task Refresh1915Composition()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var data = await Read1915(root); using var drafts = new DraftStore(root); await drafts.LoadAsync();
            var tank = data.Units.Units.First(); var other = data.Units.Units.Last();
            var pending = CreateFieldDraft(other, other.Field("survival.health")!, "16", "16");
            await drafts.UpsertAsync(pending);
            data = await Apply1915(data, drafts, CreateFieldDraft(tank, tank.Field("survival.health")!, "123", "123"));
            Assert(drafts.Operations.Single().Id == pending.Id, "部分应用保留未选草稿");
            var resolutions = DraftResolver.Resolve(data.Units, data.Weapons, data.Divisions, drafts.Operations, data.Strategic);
            Assert(resolutions.All(r => r.Status == DraftResolutionStatus.Active), "同文件偏移移动后剩余草稿仍按原基线解析");
            data = await Apply1915(data, drafts, pending);
        }
        finally { DeleteTemporaryFixture(root); }
        root = CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var data = await Read1915(root); using var drafts = new DraftStore(root); await drafts.LoadAsync();
            data = await Apply1915(data, drafts, CreateWeaponDraft(data.Weapons!.Ammo("Ammo_P4_Shared")!.Field("ammo.damage.physical")!, "12", DraftEditScope.AllReferences, [], null));
            var weapon = data.Weapons!.Weapons.Single(w => w.Name == "WeaponDescriptor_P4_Shared");
            data = await Apply1915(data, drafts, CreateWeaponDraft(weapon.Field("weapon.salves.0")!, "6", DraftEditScope.CurrentUnit, ["Descriptor_Unit_P4_One"], weapon.Name));
        }
        finally { DeleteTemporaryFixture(root); }
        root = Setup1913();
        try
        {
            var data = await Read1915(root); using var drafts = new DraftStore(root); await drafts.LoadAsync();
            var division = data.Divisions!.Divisions.Single();
            var text = DivisionText.Read(data.Divisions, division, "SummaryTextToken", "SC");
            data = await Apply1915(data, drafts, DivisionText.Operation(data.Divisions, division, text, "局部刷新;正文\n第二行", []));
            var terrain = data.Units.Rules!.Terrain.Terrains.First();
            var cell = terrain.Cells.First(c => c.Basic);
            data = await Apply1915(data, drafts, data.Units.Rules.Terrain.Operation(terrain, cell, "0.4"));
        }
        finally { DeleteTemporaryFixture(root); }
        root = Fixture1910();
        try
        {
            var data = await Read1915(root); using var drafts = new DraftStore(root); await drafts.LoadAsync();
            var unit = data.Units.Units.First();
            data = await Apply1915(data, drafts, CreateFieldDraft(unit, unit.Field("survival.health")!, "18", "18"));
            var template = Core.Images.UnitPictures.Require(Core.Images.UnitPictures.Catalog(root), Picture1910Key);
            var picture = Core.Images.UnitPictures.Import(template, Png1910());
            data = await Apply1915(data, drafts, Core.Images.UnitPictures.Operation(data.Units.Units.First(), picture));
            Assert(data.PictureTextures.Any(t => t.Key == picture.Key), "图片应用后目录包含新纹理");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task Refresh1915Guards()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var before = await Read1915(root);
            var file = before.Units.Units[0].Source.SourceFile;
            File.AppendAllText(file, "\n// external change\n");
            var external = await ProjectWorkspaceSnapshot.LoadRootAsync(root, before.Cache.Next(), previous: before);
            Equal1915(external, await Read1915(root));
            using var drafts = new DraftStore(root); await drafts.LoadAsync();
            var tank = external.Units.Units[0]; var field = tank.Field("survival.health")!;
            var operation = CreateFieldDraft(tank, field, "17", "17"); await drafts.UpsertAsync(operation);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, [operation]);
            await service.CommitApplyAsync(preview, drafts);
            File.AppendAllText(file, "\n// edited after commit\n");
            var rejected = false;
            try { await ProjectWorkspaceSnapshot.LoadRootAsync(root, external.Cache.Next(), previous: external, committed: preview.Files); }
            catch (IOException) { rejected = true; }
            Assert(rejected && drafts.Operations.Count == 0, "刷新拒绝提交后变化，但正式提交及草稿清理已经成功");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
