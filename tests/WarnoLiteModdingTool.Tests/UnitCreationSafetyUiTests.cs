using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Advanced;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static async Task UnitCreationSafetyUi()
    {
        var root = CreationSafetyFixture();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                ThemeManager.Initialize(new UiThemeStore(Path.Combine(root, ".qa/theme.txt")));
                var vm = new MainViewModel(new RecentProjectStore(Path.Combine(root, ".qa/recent.json")));
                var owner = new App.MainWindow(vm) { Width = 1200, Height = 900, ShowActivated = false }; owner.Show();
                RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher);
                foreach (var language in new[] { "zh-CN", "en" }) foreach (var advanced in new[] { false, true })
                {
                    UiText.Current.SetLanguage(language); vm.AdvancedMode = advanced;
                    ThemeManager.ApplyTheme(language == "en" ? AppTheme.DarkBlue : AppTheme.LightBlue, false);
                    var shot = $"{language}-{(advanced ? "professional" : "basic")}";
                    var before = Directory.GetFiles(Path.Combine(root, "GameData"), "*.ndf", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
                    // Drive the actual modal wizard while MainViewModel runs its normal candidate validation/save.
                    Exception? wizardError = null;
                    var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.ApplicationIdle, (_, _) => { }, dispatcher);
                    timer.Tick += (_, _) =>
                    {
                        var wizard = app.Windows.OfType<UnitCreationWindow>().FirstOrDefault();
                        if (wizard is null) return;
                        timer.Stop();
                        try
                        {
                            wizard.Width = advanced ? 1080 : 950;
                            if (advanced) ((FrameworkElement)wizard.Content).LayoutTransform = new ScaleTransform(1.15, 1.15);
                            FindVisualChildren<TabControl>(wizard).Single().SelectedIndex = 4;
                            wizard.UpdateLayout(); DrainDispatcher(dispatcher); Capture(wizard, "wizard-" + shot);
                            FindVisualChildren<Button>(wizard).Single(b => b.Content?.ToString() == UiText.T("加入草稿")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch (Exception ex) { wizardError = ex; wizard.Close(); }
                    };
                    timer.Start(); RunWithDispatcher(vm.CreateUnitAsync(owner), dispatcher); timer.Stop();
                    if (wizardError is not null) throw wizardError;
                    using var store = new DraftStore(root); RunWithDispatcher(store.LoadAsync(), dispatcher);
                    var operation = store.Operations.Last(o => o.TargetKind == DraftTargetKind.UnitCreate);
                    var task = new UnitTransactionService().PrepareApplyAsync(root, [operation]); RunWithDispatcher(task, dispatcher);
                    var preview = task.GetAwaiter().GetResult();
                    Assert(preview.ValidationMessages.Any(m => m.Contains("继承母版")), "actual wizard draft validated and saved with inherited reference notice");
                    Assert(before.All(p => p.Value.SequenceEqual(File.ReadAllBytes(p.Key))), "wizard/preview did not write formal NDF");
                    var diff = new DiffWindow(preview) { Owner = owner, Width = advanced ? 1200 : 1000, Height = 800, ShowActivated = false };
                    diff.Show(); diff.UpdateLayout(); DrainDispatcher(dispatcher);
                    var notice = language == "en" ? "Inherited unverified" : "继承母版";
                    var summary = FindVisualChildren<TextBox>(diff).Single(b => b.Text.Contains(notice));
                    if (language == "en") Assert(!summary.Text.Contains("继承母版"), "new notice translated in English preview");
                    summary.ScrollToLine(summary.Text[..summary.Text.IndexOf(notice, StringComparison.Ordinal)].Count(c => c == '\n'));
                    diff.UpdateLayout(); DrainDispatcher(dispatcher); Capture(diff, "preview-" + shot); diff.Close();
                }
                owner.Close(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception ex) { app?.Shutdown(); completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task; Console.WriteLine("PASS creation safety actual wizard/save/preview; bilingual modes/themes/widths and 115% layout"); }
        finally { thread.Join(); DeleteTemporaryFixture(root); }

        static void Capture(Window window, string name)
        {
            const string output = "publish/qa-2.11.10/creation"; Directory.CreateDirectory(output);
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
        }
    }
}
