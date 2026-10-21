using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.SourceWorkspace;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static Task SourceNavigationContracts()
    {
        const string body = "U is TEntityDescriptor(ModulesDescriptors = [ TLandMovementModuleDescriptor(Maxspeed = 42), ])";
        var selector = new NdfFieldSelector("TLandMovementModuleDescriptor", "Maxspeed");
        foreach (var prefix in new[] { "", "// 中文\r\n\r\n", "/* U is TEntityDescriptor() */\n" })
        {
            var text = prefix + body;
            var obj = SourceNavigation.Object(text, "U", "TEntityDescriptor").Single();
            Assert(text.Substring(obj.Offset, obj.Length) == body, "object identity unaffected by prefix/comment/Unicode");
            var field = SourceNavigation.UnitField(text, "U", "TEntityDescriptor", selector).Single();
            Assert(text.Substring(field.Offset, field.Length) == "42", "field resolves in current revision");
        }
        Assert(SourceNavigation.Object(body + "\n" + body, "U").Count == 2, "duplicate declarations remain candidates");
        Assert(SourceNavigation.UnitField(body.Replace("Maxspeed = 42", "Maxspeed = 42 Maxspeed = 9"), "U", "TEntityDescriptor", selector).Count == 2, "duplicate fields are not guessed");
        Assert(SourceNavigation.Object(body, "Deleted").Count == 0, "deleted/renamed target not guessed");
        Assert(SourceNavigation.UnitField(body.Replace("42", "73"), "U", "TEntityDescriptor", selector).Count == 1, "edited value still locatable");
        const string weapons = "W is TWeaponManagerModuleDescriptor(TurretDescriptorList=[TTurretUnitDescriptor(MountedWeaponDescriptorList=[TMountedWeaponDescriptor(Ammunition=~/A AmmoBoxIndex=0 NbWeapons=2),TMountedWeaponDescriptor(Ammunition=~/B AmmoBoxIndex=1 NbWeapons=3)])])";
        var mount = new MountedWeaponRecord(1, 0, "TTurretUnitDescriptor", 1, "B", [], "", "", "", "");
        var def = new WeaponFieldDefinition("mount.1.count", "", "", "", WeaponFieldOwner.MountedWeapon, "NbWeapons", WeaponValueKind.Integer);
        var hit = SourceNavigation.WeaponField(weapons, "W", "TWeaponManagerModuleDescriptor", def, mount).Single();
        Assert(weapons.Substring(hit.Offset, hit.Length) == "3", "mount is identified by reference and box, not UI row number");
        Assert(SourceNavigation.Mount(weapons, "W", "TWeaponManagerModuleDescriptor", "A", 0).Count == 1, "mount object distinct from ammo");
        var csv = "TOKEN;\"中文\ncontinued\"\nOTHER;text\n";
        var row = SourceNavigation.Csv(csv, "TOKEN").Single();
        Assert(csv.Substring(row.Offset, row.Length).Contains("continued"), "CSV quoted multiline row preserved");
        const string division = "D is TDeckDivisionRule(UnitRuleList=[TDeckUniteRule(UnitDescriptor=~/U MaxPackNumber=2)])";
        Assert(SourceNavigation.DivisionMember(division, "D", "U").Count == 1, "division membership resolves to inline rule");
        Assert(SourceNavigation.Fragment("G is TGroup(List=[ ( 4 , /* note */ 3 ), ])", "G", "(4,3)", "member").Count == 1, "strategic membership survives whitespace and comments");
        Assert(SourceNavigation.Fragment("G is TGroup(List=[(5,3)])", "G", "(4,3)", "member").Count == 0, "changed membership never guessed by index");
        var scale = RuleCatalog.All.Single(d => d.Number == 55);
        var ruleText = "// moved\nunnamed TInitialisationGameDistanceUnits(LBUToGRUConversionFactor = 2.5)";
        Assert(SourceNavigation.Rule(ruleText, scale, "LBUToGRUConversionFactor").Count == 1, "rule without units navigates");
        Assert(SourceNavigation.Rule(ruleText.Replace("2.5", "OtherFactor * 2"), scale, "LBUToGRUConversionFactor").Count == 1, "view-only expression is still locatable");
        var terrain = "R is TGameplayTerrainsRegistration(Terrains=[TGameplayTerrain(Name='Forest' TerrainType='Wood' HeightInMeters=4)])";
        Assert(SourceNavigation.Terrain(terrain, "Forest", null).Count == 1 && SourceNavigation.Terrain(terrain, "Forest", "HeightInMeters").Count == 1, "inline terrain and read-only field navigate");
        return Task.CompletedTask;
    }

    private static async Task SourceNavigationUi()
    {
        var root = DeletionFixture(); RulesFixture184(root, "\n");
        var divisionRoot = CreateTemporaryFixtureCopy("p5-division");
        var strategicRoot = CreateTemporaryFixtureCopy("p2-unit-complete"); WriteStrategicFixture(strategicRoot, "\n");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                ThemeManager.Initialize(new UiThemeStore(Path.Combine(root, ".qa/theme.txt")));
                var vm = new MainViewModel(new RecentProjectStore(Path.Combine(root, ".qa/recent.json")));
                var owner = new App.MainWindow(vm) { Width = 1480, Height = 960, ShowActivated = false }; owner.Show();
                RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher); vm.AdvancedMode = true;
                vm.SelectedModule = vm.Modules.Single(m => m.Key == "units");
                var selected = vm.UnitWorkspace!.SelectedUnit!;
                var original = File.ReadAllBytes(Path.Combine(root, selected.Unit.Source.RelativeSourceFile));
                var path = selected.Unit.Source.RelativeSourceFile.Replace('\\', '/');
                new SourceSession([new(path, 1, 250)], null).Save(root);
                DrainDispatcher(dispatcher);
                var link = FindVisualChildren<SourceLink>(owner).Single(b => Equals(b.Tag, "source-unit"));
                Assert(link.IsVisible && link.IsEnabled, "object link visible and enabled in professional mode");
                RunWithDispatcher(SourceLink.OpenAsync(link, link.Target), dispatcher); DrainDispatcher(dispatcher);
                var window = owner.OwnedWindows.OfType<SourceWorkspaceWindow>().Single();
                var editor = window.ActiveEditor!;
                Assert(editor.SelectedText.Contains(selected.InternalName) && editor.SelectionLength > 0, "explicit object jump overrides restored tab position and selects source");
                Assert(vm.SourceDraftCount == 0 && File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "navigation produces no source draft or formal write");
                editor.Document.Insert(0, "// unsaved prefix 中文\n");
                var changed = editor.Text;
                RunWithDispatcher(SourceLink.OpenAsync(link, link.Target), dispatcher); DrainDispatcher(dispatcher);
                Assert(ReferenceEquals(window, owner.OwnedWindows.OfType<SourceWorkspaceWindow>().Single()) && ReferenceEquals(editor, window.ActiveEditor), "existing window/tab/buffer reused");
                Assert(editor.Text == changed && editor.SelectedText.Contains(selected.InternalName) && editor.SelectionStart >= 21, "unsaved text retained and target relocated");
                Assert(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "source draft not applied during navigation");
                vm.UnitWorkspace.TextFilter = "not-the-original-filter";
                ((Button)window.FindName("ReturnToBusiness")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunWithDispatcher(Task.Delay(100), dispatcher);
                Assert(vm.UnitWorkspace.TextFilter == "" && vm.UnitWorkspace.SelectedUnit == selected, "return restores captured filter and selected object");
                var page = FindVisualChildren<App.Workspaces.UnitWorkspaceView>(owner).Single();
                var dataRow = FindVisualChildren<DataGridRow>(page).First(r => r.DataContext is App.ViewModels.Units.UnitListItemViewModel);
                var menuConstructor = typeof(ContextMenuEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).First(c => c.GetParameters().Length == 2);
                var menuEvent = (ContextMenuEventArgs)menuConstructor.Invoke([dataRow, true]);
                page.RaiseEvent(menuEvent);
                Assert(menuEvent.Handled && page.ContextMenu.IsOpen && page.ContextMenu.Items.Count == 1, "business row context menu exposes source command");
                page.ContextMenu.IsOpen = false;
                var detachedLink = new SourceLink { Target = selected };
                var detached = new Window { Owner = owner, Content = detachedLink, Width = 320, Height = 180, ShowActivated = false };
                detached.Show(); DrainDispatcher(dispatcher);
                Assert(detachedLink.IsVisible && SourceLink.Host(detachedLink) == owner, "detached panel resolves professional mode and project host");
                RunWithDispatcher(SourceLink.OpenAsync(detachedLink, selected), dispatcher); detached.Close();
                var returned = false;
                RunWithDispatcher(window.NavigateBusinessAsync(BusinessSourceTargets.Object(selected.Unit.Source), () => returned = true), dispatcher);
                ((Button)window.FindName("ReturnToBusiness")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); DrainDispatcher(dispatcher);
                Assert(returned, "return action is accessible");
                var field = vm.UnitWorkspace.Fields.First(f => f.Field?.Location is not null);
                var target = BusinessSourceTargets.Create(vm, field).Single();
                RunWithDispatcher(window.NavigateBusinessAsync(target, () => {}), dispatcher);
                Assert(editor.SelectionLength > 0 && editor.SelectionLength < selected.Unit.Source.CharacterLength, "field link selects a parameter rather than whole object");
                vm.AdvancedMode = false; DrainDispatcher(dispatcher);
                Assert(!link.IsVisible, "new business link hidden in basic mode"); vm.AdvancedMode = true;
                Directory.CreateDirectory("publish/qa-2.11.9/source-navigation");
                foreach (var language in new[] { "zh-CN", "en" })
                foreach (var theme in Enum.GetValues<AppTheme>())
                {
                    UiText.Current.SetLanguage(language); ThemeManager.ApplyTheme(theme, false);
                    owner.Width = language == "en" ? 1220 : 1480;
                    ((FrameworkElement)owner.Content).LayoutTransform = new ScaleTransform(language == "en" ? 1.1 : 1, language == "en" ? 1.1 : 1);
                    DrainDispatcher(dispatcher); owner.UpdateLayout();
                    Assert(FindVisualChildren<SourceLink>(owner).Any(b => b.IsVisible && Equals(b.Content, "</>")), "compact field caption survives control template");
                    Assert(link.Content?.ToString() == (language == "en" ? "Show in workspace" : "在工作台中查看"), "source action translates");
                    Assert(((Button)window.FindName("ReturnToBusiness")).Content?.ToString() == (language == "en" ? "Return to source page" : "返回来源页面"), "return action translates");
                    Snapshot(owner, $"unit-{language}-{theme}.png"); Snapshot(window, $"workspace-{language}-{theme}.png");
                }
                UiText.Current.SetLanguage("zh-CN"); ThemeManager.ApplyTheme(AppTheme.LightBlue, false);
                ((FrameworkElement)owner.Content).LayoutTransform = Transform.Identity; owner.Width = 1480;
                foreach (var module in new[] { "weapons", "ammo", "divisions", "strategic", "sp", "rules" })
                {
                    if (vm.Modules.FirstOrDefault(m => m.Key == module) is not {} entry) continue;
                    vm.SelectedModule = entry; DrainDispatcher(dispatcher);
                    if (module == "rules")
                    {
                        for (var depth = 0; depth < 2; depth++) { foreach (var expander in FindVisualChildren<Expander>(owner).ToArray()) expander.IsExpanded = true; DrainDispatcher(dispatcher); }
                        var group = vm.RulesWorkspace!.Groups.First(g => g.Cells.Count > 0);
                        var ruleTarget = BusinessSourceTargets.Create(vm, new RuleSourceTarget(group, group.Cells[0])).Single();
                        RunWithDispatcher(window.NavigateBusinessAsync(ruleTarget, () => {}), dispatcher);
                        Assert(window.ActiveEditor!.SelectionLength > 0, "independent rule field navigates through actual workspace");
                    }
                    owner.UpdateLayout(); Snapshot(owner, module + ".png");
                }
                RunWithDispatcher(window.SaveAllAsync(), dispatcher); window.Close(); DrainDispatcher(dispatcher);
                foreach (var fixture in new[] { divisionRoot, strategicRoot })
                {
                    RunWithDispatcher(vm.OpenProjectAsync(fixture), dispatcher); vm.AdvancedMode = true;
                    var modules = fixture == divisionRoot ? new[] { "divisions" } : new[] { "strategic", "sp" };
                    foreach (var module in modules)
                    {
                        vm.SelectedModule = vm.Modules.Single(m => m.Key == module); DrainDispatcher(dispatcher);
                        var targets = new List<BusinessSourceTarget>();
                        if (module == "divisions")
                        {
                            Assert(vm.DivisionWorkspace is not null, "complete division page loaded");
                            targets.AddRange(BusinessSourceTargets.Create(vm, vm.DivisionWorkspace!.SelectedDivision));
                            targets.AddRange(BusinessSourceTargets.Create(vm, vm.DivisionWorkspace.SelectedRule));
                            targets.AddRange(BusinessSourceTargets.Create(vm, vm.DivisionWorkspace.SelectedPack));
                        }
                        else if (module == "strategic")
                        {
                            Assert(vm.StrategicWorkspace is not null, "strategic page loaded");
                            targets.AddRange(BusinessSourceTargets.Create(vm, vm.StrategicWorkspace!.Selected));
                            foreach (var company in vm.StrategicWorkspace.Companies)
                            { targets.AddRange(BusinessSourceTargets.Create(vm, company)); foreach (var group in company.Children) { targets.AddRange(BusinessSourceTargets.Create(vm, group)); foreach (var slot in group.Children) targets.AddRange(BusinessSourceTargets.Create(vm, slot)); } }
                        }
                        else targets.AddRange(BusinessSourceTargets.Create(vm, vm.StrategicWorkspace!.Packs.Selected));
                        Assert(targets.Count > 0, module + " exposes source targets");
                        foreach (var candidate in targets) Assert(candidate.Locate(File.ReadAllText(Path.Combine(fixture, candidate.Path))).Count > 0, module + " source target exists: " + candidate.Label);
                        Snapshot(owner, module + "-complete.png");
                    }
                }
                owner.Close(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception ex) { app?.Shutdown(); completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task; Console.WriteLine("PASS source navigation WPF controls, buffer relocation, session priority, return, mode, language, themes and screenshots"); }
        finally { thread.Join(); DeleteTemporaryFixture(root); DeleteTemporaryFixture(divisionRoot); DeleteTemporaryFixture(strategicRoot); }

        static void Snapshot(Window window, string name)
        {
            window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine("publish/qa-2.11.9/source-navigation", name)); encoder.Save(stream);
        }
    }
}
