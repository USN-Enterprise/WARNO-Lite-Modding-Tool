using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task ApBindingUi()
    {
        var roots = new List<string>(); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Directory.CreateDirectory("publish/qa-2.11.9/ap");
        var thread = new Thread(() =>
        {
            App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var settings = ApFixture(); roots.Add(settings); ThemeManager.Initialize(new UiThemeStore(Path.Combine(settings, ".qa/theme.txt")));
                var vm = new App.ViewModels.MainViewModel(new RecentProjectStore(Path.Combine(settings, ".qa/recent.json")));
                var host = new App.MainWindow(vm) { Width = 1480, Height = 950, ShowActivated = false }; host.Show();
                T Find<T>(Window w, string tag) where T : FrameworkElement => FindVisualChildren<T>(w).Single(e => Equals(e.Tag, tag));
                void Click(Window w, string tag) { Find<Button>(w, tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); DrainDispatcher(dispatcher); }
                async Task Until(Func<bool> condition)
                {
                    for (var i = 0; i < 1000 && !condition(); i++) await Task.Delay(10);
                    Assert(condition(), "AP UI异步操作完成");
                }
                foreach (var language in new[] { "zh-CN", "en" })
                foreach (var advanced in new[] { false, true })
                {
                    var root = ApFixture(missing: true, noStairs: true); roots.Add(root);
                    UiText.Current.SetLanguage(language); vm.AdvancedMode = advanced;
                    ThemeManager.ApplyTheme(language == "en" ? AppTheme.DarkBlue : AppTheme.LightBlue, false);
                    host.Width = advanced ? 1200 : 1480;
                    RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher); vm.SelectedModule = vm.Modules.Single(m => m.Key == "ammo");
                    Assert(FindVisualChildren<TextBlock>(host).Any(t => t.Text == typeof(App.MainWindow).Assembly.GetName().Version!.ToString(3)), "主窗口版本号同步");
                    var ammo = vm.AmmoWorkspace!; ammo.SelectedAmmo = ammo.Ammunition.Single(a => a.Name == A1911); ammo.FieldSearch = "Arme";
                    var family = ammo.Fields.Single(f => f.Field.Key == ApBinding.FamilyKey);
                    family.EditValue = "DamageFamily_ap"; RunWithDispatcher(ammo.FlushAsync(), dispatcher); RunWithDispatcher(ammo.FeedbackTask, dispatcher);
                    Assert(family.Draft?.ApBindings?.Count == 1 && family.SafetyText.Contains("175"), "真实家族输入保存完整配套并显示状态");
                    DrainDispatcher(dispatcher); foreach (var expander in FindVisualChildren<Expander>(host)) expander.IsExpanded = true;
                    Snapshot(host, $"family-{language}-{advanced}.png");
                    using var store = new DraftStore(root); RunWithDispatcher(store.LoadAsync(), dispatcher);
                    var load = LoadP4Async(root); RunWithDispatcher(load, dispatcher); var data = load.Result.Weapons;
                    var window = new DamageAmmoWindow(data.DamageSource!(), data, store, [data.Ammo(A1911)!], DraftEditScope.AllReferences, [], () => { })
                        { Owner = host, Width = language == "en" ? 960 : 1120, Height = 850, ShowActivated = false };
                    if (language == "en") ((FrameworkElement)window.Content).LayoutTransform = new ScaleTransform(1.15, 1.15);
                    window.Show(); DrainDispatcher(dispatcher);
                    Assert(Find<TextBox>(window, "damage-distance").Text == "175.0", "未应用家族草稿即可编辑联动距离");
                    Click(window, "damage-ap-repair"); RunWithDispatcher(Until(() => Find<Button>(window, "damage-ap-repair").IsEnabled), dispatcher);
                    Assert(store.Operations.Count == 1, "补齐重复操作不堆积配套草稿");
                    Find<TextBox>(window, "damage-distance").Text = "500";
                    Click(window, "damage-distance-preview"); Click(window, "damage-distance-save");
                    RunWithDispatcher(Until(() => store.Operations.Any(o => o.TargetKind == DraftTargetKind.DamageDistance)), dispatcher);
                    Assert(DamageDistance.Read(store.Operations.Single(o => o.TargetKind == DraftTargetKind.DamageDistance)).Distance == "500", "实际预览与保存按钮调整新建规则");
                    Snapshot(window, $"distance-{language}-{advanced}.png");
                    var prepare = new UnitApplyPlanner().PrepareAsync(root, store.Operations); RunWithDispatcher(prepare, dispatcher);
                    Assert(prepare.Result.Operations.Any(ApBinding.Has), "界面草稿可准备完整应用");
                    window.Close();
                }
                host.Close(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception ex) { app?.Shutdown(); completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task; Console.WriteLine("PASS AP WPF family save, repair, pending distance edits, drafts and bilingual mode/theme/width/scale snapshots"); }
        finally { thread.Join(); foreach (var root in roots) DeleteTemporaryFixture(root); }
        static void Snapshot(Window window, string name)
        {
            DrainDispatcher(window.Dispatcher); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine("publish/qa-2.11.9/ap", name)); encoder.Save(file);
        }
    }
}
