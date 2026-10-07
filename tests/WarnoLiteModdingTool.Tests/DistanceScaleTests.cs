using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private const string ScalePath = "CommonData/Gameplay/Constantes/InitialisationGameDistanceUnits.ndf";
    private const string ScaleField = "LBUToGRUConversionFactor";
    private static string ScaleFixture(string nl = "\n")
    {
        var root = Path.Combine(Path.GetTempPath(), "wl-scale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "GameData"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, ScalePath))!);
        var text = "// Synthetic scale fixture; LBUToGRUConversionFactor = 99\nunnamed TInitialisationGameDistanceUnits\n(\n    LBUToGRUConversionFactor = 2.92198967 // preserve this comment\n)\nUnrelated is 42\n";
        File.WriteAllText(Path.Combine(root, ScalePath), text.Replace("\n", nl), new UTF8Encoding(false));
        return root;
    }
    private static RuleGroup ScaleGroup(RuleWorkspace workspace) => workspace.Groups.Single(g => g.Definition.Number == 55);
    private static DraftOperation ScaleOp(RuleWorkspace workspace, string value) => workspace.Operation(ScaleGroup(workspace), new Dictionary<string, string> { [ScaleField] = value });

    private static async Task DistanceScaleContracts()
    {
        var root = ScaleFixture();
        try
        {
            var workspace = RuleWorkspace.Load(root);
            var context = new ModProjectDetector().Detect(root);
            Assert(context.IsRecognized && context.Modules.Single(m => m.Key == "rules").CanScan && !context.Modules.Single(m => m.Key == "units").CanScan, "CommonData scale works without units or gameplay rules");
            var group = ScaleGroup(workspace);
            Assert(group.CanEdit && group.Cells.Single().Raw == "2.92198967" && !group.Definition.Basic, "professional raw coefficient, no reciprocal conversion");
            foreach (var value in new[] { "1.0", "0.0001", "4.12345678", "1e2" })
                Assert(workspace.Resolve(ScaleOp(workspace, value)).Status == DraftResolutionStatus.Active, "positive coefficient " + value);
            foreach (var value in new[] { "0", "-1", "NaN", "Infinity", "1e999", "1e-999", "1.0 div 2.83", "~/Other" })
                await TestAssert.ThrowsAsync<InvalidDataException>(() => Task.Run(() => ScaleOp(workspace, value)), "reject invalid scale " + value);
            var path = Path.Combine(root, ScalePath); var original = File.ReadAllText(path);
            foreach (var value in new[] { "0", "-1" })
                ChangeReject(() => WarnoLiteModdingTool.Core.Changes.ChangeMerge.ValidateBytes(ScalePath, Encoding.UTF8.GetBytes(original.Replace("2.92198967", value))), "change restore and source candidate reject nonpositive scale");
            foreach (var value in new[] { "~/Other", "1.0 div 2.83", "[1,2]", "1 LBUToGRUConversionFactor = 2" })
            {
                File.WriteAllText(path, original.Replace("2.92198967", value), new UTF8Encoding(false));
                Assert(!ScaleGroup(RuleWorkspace.Load(root)).CanEdit, "no guessed write for " + value);
            }
            File.WriteAllText(path, original + "unnamed TInitialisationGameDistanceUnits()\n", new UTF8Encoding(false));
            Assert(!ScaleGroup(RuleWorkspace.Load(root)).CanEdit, "duplicate initialization rejected even when only one has a value");
            File.WriteAllText(path, original.Replace("LBUToGRUConversionFactor = 2.92198967", "Other = 1"), new UTF8Encoding(false));
            Assert(!ScaleGroup(RuleWorkspace.Load(root)).CanEdit, "missing field not inserted");
            File.Delete(path);
            Assert(!ScaleGroup(RuleWorkspace.Load(root)).CanEdit && !File.Exists(path), "missing file not created");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task DistanceScaleTransactions()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        {
            var root = ScaleFixture(nl); var path = Path.Combine(root, ScalePath);
            var transport = Path.Combine(root, "GameData/Gameplay/Constantes/Transport.ndf");
            try
            {
                RulesFixture184(root, nl);
                var workspace = RuleWorkspace.Load(root); var before = File.ReadAllBytes(path);
                using var store = new DraftStore(root); await store.LoadAsync();
                await store.UpsertAsync(ScaleOp(workspace, "1.0"));
                using (var reopened = new DraftStore(root))
                {
                    await reopened.LoadAsync();
                    Assert(workspace.Resolve(reopened.Operations.Single()).Status == DraftResolutionStatus.Active, "scale draft reopens");
                }
                Assert(File.ReadAllBytes(path).SequenceEqual(before), "draft does not modify CommonData");
                var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
                Assert(preview.FormalFileCount == 1 && preview.Files.Single(f => f.Kind == FormalTextFileKind.Ndf).RelativePath.Replace('\\', '/') == ScalePath, "global write touches only selected Mod scale file");
                var applied = await service.CommitApplyAsync(preview, store);
                Assert(applied.Succeeded && File.ReadAllBytes(path).SequenceEqual(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(before).Replace("2.92198967", "1.0"))), "exact span write, comments/newlines retained, no BOM");
                await service.CommitRestoreAsync(service.PrepareRestore(root, applied.BackupId));
                Assert(File.ReadAllBytes(path).SequenceEqual(before), "restore exact scale baseline");
                await store.UpsertAsync(ScaleOp(workspace, "1.25"));
                var loadRadius = workspace.Groups.Single(g => g.Definition.Number == 31);
                await store.UpsertAsync(workspace.Operation(loadRadius, loadRadius.Cells.ToDictionary(c => c.Key, _ => "80")));
                preview = await service.PrepareApplyAsync(root, store.Operations);
                File.SetAttributes(transport, FileAttributes.ReadOnly);
                await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "CommonData plus GameData failure");
                File.SetAttributes(transport, FileAttributes.Normal);
                Assert(File.ReadAllBytes(path).SequenceEqual(before) && store.Operations.Count == 2, "failed combined apply restores CommonData and retains drafts");
                File.WriteAllText(path, Encoding.UTF8.GetString(before).Replace("2.92198967", "3.0"), new UTF8Encoding(false));
                await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "stale coefficient rejected");
            }
            finally { if (File.Exists(transport)) File.SetAttributes(transport, FileAttributes.Normal); Directory.Delete(root, true); }
        }
    }

    private static async Task DistanceScaleUi()
    {
        var root = ScaleFixture(); RulesFixture184(root, "\n");
        File.WriteAllText(Path.Combine(root, "GameData/Gameplay/Constantes/Airplane.ndf"), "unnamed TAirplaneConstantesModernWarfareDescriptor(EvacuationAltitudeGRU = 500)\n", new UTF8Encoding(false));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            WarnoLiteModdingTool.App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                WarnoLiteModdingTool.App.Theming.ThemeManager.Initialize(new WarnoLiteModdingTool.App.Theming.UiThemeStore(Path.Combine(root, ".qa/theme.txt")));
                var vm = new WarnoLiteModdingTool.App.ViewModels.MainViewModel(new RecentProjectStore(Path.Combine(root, ".qa/recent.json")));
                var window = new WarnoLiteModdingTool.App.MainWindow(vm) { Width = 1480, Height = 1000 };
                window.Show(); DrainDispatcher(dispatcher); RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher);
                vm.SelectedModule = vm.Modules.Single(m => m.Key == "rules");
                var rules = vm.RulesWorkspace!;
                vm.AdvancedMode = false; rules.Refresh();
                Assert(!rules.View.Cast<WarnoLiteModdingTool.App.ViewModels.RuleGroupViewModel>().Any(g => g.Group.Definition.Number == 55), "scale hidden in basic mode");
                vm.AdvancedMode = true;
                foreach (var lang in new[] { "zh-CN", "en" })
                foreach (var theme in Enum.GetValues<WarnoLiteModdingTool.App.Theming.AppTheme>())
                {
                    WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(lang);
                    WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(theme, false);
                    window.Width = lang == "en" ? 1240 : 1480;
                    ((FrameworkElement)window.Content).LayoutTransform = new ScaleTransform(lang == "en" ? 1.1 : 1, lang == "en" ? 1.1 : 1);
                    rules.Category = "全部"; rules.Search = ""; rules.Refresh(); DrainDispatcher(dispatcher);
                    var view = FindVisualChildren<WarnoLiteModdingTool.App.Controls.RulesView>(window).Single();
                    foreach (var expander in FindVisualChildren<Expander>(view).Where(e => e.Tag is string s && s is "距离与比例尺" or "空军")) expander.IsExpanded = true;
                    DrainDispatcher(dispatcher);
                    var scale = FindVisualChildren<Expander>(view).Single(e => e.Tag is int n && n == 55);
                    scale.IsExpanded = true; DrainDispatcher(dispatcher);
                    var neighbor = FindVisualChildren<Expander>(view).Single(e => e.Tag is int n && n == 53); neighbor.IsExpanded = true;
                    var group = rules.Groups.Single(g => g.Group.Definition.Number == 55);
                    var input = FindVisualChildren<TextBox>(scale).Single();
                    Assert(input.IsEnabled, "rule editable without a unit module");
                    input.Text = "0"; input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); RunWithDispatcher(group.SaveAsync(), dispatcher);
                    Assert(group.HasPendingError, "invalid scale stays pending");
                    input.Text = "1.25"; input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); RunWithDispatcher(group.SaveAsync(), dispatcher);
                    Assert(!group.HasPendingError && vm.UnitWorkspace!.DraftItems.Any(d => d.Resolved.Operation.FieldKey == "55"), "actual control saves scale draft");
                    scale.BringIntoView(); DrainDispatcher(dispatcher); window.UpdateLayout();
                    Directory.CreateDirectory("publish/qa-2.11.2");
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render((FrameworkElement)window.Content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create($"publish/qa-2.11.2/scale-{lang}-{theme}.png")) encoder.Save(stream);
                    RunWithDispatcher(group.UndoAsync(), dispatcher);
                    Assert(group.Cells.Single().Value == "2.92198967", "undo restores actual Mod value");
                }
                window.Close(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception e) { app?.Shutdown(); completion.SetException(e); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task; Console.WriteLine("PASS scale WPF editing, mode, languages, themes, widths and scaling"); }
        finally { Directory.Delete(root, true); }
    }
}
