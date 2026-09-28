using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private const string SupplyKey = "supply.capacity";
    private static string SupplyFixture(string newline = "\n")
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        var path = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");
        var text = File.ReadAllText(path).Replace("\r\n", "\n")
            .Replace("TVisibilityModuleDescriptor(UnitConcealmentBonus = 1.25),",
                "TSupplyModuleDescriptor(SupplyDescriptor = $/GFX/Weapon/StandardSupply SupplyCapacity = 2500.5 SupplyPriority = 4), // supply comment\n        TVisibilityModuleDescriptor(UnitConcealmentBonus = 1.25),")
            .Replace("TVisibilityModuleDescriptor(UnitConcealmentBonus = 2.0),",
                "TSupplyModuleDescriptor(SupplyCapacity = 750.0),\n        TVisibilityModuleDescriptor(UnitConcealmentBonus = 2.0),");
        text += "\nexport Descriptor_Unit_No_Supply is TEntityDescriptor(ModulesDescriptors = [TBaseDamageModuleDescriptor(MaxPhysicalDamages = 10)])\n";
        File.WriteAllText(path, text.Replace("\n", newline), new UTF8Encoding(false));
        return root;
    }

    private static async Task SupplyCapacityContracts()
    {
        var root = SupplyFixture();
        try
        {
            var (data, tank) = await LoadTankAsync(root);
            var field = tank.Field(SupplyKey)!;
            Assert(field.CanEdit && field.DisplayValue == "2500.5", "按原值读取小数补给点");
            foreach (var input in new[] { "0", "3200.75", "1000000" })
                Assert(UnitValueConverter.TryFormatTarget(field, input, out _, out _, out _), "接受非负补给量 " + input);
            foreach (var input in new[] { "-1", "NaN", "Infinity", "1e999", "~/Other", "2 * 100" })
                Assert(!UnitValueConverter.TryFormatTarget(field, input, out _, out _, out _), "拒绝无效目标 " + input);
            var missing = data.Units.Single(u => u.Name == "Descriptor_Unit_No_Supply").Field(SupplyKey)!;
            Assert(!missing.CanEdit && !missing.Definition.CanInsertWhenMissing, "无补给模块不自动补建");
            var batch = UnitBatchPlanner.Preview(new("supply-test", data.Units, SupplyKey, UnitBatchOperation.IncreasePercent, "20", UnitBatchRounding.None, null, null, []));
            Assert(batch.CanAddToDrafts && batch.CompatibleCount == 2 && batch.Upserts.Count == 2 && batch.Warnings.Count > 0, "批量仅修改兼容单位并提示跳过");
            Assert(batch.Upserts.Single(o => o.ObjectName == tank.Name).TargetValue == "3000.6", "百分比不丢小数");
            var stacked = UnitBatchPlanner.Preview(new("supply-next", [tank], SupplyKey, UnitBatchOperation.Add, "10", UnitBatchRounding.None, null, null, batch.Upserts));
            Assert(stacked.Upserts.Single().TargetValue == "3010.6", "公式叠加既有草稿值");

            var original = File.ReadAllText(tank.Source.SourceFile);
            foreach (var variant in new[] {
                ("SupplyCapacity = ~/Capacity", UnitFieldAvailability.UnsupportedValue),
                ("SupplyCapacity = (2500 + 0.5)", UnitFieldAvailability.UnsupportedValue),
                ("SupplyCapacity = 100 SupplyCapacity = 200", UnitFieldAvailability.Ambiguous) })
            {
                File.WriteAllText(tank.Source.SourceFile, original.Replace("SupplyCapacity = 2500.5", variant.Item1), new UTF8Encoding(false));
                var (_, changed) = await LoadTankAsync(root);
                Assert(changed.Field(SupplyKey)!.Availability == variant.Item2, "表达式或重复字段不猜写");
            }
            File.WriteAllText(tank.Source.SourceFile, original.Replace("SupplyCapacity = 2500.5 SupplyPriority = 4)", "SupplyCapacity = 2500.5 SupplyPriority = 4), TSupplyModuleDescriptor(SupplyCapacity = 100)"), new UTF8Encoding(false));
            Assert((await LoadTankAsync(root)).Tank.Field(SupplyKey)!.Availability == UnitFieldAvailability.Ambiguous, "重复补给模块不猜写");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static async Task SupplyCapacityTransactions()
    {
        foreach (var newline in new[] { "\n", "\r\n" })
        {
            var root = SupplyFixture(newline);
            var csv = Path.Combine(root, "GameData/Localisation/UnexpectedP2Dictionary/UNITS.csv");
            try
            {
                var (data, tank) = await LoadTankAsync(root);
                var original = File.ReadAllBytes(tank.Source.SourceFile);
                using var store = new DraftStore(root); await store.LoadAsync();
                var batch = UnitBatchPlanner.Preview(new("supply-apply", data.Units, SupplyKey, UnitBatchOperation.Set, "3200.75", UnitBatchRounding.None, null, null, []));
                foreach (var op in batch.Upserts) await store.UpsertAsync(op);
                await store.UpsertAsync(CreateFieldDraft(tank, tank.Field("survival.health")!, "12", "12"));
                using (var reopened = new DraftStore(root))
                {
                    await reopened.LoadAsync();
                    Assert(reopened.Operations.Count == 3 && DraftResolver.Resolve(data, reopened.Operations).All(r => r.Status == DraftResolutionStatus.Active), "补给草稿重开与解析");
                }
                Assert(File.ReadAllBytes(tank.Source.SourceFile).SequenceEqual(original), "草稿不写正式文件");
                var service = new UnitTransactionService();
                var preview = await service.PrepareApplyAsync(root, store.Operations);
                var applied = await service.CommitApplyAsync(preview, store);
                var expected = Encoding.UTF8.GetString(original).Replace("SupplyCapacity = 2500.5", "SupplyCapacity = 3200.75")
                    .Replace("SupplyCapacity = 750.0", "SupplyCapacity = 3200.75").Replace("MaxPhysicalDamages = 10\n", "MaxPhysicalDamages = 12\n")
                    .Replace("MaxPhysicalDamages = 10\r\n", "MaxPhysicalDamages = 12\r\n");
                Assert(applied.Succeeded && File.ReadAllBytes(tank.Source.SourceFile).SequenceEqual(new UTF8Encoding(false).GetBytes(expected)), "仅目标值改变，保留换行、注释、其他单位和补给优先级，无BOM");
                Assert((await LoadTankAsync(root)).Tank.Field(SupplyKey)!.DisplayValue == "3200.75", "正式文件重载补给量");
                await service.CommitRestoreAsync(service.PrepareRestore(root, applied.BackupId));
                Assert(File.ReadAllBytes(tank.Source.SourceFile).SequenceEqual(original), "备份完整恢复");

                UnitValueConverter.TryFormatTarget(tank.Field(SupplyKey)!, "0", out var zero, out var rawZero, out _);
                await store.UpsertAsync(CreateFieldDraft(tank, tank.Field(SupplyKey)!, zero, rawZero));
                await store.UpsertAsync(CreateNameDraft(tank, "补给失败恢复", tank.NameToken!));
                preview = await service.PrepareApplyAsync(root, store.Operations);
                File.SetAttributes(csv, File.GetAttributes(csv) | FileAttributes.ReadOnly);
                await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "多文件提交失败");
                Assert(File.ReadAllBytes(tank.Source.SourceFile).SequenceEqual(original) && File.Exists(store.DraftPath), "失败回滚补给值并保留草稿");
                File.SetAttributes(csv, FileAttributes.Normal);
                File.WriteAllText(tank.Source.SourceFile, Encoding.UTF8.GetString(original).Replace("SupplyCapacity = 2500.5", "SupplyCapacity = 999"), new UTF8Encoding(false));
                await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "外部改动阻止陈旧补给草稿");
            }
            finally { if (File.Exists(csv)) File.SetAttributes(csv, FileAttributes.Normal); DeleteTemporaryFixture(root); }
        }
    }

    private static async Task SupplyCapacityUi()
    {
        var root = SupplyFixture(); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            WarnoLiteModdingTool.App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
                WarnoLiteModdingTool.App.Theming.ThemeManager.Initialize(new WarnoLiteModdingTool.App.Theming.UiThemeStore(Path.Combine(root, ".qa/theme.txt")));
                var vm = new WarnoLiteModdingTool.App.ViewModels.MainViewModel(new WarnoLiteModdingTool.Core.Projects.RecentProjectStore(Path.Combine(root, ".qa/recent.json")));
                var window = new WarnoLiteModdingTool.App.MainWindow(vm) { Width = 1480, Height = 1000 }; window.Show(); DrainDispatcher(dispatcher);
                RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher);
                foreach (var lang in new[] { "zh-CN", "en" })
                foreach (var pro in new[] { false, true })
                {
                    vm.AdvancedMode = pro; WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(lang);
                    WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(pro ? WarnoLiteModdingTool.App.Theming.AppTheme.DarkBlue : WarnoLiteModdingTool.App.Theming.AppTheme.LightBlue, false);
                    window.Width = lang == "en" ? 1240 : 1480;
                    var scale = lang == "en" ? 1.15 : 1;
                    ((System.Windows.FrameworkElement)window.Content).LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
                    var units = vm.UnitWorkspace!; units.SelectedUnit = units.Units.Single(u => u.InternalName == "Descriptor_Unit_Test_Tank_US");
                    foreach (var section in units.FieldSections) section.IsExpanded = section.Title == "机动与续航";
                    var field = units.Fields.Single(f => f.Field?.Definition.Key == SupplyKey);
                    Assert(field.IsEditable && units.VisibleBatchFields.Any(f => f.Definition.Key == SupplyKey), "两种模式支持补给量和批改");
                    DrainDispatcher(dispatcher);
                    var input = FindVisualChildren<System.Windows.Controls.TextBox>(window).Single(c => ReferenceEquals(c.DataContext, field) && c.IsVisible);
                    input.BringIntoView(); DrainDispatcher(dispatcher);
                    input.Text = "3600.5"; input.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
                    RunWithDispatcher(field.FlushAsync(), dispatcher); Assert(field.ActiveDraft?.TargetValue == "3600.5", "实际输入控件保存补给草稿");
                    System.Windows.DependencyObject? parent = input;
                    while (parent is not null && parent is not System.Windows.Controls.ScrollViewer) parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                    if (parent is System.Windows.Controls.ScrollViewer scroll) scroll.ScrollToBottom();
                    DrainDispatcher(dispatcher); window.UpdateLayout();
                    var surface = (System.Windows.FrameworkElement)window.Content;
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(surface);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory("publish/qa-1.9.20"); using (var output = File.Create($"publish/qa-1.9.20/supply-{lang}-{pro}.png")) encoder.Save(output);
                    RunWithDispatcher(units.UndoFieldAsync(field), dispatcher);
                }
                window.Close(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception e) { app?.Shutdown(); completion.SetException(e); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task; Console.WriteLine("PASS supply capacity WPF input, modes, languages, themes, widths and scaling"); }
        finally { DeleteTemporaryFixture(root); }
    }
}
