using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Rules;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static void Verify196Ui(MainViewModel vm,Window main,string root,string divisionRoot)
    {
        main.ShowActivated=false;main.ShowInTaskbar=false;main.Left=-10000;main.Top=-10000;main.Show();
        File.AppendAllText(Path.Combine(root,WarnoLiteModdingTool.Core.Strategic.StrategicLoader.DirectoryPath+"StrategicPacks.ndf"),"\nPack_B is DeckPackDescriptor ( Unit = $/GFX/Unit/Descriptor_Unit_Test_Tank_US )\n");
        WriteAir196(root,"\n");RunWithDispatcher(vm.OpenProjectAsync(root),main.Dispatcher);vm.AdvancedMode=false;
        vm.SelectedModule=vm.Modules.Single(m=>m.Key=="sp");DrainDispatcher(main.Dispatcher);SaveUiSnapshot(main,"196-sp.png");
        Assert(vm.IsPackModule && !vm.IsObjectModule && vm.StrategicWorkspace!.Packs.Items.Count>0,"SP独立浏览页");
        var sp=FindVisualChildren<StrategicPackView>(main).Single();
        Assert(!FindVisualChildren<Button>(sp).Any(b=>Equals(b.Content,"编辑 Pack / 重命名")),"SP使用就地编辑");
        var packVm=vm.StrategicWorkspace!.Packs;var pack=packVm.Selected!;
        var nameEditor=FindVisualChildren<TextBox>(sp).Single(t=>t.Text==pack.Name);
        nameEditor.Text=pack.Name+"_mod_patch";DrainDispatcher(main.Dispatcher);
        Assert(packVm.PendingEdits[pack.Id].Name.EndsWith("_mod_patch"),"SP未保存输入保存在工作区");
        FindVisualChildren<Button>(sp).Single(b=>Equals(b.Content,"撤销编辑")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(!packVm.PendingEdits.ContainsKey(pack.Id),"SP撤销编辑恢复已保存值");
        var packList=FindVisualChildren<ListBox>(sp).Single(l=>ReferenceEquals(l.ItemsSource,packVm.View));
        foreach(var action in new[]{"取消","放弃并切换","加入草稿并切换"})
        {
            pack=packVm.Selected!;var target=packVm.Items.First(p=>p.Id!=pack.Id);
            DrainDispatcher(main.Dispatcher);
            FindVisualChildren<TextBox>(sp).Single(t=>t.Text==packVm.PendingEdits.GetValueOrDefault(pack.Id,pack.State).Name).Text=pack.Name+"_mod_patch";
            Exception? dialogError=null;var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
            timer.Tick+=(_,_)=>{timer.Stop();var dialog=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.Title=="未保存编辑");try{Assert(dialog is not null,"SP切换弹出未保存编辑选择");FindVisualChildren<Button>(dialog!).Single(b=>Equals(b.Content,action)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}catch(Exception ex){dialogError=ex;dialog?.Close();}};
            timer.Start();packList.SelectedItem=target;RunWithDispatcher(Task.Delay(200),main.Dispatcher);timer.Stop();if(dialogError is not null)throw dialogError;
            var saveDeadline=DateTime.UtcNow.AddSeconds(5);while(!sp.IsEnabled&&DateTime.UtcNow<saveDeadline)RunWithDispatcher(Task.Delay(20),main.Dispatcher);
            Assert(packVm.Selected!.Id==(action=="取消"?pack.Id:target.Id),"SP切换选择结果："+action+"，当前="+packVm.Selected!.Id+"，原="+pack.Id+"，目标="+target.Id);
            Assert(packVm.PendingEdits.ContainsKey(pack.Id)==(action=="取消"),"SP切换保留或清除临时输入");
            if(action=="加入草稿并切换")Assert(packVm.Items.Single(p=>p.Id==pack.Id).Status=="有草稿","SP保存后切换确实产生草稿");
        }
        SaveUiSnapshot(main,"196-sp-patch.png");
        var originalWidth=main.Width;main.Width=900;DrainDispatcher(main.Dispatcher);
        var back=FindVisualChildren<Button>(sp).Single(b=>Equals(b.Content,"返回列表"));if(back.IsVisible)back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));DrainDispatcher(main.Dispatcher);
        Assert(FindVisualChildren<Button>(sp).Single(b=>Equals(b.Content,"查看详情")).IsVisible,"SP窄窗口列表入口");
        FindVisualChildren<Button>(sp).Single(b=>Equals(b.Content,"查看详情")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));DrainDispatcher(main.Dispatcher);
        Assert(FindVisualChildren<Button>(sp).Single(b=>Equals(b.Content,"返回列表")).IsVisible,"SP窄窗口详情返回入口");
        SaveUiSnapshot(main,"196-sp-narrow.png");main.Width=originalWidth;DrainDispatcher(main.Dispatcher);
        Assert(FindVisualChildren<DataGrid>(sp).Single().Columns.All(c=>c.ActualWidth>65),"SP引用列具有可读宽度");SaveUiSnapshot(main,"196-sp-patch.png");
        vm.SelectedModule=vm.Modules.Single(m=>m.Key=="rules");vm.RulesWorkspace!.Category="空军";vm.RulesWorkspace.Refresh();DrainDispatcher(main.Dispatcher);
        Assert(vm.RulesWorkspace.View.Cast<RuleGroupViewModel>().Select(g=>g.Group.Definition.Number).SequenceEqual(new[]{54}),"普通空军只有布局");
        var view=FindVisualChildren<RulesView>(main).Single();
        for(var depth=0;depth<2;depth++){foreach(var expander in FindVisualChildren<Expander>(view).ToArray())expander.IsExpanded=true;DrainDispatcher(main.Dispatcher);}
        var category=FindVisualChildren<Expander>(view).Single(e=>Equals(e.Tag,"空军"));
        Assert(category.Content is StackPanel children && children.Children.OfType<Expander>().Single().Tag is 54,"大类包含规则小类");
        vm.RulesWorkspace.Refresh();DrainDispatcher(main.Dispatcher);
        Assert(FindVisualChildren<Expander>(view).Single(e=>Equals(e.Tag,"空军")).IsExpanded,"刷新保留大类展开状态");
        SaveUiSnapshot(main,"196-air-basic.png");
        Assert(!FindVisualChildren<TextBox>(view).Any(t=>t.DataContext is RuleCellViewModel),"容量不提供自由数值编辑");
        vm.AdvancedMode=true;Assert(vm.RulesWorkspace.View.Cast<RuleGroupViewModel>().Count()==4,"专业显示三项飞行规则");
        for(var depth=0;depth<2;depth++){foreach(var expander in FindVisualChildren<Expander>(view).ToArray())expander.IsExpanded=true;DrainDispatcher(main.Dispatcher);}
        SaveUiSnapshot(main,"196-air-professional.png");vm.AdvancedMode=false;
        vm.RulesWorkspace.Category="全部";vm.RulesWorkspace.Refresh();DrainDispatcher(main.Dispatcher);
        var air=FindVisualChildren<Expander>(view).Single(e=>Equals(e.Tag,"空军"));air.IsExpanded=false;
        vm.RulesWorkspace.Search="空军";vm.RulesWorkspace.Refresh();DrainDispatcher(main.Dispatcher);
        Assert(FindVisualChildren<Expander>(view).Single(e=>Equals(e.Tag,"空军")).IsExpanded,"搜索展开命中大类");
        vm.RulesWorkspace.Search="";vm.RulesWorkspace.Refresh();DrainDispatcher(main.Dispatcher);
        Assert(!FindVisualChildren<Expander>(view).Single(e=>Equals(e.Tag,"空军")).IsExpanded,"清空搜索恢复折叠状态");
        SaveUiSnapshot(main,"196-rules-categories.png");
        var filter=new FacetFilter {Rows=[new("A",new Dictionary<string,string[]> { ["国家"]=["DDR"] }),new("B",new Dictionary<string,string[]> { ["国家"]=["US"] })]};
        var deadline=DateTime.UtcNow.AddSeconds(5);while(filter.Content is null&&DateTime.UtcNow<deadline){DrainDispatcher(main.Dispatcher);Thread.Sleep(10);}
        // Exercise actual modal opening and closing, then reopen to check persisted selection.
        for(var pass=0;pass<2;pass++)
        {
            Exception? failure=null;var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
            timer.Tick+=(_,_)=>{timer.Stop();var w=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.Title=="筛选");try{Assert(w is not null,"筛选呼出窗口");SaveUiSnapshot(w!,"196-filter.png");var boxes=FindVisualChildren<CheckBox>(w!).ToArray();var box=boxes.Single(c=>Equals(c.Content,"东德"));if(pass==0)box.IsChecked=true;else Assert(box.IsChecked==true,"重开保留筛选条件");Assert(filter.Matches("A")&&!filter.Matches("B"),"窗口筛选生效");}catch(Exception ex){failure=ex;}finally{w?.Close();}};
            timer.Start();filter.Open();timer.Stop();if(failure is not null)throw failure;
        }
        filter.ClearFilters!();DrainDispatcher(main.Dispatcher);Assert(filter.Matches("A")&&filter.Matches("B"),"清空筛选");
        var emblemFile=Path.Combine(divisionRoot,"GameData/Assets/emblem.png");Directory.CreateDirectory(Path.GetDirectoryName(emblemFile)!);File.Copy(Path.Combine(root,"test-background.png"),emblemFile,true);
        var textureFile=Path.Combine(divisionRoot,"GameData/Generated/UserInterface/Textures/Emblem.ndf");Directory.CreateDirectory(Path.GetDirectoryName(textureFile)!);File.WriteAllText(textureFile,"Texture_Division_Emblem_Test is TUIResourceTexture_Common ( FileName = 'GameData:/Assets/emblem.png' )",new System.Text.UTF8Encoding(false));
        var divisions=Path.Combine(divisionRoot,"GameData/Generated/Gameplay/Decks/Divisions.ndf");var divisionText=File.ReadAllText(divisions);if(!divisionText.Contains("EmblemTexture"))File.WriteAllText(divisions,divisionText.Replace("CfgName =","EmblemTexture = 'Texture_Division_Emblem_Test'\n    CfgName ="),new System.Text.UTF8Encoding(false));
        RunWithDispatcher(vm.OpenProjectAsync(divisionRoot),main.Dispatcher);vm.SelectedModule=vm.Modules.Single(m=>m.Key=="divisions");DrainDispatcher(main.Dispatcher);RunWithDispatcher(Task.Delay(120),main.Dispatcher);SaveUiSnapshot(main,"196-divisions.png");
        Assert(vm.DivisionWorkspace!.DivisionFilterRows.All(r=>r.Values.ContainsKey("国家")),"战术师国家筛选");
        var emblem=FindVisualChildren<DivisionEmblem>(main).First();var end=DateTime.UtcNow.AddSeconds(5);while(emblem.Source is null&&DateTime.UtcNow<end){DrainDispatcher(main.Dispatcher);Thread.Sleep(10);}Assert(emblem.Source is not null,"战术师实际加载当前Mod师徽");SaveUiSnapshot(main,"196-divisions.png");
        var grid=FindVisualChildren<DataGrid>(main).Single(g=>ReferenceEquals(g.ItemsSource,vm.DivisionWorkspace.RulesView));var numbers=grid.Columns.OfType<DataGridTextColumn>().Where(c=>c.Binding is System.Windows.Data.Binding b && b.Path.Path is "MaxPackNumber" or "NumberOfUnitInPack").ToArray();
        Assert(numbers.Length==2&&numbers.All(c=>c.EditingElementStyle is not null),"两种数量编辑器统一居中");
        grid.SelectedItem=grid.Items[0];grid.CurrentCell=new DataGridCellInfo(grid.Items[0],numbers[1]);Assert(grid.BeginEdit(),"可以进入数量单元格编辑");DrainDispatcher(main.Dispatcher);
        var editor=FindVisualChildren<TextBox>(grid).First(t=>t.TextAlignment==TextAlignment.Center);Assert(editor.VerticalContentAlignment==VerticalAlignment.Center,"编辑数值水平垂直居中");SaveUiSnapshot(main,"196-division-edit.png");grid.CancelEdit();
        RunWithDispatcher(vm.OpenProjectAsync(root),main.Dispatcher);
    }
}
