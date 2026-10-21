using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task MissileDebugRepairUi()
    {
        var roots = new List<string>(); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Directory.CreateDirectory("publish/qa-2.11.9/repair");
        var thread = new Thread(() =>
        {
            App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var settings = BrokenMissileFixture(); roots.Add(settings); ThemeManager.Initialize(new UiThemeStore(Path.Combine(settings, ".qa/theme.txt")));
                var vm = new App.ViewModels.MainViewModel(new RecentProjectStore(Path.Combine(settings, ".qa/recent.json")));
                var host = new App.MainWindow(vm) { Width = 1480, Height = 960, ShowActivated = false }; host.Show();
                async Task Until(Func<bool> predicate)
                {
                    for (var i = 0; i < 1000 && !predicate(); i++) await Task.Delay(10);
                    Assert(predicate(), "修复界面异步操作完成");
                }
                void Click(Window window, string tag) => FindVisualChildren<Button>(window).Single(b => Equals(b.Tag, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (var language in new[] { "zh-CN", "en" })
                foreach (var advanced in new[] { false, true })
                {
                    var root = BrokenMissileFixture(); roots.Add(root);
                    foreach (var file in Directory.GetFiles(root, "*.ndf", SearchOption.AllDirectories))
                        File.WriteAllText(file, File.ReadAllText(file).Replace("TestFlight", "Descriptor_Missile_AA_AIM9L_Sidewinder").Replace("keep flight visual", "Missile_AA_AIM9L_Sidewinder"), new System.Text.UTF8Encoding(false));
                    UiText.Current.SetLanguage(language); vm.AdvancedMode = advanced;
                    ThemeManager.ApplyTheme(language == "en" ? AppTheme.DarkBlue : AppTheme.LightBlue, false);
                    RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher);
                    var popup = (System.Windows.Controls.Primitives.Popup)host.FindName("ModToolsPopup");
                    vm.IsModToolsOpen = true; DrainDispatcher(dispatcher);
                    Assert(FindVisualChildren<Button>(popup.Child).Single(b => Equals(b.Tag, "missile-repair-entry")).IsEnabled, "两模式Mod工具修复入口可用");
                    vm.IsModToolsOpen = false;
                    var window = new MissileRepairWindow(vm) { Owner = host, Width = advanced ? 850 : 1180, Height = 830, ShowActivated = false }; window.Show();
                    Click(window, "missile-repair-scan"); Click(window, "missile-repair-cancel");
                    RunWithDispatcher(Until(() => !window.IsBusy), dispatcher);
                    Assert(vm.SourceDraftStore.Operations.Count == 0, "取消扫描不保存半套草稿");
                    Click(window, "missile-repair-scan"); RunWithDispatcher(Until(() => !window.IsBusy), dispatcher);
                    var table = FindVisualChildren<DataGrid>(window).Single(g => Equals(g.Tag, "missile-repair-table"));
                    var rows = table.Items.Cast<MissileRepairWindow.Row>().ToArray();
                    Assert(rows.Length == 2 && rows.Count(r => r.CanRepair && r.Selected) == 1, "原实体保留，仅勾选可修复副本");
                    table.SelectedItem = rows.Single(r => r.CanRepair);
                    foreach (var expander in FindVisualChildren<Expander>(window)) expander.IsExpanded = true;
                    Click(window, "missile-repair-preview"); RunWithDispatcher(Until(() => !window.IsBusy), dispatcher);
                    var save = FindVisualChildren<Button>(window).Single(b => Equals(b.Tag, "missile-repair-save"));
                    Assert(save.IsEnabled, "预览成功方可加入草稿");
                    rows.Single(r => r.CanRepair).Selected = false; Assert(!save.IsEnabled, "更改勾选使旧预览失效");
                    rows.Single(r => r.CanRepair).Selected = true;
                    Click(window, "missile-repair-preview"); RunWithDispatcher(Until(() => !window.IsBusy), dispatcher);
                    if (language == "en" && advanced) ((FrameworkElement)window.Content).LayoutTransform = new ScaleTransform(1.15, 1.15);
                    Snapshot(window, $"repair-{language}-{advanced}.png");
                    var before = File.ReadAllBytes(Path.Combine(root, MotionFile));
                    Click(window, "missile-repair-save"); RunWithDispatcher(Until(() => !window.IsBusy), dispatcher);
                    Assert(vm.SourceDraftStore.Operations.Single().TargetKind == Core.Drafts.DraftTargetKind.MissileDebugRepair && !save.IsEnabled, "实际按钮保存修复草稿并禁用过期预览");
                    Assert(File.ReadAllBytes(Path.Combine(root, MotionFile)).SequenceEqual(before), "界面保存不改正式文件");
                    Assert(vm.UnitWorkspace!.CanApply, "修复接入统一草稿总览应用");
                    window.Close();
                    var service = new UnitTransactionService(); var prepare = service.PrepareApplyAsync(root, vm.SourceDraftStore.Operations); RunWithDispatcher(prepare, dispatcher);
                    var commit = service.CommitApplyAsync(prepare.Result, vm.SourceDraftStore); RunWithDispatcher(commit, dispatcher);
                    Assert(commit.Result.Succeeded && !MissileDebugRepair.Scan(root).Rows.Any(r => r.CanRepair), "真实界面草稿提交后重复扫描无修复");
                    RunWithDispatcher(vm.RefreshSourceProjectAsync(prepare.Result), dispatcher);
                    Assert(!vm.UnitWorkspace!.CanApply, "应用后刷新清理草稿状态");
                }
                host.Close(); app.Shutdown(); done.SetResult();
            }
            catch (Exception ex) { app?.Shutdown(); done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await done.Task; Console.WriteLine("PASS missile repair WPF menu, scanning, selection invalidation, preview, save, commit, refresh and bilingual/mode/theme/width/scale snapshots"); }
        finally { thread.Join(); foreach (var root in roots) DeleteTemporaryFixture(root); }
        static void Snapshot(Window window, string name)
        {
            DrainDispatcher(window.Dispatcher); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine("publish/qa-2.11.9/repair", name)); encoder.Save(file);
        }
    }
}
