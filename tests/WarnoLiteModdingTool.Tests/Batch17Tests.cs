using System.IO;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Strategic;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.App.Localisation;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task PartialDraftApplyAndDelete()
    {
        var root=CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var (_,tank)=await LoadTankAsync(root);
            var a=CreateFieldDraft(tank,tank.Field("survival.health")!,"12","12") with {GroupId="test-batch"};
            var b=CreateNameDraft(tank,"尚未应用的名称",tank.NameToken!);
            using var store=new DraftStore(root);await store.LoadAsync();await store.ApplyBatchAsync([a,b],[]);
            var transaction=new UnitTransactionService();var preview=await transaction.PrepareApplyAsync(root,[a]);await transaction.CommitApplyAsync(preview,store);
            TestAssert.Equal(1,store.Operations.Count,"部分应用只能移除已应用草稿");TestAssert.Equal(b,store.Operations[0],"未选草稿逐值保留");
            var (_,after)=await LoadTankAsync(root);TestAssert.Equal("12",after.Field("survival.health")!.DisplayValue,"选中字段已应用");TestAssert.True(after.DisplayName!="尚未应用的名称","未选名称没有应用");
            var remaining=await transaction.PrepareApplyAsync(root,store.Operations);TestAssert.Equal(1,remaining.Operations.Count,"剩余草稿仍能独立预览");
            var bytes=File.ReadAllBytes(after.Source.SourceFile);await store.ApplyBatchAsync([], [b.Id]);TestAssert.Equal(0,store.Operations.Count,"部分删除完成");TestAssert.True(bytes.SequenceEqual(File.ReadAllBytes(after.Source.SourceFile)),"删除草稿不写 NDF");
        }
        finally{DeleteTemporaryFixture(root);}
    }
    private static async Task StrategicExplicitIndexMapping()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=CreateTemporaryFixtureCopy("p2-unit-complete");
            try
            {
                WriteStrategicFixture(root,nl);var data=await ReadStrategic(root);var record=data.Records.Single(r=>r.Id=="Pawn_A");var company=record.Baseline.Companies[0];var group=company.Groups[0];var slot=group.Slots[0];
                var state=record.Baseline with { Companies=[company with { Groups=[group with {Slots=[slot with {Count=1,Start=1},slot with {Id="new:index-slot",Count=1,Start=0}]}]}] };
                var operation=StrategicCodec.Operation(record,state);TestAssert.Equal(DraftResolutionStatus.Active,StrategicPlanner.Resolve(data,operation).Status,"允许逆序连续段映射");
                var invalid=state with {Companies=[company with {Groups=[group with {Slots=[slot with {Count=1,Start=0},slot with {Id="new:index-slot",Count=1,Start=0}]}]}]};
                TestAssert.Equal(DraftResolutionStatus.Conflict,StrategicPlanner.Resolve(data,StrategicCodec.Operation(record,invalid)).Status,"拒绝索引重叠");
                using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(operation);var service=new UnitTransactionService();await service.CommitApplyAsync(await service.PrepareApplyAsync(root,store.Operations),store);
                var after=await ReadStrategic(root);var changed=after.Records.Single(r=>r.Id=="Pawn_A");var changedGroup=changed.Baseline.Companies[0].Groups[0];
                TestAssert.Equal("1",changed.Templates[changedGroup.Slots[0].Id+"/start"],"候选首段实际从索引1开始");TestAssert.Equal("0",changed.Templates[changedGroup.Slots[1].Id+"/start"],"第二段实际映射索引0");
                TestAssert.Equal(StrategicCodec.Serialize(data.Records.Single(r=>r.Id=="Pawn_B").Baseline),StrategicCodec.Serialize(after.Records.Single(r=>r.Id=="Pawn_B").Baseline),"共享另一棋子不变");
            }finally{DeleteTemporaryFixture(root);}
        }
    }
    private static async Task FixedSubtractAndNames()
    {
        var root=CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var (_,tank)=await LoadTankAsync(root);
            var preview=UnitBatchPlanner.Preview(new("subtract-test",[tank],"survival.health",UnitBatchOperation.Subtract,"2",UnitBatchRounding.None,null,null,[]));
            TestAssert.True(WarnoLiteModdingTool.App.Controls.ModFinder.FindRoots(root,CancellationToken.None).Contains(root),"已保存的有效路径可发现且不依赖 Exp");
            var discoveredRoots=WarnoLiteModdingTool.App.Controls.ModFinder.DistinctRoots([root,root.Replace('\\','/'),root.ToUpperInvariant()+"\\",Path.Combine(root,"."),root+"-other"]);
            TestAssert.Equal(2,discoveredRoots.Count,"保存路径与扫描路径的斜杠、大小写、尾分隔符差异不能重复列出 Mod；不同目录仍保留");
            var cancelled=false;try { WarnoLiteModdingTool.App.Controls.ModFinder.FindRoots(null,new CancellationToken(true)); }catch(OperationCanceledException){cancelled=true;}TestAssert.True(cancelled,"自动查找可取消");
            TestAssert.True(preview.CanAddToDrafts,"固定减法可生成草稿");
            var before=decimal.Parse(tank.Field("survival.health")!.DisplayValue,System.Globalization.CultureInfo.InvariantCulture);
            TestAssert.Equal((before-2).ToString(System.Globalization.CultureInfo.InvariantCulture),preview.Upserts.Single().TargetValue,"固定减法与百分比无关");
            TestAssert.Equal("Test name",VanillaNames.Lookup("UNITS","TESTNAME01"),"人工词典名称解码");
            TestAssert.True(VanillaNames.Lookup("COMPANIES","TESTNAME01") is not null,"人工连名称词典");
            WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced=false;UiText.Current.SetLanguage("zh-CN");
            TestAssert.Equal("火箭炮",GameText.Display("role","mlrs"),"角色中文确认版");TestAssert.Equal("苏联 近卫第79坦克师〔剧情〕",GameText.Display("division","SOV 79 Gds Tank multi HB Rule"),"近卫词序及剧情后缀");
            TestAssert.True(!GameText.VisibleCategory("TAcknowUnitType_Tank_Legion_CZ"),"普通模式移除外籍标签");TestAssert.True(GameText.VisibleCategory("TAcknowUnitType_Tank"),"基础标签保留");
            UiText.Current.SetLanguage("en");TestAssert.Equal("CMD Helo",GameText.Display("role","hq_helo"),"英文角色缩写确认版");TestAssert.Equal("TAcknowUnitType_Tank",GameText.Display("category","TAcknowUnitType_Tank"),"单位类型不改英文名称");UiText.Current.SetLanguage("zh-CN");
        }finally{DeleteTemporaryFixture(root);}
    }
}
