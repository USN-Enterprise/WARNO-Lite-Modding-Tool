using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    // Explicit optional verification only. Ordinary regression never requires user data.
    private static async Task MissileRepairRealUi(string root, string output)
    {
        root = Path.GetFullPath(root);
        var allowed = Path.GetFullPath(Path.Combine("tmp", "opfor-2118")) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Disposable fixture roots only");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            App.App? app = null;
            try
            {
                var stages = new List<object>(); var watch = Stopwatch.StartNew();
                void Record(string stage)
                {
                    var process = Process.GetCurrentProcess();
                    stages.Add(new { Stage = stage, Seconds = watch.Elapsed.TotalSeconds, WorkingSet = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64, PeakWorkingSet = process.PeakWorkingSet64 });
                    Console.WriteLine($"{stage}: {watch.Elapsed.TotalSeconds:F2}s; working set {process.WorkingSet64 / 1048576} MiB; private {process.PrivateMemorySize64 / 1048576} MiB"); watch.Restart();
                }
                app = new(false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var vm = new App.ViewModels.MainViewModel(new RecentProjectStore(Path.Combine(root, ".qa/recent-real-ui.json")));
                var host = new App.MainWindow(vm) { Width = 1480, Height = 960, ShowActivated = false }; host.Show();
                Record("host"); RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher); Record("opened-project");
                var file = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/MissileDescriptors.ndf"); var before = File.ReadAllBytes(file);
                var window = new MissileRepairWindow(vm) { Owner = host, ShowActivated = false }; window.Show();
                var responsiveness = Stopwatch.StartNew(); var lastTick = responsiveness.Elapsed; var longestGap = TimeSpan.Zero;
                var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                heartbeat.Tick += (_, _) => { var now = responsiveness.Elapsed; if (now - lastTick > longestGap) longestGap = now - lastTick; lastTick = now; }; heartbeat.Start();
                void Click(string tag) => FindVisualChildren<Button>(window).Single(b => Equals(b.Tag, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                async Task Idle()
                {
                    var timeout = Stopwatch.StartNew();
                    while (window.IsBusy && timeout.Elapsed < TimeSpan.FromSeconds(90)) await Task.Delay(25);
                    Assert(!window.IsBusy, "真实副本窗口操作在90秒以内完成");
                }
                Click("missile-repair-scan"); RunWithDispatcher(Idle(), dispatcher); Record("scanned");
                var table = FindVisualChildren<DataGrid>(window).Single(g => Equals(g.Tag, "missile-repair-table"));
                Assert(table.Items.Cast<MissileRepairWindow.Row>().Count(r => r.CanRepair) == 13, "真实窗口扫描13项");
                Click("missile-repair-preview"); RunWithDispatcher(Idle(), dispatcher); Record("previewed");
                Assert(FindVisualChildren<Button>(window).Single(b => Equals(b.Tag, "missile-repair-save")).IsEnabled, "真实窗口预览成功");
                Click("missile-repair-save"); RunWithDispatcher(Idle(), dispatcher); Record("saved-drafts");
                DrainDispatcher(dispatcher); heartbeat.Stop();
                Assert(longestGap < TimeSpan.FromSeconds(2), "真实修复窗口计算期间保持Dispatcher响应");
                Assert(vm.SourceDraftStore.Operations.Count(Core.Weapons.MissileDebugRepair.IsRepair) == 13 && File.ReadAllBytes(file).SequenceEqual(before), "真实窗口保存13项且正式源文件保持");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output, JsonSerializer.Serialize(new { Version = "2.11.8", Result = "passed", LongestDispatcherGapMs = longestGap.TotalMilliseconds, Stages = stages }, new JsonSerializerOptions { WriteIndented = true }));
                window.Close(); host.Close(); app.Shutdown(); done.SetResult();
            }
            catch (Exception ex) { app?.Shutdown(); done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task; thread.Join(); Console.WriteLine("PASS full-size repair window scan, preview and draft save without formal writes");
    }
}
