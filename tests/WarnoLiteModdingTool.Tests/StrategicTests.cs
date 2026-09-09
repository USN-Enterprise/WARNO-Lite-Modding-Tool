using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Strategic;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
    private static void SaveUiSnapshot(System.Windows.Window window, string name)
    {
        if (!Directory.Exists("src/WarnoLiteModdingTool.App")) return;
        var content = (System.Windows.FrameworkElement)window.Content;
        content.Measure(new System.Windows.Size(window.Width, window.Height));
        content.Arrange(new System.Windows.Rect(0, 0, window.Width, window.Height)); content.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath("publish/qa-1.8.6"); Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }
    private static async Task StrategicCompositionTransaction()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        foreach (var sharedDeck in new[] { true, false })
        {
            var root = CreateTemporaryFixtureCopy("p2-unit-complete");
            try
            {
                WriteStrategicFixture(root, nl);
                if (!sharedDeck)
                {
                    File.AppendAllText(Path.Combine(root, StrategicLoader.DirectoryPath, "StrategicDecks.ndf"), nl + "export Deck_B is TDeckDescriptor ( DeckIdentifier = 'deck_b' DeckPackList = [~/Pack_A, ~/Pack_A,] DeckCombatGroupList = [~/Company_A,] )" + nl, new UTF8Encoding(false));
                    var pawnPath = Path.Combine(root, "GameData/Generated/Gameplay/Unit/Strategic/Units.ndf");
                    var pawnText = File.ReadAllText(pawnPath); var split = pawnText.IndexOf("export Pawn_B", StringComparison.Ordinal);
                    File.WriteAllText(pawnPath, pawnText[..split] + pawnText[split..].Replace("'deck_a'", "'deck_b'"), new UTF8Encoding(false));
                }
                var data = await ReadStrategic(root);
                Assert(data.Records.Count == 2 && data.Diagnostics.Count == 0, string.Join(";", data.Diagnostics));
                var record = data.Records.Single(r => r.Id == "Pawn_A");
                var untouched = data.Records.Single(r => r.Id == "Pawn_B");
                var company = record.Baseline.Companies[0];
                var group = company.Groups[0];
                var changed = group with { Name = "新排;带\"引号", IsHQ = true, Slots = [group.Slots[0] with { Count = 3, Xp = 2 }] };
                var added = new StrategicGroup("new:group", "第二排", false, [new("new:slot", "", "Descriptor_Unit_Test_Recon_SOV", "", 1, 2)]);
                var state = record.Baseline with { Name = "新战略营", Companies = [company with { Name = "新连", Groups = [added, changed] }],
                    PawnValues = new Dictionary<string, string>(record.Baseline.PawnValues) { ["InitialActionPoint"] = "9" } };
                using var store = new DraftStore(root); await store.LoadAsync();
                var op = StrategicCodec.Operation(record, state); await store.UpsertAsync(op);
                using (var restored = new DraftStore(root))
                {
                    await restored.LoadAsync(); Assert(restored.Operations.Single().TargetValue == op.TargetValue, "战略草稿恢复");
                }
                var service = new UnitTransactionService();
                var preview = await service.PrepareApplyAsync(root, store.Operations);
                Assert(preview.Files.Count(f => f.Kind == FormalTextFileKind.Ndf) == 4, "战略三文件和 Pawn 同事务");
                Assert(preview.Files.Where(f => f.Kind == FormalTextFileKind.Ndf).All(f => !f.CandidateBytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 })), "无 BOM");
                foreach (var file in preview.Files.Where(f => f.Kind == FormalTextFileKind.Ndf))
                {
                    var text = Encoding.UTF8.GetString(file.CandidateBytes);
                    Assert(text.Contains("// fixture untouched", StringComparison.Ordinal), "保留原注释");
                    if (nl == "\r\n") Assert(!text.Replace("\r\n", "").Contains('\n'), "CRLF 保持");
                }
                var result = await service.CommitApplyAsync(preview, store);
                Assert(result.Succeeded, result.Message);
                var after = await ReadStrategic(root);
                Assert(StrategicCodec.Serialize(after.Records.Single(r => r.Id == "Pawn_B").Baseline) == StrategicCodec.Serialize(untouched.Baseline), "共享 Deck 的另一个 Pawn 不变");
                var target = after.Records.Single(r => r.Id == "Pawn_A");
                Assert(target.Error is null && target.Baseline.Name == "新战略营", target.Error ?? "名称写入");
                Assert(target.Baseline.Companies[0].Groups[1].Name == changed.Name, "CSV 转义名称回读");
                Assert(target.Baseline.Companies[0].Groups.SelectMany(g => g.Slots).Sum(s => s.Count) == 5, "索引与数量闭包");
                // Shrink, delete and move back after the first transaction; no shared target may change.
                var next = target.Baseline with { Companies = [target.Baseline.Companies[0] with { Groups = [target.Baseline.Companies[0].Groups[1] with {
                    Slots = [target.Baseline.Companies[0].Groups[1].Slots[0] with { Count = 1 }] }] }] };
                await store.UpsertAsync(StrategicCodec.Operation(target, next));
                var second = await service.PrepareApplyAsync(root, store.Operations);
                Assert((await service.CommitApplyAsync(second, store)).Succeeded, "缩容与删除事务");
                var reduced = await ReadStrategic(root);
                Assert(reduced.Records.Single(r => r.Id == "Pawn_A").Baseline.Companies.Single().Groups.Single().Slots.Single().Count == 1, "负增量索引回读");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }

    private static async Task StrategicRejectsInvalidAndConflictingEdits()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            WriteStrategicFixture(root, "\n");
            var data = await ReadStrategic(root); var record = data.Records[0];
            var company = record.Baseline.Companies[0]; var group = company.Groups[0];
            var bad = record.Baseline with { Companies = [company with { Groups = [group with { Slots = [group.Slots[0] with { Count = 0 }] }] }] };
            Assert(StrategicPlanner.Resolve(data, StrategicCodec.Operation(record, bad)).Status == DraftResolutionStatus.Conflict, "拒绝无效数量");
            var state = record.Baseline with { PawnValues = new Dictionary<string, string>(record.Baseline.PawnValues) { ["InitialActionPoint"] = "8" } };
            var operation = StrategicCodec.Operation(record, state);
            var file = Path.Combine(root, "GameData/Generated/Gameplay/Unit/Strategic/Units.ndf");
            File.WriteAllText(file, File.ReadAllText(file).Replace("InitialActionPoint = 4", "InitialActionPoint = 5"), new UTF8Encoding(false));
            var changed = await ReadStrategic(root);
            Assert(StrategicPlanner.Resolve(changed, operation).Status == DraftResolutionStatus.Conflict, "检测棋子基线变化");
            Assert(!Directory.Exists(Path.Combine(root, ".warno-editor/backups")), "校验不写备份");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static async Task StrategicRollbackAndSettings()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            WriteStrategicFixture(root, "\n");
            var data = await ReadStrategic(root); var record = data.Records[0];
            var state = record.Baseline with { Name = "回滚测试", PawnValues = new Dictionary<string, string>(record.Baseline.PawnValues) { ["InitialActionPoint"] = "9" } };
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(StrategicCodec.Operation(record, state));
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
            var csv = preview.Files.Single(f => f.Kind == FormalTextFileKind.Csv);
            File.SetAttributes(csv.FullPath, File.GetAttributes(csv.FullPath) | FileAttributes.ReadOnly);
            try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "战略事务后续文件失败必须回滚"); }
            finally { File.SetAttributes(csv.FullPath, FileAttributes.Normal); }
            foreach (var file in preview.Files.Where(f => f.Kind is FormalTextFileKind.Ndf or FormalTextFileKind.Csv))
                Assert(File.ReadAllBytes(file.FullPath).SequenceEqual(file.OriginalBytes), "战略事务回滚原始字节");
            Assert(store.Operations.Count == 1, "回滚后保留战略草稿");
            var settings = new WarnoLiteModdingTool.App.Settings.UiSettings(Path.Combine(root, "settings.json"));
            settings.Save(new("en", true)); Assert(settings.Load() == new WarnoLiteModdingTool.App.Settings.UiPreferences("en", true), "语言和模式设置持久化");
            var localizer = WarnoLiteModdingTool.App.Localisation.UiText.Current;
            localizer.SetLanguage("en");
            Assert(WarnoLiteModdingTool.App.Localisation.UiText.T("部分可用 · 找到 4/5 个源文件 · 10 个对象") == "Partial · 4/5 source files · 10 objects", "嵌套状态模板翻译");
            localizer.SetLanguage("zh-CN");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static async Task<StrategicWorkspace> ReadStrategic(string root)
    {
        var context = new ModProjectDetector().Detect(root);
        var index = await new ProjectIndexer().IndexAsync(context);
        var units = await new UnitProjectLoader().LoadAsync(context, index);
        return await new StrategicLoader().LoadAsync(context, index, units);
    }
    private static void WriteStrategicFixture(string root, string nl)
    {
        void Write(string relative, string text)
        {
            var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ("// fixture untouched \"([)]\"\n" + text + "\n").Replace("\r\n", "\n").Replace("\n", nl), new UTF8Encoding(false));
        }
        Write(StrategicLoader.DirectoryPath + "StrategicPacks.ndf", "Pack_A is DeckPackDescriptor ( Unit = $/GFX/Unit/Descriptor_Unit_Test_Tank_US )");
        Write(StrategicLoader.DirectoryPath + "StrategicDecks.ndf", "export Deck_A is TDeckDescriptor ( DeckIdentifier = 'deck_a' DeckPackList = [~/Pack_A, ~/Pack_A,] DeckCombatGroupList = [~/Company_A,] )");
        Write(StrategicLoader.DirectoryPath + "StrategicCombatGroups.ndf", "Company_A is TDeckCombatGroupDescriptor ( Name = 'COMPANY001' SmartGroupList = [ TDeckSmartGroupDescriptor ( Name = 'PLATOON001' PackIndexUnitNumberList = [(0,2),] ), ] )");
        var pawn = "export Pawn_{0} is TEntityDescriptor ( ModulesDescriptors = [ TDeckModuleDescriptor ( DeckIdentifier = 'deck_a' ), TActionPointsModuleDescriptor ( InitialActionPoint = 4 ActionPointRecoveryPerTurn = 4 ), StrategicUIModuleDescriptor ( NameToken = 'PAWNNAME0{0}' ), ] )";
        Write("GameData/Generated/Gameplay/Unit/Strategic/Units.ndf", pawn.Replace("{0}", "A") + "\n" + pawn.Replace("{0}", "B"));
        var localisation = Directory.GetFiles(Path.Combine(root, "GameData/Localisation"), "LocalisationDicos.ndf", SearchOption.AllDirectories).Single();
        var dir = Path.GetDirectoryName(localisation)!;
        File.AppendAllText(localisation, "\nunnamed TLocalisationDicoResource ( FileName = 'COMPANIES.csv' )\nunnamed TLocalisationDicoResource ( FileName = 'PLATOONS.csv' )\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "COMPANIES.csv"), "\"TOKEN\";\"REFTEXT\"" + nl, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "PLATOONS.csv"), "\"TOKEN\";\"REFTEXT\"" + nl, new UTF8Encoding(false));
    }
}
