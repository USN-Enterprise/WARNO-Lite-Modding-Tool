using System.IO;
using System.Windows;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static void Verify1915Ui(MainViewModel main, Window window, string returnRoot)
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        var oldPrepare = main.PrepareNamesAsync; var preparations = 0;
        var width = window.Width; var height = window.Height;
        try
        {
            main.PrepareNamesAsync = () => { preparations++; return Task.CompletedTask; };
            RunWithDispatcher(main.OpenProjectAsync(root), window.Dispatcher);
            foreach (var english in new[] { false, true }) foreach (var advanced in new[] { false, true })
            {
                UiText.Current.SetLanguage(english ? "en" : "zh-CN"); main.AdvancedMode = advanced;
                ThemeManager.ApplyTheme(english ? AppTheme.DarkBlue : AppTheme.LightBlue, false);
                window.Width = advanced ? 1200 : 1420; window.Height = 950;
                main.SelectedModule = main.Modules.Single(m => m.Key == "units");
                var unit = main.UnitWorkspace!;
                unit.SelectedUnit = unit.Units.Last(); unit.TextFilter = "Test";
                var name = unit.SelectedUnit.InternalName;
                unit.Units[0].IsBatchSelected = true;
                var field = unit.Fields.Single(f => f.Key == "survival.health");
                field.EditValue = (decimal.Parse(field.EditValue) + 1).ToString();
                RunWithDispatcher(unit.FlushAsync(), window.Dispatcher);
                foreach (var section in unit.FieldSections) section.IsExpanded = section.Title == unit.FieldSections.First().Title;
                var expanded = unit.FieldSections.ToDictionary(s => s.Title, s => s.IsExpanded);
                DrainDispatcher(window.Dispatcher);
                var inspector = (System.Windows.Controls.Border)FindWorkspaceName(window, "SingleUnitInspector");
                var scroll = FindVisualChildren<System.Windows.Controls.ScrollViewer>(inspector).First();
                scroll.ScrollToVerticalOffset(120); DrainDispatcher(window.Dispatcher);
                var offset = scroll.VerticalOffset;
                Assert(offset > 0, "实际滚动检查器");
                SaveUiSnapshot(window, $"1915-before-{english}-{advanced}.png");
                var preview = unit.PrepareApplyAsync(); RunWithDispatcher(preview, window.Dispatcher);
                var commit = unit.CommitApplyAsync(preview.Result); RunWithDispatcher(commit, window.Dispatcher); DrainDispatcher(window.Dispatcher);
                Assert(commit.Result.Succeeded && commit.Result.Warnings.Count == 0, "窗口局部刷新完成：" + string.Join(";", commit.Result.Warnings));
                Assert(preparations == 1, "应用后不重新打开项目或重载原版名称");
                Assert(main.SelectedModule?.Key == "units" && main.UnitWorkspace!.SelectedUnit?.InternalName == name, "保持页面与当前对象");
                Assert(main.UnitWorkspace!.TextFilter == "Test" && main.UnitWorkspace.BatchSelectedCount == 1, "保持搜索和勾选");
                Assert(main.UnitWorkspace.FieldSections.All(s => s.IsExpanded == expanded.GetValueOrDefault(s.Title)), "保持字段展开状态");
                Assert(main.LastRefreshReadFiles == 1, "单字段应用后只回读一个正式文件");
                Assert(Math.Abs(scroll.VerticalOffset - offset) < 1, "应用后保持检查器滚动位置");
                SaveUiSnapshot(window, $"1915-after-{english}-{advanced}.png");
                RunWithDispatcher(main.CacheSaveTask, window.Dispatcher);
            }
            var stale = main.UnitWorkspace!;
            var changedField = stale.Fields.Single(f => f.Key == "survival.health"); changedField.EditValue = "25";
            RunWithDispatcher(stale.FlushAsync(), window.Dispatcher);
            var prepared = stale.PrepareApplyAsync(); RunWithDispatcher(prepared, window.Dispatcher);
            void ConcurrentEdit() => File.AppendAllText(stale.SelectedUnit!.Unit.Source.SourceFile, "\n// external edit after commit\n");
            main.WorkspaceRefreshing += ConcurrentEdit;
            try
            {
                var result = stale.CommitApplyAsync(prepared.Result); RunWithDispatcher(result, window.Dispatcher);
                Assert(result.Result.Succeeded && result.Result.Warnings.Count > 0 && main.UnitWorkspace!.RefreshRequired, "已提交但刷新失败明确区分并锁定旧编辑器");
            }
            finally { main.WorkspaceRefreshing -= ConcurrentEdit; }
            RunWithDispatcher(main.OpenProjectAsync(root), window.Dispatcher);
            Assert(!main.UnitWorkspace!.RefreshRequired && main.UnitWorkspace.DraftCount == 0, "重新打开恢复编辑且不重复生成已应用草稿");
        }
        finally
        {
            main.PrepareNamesAsync = oldPrepare;
            UiText.Current.SetLanguage("zh-CN"); main.AdvancedMode = false; window.Width = width; window.Height = height;
            RunWithDispatcher(main.CacheSaveTask, window.Dispatcher);
            RunWithDispatcher(main.OpenProjectAsync(returnRoot), window.Dispatcher);
            DeleteTemporaryFixture(root);
        }
    }
}
