using System.Globalization;
using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static string RulesFixture184(string root,string nl)
    {
        var dir=Path.Combine(root,"GameData/Gameplay/Constantes");Directory.CreateDirectory(Path.Combine(dir,"Strategic"));
        var g="""
// preserve { [ ) quoted text
Constantes is TTunableConstante (
 DefaultTimeLimitInMinutes = 40
 TimeLimitTable = [20, 40, 0]
)
WargameConstantes is TWargameTunableConstante (
 DefaultArgentInitial = 1500
 ArgentInitialSetting = [500, 1500, 3000]
 DefaultDestructionScoreToReachSetting = [3000, 0]
 VictoryTypeDestructionLevelsTable = MAP [(500, [3000,0]), (1500, [3000,0]), (3000,[3000,0]), (750,[2000,0]),]
 BaseIncome = MAP [(ECombatRule/Conquest, 260), (ECombatRule/Destruction, 4), (ECombatRule/Unknown, 999),]
 UpkeepPercentAvailableSettings = [0,5,7]
 UpkeepPercentDefaultSetting = 0
 Unapproved = 123 // unchanged
 Quoted = 'BaseIncome = MAP [ ( fake ) ]'
)
""";
        File.WriteAllText(Path.Combine(dir,"GDConstants.ndf"),g.Replace("\n",nl),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir,"Transport.ndf"),"// DefaultTransportLoadRadius is 99"+nl+"DefaultTransportLoadRadius is 78 // retain"+nl,new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir,"Strategic/StrategicFatigueConstants.ndf"),"StrategicMaxFatiguePerUnit is 8"+nl+"StrategicEmptyActionPointsOnMaxFatigueRout is true"+nl+"StrategicBattleAttackerFatigueGainAfterBattle is MAP [(EVictoryType/Draw,2),(EVictoryType/TotalVictory,2),]"+nl,new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir,"Strategic/GDConstants.ndf"),("""
StrategicConstantes is TStrategicTunableConstante (
 BattleNbMaxPawnByRole = [2,1,1,1]
 DefaultStartingTicketsPointsByPawnNumber = [[0,40,50,55,60,65],[0,40,50,55,60,65]]
 DefaultTicketsPointsIncomeByPawnNumber = [[0,6,7,8,9,10],[0,6,7,8,9,10]]
 MaxTicketFactorByPawnNumber = [2,1.6,1.5,1.4,1.3]
 UpkeepPercentByPawnNumber = [5,5,5,5,5]
 Unapproved = 77
)
""").Replace("\n",nl),new UTF8Encoding(false));
        return dir;
    }
    private static async Task RulesTransaction184()
    {
        Assert(RuleCatalog.All.Count==50&&RuleCatalog.All.Count(d=>d.Basic)==32,"严格限定50/32项");
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=Path.Combine(Path.GetTempPath(),"wl-rules184-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                var dir=RulesFixture184(root,nl);var workspace=RuleWorkspace.Load(root);
                Assert(workspace.Groups.Single(g=>g.Definition.Number==31).Cells.Single().Raw=="78","顶层别名忽略注释");
                Assert(!workspace.Groups.Single(g=>g.Definition.Number==32).CanEdit,"缺文件只降级该项");
                DraftOperation Op(int n,Func<RuleCell,string> target){var g=workspace.Groups.Single(g=>g.Definition.Number==n);return workspace.Operation(g,g.Cells.ToDictionary(c=>c.Key,target));}
                var income=Op(7,_=>"300");var slot=Op(33,c=>c.Key.EndsWith("/0")?"3":c.Raw);var money=Op(2,c=>c.Raw=="500"?"600":c.Raw);var fatigue=Op(46,_=>"false");var alias=Op(31,_=>"80");
                using var store=new DraftStore(root);await store.LoadAsync();await store.ApplyBatchAsync([income,slot,money,fatigue,alias],[]);
                var original=File.ReadAllText(Path.Combine(dir,"GDConstants.ndf"));
                var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);
                Assert(File.ReadAllText(Path.Combine(dir,"GDConstants.ndf"))==original,"规则预览不写文件");
                var candidate=Encoding.UTF8.GetString(preview.Files.Single(f=>f.RelativePath.EndsWith("Constantes/GDConstants.ndf")).CandidateBytes);
                Assert(candidate.Contains("(ECombatRule/Destruction, 4)")&&candidate.Contains("(ECombatRule/Unknown, 999)")&&candidate.Contains("Unapproved = 123 // unchanged"),"只改指定MAP数值和必要关联");
                Assert(candidate.Contains("(600, [3000, 0])")&&candidate.Contains("(750,[2000,0])"),"新增资金映射且保留未知旧选项");
                var strategic=Encoding.UTF8.GetString(preview.Files.Single(f=>f.RelativePath.EndsWith("Strategic/GDConstants.ndf")).CandidateBytes);var doc=new NdfSyntaxDocument(strategic);
                var arrays=doc.ReadArrayElements(RuleWorkspace.Locate(doc,"TStrategicTunableConstante","DefaultStartingTicketsPointsByPawnNumber"));
                Assert(arrays.All(a=>doc.ReadArrayElements(a).Count==7),"参战名额联动攻守表长");
                Assert(doc.ReadArrayElements(RuleWorkspace.Locate(doc,"TStrategicTunableConstante","MaxTicketFactorByPawnNumber")).Count==6,"同步隐藏关联表");
                Assert(workspace.Resolve(income with{TargetRaw="{\"Unapproved\":\"7\"}",TargetValue="{\"Unapproved\":\"7\"}"}).Status==DraftResolutionStatus.Conflict,"拒绝非白名单格子");
                var rejected=false;try{await service.PrepareApplyAsync(root,[Op(1,_=>"999")]);}catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException){rejected=true;}Assert(rejected,"默认资金须属于选项");
                File.SetAttributes(Path.Combine(dir,"Transport.ndf"),FileAttributes.ReadOnly);
                try{await service.CommitApplyAsync(preview,store);}catch(IOException){}finally{File.SetAttributes(Path.Combine(dir,"Transport.ndf"),FileAttributes.Normal);}
                Assert(File.ReadAllText(Path.Combine(dir,"GDConstants.ndf"))==original,"规则事务失败回滚");
                preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
                Assert(RuleWorkspace.Load(root).Groups.Single(g=>g.Definition.Number==7).Cells.Single().Raw=="300","规则可提交重载");
                Assert(!File.ReadAllBytes(Path.Combine(dir,"GDConstants.ndf")).Take(3).SequenceEqual(new byte[]{239,187,191}),"无BOM");
                if(nl=="\r\n")Assert(!File.ReadAllText(Path.Combine(dir,"GDConstants.ndf")).Replace("\r\n","").Contains('\n'),"保留CRLF");
            }
            finally{Directory.Delete(root,true);}
        }
    }
    private static async Task UnitVisionAndSpecialties184()
    {
        foreach(var nl in new[]{"\n","\r\n"}){
        var root=CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var path=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");var source=File.ReadAllText(path).Replace("\r\n","\n").Replace("\n",nl);File.WriteAllText(path,source,new UTF8Encoding(false));
            var data=await LoadWorkspaceAsync(root);var unit=data.Units.First(u=>u.Field("recon.optics.standard")?.CanEdit==true);
            var ratio=VisionRatio.Preview(unit,"recon.optics.standard","1768",[]);
            var low=decimal.Parse(unit.Field("recon.optics.low")!.DisplayValue,CultureInfo.InvariantCulture);var standard=decimal.Parse(unit.Field("recon.optics.standard")!.DisplayValue,CultureInfo.InvariantCulture);
            Assert(ratio.Upserts.Single(o=>o.FieldKey=="recon.optics.low").TargetValue==Math.Round(low*1768/standard,0,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture),"原始比例取整");
            var again=VisionRatio.Preview(unit,"recon.optics.standard","2651",ratio.Upserts);var direct=VisionRatio.Preview(unit,"recon.optics.standard","2651",[]);
            Assert(again.Upserts.Select(o=>o.TargetRaw).SequenceEqual(direct.Upserts.Select(o=>o.TargetRaw)),"连续修改不累计取整误差");
            var field=unit.Field("structure.specialties")!;
            Assert(field.Availability==UnitFieldAvailability.Missing,"夹具故意缺少特性字段");
            Assert(UnitValueConverter.TryFormatTarget(field,"_transport1, _custom",out var display,out var raw,out _),"特性支持自定义标签");
            var op=new DraftOperation(DraftOperation.CreateId(DraftTargetKind.OptionalUnitModule,unit.Source.RelativeSourceFile,unit.Name,field.Definition.Key),null,DraftTargetKind.OptionalUnitModule,"units",unit.Source.RelativeSourceFile,unit.Name,unit.Source.TypeName,field.Definition.Key,field.Definition.Selector.DisplayPath,"StringList",field.DisplayValue,field.RawValue,display,raw,"特性补建",null,false,DateTimeOffset.UtcNow);
            using var store=new DraftStore(root);await store.LoadAsync();await store.ApplyBatchAsync(ratio.Upserts.Append(op).ToArray(),[]);
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            var after=await LoadWorkspaceAsync(root);var updated=after.Units.Single(u=>u.Name==unit.Name);var specialties=updated.Field("structure.specialties")!;
            Assert(specialties.DisplayValue=="_transport1, _custom","补建后可读取");
            Assert(updated.Field("structure.tags")!.RawValue==unit.Field("structure.tags")!.RawValue,"不联动TagSet");
            Assert(UnitValueConverter.TryFormatTarget(specialties,"",out var empty,out var emptyRaw,out _)&&emptyRaw=="[]","允许清空特性");
            var clear=CreateFieldDraft(updated,specialties,empty,emptyRaw);await store.UpsertAsync(clear);preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            Assert((await LoadWorkspaceAsync(root)).Units.Single(u=>u.Name==unit.Name).Field("structure.specialties")!.DisplayValue=="","空特性可保存");
        }finally{DeleteTemporaryFixture(root);}}
    }
    private static void Verify184Ui(WarnoLiteModdingTool.App.ViewModels.MainViewModel vm, System.Windows.Window window)
    {
        vm.AdvancedMode=false;
        WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh-CN");
        vm.SelectedModule=vm.Modules.Single(m=>m.Key=="units");
        var units=vm.UnitWorkspace!;
        Assert(units.Fields.Where(f=>f.Key is "survival.suppression" or "survival.stun").All(f=>!f.IsVisible),"基础模式隐藏承受引用");
        Assert(units.Fields.Count(f=>f.Key.StartsWith("recon.vision.")&&f.IsVisible)==1&&units.Fields.Count(f=>f.Key.StartsWith("recon.optics.")&&f.IsVisible)==1,"基础模式两组各一个输入");
        Assert(units.Fields.Single(f=>f.Key=="structure.specialties").IsPillEditor,"特性标签化");
        var rules=vm.RulesWorkspace!;vm.SelectedModule=vm.Modules.Single(m=>m.Key=="rules");
        Assert(rules.View.Cast<object>().Count()==32,"基础界面32项");
        DrainDispatcher(window.Dispatcher);
        var view=FindVisualChildren<WarnoLiteModdingTool.App.Controls.RulesView>((System.Windows.DependencyObject)window.Content).Single();
        var expanders=FindVisualChildren<System.Windows.Controls.Expander>(view).Take(2).ToArray();foreach(var ex in expanders)ex.IsExpanded=true;
        DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"184-rules-basic.png");
        var income=rules.Groups.Single(g=>g.Group.Definition.Number==7);income.Cells.Single().Value="301";
        RunWithDispatcher(rules.FlushAsync(),window.Dispatcher);
        Assert(units.DraftItems.Any(d=>d.Resolved.Operation.TargetKind==DraftTargetKind.GlobalRule&&d.Resolved.Operation.FieldKey=="7"),"规则UI进入草稿");
        RunWithDispatcher(income.UndoAsync(),window.Dispatcher);
        Assert(income.Cells.Single().Value=="260","规则撤销恢复值");
        vm.AdvancedMode=true;Assert(rules.View.Cast<object>().Count()==50,"专业界面50项");
        var workbench=new WarnoLiteModdingTool.App.Advanced.AdvancedWindow(vm);
        typeof(WarnoLiteModdingTool.App.Advanced.AdvancedWindow).GetMethod("LoadFields",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(workbench,null);
        ((System.Windows.Controls.TabControl)workbench.Content).SelectedIndex=1;SaveUiSnapshot(workbench,"184-workbench.png");
        var list=(System.Windows.Controls.ListBox)typeof(WarnoLiteModdingTool.App.Advanced.AdvancedWindow).GetField("_fields",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(workbench)!;
        Assert(list.Items.Count>0,"规则数值进入专业工作台");
        Assert(list.Items.Cast<object>().All(o=>!o.ToString()!.Contains("StrategicEmptyActionPointsOnMaxFatigueRout")),"工作台不暴露布尔字段");
        workbench.Close();
        WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("en");DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"184-rules-english.png");
        WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh-CN");vm.AdvancedMode=false;vm.SelectedModule=vm.Modules.Single(m=>m.Key=="units");
    }

}
