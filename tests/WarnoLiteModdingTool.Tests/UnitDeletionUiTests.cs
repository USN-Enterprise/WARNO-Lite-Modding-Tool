using System.IO;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task DeletionUi()
    {
        var root = DeletionFixture(); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                ThemeManager.Initialize(new UiThemeStore(Path.Combine(root, ".qa/theme.txt")));
                var vm = new MainViewModel(new RecentProjectStore(Path.Combine(root, ".qa/recent.json"))) { AdvancedMode = true };
                var owner = new App.MainWindow(vm) { Width = 1400, Height = 900, ShowActivated = false }; owner.Show();
                RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher);
                var loading = LoadP4Async(root); RunWithDispatcher(loading, dispatcher); var (_, units, _) = loading.Result;
                vm.UnitWorkspace!.SelectedUnit = vm.UnitWorkspace.Units.First();
                var graph = new UnitProjectGraph(root, deletionScope: true);
                Directory.CreateDirectory("publish/qa-deletion");
                foreach (var language in new[] { "zh-CN", "en" })
                foreach (var theme in Enum.GetValues<AppTheme>())
                {
                    UiText.Current.SetLanguage(language); ThemeManager.ApplyTheme(theme, false); vm.AdvancedMode = language == "zh-CN";
                    var dialog = new UnitDeletionWindow(units.Units.First(), units, graph, _ => Task.CompletedTask) { Owner = owner, ShowActivated = false };
                    dialog.Show(); dialog.Force.IsChecked = vm.AdvancedMode;
                    if (language == "en") { dialog.Width = 720; ((FrameworkElement)dialog.Content).LayoutTransform = new ScaleTransform(1.25, 1.25); }
                    dialog.UpdateLayout(); DrainDispatcher(dispatcher);
                    TestAssert.Equal(vm.AdvancedMode ? Visibility.Visible : Visibility.Collapsed, dialog.Force.Visibility, "force only professional");
                    TestAssert.True(FindVisualChildren<Button>(dialog).All(b => b.ActualHeight > 0), "buttons laid out");
                    DeletionScreenshot(dialog, $"{language}-{theme}.png"); dialog.Close();
                }
                UiText.Current.SetLanguage("en"); vm.AdvancedMode = true; ThemeManager.ApplyTheme(AppTheme.DarkBlue, false);
                using var store = new DraftStore(root); RunWithDispatcher(store.LoadAsync(), dispatcher);
                var service = new UnitTransactionService();
                var window = new UnitDeletionWindow(units.Units.First(), units, graph, async op => { await service.PrepareApplyAsync(root, [op]); await store.UpsertAsync(op); }) { Owner = owner };
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                Exception? failure = null;
                timer.Tick += async (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        window.Force.IsChecked = true; window.Width = 720; ((FrameworkElement)window.Content).LayoutTransform = new ScaleTransform(1.25, 1.25); window.UpdateLayout(); DeletionScreenshot(window, "en-force-narrow.png"); await window.SaveAsync();
                        TestAssert.Equal(0, store.Operations.Count, "force requires visible acknowledgement");
                        window.Acknowledge.IsChecked = true; await window.SaveAsync();
                        TestAssert.Equal(1, store.Operations.Count, "actual dialog saves deletion draft");
                    }
                    catch (Exception ex) { failure = ex; window.Close(); }
                    finally { if (window.IsVisible) window.Close(); }
                };
                timer.Start(); window.ShowDialog(); if (failure is not null) throw failure;
                var preparing = service.PrepareApplyAsync(root, store.Operations); RunWithDispatcher(preparing, dispatcher);
                var commit = service.CommitApplyAsync(preparing.Result, store); RunWithDispatcher(commit, dispatcher);
                TestAssert.True(commit.Result.Succeeded && commit.Result.Warnings.Count > 0, "UI force draft applied with warnings");
                RunWithDispatcher(service.CommitRestoreAsync(service.PrepareRestore(root, commit.Result.BackupId)), dispatcher);
                RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher); vm.UnitWorkspace!.SelectedUnit = vm.UnitWorkspace.Units.First();
                var actualTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                actualTimer.Tick += async (_, _) =>
                {
                    var actual = app.Windows.OfType<UnitDeletionWindow>().FirstOrDefault(); if (actual is null) return;
                    actualTimer.Stop(); actual.Force.IsChecked = true; actual.Acknowledge.IsChecked = true;
                    await actual.SaveAsync(); if (actual.IsVisible) { failure = new Exception("Inspector deletion did not save"); actual.Close(); }
                };
                actualTimer.Start(); RunWithDispatcher(vm.UnitWorkspace.DeleteNewUnitAsync(owner), dispatcher); if (failure is not null) throw failure;
                TestAssert.True(vm.UnitWorkspace.SelectedPendingDelete, "inspector action saves force draft");
                owner.UpdateLayout(); DrainDispatcher(dispatcher); DeletionScreenshot(owner, "unit-inspector-pending.png");
                RunWithDispatcher(vm.UnitWorkspace.DeleteNewUnitAsync(owner), dispatcher); TestAssert.True(!vm.UnitWorkspace.SelectedPendingDelete, "inspector undo restores editing");
                var locate = new UnitDeletionWindow(units.Units.First(), units, new(root, deletionScope: true), _ => Task.CompletedTask) { Owner = owner };
                var locateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                locateTimer.Tick += (_, _) => { locateTimer.Stop(); FindVisualChildren<Button>(locate).First(b => Equals(b.Content, UiText.T("在工作台定位"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
                locateTimer.Start(); locate.ShowDialog(); TestAssert.True(locate.OpenRequested is not null, "locate returns file and offset");
                RunWithDispatcher((Task)typeof(App.MainWindow).GetMethod("OpenSourceReferenceAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(owner, [locate.OpenRequested!.Value.Path, locate.OpenRequested.Value.Offset])!, dispatcher);
                RunWithDispatcher(vm.SaveBeforeLeavingAsync(), dispatcher);
                foreach (var extra in app.Windows.OfType<Window>().Where(w => w != owner).ToArray()) extra.Close();
                DrainDispatcher(dispatcher); owner.Close(); DrainDispatcher(dispatcher); app.Shutdown(); dispatcher.InvokeShutdown(); completion.SetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task; Console.WriteLine("Deletion WPF matrix and save/apply/restore/location PASS"); }
        finally { thread.Join(); DeleteTemporaryFixture(root); }
    }
    private static void DeletionScreenshot(Window window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine("publish/qa-deletion", name)); encoder.Save(file);
    }
}
