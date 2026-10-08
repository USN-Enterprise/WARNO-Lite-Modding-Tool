using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task WeaponSafetyUi()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        var path = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
        var original = File.ReadAllText(path).Replace("TimeBetweenTwoShots = 1.0", "SimultaneousShotsCount = 2\n    TimeBetweenTwoShots = 1.0");
        File.WriteAllText(path, original, new UTF8Encoding(false));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            App.App? app = null;
            try
            {
                app = new(launchWorkspace:false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                ThemeManager.Initialize(new UiThemeStore(Path.Combine(root, ".qa/theme.txt")));
                var vm = new MainViewModel(new RecentProjectStore(Path.Combine(root, ".qa/recent.json")));
                var window = new App.MainWindow(vm) { Width = 1400, Height = 950, ShowActivated = false }; window.Show();
                RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher); vm.SelectedModule = vm.Modules.Single(m=>m.Key=="ammo");
                vm.AmmoWorkspace!.SelectedAmmo = vm.AmmoWorkspace.Ammunition.Single(a=>a.Name=="Ammo_P4_Shared");
                var ammo = vm.AmmoWorkspace;
                var shot = ammo.Fields.Single(f=>f.Field.Key=="ammo.shotsPerSalvo");
                ammo.FieldSearch="ShotsCountPerSalvo";
                shot.EditValue = "-1";
                try { RunWithDispatcher(ammo.FlushAsync(), dispatcher); throw new Exception("Invalid value was accepted"); }
                catch (InvalidOperationException) { }
                Assert(shot.EditValue == "-1" && shot.Draft is null && shot.StatusText.Contains("未保存"), "invalid text retained without overwriting draft");
                DrainDispatcher(dispatcher); foreach(var e in FindVisualChildren<Expander>(window)) e.IsExpanded=true; DrainDispatcher(dispatcher); Snapshot(window,"invalid-input.png");
                shot.EditValue = "1"; RunWithDispatcher(ammo.FlushAsync(), dispatcher);
                Assert(shot.Draft is not null && shot.StatusText.Contains("配套"), "combination-invalid draft persists with immediate feedback");
                DrainDispatcher(dispatcher); Snapshot(window,"pending-combination.png");
                using (var store = new DraftStore(root))
                {
                    RunWithDispatcher(store.LoadAsync(), dispatcher);
                    try { RunWithDispatcher(new UnitApplyPlanner().PrepareAsync(root, store.Operations), dispatcher); throw new Exception("Invalid combination applied"); }
                    catch (TransactionValidationException ex) { Assert(ex.Code=="weapon.safety", "final combination refused"); }
                    Assert(store.Operations.Count>0 && File.ReadAllText(path)==original, "refusal retains draft and source bytes");
                }
                shot.EditValue = "2"; RunWithDispatcher(ammo.FlushAsync(), dispatcher);
                var time = ammo.Fields.Single(f=>f.Field.Key=="ammo.time.shot"); time.EditValue="0.163"; RunWithDispatcher(ammo.FlushAsync(), dispatcher);
                Assert(time.Draft is not null && time.SafetyText.Contains("0.1"), "uncertain time threshold remains warning");
                foreach (var language in new[]{"zh-CN","en"}) foreach (var advanced in new[]{false,true})
                {
                    UiText.Current.SetLanguage(language); vm.AdvancedMode=advanced;
                    // Recreate rows to exercise language/mode rebuilding and preservation of the saved draft.
                    ammo.SelectedAmmo=ammo.Ammunition.First(a=>a.Name!="Ammo_P4_Shared"); ammo.SelectedAmmo=ammo.Ammunition.Single(a=>a.Name=="Ammo_P4_Shared");
                    ammo.FieldSearch="TimeBetweenTwoShots";
                    ThemeManager.ApplyTheme(language=="en"?AppTheme.DarkBlue:AppTheme.LightBlue,false);
                    window.Width=advanced?1200:1480; window.Height=950;
                    DrainDispatcher(dispatcher);
                    foreach(var expander in FindVisualChildren<Expander>(window)) expander.IsExpanded=true;
                    DrainDispatcher(dispatcher); Snapshot(window,$"ammo-{language}-{advanced}.png");
                    Assert(ammo.Fields.Single(f=>f.Field.Key=="ammo.time.shot").SafetyText.Length>0,"feedback preserved across modes");
                }
                window.Close(); app.Shutdown(); completion.SetResult();
            }
            catch(Exception ex) { app?.Shutdown(); completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        try { await completion.Task; Console.WriteLine("PASS weapon safety WPF invalid input, persisted combinations, apply refusal, warnings, language/mode/theme snapshots"); }
        finally { thread.Join();DeleteTemporaryFixture(root); }
        static void Snapshot(Window window,string name)
        {
            window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine("publish/qa-2.11.4",name));encoder.Save(stream);
        }
    }
}
