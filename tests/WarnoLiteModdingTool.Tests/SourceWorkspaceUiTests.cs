using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.SourceWorkspace;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Drafts;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task SourceUi(bool largeFiles = false)
    {
        var root = SourceFixture(); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
                var window = new SourceWorkspaceWindow(vm) { Owner = owner, ShowActivated = false }; window.Show(); DrainDispatcher(dispatcher); RunWithDispatcher(window.Initialization, dispatcher);
                RunWithDispatcher(window.OpenFileAsync("Custom/Nested/Unknown.NDF"), dispatcher);
                Assert(window.ActiveEditor is not null, "WPF source editor opens arbitrary file");
                var old = window.ActiveEditor!.Text; window.ActiveEditor.Text = old.Replace("Value = 7", "Value = 8"); RunWithDispatcher(window.SaveAllAsync(), dispatcher);
                Assert(vm.SourceDraftCount == 1 && File.ReadAllText(Path.Combine(root, "Custom/Nested/Unknown.NDF")) == old, "actual editor persists draft without formal write");
                Assert(window.ActiveEditor.Document.Text.Contains("\r\n") && window.ActiveEditor.Document.Text.Contains("\n("), "editor retains mixed line endings");
                Directory.CreateDirectory("publish/qa-2.11.9/source");
                if(largeFiles)
                {
                    var measurements=new List<object>();UiText.Current.SetLanguage("en");
                    foreach(var mb in new[]{10,50})
                    {
                        var builder=new System.Text.StringBuilder();var line="// "+new string('x',180)+"\n";while(builder.Length<mb*1_000_000)builder.Append(line);builder.Append("\nLarge is TFuture(Value = 1)\n");var content=builder.ToString();var file=$"Large{mb}.ndf";File.WriteAllText(Path.Combine(root,file),content);
                        var watch=System.Diagnostics.Stopwatch.StartNew();RunWithDispatcher(window.OpenFileAsync(file),dispatcher);window.UpdateLayout();DrainDispatcher(dispatcher);var openMs=watch.Elapsed.TotalMilliseconds;
                        var inputs=new List<double>();for(var i=0;i<12;i++){watch.Restart();window.ActiveEditor!.Document.Insert(window.ActiveEditor.Document.TextLength,"// x\n");window.ActiveEditor.CaretOffset=window.ActiveEditor.Document.TextLength;window.ActiveEditor.ScrollToEnd();window.UpdateLayout();DrainDispatcher(dispatcher);inputs.Add(watch.Elapsed.TotalMilliseconds);}
                        watch.Restart();RunWithDispatcher(window.SaveAllAsync(),dispatcher);var saveMs=watch.Elapsed.TotalMilliseconds;
                        var diffTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};var rendered=new TaskCompletionSource();var diffMilliseconds=0.0;
                        diffTimer.Tick+=(_,_)=>{var dialog=app.Windows.OfType<Window>().FirstOrDefault(w=>w.Title=="Source comparison and apply");if(dialog is null)return;if(watch.Elapsed.TotalSeconds>30){diffTimer.Stop();dialog.DialogResult=false;rendered.SetException(new TimeoutException("Large diff navigation did not align both panes"));return;}var selector=FindVisualChildren<ComboBox>((DependencyObject)dialog.Content).Single();if(selector.SelectedItem is not Core.Transactions.PlannedFileChange chosen || chosen.RelativePath!=file){selector.SelectedItem=selector.Items.OfType<Core.Transactions.PlannedFileChange>().Single(f=>f.RelativePath==file);return;}var info=FindVisualChildren<TextBlock>((DependencyObject)dialog.Content).Any(t=>t.Text.StartsWith("1/"));if(!info)return;var panes=FindVisualChildren<ICSharpCode.AvalonEdit.TextEditor>((DependencyObject)dialog.Content).ToArray();if(panes.Any(p=>p.VerticalOffset<=0))return;diffTimer.Stop();dialog.UpdateLayout();diffMilliseconds=watch.Elapsed.TotalMilliseconds;SourceScreenshot(dialog,$"source-large-{mb}.png");dialog.DialogResult=false;rendered.SetResult();};
                        watch.Restart();diffTimer.Start();RunWithDispatcher(window.ApplyAsync(),dispatcher);RunWithDispatcher(rendered.Task,dispatcher);
                        Assert(File.ReadAllText(Path.Combine(root,file))==content && vm.SourceDraftStore.Operations.Any(o=>o.RelativeSourceFile==file),"large preview cancel preserves draft and formal original");
                        measurements.Add(new{Megabytes=mb,OpenUiMs=openMs,InputP95=inputs.Order().ElementAt(11),InputSamples=inputs,SaveDraftMs=saveMs,PrepareAndDiffUiMs=diffMilliseconds,PeakWorkingSet=System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64});
                        window.ActiveEditor!.Text=content;RunWithDispatcher(window.SaveAllAsync(),dispatcher);
                    }
                    Directory.CreateDirectory("publish/qa-2.11.9/source");File.WriteAllText("publish/qa-2.11.9/source/large-ui.json",System.Text.Json.JsonSerializer.Serialize(new{Results=measurements,Note="10 then 50 MB multiline comments, 183-character lines, actual WPF; whole-process peak includes previous steps, no real IME"},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                    window.Close();DrainDispatcher(dispatcher);owner.Close();app.Shutdown();completion.SetResult();return;
                }
                UiText.Current.SetLanguage("en");
                Task InvokeTask(string method) => (Task)typeof(SourceWorkspaceWindow).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,[])!;
                void InvokeEvent(string method) => typeof(SourceWorkspaceWindow).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,[window,new RoutedEventArgs()]);
                async Task Until(Func<bool> condition) { for(var i=0;i<500;i++) { if(condition())return;await Task.Delay(10); } throw new TimeoutException("Source UI operation did not complete"); }
                ((TextBox)window.FindName("Query")).Text="Value";((TextBox)window.FindName("Replacement")).Text="Tuned";((CheckBox)window.FindName("CurrentOnly")).IsChecked=true;
                RunWithDispatcher(InvokeTask("SearchAsync"),dispatcher);var hits=(DataGrid)window.FindName("Hits");Assert(hits.Items.Count==1,"actual UI search reports matching draft text");hits.SelectAll();
                var replacementTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
                replacementTimer.Tick+=(_,_)=>{var review=app.Windows.OfType<Window>().FirstOrDefault(w=>w.Title=="Replacement preview");if(review is null)return;replacementTimer.Stop();SourceScreenshot(review,"source-replacement.png");FindVisualChildren<CheckBox>((DependencyObject)review.Content).Single(c=>c.Content?.ToString()?.StartsWith("I reviewed")==true).IsChecked=true;FindVisualChildren<Button>((DependencyObject)review.Content).Single(b=>b.Content?.ToString()=="Save replacement drafts").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));};
                replacementTimer.Start();InvokeEvent("Replace_Click");RunWithDispatcher(Until(()=>vm.SourceDraftStore.Operations.Any(o=>o.Source?.Text.Contains("Tuned = 8")==true) && window.ActiveEditor!.IsEnabled),dispatcher);
                Assert(File.ReadAllText(Path.Combine(root,"Custom/Nested/Unknown.NDF"))==old,"replacement confirmation only updates drafts");
                InvokeEvent("UndoReplacement_Click");RunWithDispatcher(Until(()=>vm.SourceDraftStore.Operations.Any(o=>o.Source?.Text.Contains("Value = 8")==true)&&window.ActiveEditor!.IsEnabled),dispatcher);
                Assert(window.ActiveEditor!.Text.Contains("Value = 8"),"whole replacement undo restores draft");
                var inputText=string.Concat(Enumerable.Range(0,9000).Select(i=>$"Object_{i} is TFuture\n(\n    Value = {i}\n    // ordinary multiline object padding, 中文 and retained whitespace\n)\n"));
                File.WriteAllText(Path.Combine(root,"Input.ndf"),inputText);RunWithDispatcher(window.OpenFileAsync("Input.ndf"),dispatcher);
                var latencies=new List<double>();
                async Task InputSamples()
                {
                    window.ActiveEditor!.AppendText("// typing "); var saving=window.SaveAllAsync();
                    for(var sample=0;sample<40;sample++) { var watch=System.Diagnostics.Stopwatch.StartNew();window.ActiveEditor.Document.Insert(window.ActiveEditor.Document.TextLength,"x");window.ActiveEditor.CaretOffset=window.ActiveEditor.Document.TextLength;window.ActiveEditor.ScrollToEnd();window.UpdateLayout();await dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);latencies.Add(watch.Elapsed.TotalMilliseconds);await Task.Delay(20); }
                    await saving;await window.SaveAllAsync();
                }
                RunWithDispatcher(InputSamples(),dispatcher);
                Assert(vm.SourceDraftStore.Operations.Single(o=>o.RelativeSourceFile=="Input.ndf").Source!.Text.EndsWith(new string('x',40)),"input during save retains newer revision");
                Directory.CreateDirectory("publish/qa-2.11.9/source");File.WriteAllText("publish/qa-2.11.9/source/input-ui.json",System.Text.Json.JsonSerializer.Serialize(new {Bytes=System.Text.Encoding.UTF8.GetByteCount(inputText),Samples=latencies, P50=latencies.Order().ElementAt(19),P95=latencies.Order().ElementAt(37),Max=latencies.Max(),Note="Programmatic WPF insert/caret/layout through ContextIdle; not real IME or cross-screen DPI"},new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
                window.ActiveEditor.Text=inputText;RunWithDispatcher(window.SaveAllAsync(),dispatcher);RunWithDispatcher(window.OpenFileAsync("Custom/Nested/Unknown.NDF"),dispatcher);
                foreach (var language in new[] { "zh-CN", "en" })
                foreach (var theme in Enum.GetValues<AppTheme>())
                {
                    UiText.Current.SetLanguage(language); ThemeManager.ApplyTheme(theme, false); vm.AdvancedMode = language != "en";
                    Assert(vm.HasSourceDrafts, "source drafts survive basic/professional switch");
                    window.Width = language == "en" ? 1040 : 1400; window.Height = 880;
                    ((FrameworkElement)window.Content).LayoutTransform = new ScaleTransform(language == "en" ? 1.25 : 1, language == "en" ? 1.25 : 1);
                    window.UpdateLayout(); DrainDispatcher(dispatcher);
                    Assert(window.ActiveEditor.ActualWidth > 200 && window.ActiveEditor.ActualHeight > 100, "editor remains accessible");
                    SourceScreenshot(window, $"source-{language}-{theme}.png");
                }
                foreach (var scale in new[] { 1.5, 2.0 })
                {
                    ((FrameworkElement)window.Content).LayoutTransform = new ScaleTransform(scale, scale); window.Width = 1500; window.Height = 1050; window.UpdateLayout(); DrainDispatcher(dispatcher);
                    SourceScreenshot(window, $"source-scale-{scale}.png");
                }
                ((FrameworkElement)window.Content).LayoutTransform = Transform.Identity;
                UiText.Current.SetLanguage("en"); ThemeManager.ApplyTheme(AppTheme.DarkBlue, false);
                File.WriteAllText(Path.Combine(root, "BrowseOnly.ndf"), "Browse is 1\n", new System.Text.UTF8Encoding(true));
                var bomBytes = File.ReadAllBytes(Path.Combine(root, "BrowseOnly.ndf"));
                RunWithDispatcher(window.OpenFileAsync("BrowseOnly.ndf"), dispatcher);
                RunWithDispatcher(window.OpenFileAsync("Root.ndf"), dispatcher);
                File.WriteAllText(Path.Combine(root, "Root.ndf"), "External is 23\n");
                RunWithDispatcher(window.OpenFileAsync("Custom/Nested/Unknown.NDF"), dispatcher);
                var reviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                reviewTimer.Tick += (_, _) =>
                {
                    var review = app.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "Source comparison and apply"); if (review is null) return;
                    reviewTimer.Stop(); review.UpdateLayout(); SourceScreenshot(review, "source-review.png");
                    var apply = FindVisualChildren<Button>((DependencyObject)review.Content).Single(b => b.Content?.ToString() == "Back up and apply the entire source group");
                    Assert(!apply.IsEnabled && !window.ActiveEditor!.IsEnabled, "review requires acknowledgment and freezes source input");
                    FindVisualChildren<CheckBox>((DependencyObject)review.Content).Single(c => c.Content?.ToString()?.StartsWith("I reviewed") == true).IsChecked = true;
                    apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                };
                reviewTimer.Start(); RunWithDispatcher(window.ApplyAsync(), dispatcher);
                Assert(vm.SourceDraftCount == 0 && File.ReadAllText(Path.Combine(root, "Custom/Nested/Unknown.NDF")).Contains("Value = 8"), "actual review applies full source group");
                Assert(bomBytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "BrowseOnly.ndf"))), "viewing BOM file never enrolls it in conversion");
                void InspectDialog(string method,string title,string screenshot)
                {
                    var seen=false;var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(180)};
                    timer.Tick+=(_,_)=>{var dialog=app.Windows.OfType<Window>().FirstOrDefault(w=>w.Title.StartsWith(title));if(dialog is null)return;timer.Stop();dialog.UpdateLayout();SourceScreenshot(dialog,screenshot);dialog.Close();seen=true;};timer.Start();InvokeEvent(method);RunWithDispatcher(Until(()=>seen&&window.ActiveEditor!.IsEnabled),dispatcher);
                }
                InspectDialog("HistoryOverview_Click","Backup overview","source-history.png");
                InspectDialog("Conflict_Click","Three-way review","source-merge.png");
                RunWithDispatcher(window.OpenFileAsync("Root.ndf"), dispatcher); window.ActiveEditor!.AppendText("// local edit\n"); RunWithDispatcher(window.SaveAllAsync(), dispatcher);
                var externalDraft = vm.SourceDraftStore.Operations.Single();
                Assert(Core.SourceWorkspace.SourceEditing.Resolve(root, externalDraft).Status == DraftResolutionStatus.Conflict, "unrelated apply cannot silently advance baseline beneath stale editor text");
                RunWithDispatcher(vm.SourceDraftStore.RemoveAsync(externalDraft.Id), dispatcher);
                RunWithDispatcher(window.OpenFileAsync("Custom/Nested/Unknown.NDF"), dispatcher);
                window.ActiveEditor!.Text = window.ActiveEditor.Text.Replace("Value = 8", "Value = 9");
                RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher);
                Assert(!window.IsVisible, "reopening same root closes old source session safely");
                window = new SourceWorkspaceWindow(vm) { Owner = owner, ShowActivated = false }; window.Show(); DrainDispatcher(dispatcher); RunWithDispatcher(window.Initialization, dispatcher);
                var tabs=(TabControl)window.FindName("Documents");Assert(tabs.Items.Count>=3 && tabs.Items.OfType<TabItem>().Count(t=>t.Content is ICSharpCode.AvalonEdit.TextEditor)==1,"reopen restores tab order with inactive editors loaded lazily");
                RunWithDispatcher(window.OpenFileAsync("Custom/Nested/Unknown.NDF"), dispatcher);
                Assert(window.ActiveEditor!.Text.Contains("Value = 9"), "reopening preserves latest unsaved source input");
                window.Close(); DrainDispatcher(dispatcher);
                using var reload = new DraftStore(root); RunWithDispatcher(reload.LoadAsync(), dispatcher);
                Assert(reload.Operations.Single().Source!.Text.Contains("Value = 9"), "close persists source");
                var businessRoot=AviationFixture();
                try
                {
                    RunWithDispatcher(vm.OpenProjectAsync(businessRoot),dispatcher);vm.AdvancedMode=true;
                    var loadUnit=LoadTankAsync(businessRoot);RunWithDispatcher(loadUnit,dispatcher);var businessOperation=AviationOp(loadUnit.Result.Item2,"flight.altitude","850");
                    RunWithDispatcher(vm.SourceDraftStore.UpsertAsync(businessOperation),dispatcher);
                    RunWithDispatcher(vm.SourceDraftStore.UpsertAsync(Core.SourceWorkspace.SourceEditing.Operation("Custom/Independent.ndf",new(null,"Independent is 1\n"))),dispatcher);vm.SourceDraftsChanged();
                    window=new SourceWorkspaceWindow(vm) {Owner=owner,ShowActivated=false};window.Show();DrainDispatcher(dispatcher);RunWithDispatcher(window.Initialization,dispatcher);RunWithDispatcher(window.OpenFileAsync("Custom/Independent.ndf"),dispatcher);
                    vm.UnitWorkspace!.DraftItems.Single(i=>i.Resolved.Operation.Id==businessOperation.Id).IsSelected=true;
                    window.ActiveEditor!.AppendText("// last source input\n");
                    var seenBusinessPreview=false;var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
                    timer.Tick+=(_,_)=>{var dialog=app.Windows.OfType<App.Advanced.DiffWindow>().FirstOrDefault();if(dialog is null)return;timer.Stop();seenBusinessPreview=true;SourceScreenshot(dialog,"source-business-selection.png");dialog.DialogResult=false;};timer.Start();
                    typeof(App.MainWindow).GetMethod("PreviewApply_Click",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(owner,[new Button {Tag="selected"},new RoutedEventArgs()]);RunWithDispatcher(Until(()=>seenBusinessPreview),dispatcher);
                    Assert(vm.SourceDraftStore.Operations.Count==2 && vm.SourceDraftStore.Operations.Single(o=>o.TargetKind==DraftTargetKind.SourceFile).Source!.Text.Contains("last source input"),"business-only routing preserves selection across flushing dirty source inputs");
                    window.Close();DrainDispatcher(dispatcher);
                }
                finally {DeleteTemporaryFixture(businessRoot);}
                owner.Close(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception ex) { app?.Shutdown(); completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task; Console.WriteLine("PASS Source WPF editor, draft persistence, languages, seven themes, narrow widths and layout scaling"); }
        finally { DeleteTemporaryFixture(root); }
    }
    private static void SourceScreenshot(Window window, string name)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render((FrameworkElement)window.Content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine("publish/qa-2.11.9/source", name)); encoder.Save(stream);
    }
}
