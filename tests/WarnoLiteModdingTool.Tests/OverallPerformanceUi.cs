using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Diagnostics;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.App.ViewModels.Units;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static async Task OverallPerformanceUi(string root, string output, int rounds)
    {
        root = Path.GetFullPath(root); output = Path.GetFullPath(output);
        if (!File.Exists(Path.Combine(root, ".performance-synthetic.json")))
            throw new ArgumentException("Only an explicitly generated synthetic sample may be edited.");
        Directory.CreateDirectory(output);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new App.App(false) { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            ThemeManager.Initialize(new UiThemeStore(Path.Combine(output, "theme.txt")));
            UiText.Current.SetLanguage("zh-CN");
            using var main = new MainViewModel(new RecentProjectStore(Path.Combine(output, "recent.json")),
                new WarnoModsRootStore(Path.Combine(output, "roots.txt")), problemLog: new ApplicationProblemLog(Path.Combine(output, "problems")))
            {
                OpenLoadCache = p => ProjectLoadCache.Open(p, path: Path.Combine(output, "cache.zip")),
                PrepareNamesAsync = () => Task.CompletedTask
            };
            var host = new App.MainWindow(main) { Width = 1420, Height = 950, ShowActivated = false };
            var clock = Stopwatch.StartNew(); var beats = new List<long>(); long last = 0;
            using var memory = new PerformanceMemory();
            var actionStage = "open"; var previousGen2 = GC.CollectionCount(2);
            var heartbeat = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
            heartbeat.Tick += (_, _) =>
            {
                var now = clock.ElapsedMilliseconds; beats.Add(now - last);
                if (now - last > 250) Report(new { stage = "heartbeat-gap", action = actionStage, gapMs = now - last,
                    status = main.StatusText, gen2 = GC.CollectionCount(2) - previousGen2 });
                previousGen2 = GC.CollectionCount(2); last = now;
            };
            void StartBeats() { beats.Clear(); last = clock.ElapsedMilliseconds; previousGen2 = GC.CollectionCount(2); heartbeat.Start(); }
            void Report(object value)
            {
                var row = JsonSerializer.Serialize(value);
                File.AppendAllText(Path.Combine(output, "ui.jsonl"), row + "\n"); Console.WriteLine(row);
            }
            var operations = new Dictionary<DispatcherOperation, long>();
            dispatcher.Hooks.OperationStarted += (_, e) => operations[e.Operation] = clock.ElapsedMilliseconds;
            dispatcher.Hooks.OperationCompleted += (_, e) =>
            {
                if (!operations.Remove(e.Operation, out var started) || clock.ElapsedMilliseconds - started < 150) return;
                var callback = typeof(DispatcherOperation).GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(e.Operation) as Delegate;
                Report(new { stage = "dispatcher-slow", action = actionStage, ms = clock.ElapsedMilliseconds - started,
                    priority = e.Operation.Priority.ToString(), callback = callback?.Method.ToString(), owner = callback?.Method.DeclaringType?.FullName });
            };
            async Task Layout() => await dispatcher.InvokeAsync(host.UpdateLayout, DispatcherPriority.ContextIdle);
            async Task Until(Func<bool> condition, int timeout = 15000)
            {
                var timer = Stopwatch.StartNew();
                while (!condition() && timer.ElapsedMilliseconds < timeout) await Task.Delay(5);
                Assert(condition(), "UI异步操作在限时内完成");
            }
            void Snapshot(string name)
            {
                host.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, name + ".png")); png.Save(stream);
            }
            host.Loaded += async (_, _) =>
            {
                try
                {
                    StartBeats(); var timer = Stopwatch.StartNew();
                    await main.OpenProjectAsync(root); await Layout();
                    Assert(main.UnitWorkspace is not null, main.StatusText);
                    Report(new { stage = "open", ms = timer.ElapsedMilliseconds, phases = main.LastLoadTimings });
                    actionStage = "selection";
                    var selections = new List<long>(); var selectionFeedback = new List<long>(); var selectionSetters = new List<long>();
                    for (var i = 0; i < 30; i++)
                    {
                        timer.Restart(); main.UnitWorkspace!.SelectedUnit = main.UnitWorkspace.Units[i];
                        selectionSetters.Add(timer.ElapsedMilliseconds);
                        await dispatcher.InvokeAsync(host.UpdateLayout, DispatcherPriority.Render);
                        selectionFeedback.Add(timer.ElapsedMilliseconds);
                        await Layout();
                        selections.Add(timer.ElapsedMilliseconds);
                    }
                    Report(new { stage = "selection", count = selections.Count, feedbackP95 = selectionFeedback.Order().ElementAt(28),
                        feedbackMax = selectionFeedback.Max(), setterP95 = selectionSetters.Order().ElementAt(28), p95 = selections.Order().ElementAt(28), maximum = selections.Max() });
                    async Task Measure(string stage, Action<int> action)
                    {
                        var samples = new List<long>();
                        for (var i = 0; i < 30; i++)
                        {
                            timer.Restart(); action(i); await Layout(); samples.Add(timer.ElapsedMilliseconds);
                        }
                        Report(new { stage, count = samples.Count, p95 = samples.Order().ElementAt(28), maximum = samples.Max() });
                    }
                    var visitModules = Environment.GetEnvironmentVariable("WLMT_PERF_VISIT_MODULES")?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        ?? ["units", "weapons", "ammo", "divisions", "strategic", "sp"];
                    foreach (var key in visitModules)
                    {
                        actionStage = "search-" + key;
                        main.SelectedModule = main.Modules.Single(m => m.Key == key); await Layout();
                        void Search(string text)
                        {
                            switch (key)
                            {
                                case "units": main.UnitWorkspace!.TextFilter = text; break;
                                case "weapons": main.WeaponWorkspace!.UnitSearchText = text; break;
                                case "ammo": main.AmmoWorkspace!.SearchText = text; break;
                                case "divisions": main.DivisionWorkspace!.Search = text; break;
                                case "strategic": main.StrategicWorkspace!.Search = text; break;
                                case "sp": main.StrategicWorkspace!.Packs.Search = text; break;
                            }
                        }
                        await Measure("search-" + key, i => Search(i % 2 == 0 ? "Perf_0" : ""));
                        Search(""); await Layout();
                    }
                    main.SelectedModule = main.Modules.Single(m => m.Key == "units"); await Layout();
                    actionStage = "mode";
                    if (Environment.GetEnvironmentVariable("WLMT_PERF_SKIP_MODE") != "1")
                        await Measure("mode", i => main.AdvancedMode = i % 2 == 0);
                    main.AdvancedMode = false; await Layout();
                    var inputs = new List<long>(); var saves = new List<long>(); var old = new List<WeakReference>();
                    for (var run = 0; run < rounds; run++)
                    {
                        actionStage = "edit";
                        main.SelectedModule = main.Modules.Single(m => m.Key == "units");
                        var workspace = main.UnitWorkspace!;
                        workspace.SelectedUnit = workspace.Units.First(u => u.InternalName.StartsWith("Descriptor_Unit_Perf_") && u.Unit.Field("survival.health")?.CanEdit == true);
                        foreach (var section in workspace.FieldSections) section.IsExpanded = section.Groups.SelectMany(g => g.Fields).Any(f => f.Key == "survival.health");
                        await Layout();
                        var field = workspace.Fields.Single(f => f.Key == "survival.health");
                        var box = FindVisualChildren<TextBox>(host).First(b => b.IsVisible && ReferenceEquals(b.DataContext, field) && !b.IsReadOnly);
                        for (var edit = 0; edit < 3; edit++)
                        {
                            var value = (int.Parse(field.BaseValue, CultureInfo.InvariantCulture) + 1 + edit).ToString(CultureInfo.InvariantCulture);
                            timer.Restart(); box.Text = value; await Layout(); inputs.Add(timer.ElapsedMilliseconds);
                            await Until(() => !field.HasUnsavedEdit); saves.Add(timer.ElapsedMilliseconds);
                            Assert(field.EditValue == value && field.ActiveDraft?.TargetValue == value,
                                $"真实输入保存最新草稿: expected={value}, input={box.Text}, field={field.EditValue}, draft={field.ActiveDraft?.TargetValue}, base={field.BaseValue}, status={field.StatusText}, editable={field.IsEditable}");
                        }
                        actionStage = "cancel";
                        var preparation = workspace.PrepareApplyAsync(() => main.SaveBeforeLeavingAsync());
                        await Until(() => workspace.IsPreparingPreview || preparation.IsCompleted);
                        timer.Restart(); workspace.CancelPreview();
                        try { await preparation; throw new InvalidOperationException("预览应响应取消"); }
                        catch (OperationCanceledException) { }
                        Assert(!workspace.IsTransactionBusy && !workspace.IsPreparingPreview, "取消恢复界面准入");
                        Report(new { stage = "cancel", run, ms = timer.ElapsedMilliseconds });
                        actionStage = "preview";
                        timer.Restart(); var preview = await workspace.PrepareApplyAsync(() => main.SaveBeforeLeavingAsync());
                        Report(new { stage = "preview", run, ms = timer.ElapsedMilliseconds, files = preview.FormalFileCount });
                        old.Add(new WeakReference(workspace));
                        old.Add(new WeakReference(typeof(MainViewModel).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!));
                        actionStage = "commit-refresh";
                        timer.Restart(); var committed = await workspace.CommitApplyAsync(preview); await Layout();
                        Assert(committed.Succeeded && !main.UnitWorkspace!.RefreshRequired && main.UnitWorkspace.DraftCount == 0, "真实窗口应用刷新完整");
                        Report(new { stage = "commit-refresh", run, ms = timer.ElapsedMilliseconds, phases = main.LastRefreshTimings });
                        // Drop the test's own strong references before measuring session retention.
                        workspace = null!; field = null!; box = null!; preparation = null!; preview = null!;
                        actionStage = "reopen";
                        await main.OpenProjectAsync(root); await main.CacheSaveTask; await Layout(); await Task.Delay(100);
                        heartbeat.Stop();
                        var maxBeat = beats.DefaultIfEmpty().Max();
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                        await Layout(); GC.Collect();
                        Report(new { stage = "settled", run, heartbeatMaxMs = maxBeat, liveMiB = GC.GetTotalMemory(true) / 1048576d,
                            privateMiB = Process.GetCurrentProcess().PrivateMemorySize64 / 1048576d,
                            sampledPeakPrivateMiB = memory.PeakMiB, oldAlive = old.Count(w => w.IsAlive),
                            oldAliveKinds = old.Select((w, i) => new { index = i, kind = i % 2 == 0 ? "workspace" : "snapshot", alive = w.IsAlive }).Where(w => w.alive).ToArray() });
                        Assert(old.All(w => !w.IsAlive), "访问所有模块后旧工作区与快照可释放");
                        StartBeats();
                    }
                    if (inputs.Count > 0) Report(new { stage = "input-save", count = inputs.Count,
                        inputP95 = inputs.Order().ElementAt((int)Math.Ceiling(inputs.Count * .95)-1),
                        saveP95 = saves.Order().ElementAt((int)Math.Ceiling(saves.Count * .95)-1), saveMax = saves.Max() });
                    heartbeat.Stop(); actionStage = "visual-capture";
                    foreach (var english in new[] { false, true })
                    {
                        UiText.Current.SetLanguage(english ? "en" : "zh-CN");
                        ThemeManager.ApplyTheme(english ? AppTheme.DarkBlue : AppTheme.LightBlue, false);
                        main.AdvancedMode = english; host.Width = english ? 1200 : 1420;
                        foreach (var key in new[] { "units", "weapons", "ammo", "divisions", "rules", "strategic", "sp", "drafts" })
                        {
                            main.SelectedModule = main.Modules.Single(m => m.Key == key); await Layout();
                            if (key == "weapons") await main.WeaponWorkspace!.FeedbackTask;
                            if (key == "ammo") await main.AmmoWorkspace!.FeedbackTask;
                            Snapshot($"{key}-{(english ? "en-advanced-dark-1200" : "zh-basic-light-1420")}");
                            if (key == "units")
                            {
                                foreach (var button in FindVisualChildren<Button>((DependencyObject)host.FindName("HeaderBar")).Where(b => b.IsVisible))
                                    foreach (var label in FindVisualChildren<TextBlock>(button))
                                        Assert(button.ActualWidth + .5 >= label.DesiredSize.Width + button.Padding.Left + button.Padding.Right,
                                            "语言切换后顶部按钮完整显示文本");
                                Report(new { stage = "header-layout", english, buttons = FindVisualChildren<Button>((DependencyObject)host.FindName("HeaderBar"))
                                    .Where(b => b.IsVisible).Select(b => new { content = b.Content?.ToString(), width = b.ActualWidth,
                                        desired = b.DesiredSize.Width, text = FindVisualChildren<TextBlock>(b).Select(t => new { t.Text, width = t.ActualWidth, desired = t.DesiredSize.Width }).ToArray() }).ToArray() });
                            }
                        }
                    }
                    // Independent language/mode combinations for the changed unit editor templates.
                    foreach (var english in new[] { false, true })
                    {
                        UiText.Current.SetLanguage(english ? "en" : "zh-CN"); main.AdvancedMode = !english;
                        main.SelectedModule = main.Modules.Single(m => m.Key == "units");
                        ((FrameworkElement)host.Content).LayoutTransform = new ScaleTransform(1.15, 1.15);
                        await Layout(); Snapshot($"units-{(english ? "en-basic" : "zh-advanced")}-115pct");
                    }
                    done.SetResult();
                }
                catch (Exception ex) { done.SetException(ex); }
                finally
                {
                    heartbeat.Stop();
                    typeof(App.MainWindow).GetField("_closeAfterFlush", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, true);
                    host.Close(); app.Shutdown();
                }
            };
            host.Show(); app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task; thread.Join();
    }

}
