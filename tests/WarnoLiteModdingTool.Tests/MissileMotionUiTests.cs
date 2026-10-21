using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task MissileMotionUi()
    {
        var roots = new List<string>(); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Directory.CreateDirectory("publish/qa-2.11.9/motion");
        var thread = new Thread(() =>
        {
            App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var settings = MotionFixture(); roots.Add(settings); ThemeManager.Initialize(new UiThemeStore(Path.Combine(settings, ".qa/theme.txt")));
                var vm = new App.ViewModels.MainViewModel(new RecentProjectStore(Path.Combine(settings, ".qa/recent.json")));
                var host = new App.MainWindow(vm) { Width = 1480, Height = 1000, ShowActivated = false }; host.Show();
                foreach (var language in new[] { "zh-CN", "en" })
                foreach (var advanced in new[] { false, true })
                {
                    var root = MotionFixture(); roots.Add(root);
                    UiText.Current.SetLanguage(language); vm.AdvancedMode = advanced;
                    ThemeManager.ApplyTheme(language == "en" ? AppTheme.DarkBlue : AppTheme.LightBlue, false);
                    host.Width = advanced ? 1200 : 1480;
                    RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher); vm.SelectedModule = vm.Modules.Single(m => m.Key == "ammo");
                    var ammo = vm.AmmoWorkspace!; ammo.SelectedAmmo = ammo.Ammunition.Single(a => a.Name == A1911);
                    // Both movement fields remain adjacent for a same-window style comparison.
                    ammo.FieldSearch = "速";
                    foreach (var section in ammo.FieldSections) section.IsExpanded = true;
                    var speed = ammo.Fields.Single(f => f.Field.Key == "ammo.speed.projectile");
                    var accel = ammo.Fields.Single(f => f.Field.Key == "ammo.speed.acceleration");
                    RunWithDispatcher(ammo.FeedbackTask, dispatcher);
                    Assert(speed.SafetyText.Contains("1800") && speed.SafetyText.Contains("1600"), "原有实体差异可见");
                    speed.EditValue = "2500"; accel.EditValue = "150"; RunWithDispatcher(ammo.FlushAsync(), dispatcher); RunWithDispatcher(ammo.FeedbackTask, dispatcher);
                    Assert(speed.Draft?.MissileMotion?.Count == 1 && accel.Draft?.MissileMotion?.Count == 1, "真实输入保存三处运动配置");
                    Assert(speed.SafetyText.Contains("2500") && speed.MissileEntity is not null, "关联状态与实体导航");
                    var locations = App.SourceWorkspace.BusinessSourceTargets.Create(vm, speed);
                    Assert(locations.Count == 2 && locations.Any(l => l.Path.Replace('\\', '/') == MotionFile), "复用工作台入口列出Ammo和实体");
                    DrainDispatcher(dispatcher);
                    var content = (FrameworkElement)host.Content;
                    if (language == "en" && advanced) content.LayoutTransform = new ScaleTransform(1.15, 1.15);
                    Snapshot(host, $"ammo-{language}-{advanced}.png"); content.LayoutTransform = Transform.Identity;
                    using var store = new DraftStore(root); RunWithDispatcher(store.LoadAsync(), dispatcher);
                    var prepare = new UnitApplyPlanner().PrepareAsync(root, store.Operations); RunWithDispatcher(prepare, dispatcher);
                    Assert(prepare.Result.Files.Any(f => f.RelativePath == MotionFile), "界面草稿可准备完整实体候选");
                    speed.EditValue = "2000"; RunWithDispatcher(ammo.FlushAsync(), dispatcher); RunWithDispatcher(ammo.FeedbackTask, dispatcher);
                    Assert(speed.Draft is null && accel.Draft is not null, "恢复速度基线只移除速度组，保留加速度");
                    // Weapon scope uses the same controls and preserves the entity linkage.
                    vm.SelectedModule = vm.Modules.Single(m => m.Key == "weapons");
                    var weapon = vm.WeaponWorkspace!; weapon.SelectedUnit = weapon.Units.Single(u => u.InternalName == U1911);
                    weapon.SelectedWeapon = weapon.Weapons.Single(w => w.Name == W1911); weapon.SelectedMount = weapon.Mounts.First();
                    weapon.FieldSearch = "ProjectileSpeedGRU";
                    foreach (var section in weapon.FieldSections) section.IsExpanded = true;
                    var localSpeed = weapon.Fields.Single(f => f.Field.Key == "ammo.speed.projectile");
                    localSpeed.EditValue = "2600"; RunWithDispatcher(weapon.FlushAsync(), dispatcher); RunWithDispatcher(weapon.FeedbackTask, dispatcher);
                    Assert(localSpeed.Draft?.MissileMotion?.All(b => b.Unit == U1911) == true, "武器局部入口保存局部运动");
                    Snapshot(host, $"weapon-{language}-{advanced}.png");
                }
                host.Close(); app.Shutdown(); done.SetResult();
            }
            catch (Exception ex) { app?.Shutdown(); done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await done.Task; Console.WriteLine("PASS missile motion WPF: actual field saves, restore, local scope, entity navigation, languages/modes/themes/widths/scale"); }
        finally { thread.Join(); foreach (var root in roots) DeleteTemporaryFixture(root); }
        static void Snapshot(Window window, string name)
        {
            DrainDispatcher(window.Dispatcher); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine("publish/qa-2.11.9/motion", name)); encoder.Save(file);
        }
    }
}
