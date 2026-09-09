using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.App;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Settings;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.App.ViewModels.Units;
using WarnoLiteModdingTool.Core.Drafts;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static Task ThemeDefaults19()
    {
        var root = CreateTemporaryFixtureCopy("missing-modules");
        try
        {
            var path = Path.Combine(root, "theme.txt");
            var store = new UiThemeStore(path);
            Assert(store.Load() == AppTheme.LightBlue, "首次启动默认白蓝");
            File.WriteAllText(path, "invalid-theme");
            Assert(store.Load() == AppTheme.LightBlue, "无效配置回退白蓝");
            foreach (var theme in Enum.GetValues<AppTheme>())
            {
                store.Save(theme);
                Assert(store.Load() == theme, "升级保留七种有效个人主题：" + theme);
            }
        }
        finally { DeleteTemporaryFixture(root); }
        return Task.CompletedTask;
    }

    private static void Capture19(Window window, string name)
    {
        window.InvalidateMeasure(); window.UpdateLayout(); DrainDispatcher(window.Dispatcher);
        var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath("publish/qa-1.9.1"); Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }

    private static void Verify19Ui(MainViewModel vm, MainWindow window, string originalRoot)
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            RunWithDispatcher(vm.OpenProjectAsync(root), window.Dispatcher);
            UiText.Current.SetLanguage("zh-CN");
            ThemeManager.ApplyTheme(AppTheme.LightBlue, false);
            BackgroundAppearance.Apply(new UiPreferences(BackgroundEnabled: false));
            vm.AdvancedMode = false;
            window.ShowActivated = false; window.ShowInTaskbar = false;
            window.Left = -10000; window.Top = -10000; window.Show();
            DrainDispatcher(window.Dispatcher);
            var workspace = vm.UnitWorkspace!;
            var single = (Border)FindWorkspaceName(window, "SingleUnitInspector");
            var batch = (Border)FindWorkspaceName(window, "BatchUnitInspector");
            var list = (ColumnDefinition)FindWorkspaceName(window, "UnitListColumn");
            Assert(Grid.GetColumn(single) == Grid.GetColumn(batch), "单选与批量使用同一右侧栏位");
            foreach (var size in new[] { new Size(1200, 680), new Size(1420, 860), new Size(1920, 1080) })
            {
                window.Width = size.Width; window.Height = size.Height;
                workspace.ClearBatchSelection(); DrainDispatcher(window.Dispatcher);
                var before = list.ActualWidth;
                Assert(single.IsVisible && !batch.IsVisible, "清空选择回到单个单位检查器");
                Capture19(window, $"unit-single-{size.Width}");
                workspace.Units[0].IsBatchSelected = true; DrainDispatcher(window.Dispatcher);
                Assert(single.IsVisible && !batch.IsVisible, "只勾选一个单位仍保留单选页");
                workspace.Units[1].IsBatchSelected = true; DrainDispatcher(window.Dispatcher);
                Assert(!single.IsVisible && batch.IsVisible && Math.Abs(list.ActualWidth - before) < 1, "多选自动切换且不挤出第三栏");
                Capture19(window, $"unit-batch-{size.Width}");
            }
            var common = workspace.CommonBatchFields.First(field => field.IsMixed && field.Definition.Key == "economy.commandPoints");
            Assert(common.EditValue == "" && !common.CanAddDraft, "不同价格保持空输入和多种值提示，不显示虚假零值");
            RunWithDispatcher(workspace.AddCommonBatchFieldAsync(common), window.Dispatcher);
            Assert(workspace.DraftCount == 0, "未输入混合值不能写入草稿");
            var formal = workspace.SelectedUnit!.Unit.Source.SourceFile;
            var original = File.ReadAllBytes(formal);
            common.EditValue = "375";
            RunWithDispatcher(workspace.AddCommonBatchFieldAsync(common), window.Dispatcher);
            Assert(workspace.DraftCount == 2 && File.ReadAllBytes(formal).SequenceEqual(original), "真实批量编辑只写两份语义草稿，正式文件字节不变");
            ((Button)FindWorkspaceName(window, "TopDraftButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DrainDispatcher(window.Dispatcher);
            Assert(vm.IsDraftModule, "顶栏草稿按钮实际进入全局草稿页");
            Capture19(window, "drafts-before-apply");
            var previewTask = workspace.PrepareApplyAsync(); RunWithDispatcher(previewTask, window.Dispatcher);
            var preview = previewTask.GetAwaiter().GetResult();
            Assert(preview.Operations.Count == 2 && preview.FormalFileCount == 1, "实际应用预览准确识别两项修改和一个正式文件");
            RunWithDispatcher(workspace.CommitApplyAsync(preview), window.Dispatcher);
            workspace = vm.UnitWorkspace!;
            Assert(workspace.DraftCount == 0 && workspace.Backups.Count == 1, "事务提交后草稿清空、备份可枚举");
            Assert(workspace.Units.All(unit => unit.Unit.Field("economy.commandPoints")!.DisplayValue == "375"), "重载后读取已应用的两个真实价格");
            var after = File.ReadAllBytes(formal);
            Assert(!after.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "真实写回无BOM");
            RunWithDispatcher(vm.OpenProjectAsync(root), window.Dispatcher);
            Assert(vm.UnitWorkspace!.Units.All(unit => unit.Unit.Field("economy.commandPoints")!.DisplayValue == "375"), "重新打开项目保留正式应用结果");
            RunWithDispatcher(vm.OpenProjectAsync(originalRoot), window.Dispatcher);
            window.Width = 1420; window.Height = 860;
            foreach (var language in new[] { "zh-CN", "en" })
            {
                UiText.Current.SetLanguage(language);
                foreach (var module in vm.Modules.Where(m => m.Key is "units" or "weapons" or "ammo" or "divisions" or "strategic" or "rules" or "drafts").ToArray())
                {
                    vm.SelectedModule = module; DrainDispatcher(window.Dispatcher);
                    Capture19(window, module.Key + "-" + language);
                }
            }
            UiText.Current.SetLanguage("zh-CN");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
