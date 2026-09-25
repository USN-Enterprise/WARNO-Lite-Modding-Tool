using System.IO;
using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.App.ViewModels.Divisions;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static void Verify1913Ui(MainViewModel main, System.Windows.Window window, string returnRoot)
    {
        var root = Setup1913();
        try
        {
            foreach (var advanced in new[] { false, true }) foreach (var language in new[] { "zh-CN", "en" })
            {
                main.AdvancedMode = advanced; WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(language);
                RunWithDispatcher(main.OpenProjectAsync(root), window.Dispatcher); VanillaNames.Replace(SyntheticNames());
                main.SelectedModule = main.Modules.Single(m => m.Key == "rules"); main.RulesWorkspace!.Category = "地形规则";
                DrainDispatcher(window.Dispatcher);
                var rules = FindVisualChildren<WarnoLiteModdingTool.App.Controls.RulesView>(window).Single();
                foreach (var e in FindVisualChildren<System.Windows.Controls.Expander>(rules)) e.IsExpanded = true;
                DrainDispatcher(window.Dispatcher);
                var field = main.RulesWorkspace.Terrains[0].Fields.First(f => f.Cell.Basic);
                if (!advanced)
                {
                    field.Reduction = "50"; RunWithDispatcher(field.SaveAsync(), window.Dispatcher);
                    Assert(field.Value == "0.5" && !field.HasPendingError, "普通百分比换回承伤倍率并保存");
                    field.Reduction = "101"; RunWithDispatcher(field.SaveAsync(), window.Dispatcher); Assert(field.HasPendingError, "普通模式比例越界保留输入并阻止应用");
                    RunWithDispatcher(field.UndoAsync(), window.Dispatcher);
                }
                Assert(FindVisualChildren<System.Windows.Controls.ComboBox>(rules).Any() == advanced, "地形布尔项仅专业模式显示");
                foreach (var e in FindVisualChildren<System.Windows.Controls.Expander>(rules).Where(e => e.Header is System.Windows.Controls.TextBlock h && h.Text == "CustomWood")) e.IsExpanded = true;
                DrainDispatcher(window.Dispatcher);
                SaveUiSnapshot(window, $"1913-terrain-{language}-{advanced}.png");
                main.SelectedModule = main.Modules.Single(m => m.Key == "divisions"); DrainDispatcher(window.Dispatcher);
                var tab = FindVisualChildren<System.Windows.Controls.TabItem>(window).Single(t => t.Header?.ToString() == WarnoLiteModdingTool.App.Localisation.UiText.T("师简介")); tab.IsSelected = true; DrainDispatcher(window.Dispatcher);
                var editor = main.DivisionWorkspace!.TextEditor!; var part = editor.Parts[0]; var original = part.Text;
                part.Text = "界面编辑；\"多行\"\n\n段落";
                editor.ReferenceLanguage = "US"; Assert(part.Text.Contains("段落"), "切换参考语言不覆盖输入");
                RunWithDispatcher(TestAssert.ThrowsAsync<InvalidOperationException>(() => main.DivisionWorkspace.FlushAsync(), "未保存正文阻止关闭/切换项目"), window.Dispatcher);
                editor.RevertInput(); Assert(part.Text == original, "撤销输入恢复正式或草稿基线");
                part.Text = "保存的正文\n\n第二段"; RunWithDispatcher(editor.SaveAsync(), window.Dispatcher);
                Assert(!editor.HasPendingInput && editor.Status == "师正文草稿已保存", editor.Status);
                Assert(main.DivisionWorkspace.SelectedDivision!.DraftStatus == "有草稿", "师列表识别正文草稿");
                var displayed = FindVisualChildren<WarnoLiteModdingTool.App.Controls.DivisionTextView>(window).Single();
                Assert(FindVisualChildren<System.Windows.Controls.TextBox>(displayed).Count(t => t.AcceptsReturn && !t.IsReadOnly) == 2, "两段多行正文实际编辑框");
                SaveUiSnapshot(window, $"1913-division-text-{language}-{advanced}.png");
                RunWithDispatcher(main.UnitWorkspace!.ClearDraftsAsync(), window.Dispatcher); main.DivisionWorkspace.RefreshFromDrafts();
            }
            Verify1914Style(main, window, root);
        }
        finally
        {
            WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh-CN"); main.AdvancedMode = false;
            RunWithDispatcher(main.OpenProjectAsync(returnRoot), window.Dispatcher); DeleteTemporaryFixture(root); VanillaNames.Replace(SyntheticNames());
        }
    }
    private const string TerrainFile1913 = "GameData/Gameplay/CustomTerrain.ndf";
    private const string TerrainSource1913 = """
        // Synthetic terrain fixture; not game data.
        unnamed TGameplayTerrainsRegistration (
          Terrains = [
            TGameplayTerrain (
              Name = 'CustomWood'
              TerrainType = ~/ETerrainType/ForetLegere
              ConcealmentBonus = 3
              BloqueVehicule = true
              HeightInMeters = 20
              InflammabilityProbability = 0.6
              DamageModifierPerFamilyAndResistance = MAP [
                (DamageFamily_he, MAP [(ResistanceFamily_infanterie,0.65), (ResistanceFamily_vehicule,1.2)]),
                // (DamageFamily_superhe, MAP [(ResistanceFamily_infanterie,0.5)]),
                /* (DamageFamily_fmballe, MAP [(ResistanceFamily_infanterie,0.1)]), */
                (DamageFamily_balle, MAP [(ResistanceFamily_infanterie,0.55)]),
              ]
            ),
            TGameplayTerrain (Name='CustomWater' TerrainType=~/ETerrainType/EauProfonde BloqueVehicule=true),
          ]
        )
        """;
    private static string Setup1913(string nl = "\n")
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        var directory = Path.Combine(root, "GameData/Localisation/Text1913"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "LocalisationDicos.ndf"), "unnamed TLocalisationDicoResource ( DicoToken = ~/LocalisationConstantes/dico_units FileName = 'GameData:/Localisation/Text1913/UNITS.csv' CanBeMissing = true )");
        File.WriteAllText(Path.Combine(directory, "UNITS.csv"), "TOKEN;REFTEXT" + nl + "P5DIV00001;原师" + nl + "OLDTEXT001;旧简介" + nl, new UTF8Encoding(true));
        var file = Path.Combine(root, "GameData/Generated/Gameplay/Decks/Divisions.ndf");
        File.WriteAllText(file, File.ReadAllText(file).Replace("    TypeToken", "    SummaryTextToken = 'OLDTEXT001'\n    HistoryTextToken = 'TESTNAME01'\n    EmblemTexture = \"Texture_Test\"\n    DescriptionHintTitleToken = 'P5DIV00001'\n    TypeToken").Replace("\r\n", "\n").Replace("\n", nl), new UTF8Encoding(false));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, TerrainFile1913))!);
        File.WriteAllText(Path.Combine(root, TerrainFile1913), TerrainSource1913.Replace("\r\n", "\n").Replace("\n", nl), new UTF8Encoding(false));
        return root;
    }
    private static async Task Terrain1913Transactions()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        {
            var root = Setup1913(nl);
            try
            {
                var data = TerrainWorkspace.Load(root); var t = data.Terrains.Single(t => t.Name == "CustomWood");
                Assert(data.Terrains.Count == 2 && t.Cells.Count(c => c.Key.StartsWith("damage/")) == 3, "仅解析活动MAP，支持自定义地形名字");
                Assert(t.Cells.Count(c => c.Basic) == 2 && t.Cells.Single(c => c.Key == "HeightInMeters").Error.Length > 0, "普通模式仅已知合法比例，地形高度只读");
                var c = t.Cells.First(c => c.Basic); var d = t.Cells.Last(c => c.Basic);
                using var store = new DraftStore(root); await store.LoadAsync();
                await store.UpsertAsync(data.Operation(t, c, "0.5")); await store.UpsertAsync(data.Operation(t, d, "0.25"));
                var original = File.ReadAllBytes(Path.Combine(root, TerrainFile1913)); var service = new UnitTransactionService();
                var preview = await service.PrepareApplyAsync(root, [store.Operations.First()]);
                Assert(preview.Operations.Count == 1, "不同地形字段允许独立应用");
                await service.CommitApplyAsync(preview, store);
                var remaining = TerrainWorkspace.Load(root); Assert(remaining.Resolve(store.Operations.Single()).Status == DraftResolutionStatus.Active, "部分应用不使另一字段无故冲突");
                await service.CommitApplyAsync(await service.PrepareApplyAsync(root, store.Operations), store);
                var expected = Encoding.UTF8.GetString(original).Replace("infanterie,0.65", "infanterie,0.5").Replace("infanterie,0.55", "infanterie,0.25");
                Assert(File.ReadAllText(Path.Combine(root, TerrainFile1913)) == expected, "精确修改保留注释、未知抗性族及换行");
                var edited = TerrainWorkspace.Load(root); var op = edited.Operation(edited.Terrains[0], edited.Terrains[0].Cells.First(c => c.Basic), "0.4"); await store.UpsertAsync(op);
                preview = await service.PrepareApplyAsync(root, store.Operations);
                File.WriteAllText(Path.Combine(root, "GameData/newTerrain.ndf"), "// new file");
                await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "预览后新文件使规则影响范围过期");
                File.Delete(Path.Combine(root, "GameData/newTerrain.ndf"));
                File.AppendAllText(Path.Combine(root, TerrainFile1913), TerrainSource1913);
                Assert(TerrainWorkspace.Load(root).Resolve(op).Status == DraftResolutionStatus.Conflict, "重复地形身份拒绝写入");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task Terrain1913Standalone()
    {
        var root = Setup1913();
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, "GameData/Generated"), "*.ndf", SearchOption.AllDirectories)) File.Delete(file);
            var data = TerrainWorkspace.Load(root); var t = data.Terrains[0];
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(data.Operation(t, t.Cells.First(c => c.Basic), "0.4"));
            var service = new UnitTransactionService(); await service.CommitApplyAsync(await service.PrepareApplyAsync(root, store.Operations), store);
            Assert(File.ReadAllText(Path.Combine(root, TerrainFile1913)).Contains("infanterie,0.4"), "没有单位/弹药/师仍可应用地形规则");
            var duplicate = TerrainSource1913.Replace("ConcealmentBonus = 3", "ConcealmentBonus = 3 ConcealmentBonus = 4");
            Assert(TerrainWorkspace.Read("memory", duplicate)[0].Cells.Single(c => c.Key == "ConcealmentBonus").Error.Length > 0, "重复直接赋值不猜写");
            var repeatedMap = TerrainSource1913.Replace("(DamageFamily_balle", "(DamageFamily_he");
            Assert(TerrainWorkspace.Read("memory", repeatedMap)[0].Error.Length > 0, "重复伤害键拒绝");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task DivisionText1913Composition()
    {
        var root = Setup1913();
        try
        {
            var (_, _, data) = await LoadP5Async(root); var division = data.Divisions.Single();
            var summary = DivisionText.Read(data, division, "SummaryTextToken", "SC"); var history = DivisionText.Read(data, division, "HistoryTextToken", "SC");
            Assert(summary.Text == "旧简介" && history.Text == "测试名称", "Mod优先及原版本机词典关联");
            using var store = new DraftStore(root); await store.LoadAsync();
            var target = " 中文;\"引号\"\n\nSecond paragraph 🚁\n  保留空格 ";
            var first = DivisionText.Operation(data, division, summary, target, []); await store.UpsertAsync(first);
            var again = DivisionText.Operation(data, division, summary, target + "x", store.Operations, first);
            Assert(again.NameToken == first.NameToken, "反复编辑复用稳定token");
            var second = DivisionText.Operation(data, division, history, target, store.Operations); await store.UpsertAsync(second);
            Assert(first.NameToken != second.NameToken, "相同正文两段仍独立token");
            var rename = DivisionIdentity.Operation(division, DivisionIdentity.New(data, division, false, store.Operations) with { Name = "正文组合改名" }); await store.UpsertAsync(rename);
            using (var reopened = new DraftStore(root)) { await reopened.LoadAsync(); Assert(DivisionText.Payload(reopened.Operations.First(o => o.Id == first.Id)).Text == target, "多行草稿重开保持"); }
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
            await service.CommitApplyAsync(preview, store);
            var (_, _, after) = await LoadP5Async(root); var d = after.Divisions.Single();
            Assert(d.DisplayName == "正文组合改名" && DivisionText.Read(after, d, "SummaryTextToken", "SC").Text == target && DivisionText.Read(after, d, "HistoryTextToken", "US").Text == target, "名称和两正文同CSV累计写入无丢失");
            Assert(after.Units.Localisation.TryResolve("OLDTEXT001", out var old) && old == "旧简介", "旧正文词条保留");
            var csv = preview.Files.Single(f => f.Kind == FormalTextFileKind.Csv);
            Assert(File.ReadAllBytes(csv.FullPath).Take(3).SequenceEqual(new byte[] {239,187,191}), "CSV保留BOM");
            await service.CommitRestoreAsync(service.PrepareRestore(root, preview.BackupId));
            Assert(preview.Files.Where(f => f.Kind is FormalTextFileKind.Ndf or FormalTextFileKind.Csv).All(f => File.ReadAllBytes(f.FullPath).SequenceEqual(f.OriginalBytes)), "NDF/CSV备份恢复逐字节相同");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task DivisionText1913GuardsRecovery()
    {
        var root = Setup1913("\r\n"); string? locked = null;
        try
        {
            var (_, _, data) = await LoadP5Async(root); var division = data.Divisions.Single();
            var part = DivisionText.Read(data, division, "SummaryTextToken", "SC");
            using var store = new DraftStore(root); await store.LoadAsync(); var op = DivisionText.Operation(data, division, part, "新正文\r\n\r\n正文", []); await store.UpsertAsync(op);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
            locked = preview.Files.Single(f => f.Kind == FormalTextFileKind.Csv).FullPath; File.SetAttributes(locked, FileAttributes.ReadOnly);
            await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "CSV写入失败回滚NDF");
            Assert(preview.Files.Where(f => f.Kind is FormalTextFileKind.Ndf or FormalTextFileKind.Csv).All(f => File.ReadAllBytes(f.FullPath).SequenceEqual(f.OriginalBytes)) && store.Operations.Count == 1, "失败保留原文和草稿");
            File.SetAttributes(locked, FileAttributes.Normal); locked = null;
            var payload = DivisionText.Payload(op); var bad = op with { NameToken = "TESTNAME01", TargetRaw = JsonSerializer.Serialize(payload with { Token = "TESTNAME01" }) };
            await store.UpsertAsync(bad); await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "不覆盖已有原版token");
            await store.UpsertAsync(op); preview = await service.PrepareApplyAsync(root, store.Operations);
            await store.UpsertAsync(op with { Summary = "changed" });
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "预览后草稿变更拒绝");
            await store.UpsertAsync(op);
            File.AppendAllText(data.Units.Localisation.UniqueUnitsCsvPath!, "OLDTEXT001;重复\r\n");
            var (_, _, duplicate) = await LoadP5Async(root); Assert(DivisionText.Resolve(duplicate, op).Status == DraftResolutionStatus.Conflict, "重复源token不当成空白正文");
        }
        finally { if (locked is not null) File.SetAttributes(locked, FileAttributes.Normal); DeleteTemporaryFixture(root); }
    }
    private static Task DivisionText1913Cache()
    {
        var root = Path.Combine(Path.GetTempPath(), "wlmt-text-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var game = Path.Combine(root, "game"); var pack = Path.Combine(game, "Data/PC/1/ZZ_1.dat");
            Archive192(pack, "旧正文\n\n段落", 2); var patch = Path.Combine(game, "Data/PC/1/2/ZZ_1.dat"); Archive192(patch, "新正文;\"引号\"\n\n🚁", 3);
            // Keep UNITS only in the archive directory, without changing payload offsets.
            foreach (var file in new[] { pack, patch })
            {
                var bytes = File.ReadAllBytes(file);
                foreach (var key in new[] { "COMPANIES", "PLATOONS" })
                {
                    var from = Encoding.ASCII.GetBytes(key); var to = Encoding.ASCII.GetBytes(new string('X', key.Length));
                    for (var i = 0; i <= bytes.Length - from.Length; i++) if (bytes.AsSpan(i, from.Length).SequenceEqual(from)) to.CopyTo(bytes, i);
                }
                File.WriteAllBytes(file, bytes);
            }
            var cachePath = Path.Combine(root, "cache.json"); var cache = new GameNameCache(cachePath, true); var result = cache.Load(game);
            Assert(result.Names.Count == 2 && result.Names["SC/UNITS"].Values.Single() == "新正文;\"引号\"\n\n🚁", "正文缓存只要求UNITS；v2/v3与补丁优先级保持段落");
            var original = File.ReadAllBytes(cachePath); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { cache.Load(game, true, cancel.Token); throw new Exception("取消未生效"); } catch (OperationCanceledException) { }
            Assert(original.SequenceEqual(File.ReadAllBytes(cachePath)), "取消保留有效缓存");
            Directory.Move(game, game + "-offline"); cache.Load(game); Assert(cache.Offline, "同源离线缓存可读并标记");
            try { cache.Load(Path.Combine(root, "other")); throw new Exception("错误复用其他安装缓存"); } catch (InvalidDataException) { }
            VanillaNames.Replace(result.Names); Assert(VanillaNames.UnitsAvailable && !VanillaNames.Available, "正文可用性独立于六词典名称契约");
        }
        finally { VanillaNames.Replace(SyntheticNames()); Directory.Delete(root, true); }
        return Task.CompletedTask;
    }
}
