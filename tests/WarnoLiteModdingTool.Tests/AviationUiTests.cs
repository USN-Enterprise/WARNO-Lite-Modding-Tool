using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task AviationUi()
    {
        var root = AviationFixture(); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            WarnoLiteModdingTool.App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                ThemeManager.Initialize(new UiThemeStore(Path.Combine(root, ".qa/theme.txt")));
                var vm = new MainViewModel(new RecentProjectStore(Path.Combine(root, ".qa/recent.json")));
                var window = new MainWindow(vm) { Width = 1480, Height = 1020 }; window.Show(); DrainDispatcher(dispatcher);
                RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher);
                foreach (var language in new[] { "zh-CN", "en" })
                foreach (var professional in new[] { false, true })
                foreach (var helicopter in new[] { false, true })
                {
                    vm.AdvancedMode = professional; UiText.Current.SetLanguage(language);
                    ThemeManager.ApplyTheme(professional ? AppTheme.DarkBlue : AppTheme.LightBlue, false);
                    window.Width = language == "en" ? 1240 : 1480;
                    ((FrameworkElement)window.Content).LayoutTransform = new ScaleTransform(language == "en" ? 1.15 : 1, language == "en" ? 1.15 : 1);
                    var units = vm.UnitWorkspace!;
                    units.SelectedUnit = units.Units.Single(u => u.InternalName == (helicopter ? "Descriptor_Unit_Test_Helicopter" : "Descriptor_Unit_Test_Tank_US"));
                    foreach (var section in units.FieldSections) section.IsExpanded = section.Title == "机动与续航";
                    Assert(units.Fields.Where(f => AviationMovement.InternalSpeed(f.Key)).All(f => !f.IsVisible), "single integrated speed control");
                    Assert(units.Fields.Single(f => f.Key == "flight.pitch").IsVisible == (!helicopter && professional), "mode and aircraft capability filtering");
                    var key = helicopter ? "helicopter.altitude" : "flight.altitude";
                    var field = units.Fields.Single(f => f.Key == key);
                    Assert(field.IsEditable && units.VisibleBatchFields.Any(f => f.Definition.Key == key), "aviation input and batch available");
                    DrainDispatcher(dispatcher);
                    var input = FindVisualChildren<TextBox>(window).Single(c => ReferenceEquals(c.DataContext, field) && c.IsVisible);
                    input.BringIntoView(); DrainDispatcher(dispatcher);
                    input.Text = helicopter ? "120" : "850";
                    input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); RunWithDispatcher(field.FlushAsync(), dispatcher);
                    Assert(field.ActiveDraft?.TargetValue == input.Text, "WPF binding persists draft");
                    if (professional && !helicopter)
                    {
                        var roll = units.Fields.Single(f => f.Key == "flight.rollSpeed");
                        FindVisualChildren<TextBox>(window).Single(c => ReferenceEquals(c.DataContext, roll) && c.IsVisible).BringIntoView(); DrainDispatcher(dispatcher);
                    }
                    window.UpdateLayout(); DrainDispatcher(dispatcher);
                    var scroller = FindVisualChildren<ScrollViewer>(window).FirstOrDefault(v => FindVisualChildren<TextBox>(v).Any(t => ReferenceEquals(t.DataContext, field)));
                    if (scroller is not null && !professional) { input.BringIntoView(); DrainDispatcher(dispatcher); scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 180); DrainDispatcher(dispatcher); }
                    Directory.CreateDirectory("publish/qa-2.10.1");
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render((FrameworkElement)window.Content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create($"publish/qa-2.10.1/aviation-{language}-{professional}-{helicopter}.png")) encoder.Save(stream);
                    RunWithDispatcher(units.UndoFieldAsync(field), dispatcher);
                    var speed = units.Fields.Single(f => f.Key == AviationMovement.Speed);
                    speed.EditValue = helicopter ? "260" : "600"; RunWithDispatcher(speed.FlushAsync(), dispatcher);
                    Assert(speed.ActiveDraft is not null, "integrated speed persists");
                    RunWithDispatcher(units.UndoFieldAsync(speed), dispatcher);
                }
                window.Close(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception ex) { app?.Shutdown(); completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task; Console.WriteLine("PASS aviation WPF controls, languages, modes, themes, widths and scaling"); }
        finally { DeleteTemporaryFixture(root); }
    }
}
