using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.App.Changes;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task ChangeUiTest()
    {
        using var fixture = new ChangeFixture(); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
                App.Theming.ThemeManager.Initialize(new App.Theming.UiThemeStore(Path.Combine(fixture.Root, "theme.txt")));
                foreach (var language in new[] { "zh-CN", "en" })
                foreach (var dark in new[] { false, true })
                {
                    UiText.Current.SetLanguage(language); App.Theming.ThemeManager.ApplyTheme(dark ? App.Theming.AppTheme.DarkBlue : App.Theming.AppTheme.LightBlue, false);
                    var model = new App.ViewModels.MainViewModel(new Core.Projects.RecentProjectStore(Path.Combine(fixture.Root, "recent.json"))) { AdvancedMode = dark };
                    var window = new ChangeRecordsWindow(model) { Width = dark ? 980 : 1280, Height = dark ? 820 : 940 };
                    ((FrameworkElement)window.Content).LayoutTransform = new ScaleTransform(dark ? 1.15 : 1, dark ? 1.15 : 1);
                    window.SetInputs(fixture.Source, fixture.Basis, fixture.Target, NumericPolicy.Ratio); window.Show(); DrainDispatcher(dispatcher);
                    var capture = FindVisualChildren<Button>(window).Single(b => Equals(b.Tag, "change-capture")); capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    RunWithDispatcher(WaitFor(() => !window.IsWorking), dispatcher); Assert(window.Package?.Manifest.Complete == true, window.Status);
                    SaveChangeSnapshot(window, "capture-" + language + "-" + dark);
                    var path = Path.Combine(fixture.Root, language + "-" + dark + ".wlmtchanges"); RunWithDispatcher(window.ExportAsync(path), dispatcher); Assert(File.Exists(path), window.Status);
                    RunWithDispatcher(window.OpenPackageAsync(path), dispatcher); RunWithDispatcher(window.PreviewAsync(), dispatcher);
                    Assert(window.Preview?.CanApply == true, window.Status);
                    var filter = FindVisualChildren<TextBox>(window).Single(b => Equals(b.Tag, "change-filter")); filter.Text = "no-match";
                    Assert(window.Package!.Manifest.Files.Count == 1 && window.Preview!.CanApply, "filter never changes export or restore scope"); filter.Clear();
                    Assert(FindVisualChildren<ComboBox>(window).Count(c => Equals(c.Tag, "change-policy")) == 1, "exactly one global numeric choice");
                    SaveChangeSnapshot(window, language + "-" + dark);
                    if (!dark && language == "zh-CN")
                    {
                        RunWithDispatcher(window.ApplyAsync(), dispatcher); Assert(window.ModifiedTarget, window.Status);
                        RunWithDispatcher(window.PreviewAsync(), dispatcher); Assert(window.Preview?.AlreadyApplied == true, window.Status);
                        var id = ChangeTransactions.List(fixture.Target).Single().Id; RunWithDispatcher(window.RecoverAsync(id), dispatcher);
                        Assert(File.ReadAllText(Path.Combine(fixture.Target, ChangeNdf)) == ChangeText(150, 9), window.Status);
                    }
                    window.Close();
                    using var partialFixture = new ChangeFixture(); AddIndependentChange(partialFixture, 0, 20);
                    var partial = new ChangeRecordsWindow(model) { Width = dark ? 980 : 1280, Height = dark ? 820 : 940 };
                    ((FrameworkElement)partial.Content).LayoutTransform = new ScaleTransform(dark ? 1.15 : 1, dark ? 1.15 : 1);
                    partial.SetInputs(partialFixture.Source, partialFixture.Basis, partialFixture.Target, NumericPolicy.Ratio); partial.Show();
                    RunWithDispatcher(partial.CaptureAsync(), dispatcher); RunWithDispatcher(partial.PreviewAsync(), dispatcher);
                    Assert(partial.Preview?.Groups.Count == 2 && !partial.Preview.CanApply, partial.Status);
                    var grid = FindVisualChildren<DataGrid>(partial).Single(g => Equals(g.Tag, "change-files"));
                    grid.SelectedItem = grid.Items.Cast<object>().Single(r => (string?)r.GetType().GetProperty("Path")!.GetValue(r) == OtherChangeNdf);
                    FindVisualChildren<Button>(partial).Single(b => Equals(b.Tag, "change-defer")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    RunWithDispatcher(WaitFor(() => !partial.IsWorking), dispatcher);
                    Assert(partial.Preview?.CanApply == true && partial.Preview.DeferredPaths.Count == 1, partial.Status);
                    SaveChangeSnapshot(partial, "partial-" + language + "-" + dark);
                    RunWithDispatcher(partial.ApplyAsync(), dispatcher); RunWithDispatcher(partial.PreviewAsync(), dispatcher);
                    Assert(partial.Preview?.AppliedPaths.Count == 1 && !partial.Preview.AlreadyApplied, "UI partial completion retains pending group");
                    partial.Close();

                    using var mappedFixture = new ChangeFixture(); var mappedPackage = mappedFixture.Capture(NumericPolicy.Ratio); const string moved = "CommonData/moved.ndf";
                    File.Delete(Path.Combine(mappedFixture.Target, ChangeNdf)); ChangeFixture.Write(mappedFixture.Target, moved, ChangeText(150, 9).Replace("export Unit ", "export Renamed "));
                    var mapping = new ChangeMappingWindow(ChangeMerge.MappingSources(mappedPackage, ChangeNdf), ChangeMerge.MappingTargets(mappedFixture.Target));
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        var fileChoice = FindVisualChildren<ComboBox>(mapping).Single(c => Equals(c.Tag, "change-map-file")); fileChoice.SelectedItem = moved; mapping.UpdateLayout();
                        FindVisualChildren<ComboBox>(mapping).Single(c => Equals(c.Tag, "change-map-object")).SelectedItem = "Renamed";
                        var accept = FindVisualChildren<Button>(mapping).Single(b => Equals(b.Tag, "change-map-accept")); Assert(accept.IsEnabled, "explicit mapping selection enables preview");
                        SaveChangeSnapshot(mapping, "mapping-" + language + "-" + dark); accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
                    Assert(mapping.ShowDialog() == true && mapping.Result?.Objects["Unit"] == "Renamed", "mapping dialog returns explicit targets");
                    var mappedWindow = new ChangeRecordsWindow(model); mappedWindow.SetInputs(mappedFixture.Source, mappedFixture.Basis, mappedFixture.Target, NumericPolicy.Ratio);
                    RunWithDispatcher(mappedWindow.CaptureAsync(), dispatcher); RunWithDispatcher(mappedWindow.SetMappingAsync(mapping.Result!), dispatcher);
                    Assert(mappedWindow.Preview?.CanApply == true, mappedWindow.Status); mappedWindow.Close();

                    using var textFixture = new ChangeFixture(); AddTextChange(textFixture);
                    ChangeRecordsWindow ReviewWindow()
                    {
                        var w = new ChangeRecordsWindow(model) { Width = dark ? 980 : 1280, Height = dark ? 820 : 940 };
                        ((FrameworkElement)w.Content).LayoutTransform = new ScaleTransform(dark ? 1.15 : 1, dark ? 1.15 : 1);
                        w.SetInputs(textFixture.Source, textFixture.Basis, textFixture.Target, NumericPolicy.Delta); w.Show(); return w;
                    }
                    void SelectTextFile(ChangeRecordsWindow w)
                    {
                        var rows = FindVisualChildren<DataGrid>(w).Single(g => Equals(g.Tag, "change-files"));
                        rows.SelectedItem = rows.Items.Cast<object>().Single(r => (string?)r.GetType().GetProperty("Path")!.GetValue(r) == ChangeCsv);
                    }
                    var review = ReviewWindow(); RunWithDispatcher(review.CaptureAsync(), dispatcher); RunWithDispatcher(review.PreviewAsync(), dispatcher);
                    Assert(review.Preview?.CanApply == false, review.Status); SelectTextFile(review);
                    var keep = FindVisualChildren<Button>(review).Single(b => Equals(b.Tag, "change-keep-text")); Assert(keep.IsEnabled, "supported conflict exposes choice");
                    SaveChangeSnapshot(review, "text-conflict-" + language + "-" + dark);
                    if (language == "en" && !dark)
                    {
                        review.Width = 850; review.Height = 660; SaveChangeSnapshot(review, "text-minimum-en");
                        review.Width = 1280; review.Height = 940; DrainDispatcher(dispatcher);
                    }
                    keep.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); RunWithDispatcher(WaitFor(() => !review.IsWorking), dispatcher);
                    Assert(review.Preview?.CanApply == true && review.Preview.Decisions.Single().Choice == TextConflictChoice.KeepTarget, review.Status);
                    var textPackage = Path.Combine(textFixture.Root, "record.wlmtchanges"); RunWithDispatcher(review.ExportAsync(textPackage), dispatcher);
                    FindVisualChildren<TabControl>(review).Single(t => Equals(t.Tag, "change-detail-tabs")).SelectedIndex = 3;
                    DrainDispatcher(dispatcher); review.UpdateLayout();
                    FindVisualChildren<Button>(review).Single(b => Equals(b.Tag, "change-save-review")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    RunWithDispatcher(WaitFor(() => !review.IsWorking), dispatcher); Assert(review.Status.Contains(language == "en" ? "saved" : "已保存"), review.Status);
                    SaveChangeSnapshot(review, "review-progress-" + language + "-" + dark); review.Close();
                    review = ReviewWindow(); RunWithDispatcher(review.OpenPackageAsync(textPackage), dispatcher);
                    FindVisualChildren<TabControl>(review).Single(t => Equals(t.Tag, "change-detail-tabs")).SelectedIndex = 3;
                    DrainDispatcher(dispatcher); review.UpdateLayout();
                    FindVisualChildren<Button>(review).Single(b => Equals(b.Tag, "change-load-review")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    RunWithDispatcher(WaitFor(() => !review.IsWorking), dispatcher);
                    Assert(review.Preview?.CanApply == true && review.Preview.Decisions.Single().Choice == TextConflictChoice.KeepTarget, review.Status);
                    FindVisualChildren<TabControl>(review).Single(t => Equals(t.Tag, "change-detail-tabs")).SelectedIndex = 0; SelectTextFile(review);
                    SaveChangeSnapshot(review, "text-resolved-" + language + "-" + dark);
                    RunWithDispatcher(review.ApplyAsync(), dispatcher); RunWithDispatcher(review.PreviewAsync(), dispatcher);
                    Assert(review.Preview?.AllProcessed == true && !review.Preview.AlreadyApplied && review.Preview.RetainedChanges == 1 && !review.Preview.CanApply, review.Status);
                    SelectTextFile(review); SaveChangeSnapshot(review, "text-retained-" + language + "-" + dark);
                    RunWithDispatcher(review.RecoverAsync(ChangeTransactions.List(textFixture.Target).Single().Id), dispatcher);
                    Assert(File.ReadAllText(Path.Combine(textFixture.Target, ChangeCsv)).Contains("新版名称"), review.Status); review.Close();

                    using var mapFixture = new ChangeFixture(); AddMapChange(mapFixture);
                    var maps = new ChangeRecordsWindow(model) { Width = dark ? 980 : 1280, Height = dark ? 820 : 940 };
                    ((FrameworkElement)maps.Content).LayoutTransform = new ScaleTransform(dark ? 1.15 : 1, dark ? 1.15 : 1);
                    maps.SetInputs(mapFixture.Source, mapFixture.Basis, mapFixture.Target, NumericPolicy.Ratio); maps.Show();
                    RunWithDispatcher(maps.CaptureAsync(), dispatcher);
                    var mapPackage = Path.Combine(mapFixture.Root, "maps.wlmtchanges"); RunWithDispatcher(maps.ExportAsync(mapPackage), dispatcher);
                    RunWithDispatcher(maps.OpenPackageAsync(mapPackage), dispatcher); RunWithDispatcher(maps.PreviewAsync(), dispatcher);
                    Assert(maps.Preview?.CanApply == true && maps.Preview.Files.Single().Details.Count == 3, maps.Status);
                    var mapDetails = FindVisualChildren<DataGrid>(maps).Single(g => Equals(g.Tag, "change-details"));
                    mapDetails.SelectedItem = mapDetails.Items.Cast<ChangeDetail>().Single(d => d.Field.Contains("HighAltitude"));
                    mapDetails.ScrollIntoView(mapDetails.SelectedItem); SaveChangeSnapshot(maps, "map-add-" + language + "-" + dark);
                    RunWithDispatcher(maps.ApplyAsync(), dispatcher); RunWithDispatcher(maps.PreviewAsync(), dispatcher);
                    Assert(maps.Preview?.AlreadyApplied == true, maps.Status);
                    RunWithDispatcher(maps.RecoverAsync(ChangeTransactions.List(mapFixture.Target).Single().Id), dispatcher);
                    ChangeFixture.Write(mapFixture.Target, ChangeNdf, VisionMap(MapStandard.Replace("100", "150") + MapLow.Replace("50", "60"), 9));
                    RunWithDispatcher(maps.PreviewAsync(), dispatcher);
                    Assert(maps.Preview?.CanApply == false && maps.Preview.Files.Single().Details.Any(d => d.Field.Contains("LowAltitude") && d.Target == "60"), maps.Status);
                    mapDetails.SelectedItem = mapDetails.Items.Cast<ChangeDetail>().Single(d => d.Field.Contains("LowAltitude"));
                    mapDetails.ScrollIntoView(mapDetails.SelectedItem); SaveChangeSnapshot(maps, "map-conflict-" + language + "-" + dark); maps.Close();

                    using var scalarFixture = new ChangeFixture(); AddScalarChange(scalarFixture);
                    ChangeRecordsWindow ScalarWindow()
                    {
                        var w = new ChangeRecordsWindow(model) { Width = dark ? 980 : 1280, Height = dark ? 820 : 940 };
                        ((FrameworkElement)w.Content).LayoutTransform = new ScaleTransform(dark ? 1.15 : 1, dark ? 1.15 : 1);
                        w.SetInputs(scalarFixture.Source, scalarFixture.Basis, scalarFixture.Target, NumericPolicy.Ratio); w.Show(); return w;
                    }
                    var scalars = ScalarWindow(); RunWithDispatcher(scalars.CaptureAsync(), dispatcher);
                    var scalarPackage = Path.Combine(scalarFixture.Root, "scalars.wlmtchanges"); RunWithDispatcher(scalars.ExportAsync(scalarPackage), dispatcher);
                    RunWithDispatcher(scalars.PreviewAsync(), dispatcher);
                    Assert(scalars.Preview?.CanApply == false && ScalarChoices(scalars.Preview).Length == 2, scalars.Status);
                    void SelectScalar(string field)
                    {
                        var rows = FindVisualChildren<DataGrid>(scalars).Single(g => Equals(g.Tag, "change-details"));
                        rows.SelectedItem = rows.Items.Cast<ChangeDetail>().Single(d => d.Field.EndsWith(field)); rows.ScrollIntoView(rows.SelectedItem);
                    }
                    void ScalarButton(string tag)
                    {
                        var button = FindVisualChildren<Button>(scalars).Single(b => Equals(b.Tag, tag)); Assert(button.IsEnabled, "scalar action enabled");
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); RunWithDispatcher(WaitFor(() => !scalars.IsWorking), dispatcher);
                    }
                    SelectScalar("UnitRole"); SaveChangeSnapshot(scalars, "scalar-conflict-" + language + "-" + dark);
                    if (language == "en" && !dark)
                    {
                        scalars.Width = 850; scalars.Height = 660; SaveChangeSnapshot(scalars, "scalar-minimum-en");
                        scalars.Width = 1280; scalars.Height = 940; DrainDispatcher(dispatcher);
                    }
                    ScalarButton("change-use-text"); Assert(scalars.Preview?.CanApply == false, "second conflict remains pending");
                    SelectScalar("UnitRole"); ScalarButton("change-clear-text"); Assert(scalars.Preview?.Decisions.Count == 0, "clear restores unresolved outcome");
                    SelectScalar("UnitRole"); ScalarButton("change-use-text"); SelectScalar("FactoryType"); ScalarButton("change-keep-text");
                    Assert(scalars.Preview?.CanApply == true, scalars.Status);
                    SelectScalar("FactoryType"); SaveChangeSnapshot(scalars, "scalar-resolved-" + language + "-" + dark);
                    RunWithDispatcher(scalars.SaveReviewAsync(), dispatcher); scalars.Close();
                    scalars = ScalarWindow(); RunWithDispatcher(scalars.OpenPackageAsync(scalarPackage), dispatcher); RunWithDispatcher(scalars.LoadReviewAsync(), dispatcher);
                    Assert(scalars.Preview?.CanApply == true && scalars.Preview.Decisions.Count == 2, scalars.Status);
                    RunWithDispatcher(scalars.ApplyAsync(), dispatcher); RunWithDispatcher(scalars.PreviewAsync(), dispatcher);
                    Assert(scalars.Preview?.AllProcessed == true && !scalars.Preview.AlreadyApplied && scalars.Preview.RetainedChanges == 1, scalars.Status);
                    Assert(File.ReadAllText(Path.Combine(scalarFixture.Target, ChangeNdf)) == ScalarText(180, "Recorded", "Upstream", 9), "UI literal choices preserve upstream field and apply numeric ratio once");
                    SelectScalar("FactoryType"); SaveChangeSnapshot(scalars, "scalar-retained-" + language + "-" + dark);
                    RunWithDispatcher(scalars.RecoverAsync(ChangeTransactions.List(scalarFixture.Target).Single().Id), dispatcher);
                    Assert(File.ReadAllText(Path.Combine(scalarFixture.Target, ChangeNdf)) == ScalarText(150, "新版角色", "Upstream", 9), "UI v4 recovery restores original"); scalars.Close();

                    using var fieldFixture = new ChangeFixture(); AddFieldChange(fieldFixture);
                    var fields = new ChangeRecordsWindow(model) { Width = dark ? 980 : 1280, Height = dark ? 820 : 940 };
                    ((FrameworkElement)fields.Content).LayoutTransform = new ScaleTransform(dark ? 1.15 : 1, dark ? 1.15 : 1);
                    fields.SetInputs(fieldFixture.Source, fieldFixture.Basis, fieldFixture.Target, NumericPolicy.Ratio); fields.Show();
                    RunWithDispatcher(fields.CaptureAsync(), dispatcher);
                    var fieldPackage = Path.Combine(fieldFixture.Root, "fields.wlmtchanges"); RunWithDispatcher(fields.ExportAsync(fieldPackage), dispatcher);
                    RunWithDispatcher(fields.OpenPackageAsync(fieldPackage), dispatcher); RunWithDispatcher(fields.PreviewAsync(), dispatcher);
                    Assert(fields.Preview?.CanApply == true && fields.Preview.Files.Single().Details.Count == 4, fields.Status);
                    var fieldRows = FindVisualChildren<DataGrid>(fields).Single(g => Equals(g.Tag, "change-details"));
                    fieldRows.SelectedItem = fieldRows.Items.Cast<ChangeDetail>().Single(d => d.Field.EndsWith("IsFireAndForget"));
                    fieldRows.ScrollIntoView(fieldRows.SelectedItem); SaveChangeSnapshot(fields, "field-add-" + language + "-" + dark);
                    RunWithDispatcher(fields.ApplyAsync(), dispatcher); RunWithDispatcher(fields.PreviewAsync(), dispatcher);
                    Assert(fields.Preview?.AlreadyApplied == true, fields.Status);
                    var restoredText = File.ReadAllText(Path.Combine(fieldFixture.Target, ChangeNdf));
                    Assert(restoredText.Contains("PhysicalDamages = 180") && restoredText.Contains("AimingTime = 5") && !restoredText.Contains("SupplyCost"), "UI applies common arithmetic and field presence together");
                    RunWithDispatcher(fields.RecoverAsync(ChangeTransactions.List(fieldFixture.Target).Single().Id), dispatcher);
                    ChangeFixture.Write(fieldFixture.Target, ChangeNdf, FieldAmmo(150, FieldRemoved.Replace("= 2", "= 3"), 9));
                    RunWithDispatcher(fields.PreviewAsync(), dispatcher); Assert(fields.Preview?.CanApply == false, fields.Status);
                    fieldRows.SelectedItem = fieldRows.Items.Cast<ChangeDetail>().Single(d => d.Field.EndsWith("SupplyCost"));
                    fieldRows.ScrollIntoView(fieldRows.SelectedItem); SaveChangeSnapshot(fields, "field-conflict-" + language + "-" + dark); fields.Close();
                }
                app.Shutdown(); completion.SetResult();
            }
            catch (Exception e) { app?.Shutdown(); completion.SetException(e); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await completion.Task;
        Console.WriteLine("PASS changes WPF capture/export/open/preview/apply/recovery, partial groups, mapping, CSV/NDF choices, saved review, MAP/field additions/deletions/conflicts, two languages/themes/widths and scaling");
        static async Task WaitFor(Func<bool> done) { for (var i = 0; i < 500 && !done(); i++) await Task.Delay(10); Assert(done(), "WPF task timeout"); }
    }
    private static void SaveChangeSnapshot(Window window, string name)
    {
        DrainDispatcher(window.Dispatcher); window.UpdateLayout(); var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory("publish/qa-2.10-preview.6");
        using var output = File.Create("publish/qa-2.10-preview.6/changes-" + name + ".png"); encoder.Save(output);
    }
}
