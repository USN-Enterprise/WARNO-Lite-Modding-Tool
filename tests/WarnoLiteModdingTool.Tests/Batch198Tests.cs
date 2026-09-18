using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static readonly string[] Xp198Names = ["simple_v3", "SF_v2", "artillery", "helico", "avion"];
    private static string Xp198Name(int i) => "ExperienceLevelsPackDescriptor_XP_pack_" + Xp198Names[i];
    private static void WriteExperience198(string root, string nl = "\n")
    {
        var routes = new StringBuilder("// keep routes\n"); var effects = new StringBuilder("/* ~/Effect0_1 is only a comment */\n");
        for (var r = 0; r < 5; r++)
        {
            routes.AppendLine($"export {Xp198Name(r)} is TExperienceLevelsPackDescriptor( ExperienceLevelsDescriptors = [");
            for (var l = 0; l < 4; l++)
            {
                routes.AppendLine($"TExperienceLevelDescriptor( DescriptorId = GUID:{{0000000{r}-0000-0000-0000-00000000000{l}}} ThresholdAdditionalValue = 0 ThresholdPriceMultiplier = {l} HintBodyToken = 'hint{r}{l}'");
                if (!(r == 1 && l == 0)) routes.AppendLine($"LevelEffectsPacks = [$/GFX/EffectCapacity/Effect{r}_{l}]");
                routes.AppendLine("),");
                var values = r == 4 && l == 0 ? "" : """
                    TUnitEffectIncreaseWeaponPrecisionArretDescriptor(ModifierType = ~/ModifierType_Additionnel ModifierValue = 5),
                    TUnitEffectIncreaseDamageTakenDescriptor(ModifierType = ~/ModifierType_Pourcentage DamageType = EDamageType/Suppress BonusDamage = -14),
                    TBonusWeaponAimtimeEffectDescriptor(ModifierType = ~/ModifierType_Multiplicatif ModifierValue = 0.9),
                    TUnitEffectHealOverTimeDescriptor(NbUpdatePerSecond = 1 HealUnitsPerSecond = 1.9 DamageType = EDamageType/Suppress),
                    TUnitEffectRaiseTagDescriptor(TagListToRaise = ['xp_regular']),
                    TFutureEffect(PreserveMe = 99),
                    """;
                effects.AppendLine($"export Effect{r}_{l} is TEffectsPackDescriptor( EffectsDescriptors = [\n{values}\n] )");
            }
            routes.AppendLine("] )");
        }
        Write198(root, "GameData/Custom/Routes.ndf", routes.ToString(), nl);
        Write198(root, "GameData/Custom/Effects.ndf", effects.ToString(), nl);
        Write198(root, "GameData/Custom/Users.ndf", $"FOB is TBuilding(ExperienceLevelsPackDescriptor = ~/{Xp198Name(0)})\n", nl);
    }
    private static void Write198(string root, string path, string content, string nl = "\n")
    {
        var file = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content.Replace("\r\n", "\n").Replace("\n", nl), new UTF8Encoding(false));
    }
    private static DraftOperation Edit198(ExperienceWorkspace data, int route = 0, int level = 1)
    {
        var r = data.Routes.Single(r => r.Name == Xp198Name(route)); var l = r.Levels[level];
        var values = l.Cells.Where(c => c.Error.Length == 0).ToDictionary(c => c.Key, c => c.Raw);
        values["threshold.price"] = "3.6";
        if (values.ContainsKey($"Effect{route}_{level}/0/ModifierValue")) values[$"Effect{route}_{level}/0/ModifierValue"] = "7";
        return data.Operation(r, l, values);
    }
    private static async Task Experience198Transaction()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        {
            var root = Path.Combine(Path.GetTempPath(), "warno-xp198-" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteExperience198(root, nl);
                var context = new ModProjectDetector().Detect(root);
                Assert(context.Modules.Single(m => m.Key == "rules").CanScan, "仅经验规则的项目可用");
                var data = ExperienceWorkspace.Load(root);
                Assert(data.Routes.Count == 5 && data.Routes[0].Users.Count == 1, "五路线及非单位FOB引用");
                Assert(data.Routes[1].Levels[0].Cells.Count == 2, "SF缺省0级不补效果");
                Assert(data.Routes[4].Levels[0].Notes.Any(n => n.Contains("空效果包")), "空包可见");
                var op = Edit198(data); var op2 = Edit198(data, 0, 2);
                using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(op); await store.UpsertAsync(op2);
                using (var reopened = new DraftStore(root)) { await reopened.LoadAsync(); Assert(reopened.Operations.Count == 2 && reopened.Operations.All(o => data.Resolve(o).Status == DraftResolutionStatus.Active), "多等级草稿重开"); }
                var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
                Assert(preview.FormalFileCount == 2, "门槛和效果两文件同事务");
                var effectFile = preview.Files.Single(f => f.RelativePath.EndsWith("Effects.ndf"));
                var expectedEffects = Encoding.UTF8.GetString(effectFile.OriginalBytes);
                foreach (var l in new[] { 1, 2 })
                {
                    var anchor = $"export Effect0_{l} is TEffectsPackDescriptor"; var start = expectedEffects.IndexOf(anchor, StringComparison.Ordinal);
                    var at = expectedEffects.IndexOf("ModifierValue = 5", start, StringComparison.Ordinal);
                    expectedEffects = expectedEffects.Remove(at, "ModifierValue = 5".Length).Insert(at, "ModifierValue = 7");
                }
                Assert(Encoding.UTF8.GetString(effectFile.CandidateBytes) == expectedEffects, "只替换对应等级精度，保留负值/未知效果/标签/注释/换行");
                File.SetAttributes(effectFile.FullPath, FileAttributes.ReadOnly);
                try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "第二文件写入失败"); }
                finally { File.SetAttributes(effectFile.FullPath, FileAttributes.Normal); }
                Assert(preview.Files.Where(f => f.Existed).All(f => File.ReadAllBytes(f.FullPath).SequenceEqual(f.OriginalBytes)), "失败恢复完整基线");
                Assert(store.Operations.Count == 2, "失败保留草稿");
                preview = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(preview, store);
                Assert(File.ReadAllText(effectFile.FullPath) == expectedEffects && store.Operations.Count == 0, "应用并清理草稿");
                Assert(!File.ReadAllBytes(effectFile.FullPath).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "UTF8无BOM");
                await service.CommitRestoreAsync(service.PrepareRestore(root, preview.BackupId));
                Assert(File.ReadAllBytes(effectFile.FullPath).SequenceEqual(effectFile.OriginalBytes), "合成备份恢复");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
    private static async Task Experience198Guards()
    {
        var root = Path.Combine(Path.GetTempPath(), "warno-xp198-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteExperience198(root); var data = ExperienceWorkspace.Load(root); var op = Edit198(data);
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(op);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, [op]);
            Write198(root, "GameData/Extra/Refs.ndf", $"Added is TBuilding(ExperienceLevelsPackDescriptor = ~/{Xp198Name(0)})");
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "预览后新增文件里的使用者也应拒绝");
            Assert(ExperienceWorkspace.Load(root).Resolve(op).Status == DraftResolutionStatus.Conflict, "共享使用者变化使旧草稿冲突");
            Write198(root, "GameData/Extra/Refs.ndf", "OtherAbility is TCapacity(Effect = $/GFX/EffectCapacity/Effect0_1)");
            data = ExperienceWorkspace.Load(root); var level = data.Routes[0].Levels[1];
            Assert(level.Cells.Where(c => !c.Key.StartsWith("threshold.")).All(c => c.Error.Length > 0), "跨能力共享效果只读");
            Assert(level.Cells.Where(c => c.Key.StartsWith("threshold.")).All(c => c.Error.Length == 0), "共享只影响相关效果能力");
            var threshold = Edit198(data); await store.RemoveAsync(op.Id); await store.UpsertAsync(threshold);
            preview = await service.PrepareApplyAsync(root, [threshold]); Assert(preview.FormalFileCount == 1, "仍允许门槛单文件修改");
            var routePath = Path.Combine(root, "GameData/Custom/Routes.ndf");
            var original = File.ReadAllText(routePath); File.WriteAllText(routePath, original.Replace("ThresholdPriceMultiplier = 1", "ThresholdPriceMultiplier = 99"));
            Assert(ExperienceWorkspace.Load(root).Resolve(threshold).Status == DraftResolutionStatus.Conflict, "等级外部变化不按旧序号覆盖");
            File.WriteAllText(routePath, original + "\nexport " + Xp198Name(0) + " is TExperienceLevelsPackDescriptor(ExperienceLevelsDescriptors = [])");
            Assert(ExperienceWorkspace.Load(root).Routes.Single(r => r.Name == Xp198Name(0)).Error.Length > 0, "重复路线局部只读");
            File.WriteAllText(routePath, original);
            var fx = Path.Combine(root, "GameData/Custom/Effects.ndf");
            File.WriteAllText(fx, File.ReadAllText(fx).Replace("ModifierType = ~/ModifierType_Additionnel", "ModifierType = ~/ModifierType_Multiplicatif"));
            Assert(ExperienceWorkspace.Load(root).Routes[2].Levels[1].Cells.Single(c => c.Key == "Effect2_1/0/ModifierValue").Error.Length > 0, "不同修饰语义不得按加值编辑");
            Write198(root, "GameData/Custom/Extra.ndf", "export CustomFive is TExperienceLevelsPackDescriptor(ExperienceLevelsDescriptors = [" + string.Join(",", Enumerable.Range(0, 5).Select(i => $"TExperienceLevelDescriptor(ThresholdAdditionalValue = 0 ThresholdPriceMultiplier = {i})")) + "])");
            Assert(ExperienceWorkspace.Load(root).Routes.Single(r => r.Name == "CustomFive").Levels.Count == 5, "自定义实际档位不静默截断");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static async Task Experience198Combined()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            WriteExperience198(root);
            var file = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");
            File.WriteAllText(file, File.ReadAllText(file).Replace("ModulesDescriptors = [", $"ModulesDescriptors = [TExperienceModuleDescriptor(ExperienceLevelsPackDescriptor = ~/{Xp198Name(1)}),"));
            // Put routes in the same physical file as Unit edits to exercise offset relocation.
            File.AppendAllText(file, "\n" + File.ReadAllText(Path.Combine(root, "GameData/Custom/Routes.ndf")));
            File.Delete(Path.Combine(root, "GameData/Custom/Routes.ndf"));
            var context = new ModProjectDetector().Detect(root); var units = await new UnitProjectLoader().LoadAsync(context, await new ProjectIndexer().IndexAsync(context));
            var unit = units.Units.First(); var field = unit.Field("experience.type")!; var choice = field.Choices.Single(c => c.RawValue == "~/" + Xp198Name(0));
            var switchOp = CreateFieldDraft(unit, field, choice.Display, choice.RawValue);
            var edit = Edit198(units.Rules!.Experience);
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(switchOp); await store.UpsertAsync(edit);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
            Assert(preview.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == preview.Files.Count, "同文件联合修改无重复计划");
            Assert(preview.ValidationMessages.Any(m => m.Contains(unit.Name) && m.Contains("最终共享使用者")), "最终影响包括本批切换进入的单位");
            await service.CommitApplyAsync(preview, store);
            Assert(ExperienceWorkspace.Load(root).Routes[0].Levels[1].Cells.Single(c => c.Key == "threshold.price").Raw == "3.6", "同文件两个业务改动均生效");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static void Verify198Ui(Window owner)
    {
        var root = Path.Combine(Path.GetTempPath(), "warno-xp198-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteExperience198(root);
            using var store = new DraftStore(root); RunWithDispatcher(store.LoadAsync(), owner.Dispatcher);
            var vm = new RulesWorkspaceViewModel(RuleWorkspace.Load(root), store, () => { });
            var view = new RulesView { Workspace = vm };
            view.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush");
            var window = new Window { Title = "1.9.8 experience QA", Width = 1060, Height = 820, Content = view, Owner = owner };
            window.Show();
            try
            {
                foreach (var language in new[] { "zh-CN", "en" })
                {
                    UiText.Current.SetLanguage(language);
                    window.UpdateLayout(); DrainDispatcher(window.Dispatcher);
                    Descendants198(view).OfType<TextBox>().First().Text = Xp198Name(0);
                    vm.Refresh(); window.UpdateLayout(); DrainDispatcher(window.Dispatcher);
                    var level = vm.Experience[0].Levels[1];
                    var input = Descendants198(view).OfType<TextBox>().Single(t => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Source is ExperienceCellViewModel c && ReferenceEquals(c, level.Cells.First()));
                    input.Text = "2"; RunWithDispatcher(vm.FlushAsync(), window.Dispatcher);
                    Assert(store.Operations.Count == 1, "WPF字段绑定写入等级草稿");
                    level.Restore(); Assert(level.Cells.First().Value == "2", "WPF草稿恢复");
                    Assert(Descendants198(view).OfType<Expander>().Any(e => (string?)e.Tag == "experience"), "大类存在");
                    Assert(Descendants198(view).OfType<Expander>().Any(e => e.Header as string == (language == "en" ? "Level 1" : "等级 1")), "等级小类中英文");
                    foreach (var expander in Descendants198(view).OfType<Expander>().Where(e => e.Header as string == (language == "en" ? "Level 0" : "等级 0")).ToArray()) expander.IsExpanded = false;
                    var surface = (FrameworkElement)window.Content; window.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory("publish/qa-1.9.8"); using (var output = File.Create("publish/qa-1.9.8/experience-" + language + ".png")) encoder.Save(output);
                    RunWithDispatcher(level.UndoAsync(), window.Dispatcher); Assert(store.Operations.Count == 0, "撤销等级草稿");
                }
            }
            finally { window.Close(); UiText.Current.SetLanguage("zh-CN"); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static IEnumerable<DependencyObject> Descendants198(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants198(child)) yield return nested; }
    }
}
