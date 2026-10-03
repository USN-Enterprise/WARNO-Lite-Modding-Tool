using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task DamageUiTest()
    {
        var root=DamageFixture();var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            WarnoLiteModdingTool.App.App? app=null;
            try
            {
                app=new(launchWorkspace:false);app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
                var dispatcher=System.Windows.Threading.Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
                App.Theming.ThemeManager.Initialize(new App.Theming.UiThemeStore(Path.Combine(root,".qa/theme.txt")));
                var vm=new App.ViewModels.MainViewModel(new Core.Projects.RecentProjectStore(Path.Combine(root,".qa/recent.json")));
                var host=new App.MainWindow(vm){Width=1380,Height=1000};host.Show();DrainDispatcher(dispatcher);RunWithDispatcher(vm.OpenProjectAsync(root),dispatcher);
                var load=LoadP4Async(root);RunWithDispatcher(load,dispatcher);var weapons=load.Result.Item3;var damage=new DamageWorkspace(root);
                using var store=new DraftStore(root);RunWithDispatcher(store.LoadAsync(),dispatcher);
                T Find<T>(Window w,string tag)where T:FrameworkElement=>FindVisualChildren<T>(w).Single(e=>Equals(e.Tag,tag));
                void Click(Window w,string tag){Find<Button>(w,tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));DrainDispatcher(dispatcher);}
                async Task Until(Func<bool> condition)
                {
                    for(var n=0;n<500&&!condition();n++)await Task.Delay(10);
                    Assert(condition(),"WPF操作完成");
                }
                foreach(var lang in new[]{"zh-CN","en"})
                foreach(var pro in new[]{false,true})
                {
                    vm.AdvancedMode=pro;UiText.Current.SetLanguage(lang);
                    App.Theming.ThemeManager.ApplyTheme(pro?App.Theming.AppTheme.DarkBlue:App.Theming.AppTheme.LightBlue,false);
                    var window=new DamageAmmoWindow(damage,weapons,store,[weapons.Ammo(A1911)!],DraftEditScope.CurrentUnit,[U1911],()=>{}){Owner=host,Width=lang=="en"?960:1120,Height=820};
                    ((FrameworkElement)window.Content).LayoutTransform=new ScaleTransform(lang=="en"?1.15:1,lang=="en"?1.15:1);
                    window.Show();DrainDispatcher(dispatcher);
                    Assert(Find<TextBox>(window,"damage-ap").IsReadOnly==!pro,"普通AP只读，专业可编辑");
                    Assert(FindVisualChildren<ComboBox>(window).Any(c=>Equals(c.Tag,"damage-reference"))==pro,"已有引用切换仅专业");
                    Find<TextBox>(window,"damage-distance").Text="500";Click(window,"damage-distance-preview");Click(window,"damage-distance-save");
                    RunWithDispatcher(Until(()=>store.Operations.Any(o=>o.TargetKind==DraftTargetKind.DamageDistance)),dispatcher);
                    Assert(Core.Weapons.DamageDistance.Read(store.Operations.Single()).Distance=="500","距离输入经实际按钮保存");
                    SaveDamageSnapshot(window,$"distance-{lang}-{pro}");
                    var tabs=FindVisualChildren<TabControl>(window).Single();tabs.SelectedIndex=1;DrainDispatcher(dispatcher);
                    Assert(FindVisualChildren<TextBlock>(window).Any(t=>t.Text.Contains(" = 25")),"目标正面静态矩阵系数");
                    SaveDamageSnapshot(window,$"query-{lang}-{pro}");tabs.SelectedIndex=0;DrainDispatcher(dispatcher);
                    Click(window,"damage-distance-undo");RunWithDispatcher(Until(()=>store.Operations.Count==0),dispatcher);window.Close();
                    if(!pro)continue;
                    var rules=new DamageRulesWindow(damage,store,()=>{}){Owner=host,Width=lang=="en"?960:1180,Height=900};
                    ((FrameworkElement)rules.Content).LayoutTransform=new ScaleTransform(lang=="en"?1.15:1,lang=="en"?1.15:1);
                    rules.Show();DrainDispatcher(dispatcher);var grid=FindVisualChildren<DataGrid>(rules).Single();
                    Find<TextBox>(rules,"damage-attack-to").Text="2";Find<TextBox>(rules,"damage-resistance-to").Text="3";DrainDispatcher(dispatcher);
                    Assert(grid.Items.Count==6,"攻击/抗性范围筛出矩形选区");grid.SelectedItem=grid.Items[1];
                    Find<TextBox>(rules,"damage-operand").Text="3.5";Click(rules,"damage-preview");SaveDamageSnapshot(rules,$"matrix-{lang}");Click(rules,"damage-save");
                    RunWithDispatcher(Until(()=>store.Operations.Any(o=>o.TargetKind==DraftTargetKind.DamageRule&&o.FieldKey=="matrix")),dispatcher);
                    Assert(DamageWorkspace.Read(store.Operations.Single()).Changes.Single().After=="3.5","矩阵实际选区与预览保存");
                    var ruleTabs=FindVisualChildren<TabControl>(rules).Single();ruleTabs.SelectedIndex=1;DrainDispatcher(dispatcher);
                    Find<TextBox>(rules,"damage-shared-distance").Text="600";Click(rules,"damage-shared-save");
                    RunWithDispatcher(Until(()=>store.Operations.Count==2),dispatcher);SaveDamageSnapshot(rules,$"shared-{lang}");rules.Close();
                    vm.AdvancedMode=false;Assert(store.Operations.Count==2,"模式切换保留专业草稿");RunWithDispatcher(store.ClearAsync(),dispatcher);
                }
                foreach(var lang in new[]{"zh-CN","en"})
                foreach(var module in new[]{"ammo","weapons","rules"})
                {
                    vm.AdvancedMode=module=="rules";UiText.Current.SetLanguage(lang);
                    App.Theming.ThemeManager.ApplyTheme(module=="rules"?App.Theming.AppTheme.DarkBlue:App.Theming.AppTheme.LightBlue,false);
                    vm.SelectedModule=vm.Modules.Single(m=>m.Key==module);
                    if(module=="rules")vm.RulesWorkspace!.Category="伤害与抗性规则";
                    DrainDispatcher(dispatcher);SaveDamageSnapshot(host,$"entry-{module}-{lang}");
                    var opened=false;var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
                    timer.Tick+=(_,_)=>
                    {
                        var dialog=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w is DamageRulesWindow or DamageAmmoWindow);
                        if(dialog is null)return;opened=true;dialog.Close();timer.Stop();
                    };
                    timer.Start();
                    RunWithDispatcher(module=="ammo"?vm.AmmoWorkspace!.OpenDamageAsync(host):module=="weapons"?vm.WeaponWorkspace!.OpenDamageAsync(host):vm.RulesWorkspace!.OpenDamageAsync(host),dispatcher);
                    timer.Stop();Assert(opened,"工作区入口可以打开对应编辑器");
                }
                host.Close();app.Shutdown();completion.SetResult();
            }
            catch(Exception e){app?.Shutdown();completion.SetException(e);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        try{await completion.Task;Console.WriteLine("PASS damage WPF input, preview, drafts, queries, modes, languages, themes, widths and scaling");}
        finally{DeleteTemporaryFixture(root);}
    }
    private static void SaveDamageSnapshot(Window window,string name)
    {
        DrainDispatcher(window.Dispatcher);window.UpdateLayout();var surface=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("publish/qa-1.9.21");
        using var output=File.Create("publish/qa-1.9.21/"+name+".png");encoder.Save(output);
    }
}
