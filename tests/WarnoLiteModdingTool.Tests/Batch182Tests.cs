using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Strategic;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task AmmoNameTransaction182()
    {
        foreach(var nl in new[]{"\n","\r\n"}){
        var root=CreateTemporaryFixtureCopy("p4-shared");
        try{
            var local=Path.Combine(root,"GameData/Localisation/Names");Directory.CreateDirectory(local);
            File.WriteAllText(Path.Combine(local,"LocalisationDicos.ndf"),"unnamed TLocalisationDicoResource ( DicoToken = ~/LocalisationConstantes/dico_units FileName = 'GameData:/Localisation/Names/UNITS.csv' CanBeMissing = true )",new UTF8Encoding(false));
            var csv=Path.Combine(local,"UNITS.csv");File.WriteAllText(csv,"TOKEN;REFTEXT"+nl+"AMMONAME01;原名称"+nl,new UTF8Encoding(false));var csvBefore=File.ReadAllText(csv);
            var path=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/Ammunition.ndf");var source=File.ReadAllText(path).Replace("DescriptorId =", "Name = 'AMMONAME01'\n    DescriptorId =").Replace("\r\n","\n").Replace("\n",nl);File.WriteAllText(path,source,new UTF8Encoding(false));
            var missilePath=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/AmmunitionMissiles.ndf");File.WriteAllText(missilePath,File.ReadAllText(missilePath).Replace("DescriptorId =","Name = 'AMMONAME01'\n    DescriptorId ="),new UTF8Encoding(false));
            var (_,units,weapons)=await LoadP4Async(root);var ammo=weapons.Ammunition.First(a=>a.NameToken=="AMMONAME01");Assert(ammo.CanEditName&&ammo.DisplayName=="原名称","弹药CSV优先且名称可编辑");
            Assert(weapons.Ammunition.Count(a=>a.NameToken=="AMMONAME01")>=2,"夹具至少两个Ammo共用名称");
            var op=AmmoNames.Operation(units,ammo,"新;名称\"测试","WL18200001");using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(op);var other=weapons.Ammunition.First(a=>a.Name!=ammo.Name&&a.NameToken=="AMMONAME01");var second=AmmoNames.Operation(units,other,"第二名称","WL18200002");await store.UpsertAsync(second);Assert(File.ReadAllText(path)==source,"加入草稿不写正式文件");
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[op]);Assert(preview.Files.Any(f=>f.RelativePath.EndsWith("UNITS.csv")),"名称CSV和NDF同事务");File.SetAttributes(csv,File.GetAttributes(csv)|FileAttributes.ReadOnly);bool rolledBack=false;
            try{await service.CommitApplyAsync(preview,store);}catch(IOException){rolledBack=true;}finally{File.SetAttributes(csv,FileAttributes.Normal);}
            Assert(rolledBack&&File.ReadAllText(path)==source&&File.ReadAllText(csv)==csvBefore,"弹药名称事务提交失败全部回滚");
            preview=await service.PrepareApplyAsync(root,[op]);await service.CommitApplyAsync(preview,store);
            var (_,afterUnits,after)=await LoadP4Async(root);Assert(after.Ammo(ammo.Name)!.DisplayName==op.TargetValue,"特殊字符名称重读一致");Assert(after.Ammunition.Where(a=>a.Name!=ammo.Name&&a.NameToken=="AMMONAME01").All(a=>a.DisplayName=="原名称"),"共享旧token的其他弹药不变");Assert(File.ReadAllText(csv).StartsWith(csvBefore,StringComparison.Ordinal),"旧名称行无损保留");Assert(after.Ammo(ammo.Name)!.NameToken=="WL18200001","当前Ammo引用独立token");Assert(!File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[]{239,187,191}),"NDF无BOM");
            Assert(store.Operations.Count==1&&store.Operations[0].Id==second.Id,"部分应用保留另一改名草稿");preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            var bad=AmmoNames.Operation(afterUnits,after.Ammo(ammo.Name)!,"非法","SHORT");bool rejected=false;try{await service.PrepareApplyAsync(root,[bad]);}catch(TransactionValidationException){rejected=true;}Assert(rejected,"拒绝非10位token");
        }finally{DeleteTemporaryFixture(root);}}
    }
    private static void Verify182Ui(WarnoLiteModdingTool.App.ViewModels.MainViewModel vm,System.Windows.Window main)
    {
        vm.AdvancedMode=false;WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh-CN");
        Assert(WarnoLiteModdingTool.App.Localisation.GameText.Display("country","DDR")=="东德"&&WarnoLiteModdingTool.App.Localisation.GameText.Display("country","NEW")=="NEW","普通国家翻译与未知保留");
        var ammoVm=vm.AmmoWorkspace!;ammoVm.SelectedAmmo=ammoVm.Ammunition.First(a=>a.Ammo.CanEditName);
        var nameField=ammoVm.Fields.Single(f=>f.Field.Key=="ammo.name");Assert(nameField.IsTextEditor&&nameField.IsEditable,"弹药游戏内名称编辑器可用");vm.SelectedModule=vm.Modules.Single(m=>m.Key=="ammo");SaveUiSnapshot(main,"182-ammo-name.png");
        nameField.EditValue="弹药测试名称";RunWithDispatcher(nameField.FlushAsync(),main.Dispatcher);var token=nameField.Draft?.NameToken;Assert(token?.Length==10&&ammoVm.SelectedAmmo.DisplayName=="弹药测试名称","无需预览持久化改名并刷新列表");
        nameField.EditValue="弹药测试名称二";RunWithDispatcher(nameField.FlushAsync(),main.Dispatcher);Assert(nameField.Draft?.NameToken==token,"重编辑复用token");RunWithDispatcher(ammoVm.UndoFieldAsync(nameField),main.Dispatcher);Assert(nameField.Draft is null,"撤销名称草稿");
        var f=new WarnoLiteModdingTool.App.Controls.FacetFilter {UseTags=true,IsExpanded=true,Rows=[new("a",new Dictionary<string,string[]>{{"国家",["SOV"]},{"所属师",["师A"]},{"战斗角色",["Fighter"]}})]};
        var body=new System.Windows.Controls.StackPanel();body.Children.Add(f);body.Children.Add(f.SelectionSummary);var w=new System.Windows.Window {Content=body,Width=420,Height=600};
        var until=DateTime.UtcNow.AddSeconds(5);while(f.Content is null&&DateTime.UtcNow<until){DrainDispatcher(main.Dispatcher);System.Threading.Thread.Sleep(10);}SaveUiSnapshot(w,"182-tags.png");
        Assert(FindVisualChildren<System.Windows.Controls.Expander>(f).Where(e=>e!=f&&e.Header?.ToString()?.StartsWith("所属师")==true).All(e=>!e.IsExpanded),"所属师默认折叠");
        FindVisualChildren<System.Windows.Controls.CheckBox>(f).Single(c=>Equals(c.Content,"苏联")).IsChecked=true;Assert(f.SelectionSummary.Children.Count==1,"已选标签摘要独立显示");f.IsExpanded=false;SaveUiSnapshot(w,"182-tags-collapsed.png");w.Close();
        vm.AdvancedMode=true;Assert(WarnoLiteModdingTool.App.Localisation.GameText.Display("country","DDR")=="DDR","高级国家代码");vm.AdvancedMode=false;
        WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("en");Assert(WarnoLiteModdingTool.App.Localisation.GameText.Display("country","DDR")=="DDR","中文国名不改英文");WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh-CN");
    }
    private static Task StrategicDiff182()
    {
        var before=new StrategicState([new("c","第一连",false,[new("g","一排",false,[new("s","Pack1","Descriptor_Unit_Tank","",0,3)])])],new Dictionary<string,string>{{"InitialActionPoint","12"}},"第一营");
        var after=before with{Companies=[before.Companies[0] with{Groups=[before.Companies[0].Groups[0] with{Slots=[before.Companies[0].Groups[0].Slots[0] with{Count=4,Transport="Descriptor_Unit_Truck"}]}]}],PawnValues=new Dictionary<string,string>{{"InitialActionPoint","16"}}};
        var diff=StrategicDiff.Compare(before,after);Assert(diff.Count==3&&diff.Any(d=>d.Path.EndsWith("数量")&&d.Before=="3"&&d.After=="4")&&diff.Any(d=>d.Path.EndsWith("初始行动点")),"战略差异仅列实际改变且含旧值新值");Assert(StrategicDiff.Compare(before,before).Count==0,"相同状态无差异");Assert(StrategicDiff.Compare(before,before with{Companies=[]}).Any(d=>d.After=="—"),"删除编制可读");return Task.CompletedTask;
    }
}
