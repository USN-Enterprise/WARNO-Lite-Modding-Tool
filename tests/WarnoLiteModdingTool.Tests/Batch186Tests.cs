using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WarnoLiteModdingTool.App;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Diagnostics;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task DraftLifecycle186()
    {
        var roots = new[] { CreateTemporaryFixtureCopy("p2-unit-complete"), CreateTemporaryFixtureCopy("p4-shared"), CreateTemporaryFixtureCopy("p5-division") };
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            using var vm = new MainViewModel(new RecentProjectStore(Path.Combine(roots[0], ".test-settings/recent.json")),
                problemLog: new ApplicationProblemLog(Path.Combine(roots[0], ".test-settings/problems")));
            void Run(Task task) => RunWithDispatcher(task, dispatcher);
            DraftOperation[] Read(string root)
            {
                using var store = new DraftStore(root);
                Run(store.LoadAsync());
                return store.Operations.ToArray();
            }
            try
            {
                Run(vm.OpenProjectAsync(roots[0]));
                var workspace = vm.UnitWorkspace!;
                var field = workspace.Fields.Single(f => f.Key == "economy.commandPoints");
                var owner = field.Unit.Name;
                var before = File.ReadAllBytes(Path.Combine(roots[0], field.Unit.Source.RelativeSourceFile));
                field.EditValue = "276";
                field.EditValue = "277";
                workspace.SelectedUnit = workspace.Units.First(u => u.Unit.Name != owner);
                Run(vm.OpenProjectAsync(roots[1]));
                Assert(Read(roots[0]).Any(o => o.ObjectName == owner && o.FieldKey == field.Key && o.TargetValue == "277"), "立即切换对象及项目保留最后单位输入");
                Assert(File.ReadAllBytes(Path.Combine(roots[0], field.Unit.Source.RelativeSourceFile)).SequenceEqual(before), "保存草稿不修改正式NDF");

                var weapon = vm.WeaponWorkspace!;
                var weaponUnit = weapon.SelectedUnit!.InternalName;
                var weaponField = weapon.Fields.First(f => f.Field.Key.StartsWith("weapon.salves."));
                weaponField.EditValue = "17";
                weapon.SelectedUnit = weapon.Units.First(u => u.InternalName != weaponUnit);
                var ammo = vm.AmmoWorkspace!;
                ammo.SelectedAmmo = ammo.Ammunition.First(a => a.Name == "Ammo_P4_Shared");
                var ammoField = ammo.Fields.Single(f => f.Field.Definition.FieldName == "PhysicalDamages");
                ammoField.EditValue = "19";
                ammo.SelectedAmmo = ammo.Ammunition.First(a => a.Name != "Ammo_P4_Shared");
                Run(vm.OpenProjectAsync(roots[2]));
                var weaponDrafts = Read(roots[1]);
                Assert(weaponDrafts.Any(o => o.FieldKey == weaponField.Field.Key && o.TargetValue == "17" && o.SelectedUnitNames!.SequenceEqual(new[] { weaponUnit })), "旧武器字段保存原作用域，不跟随新选择");
                Assert(weaponDrafts.Any(o => o.ObjectName == "Ammo_P4_Shared" && o.TargetValue == "19"), "旧弹药字段仍保存");

                vm.DivisionWorkspace!.MaxActivationPoints = 21;
                Run(vm.OpenProjectAsync(roots[0]));
                Assert(Read(roots[2]).Any(o => o.TargetKind == DraftTargetKind.DivisionPlan && o.TargetRaw.Contains("21")), "立即切换保存战术师草稿");

                workspace = vm.UnitWorkspace!;
                field = workspace.Fields.Single(f => f.Key == "economy.commandPoints");
                var draftPath = Path.Combine(roots[0], ".warno-editor/draft-v1.json");
                Assert(File.Exists(draftPath), "已有草稿文件用于失败注入");
                File.SetAttributes(draftPath, FileAttributes.ReadOnly);
                field.EditValue = "288";
                var failed = false;
                try { Run(vm.OpenProjectAsync(roots[1])); }
                catch (InvalidOperationException) { failed = true; }
                finally { File.SetAttributes(draftPath, FileAttributes.Normal); }
                Assert(failed && ReferenceEquals(workspace, vm.UnitWorkspace) && field.EditValue == "288" && vm.CanInteract, "保存失败阻止离开并保留输入、恢复交互");
                Run(vm.OpenProjectAsync(roots[1]));
                Assert(Read(roots[0]).Any(o => o.TargetValue == "288"), "失败后可直接重试保存并切换");

                Run(vm.OpenProjectAsync(roots[0]));
                field = vm.UnitWorkspace!.Fields.Single(f => f.Key == "economy.commandPoints");
                field.EditValue = "not-a-number";
                failed = false;
                try { Run(vm.SaveBeforeLeavingAsync()); } catch (InvalidOperationException) { failed = true; }
                Assert(failed && field.EditValue == "not-a-number", "非法输入不能静默丢弃或伪装保存成功");
                field.EditValue = "299";
                var firstSave = vm.SaveBeforeLeavingAsync();
                var repeated = vm.SaveBeforeLeavingAsync();
                try { Run(repeated); } catch (InvalidOperationException) { }
                Run(firstSave);
                Assert(repeated.IsFaulted && Read(roots[0]).Any(o => o.TargetValue == "299"), "重复离开不重入，首个保存仍完成");

                // Revisited fields retain an unsaved value, including after an I/O error.
                workspace = vm.UnitWorkspace!;
                field = workspace.Fields.Single(f => f.Key == "economy.commandPoints");
                var selected = workspace.SelectedUnit;
                field.EditValue = "303";
                workspace.SelectedUnit = workspace.Units.First(u => u != selected);
                workspace.SelectedUnit = selected;
                Assert(ReferenceEquals(field, workspace.Fields.Single(f => f.Key == field.Key)), "返回原对象恢复尚未保存的字段与输入");
                Run(vm.SaveBeforeLeavingAsync());

                WriteStrategicFixture(roots[0], "\n");
                RulesFixture184(roots[0], "\n");
                Run(vm.OpenProjectAsync(roots[0]));
                var income = vm.RulesWorkspace!.Groups.Single(g => g.Group.Definition.Number == 7);
                income.Cells.Single().Value = "302";
                vm.StrategicWorkspace!.SetPawnValue("InitialActionPoint", "16");
                Run(vm.OpenProjectAsync(roots[1]));
                Assert(Read(roots[0]).Any(o => o.TargetKind == DraftTargetKind.GlobalRule && o.FieldKey == "7"), "全模块入口仍保存游戏规则");
                Assert(Read(roots[0]).Any(o => o.TargetKind == DraftTargetKind.StrategicPlan), "全模块入口仍保存将军模式");

                // Let older writes enter the real DraftStore gate before changing again.
                var gated = vm.AmmoWorkspace!;
                gated.SelectedAmmo = gated.Ammunition.First(a => a.Name == "Ammo_P4_Shared");
                var burst = gated.Fields.Single(f => f.Field.Definition.FieldName == "PhysicalDamages");
                burst.EditValue = "31";
                var running = burst.FlushAsync();
                burst.EditValue = "32";
                burst.EditValue = "33";
                Run(vm.SaveBeforeLeavingAsync());
                Run(running);
                Assert(Read(roots[1]).Any(o => o.FieldKey == burst.Field.Key && o.ObjectName == "Ammo_P4_Shared" && o.TargetValue == "33") && burst.EditValue == "33", "已开始写入的旧值不能覆盖连续输入的最终值");
                done.TrySetResult();
            }
            catch (Exception ex) { done.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(40)); }
        finally
        {
            thread.Join(TimeSpan.FromSeconds(5));
            foreach (var root in roots) DeleteTemporaryFixture(root);
        }
    }

    private static Task ColumnAllocation186()
    {
        var min = new[] { 40d, 66d, 66d, 66d };
        var preferred = new[] { 40d, 120d, 85d, 100d };
        var weights = new[] { 0d, 3d, 0d, 2d };
        foreach (var available in new[] { 180d, 300d, 800d })
        {
            var widths = ResponsiveColumns.Allocate(available, min, preferred, weights);
            Assert(widths.Zip(min, (w, m) => w >= m).All(v => v), "窄表格不挤破最小可读宽度");
            Assert(Math.Abs(widths.Sum() - Math.Max(available, min.Sum())) < .01, "列宽利用空间，超出最小宽度时滚动");
            if (available >= preferred.Sum()) Assert(widths.Zip(preferred, (w, p) => w >= p).All(v => v), "足够宽时完整显示所有表头");
        }
        return Task.CompletedTask;
    }

    private static void Verify186Ui(MainViewModel vm, MainWindow window, string root)
    {
        // Create a real HWND offscreen, so window chrome and constrained scrolling are exercised.
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Left = -10000;
        window.Top = -10000;
        window.Show();
        vm.SelectedModule = vm.Modules.Single(m => m.Key == "units");
        foreach (var size in new[] { new Size(1200, 680), new Size(1420, 860), new Size(1920, 1080) })
        {
            window.Width = size.Width; window.Height = size.Height;
            DrainDispatcher(window.Dispatcher);
            var moduleList = (ListBox)window.FindName("ModuleList");
            var scroller = FindVisualChildren<ScrollViewer>(moduleList).First();
            scroller.ScrollToEnd(); DrainDispatcher(window.Dispatcher);
            Assert(scroller.VerticalOffset >= scroller.ScrollableHeight - .1, "侧栏可滚到末尾");
            var title = FindVisualChildren<WindowTitleBar>(window).Single();
            Assert(Math.Abs(title.ActualHeight - 40) < .1, "标题栏实际增高到40 DIP");
            foreach (var grid in FindVisualChildren<DataGrid>(window).Where(g => g.IsVisible && g.ActualWidth > 0))
                Assert(grid.Columns.Where(c => c.Visibility == Visibility.Visible).All(c => c.ActualWidth + .5 >= c.MinWidth && c.MinWidth >= 36), "可见表格列保持最小宽度 " + grid.Name + $" enabled={ResponsiveColumns.GetEnabled(grid)} loaded={grid.IsLoaded} " + string.Join(";", grid.Columns.Select(c => $"{c.Header}:{c.ActualWidth}/{c.MinWidth}")));
            SaveUiSnapshot(window, $"186-layout-{size.Width}.png");
        }
        var scrollTest = new Window { Width = 200, Height = 200, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        var bar = new ScrollBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100000, ViewportSize = 1, Height = 160 };
        scrollTest.Content = bar; scrollTest.Show(); DrainDispatcher(window.Dispatcher);
        var track = (Track)bar.Template.FindName("PART_Track", bar);
        Assert(track.Thumb.ActualHeight >= 28 && track.Thumb.ActualWidth >= 18, "大量行时仍有可见且可拖动滑块");
        bar.Orientation = Orientation.Horizontal; bar.Height = 18; bar.Width = 160;
        DrainDispatcher(window.Dispatcher);
        Assert(track.Thumb.ActualWidth >= 28 && track.Thumb.ActualHeight >= 18, "横向长列表同样保留滑块尺寸");
        scrollTest.Close();

        window.Width = 1420; window.Height = 860;
        vm.AdvancedMode = false;
        vm.SelectedModule = vm.Modules.Single(m => m.Key == "units");
        var batch = FindVisualChildren<Expander>(window).First(e => e.Header is StackPanel panel &&
            FindVisualChildren<TextBlock>(panel).Any(t => t.Text == "批量编辑"));
        batch.IsExpanded = true;
        DrainDispatcher(window.Dispatcher);
        SaveUiSnapshot(window, "186-batch-right-note.png");
        foreach (var language in new[] { "zh-CN", "en" })
        {
            WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(language);
            foreach (var theme in Enum.GetValues<WarnoLiteModdingTool.App.Theming.AppTheme>())
            {
                WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(theme, false);
                DrainDispatcher(window.Dispatcher);
                Assert(FindVisualChildren<WindowTitleBar>(window).Single().Background is not null, "每个主题标题栏有可见背景");
            }
            foreach (var module in vm.Modules.Where(m => m.Key is "units" or "weapons" or "ammo" or "divisions" or "strategic" or "drafts" or "problems").ToArray())
            {
                vm.SelectedModule = module;
                DrainDispatcher(window.Dispatcher);
                foreach (var grid in FindVisualChildren<DataGrid>(window).Where(g => g.IsVisible && g.ActualWidth > 0))
                    Assert(ResponsiveColumns.GetEnabled(grid) && grid.Columns.Where(c => c.Visibility == Visibility.Visible).All(c => c.ActualWidth + .5 >= c.MinWidth), "所有模块的可见表格接入列宽调整");
            }
        }
        WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh-CN");
        WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(WarnoLiteModdingTool.App.Theming.AppTheme.LightBlue, false);
        vm.SelectedModule = vm.Modules.Single(m => m.Key == "drafts");
        foreach (var scale in new[] { 1d, 1.25d, 1.5d })
        {
            ((FrameworkElement)window.Content).LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
            DrainDispatcher(window.Dispatcher);
            SaveUiSnapshot(window, $"186-drafts-scale-{scale}.png");
        }
        ((FrameworkElement)window.Content).LayoutTransform = System.Windows.Media.Transform.Identity;
        window.WindowState = WindowState.Maximized;
        DrainDispatcher(window.Dispatcher);
        Assert(window.WindowState == WindowState.Maximized, "标题栏支持最大化");
        SystemCommands.RestoreWindow(window);
        DrainDispatcher(window.Dispatcher);
        Assert(window.WindowState == WindowState.Normal, "标题栏支持还原");

        // Close failure stays visible and can be retried without retyping.
        var draftPath = Path.Combine(root, ".warno-editor/draft-v1.json");
        File.SetAttributes(draftPath, FileAttributes.ReadOnly);
        var pendingField = vm.UnitWorkspace!.Fields.Single(f => f.Key == "economy.commandPoints");
        pendingField.EditValue = "310";
        window.Close();
        RunWithDispatcher(Task.Delay(150), window.Dispatcher);
        File.SetAttributes(draftPath, FileAttributes.Normal);
        Assert(window.IsVisible && pendingField.EditValue == "310", "真实关闭保存失败时窗口与输入保留");

        var field = vm.UnitWorkspace!.Fields.Single(f => f.Key == "economy.commandPoints");
        field.EditValue = "311";
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        RunWithDispatcher(WaitClosed(), window.Dispatcher);
        using var store = new DraftStore(root);
        RunWithDispatcher(store.LoadAsync(), window.Dispatcher);
        Assert(store.Operations.Any(o => o.FieldKey == field.Key && o.TargetValue == "311"), "实际Closing保存最后输入后才关闭");
        async Task WaitClosed() { while (!closed) await Task.Delay(10); }
    }
}
