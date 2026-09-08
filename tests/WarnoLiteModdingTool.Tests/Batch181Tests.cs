using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.App.ViewModels.Divisions;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task SharedNameIsolation()
    {
        foreach(var newline in new[]{"\n","\r\n"})
        {
            var root=CreateTemporaryFixtureCopy("p2-unit-complete");
            try
            {
                var path=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");
                var source=File.ReadAllText(path).Replace("TESTUNIT02","TESTUNIT01").Replace("\r\n","\n").Replace("\n",newline);File.WriteAllText(path,source,new UTF8Encoding(false));
                var data=await LoadWorkspaceAsync(root);var tank=data.Units.Single(u=>u.Name=="Descriptor_Unit_Test_Tank_US");var recon=data.Units.Single(u=>u.Name=="Descriptor_Unit_Test_Recon_SOV");
                Assert(tank.CanEditName&&recon.CanEditName&&tank.NameTokenRequiresReplacement,"共用token可独立改名");
                using var store=new DraftStore(root);await store.LoadAsync();var first=CreateNameDraft(tank,"独立坦克","WL18100001");var second=CreateNameDraft(recon,"独立侦察","WL18100002");await store.UpsertAsync(first);await store.UpsertAsync(second);
                var csv=Path.Combine(root,"GameData/Localisation/UnexpectedP2Dictionary/UNITS.csv");var before=File.ReadAllText(csv);var service=new UnitTransactionService();
                var preview=await service.PrepareApplyAsync(root,[first]);await service.CommitApplyAsync(preview,store);
                var after=await LoadWorkspaceAsync(root);Assert(after.Units.Single(u=>u.Name==tank.Name).DisplayName=="独立坦克","当前单位改名");Assert(after.Units.Single(u=>u.Name==recon.Name).DisplayName==recon.DisplayName,"其他共用单位保持名称");Assert(File.ReadAllText(csv).StartsWith(before,StringComparison.Ordinal),"旧CSV条目逐字保留");
                preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
                after=await LoadWorkspaceAsync(root);Assert(after.Units.Single(u=>u.Name==recon.Name).DisplayName=="独立侦察","剩余草稿可在共用解除后独立应用");Assert(!File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[]{239,187,191}),"NDF无BOM");Assert(newline=="\n"?!File.ReadAllText(path).Contains('\r'):!File.ReadAllText(path).Replace("\r\n","").Contains('\n'),"保留换行");
            }finally{DeleteTemporaryFixture(root);}
        }
    }
    private static void Verify181Ui(WarnoLiteModdingTool.App.ViewModels.MainViewModel vm)
    {
        var workspace=vm.UnitWorkspace!;var current=workspace.SelectedUnit!;
        var sections=workspace.FieldSections;sections[1].IsExpanded=true;var selected=sections[2].Title;sections[2].IsExpanded=true;
        workspace.SelectedUnit=workspace.Units.First(u=>u!=current);
        Assert(workspace.FieldSections.Count(s=>s.IsExpanded)==1&&workspace.FieldSections.Single(s=>s.IsExpanded).Title==selected,"切换只继承最后展开项");
        workspace.FieldSections[1].IsExpanded=true;selected=workspace.FieldSections[1].Title;workspace.RefreshExternalDraftState();Assert(workspace.FieldSections.Count(s=>s.IsExpanded)==2,"同单位刷新保留多个展开项");
        workspace.SelectedUnit=current;Assert(workspace.FieldSections.Count(s=>s.IsExpanded)==1&&workspace.FieldSections.Single(s=>s.IsExpanded).Title==selected,"连续切换按最后手动项继承");
        var root=CreateTemporaryFixtureCopy("p5-division");
        try{
            var (_,_,data)=Task.Run(()=>LoadP5Async(root)).GetAwaiter().GetResult();var d=data.Divisions.Single();
            WarnoLiteModdingTool.Core.Divisions.DivisionRecord Copy(string country,int id)=>new(d.Source with{Name=d.Name+id},d.DisplayName,d.DivisionRuleName,d.CostMatrixName,d.DefaultDeckName,d.Baseline with{CountryId=country},d.DivisionFields,d.UnitRuleList,d.DeckPackList,d.CostMatrix,d.CanEdit,d.EditReason);
            var shuffled=data with{Divisions=new[]{Copy("US",1),Copy("SOV",2),Copy("US",3)}};
            using var store=new DraftStore(root);var divisions=new DivisionWorkspaceViewModel(shuffled,store,_=>{},()=>{});
            Assert(divisions.Divisions.Select(x=>x.Country).SequenceEqual(new[]{"SOV","US","US"}),"默认国家排序稳定聚合");
        }finally{DeleteTemporaryFixture(root);}
    }
}
