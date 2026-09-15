using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Strategic;
using WarnoLiteModdingTool.Core.Transactions;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task AirLayout196()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=CreateTemporaryFixtureCopy("p2-unit-complete");
            try
            {
                WriteAir196(root,nl);var data=RuleWorkspace.Load(root);var group=data.Groups.Single(g=>g.Definition.Number==54);Assert(group.CanEdit,group.Error);
                var values=group.Cells.ToDictionary(c=>c.Key,c=>c.Raw);values["grid"]="5×5";values["scale"]="0.6";var op=data.Operation(group,values);
                using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(op);var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[op]);
                Assert(preview.Files.Count(f=>f.Kind==FormalTextFileKind.Ndf)==3,"空军三文件联动");
                string Text(string f)=>Encoding.UTF8.GetString(preview.Files.Single(p=>p.RelativePath.EndsWith(f)).CandidateBytes);
                Assert(Text(AirLayout.Files[0]).Contains("NbMaxPlanes = 25"),"容量由5×5得25");Assert(Text(AirLayout.Files[2]).Contains("[66.6, 43.2]"),"倍率基于原尺寸");
                Assert(System.Text.RegularExpressions.Regex.Matches(Text(AirLayout.Files[1]),@"\( \[\d+, \d+\], ~/DummyOffMapPanel \)").Count==25,"25个唯一槽位");
                Assert(Text(AirLayout.Files[1]).Contains("// keep other panel\n".Replace("\n",nl)),"未改注释保留");
                var preview2=await service.PrepareApplyAsync(root,[op]);Assert(preview.Files.Where(f=>f.Kind==FormalTextFileKind.Ndf).Zip(preview2.Files.Where(f=>f.Kind==FormalTextFileKind.Ndf)).All(p=>p.First.CandidateBytes.SequenceEqual(p.Second.CandidateBytes)),"重复预览不累乘");
                var last=preview.Files.Where(f=>f.Kind==FormalTextFileKind.Ndf&&f.Existed).OrderBy(p=>p.RelativePath).Last();File.SetAttributes(last.FullPath,FileAttributes.ReadOnly);
                try{await TestAssert.ThrowsAsync<IOException>(()=>service.CommitApplyAsync(preview,store),"联动写入失败回滚");}finally{File.SetAttributes(last.FullPath,FileAttributes.Normal);}
                foreach(var file in preview.Files.Where(f=>f.Existed))Assert(File.ReadAllBytes(file.FullPath).SequenceEqual(file.OriginalBytes),"三文件整体恢复");
                preview=await service.PrepareApplyAsync(root,[op]);await service.CommitApplyAsync(preview,store);
                var after=RuleWorkspace.Load(root).Groups.Single(g=>g.Definition.Number==54);Assert(after.CanEdit&&after.Cells[0].Raw=="5×5"&&after.Cells[1].Raw=="1","重载布局与倍率基准");
                Assert(nl=="\n"||preview.Files.All(f=>!Encoding.UTF8.GetString(f.CandidateBytes).Replace("\r\n","").Contains('\n')),"保留CRLF");
                var invalid=new Dictionary<string,string>(values){["grid"]="7×13"};bool blocked=false;try{data.Operation(group,invalid);}catch(InvalidDataException){blocked=true;}Assert(blocked,"拒绝自由容量/网格");
                Assert(data.Groups.Single(g=>g.Definition.Number==52).Cells.Single().Boolean,"撤离开火布尔");
            }finally{DeleteTemporaryFixture(root);}
        }
    }
    private static void WriteAir196(string root,string nl)
    {
        void Write(string path,string text){path=Path.Combine(root,path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text.Replace("\r\n","\n").Replace("\n",nl),new UTF8Encoding(false));}
        Write(AirLayout.Directory+AirLayout.Files[0],"template AirMenu [ IsFromStrategic : bool = false, ] is TUISpecificSkirmishProductionMenuViewDescriptor ( NbMaxPlanes = 9 )\n");
        var slots=string.Join("\n",from x in Enumerable.Range(0,3) from y in Enumerable.Range(0,3) select $"                ( [{x}, {y}], ~/DummyOffMapPanel ),");
        Write(AirLayout.Directory+AirLayout.Files[1],"""
DummyOffMapPanel is BUCKContainerDescriptor ( ComponentFrame = TUIFramePropertyRTTI ( MagnifiableWidthHeight = ~/OffMapAirplaneComponentDimension ) )
// keep other panel
Other is BUCKContainerDescriptor ( ComponentFrame = TUIFramePropertyRTTI ( MagnifiableWidthHeight = [999, 888] ) )
Main is BUCKContainerDescriptor
(
    ElementName = "Container"
    ComponentFrame = TUIFramePropertyRTTI ( MagnifiableWidthHeight = [360, 0] )
    Components = [ BUCKContainerDescriptor
    (
        ComponentFrame = TUIFramePropertyRTTI ( MagnifiableWidthHeight = [0, 228] )
        Components = [ BUCKGridDescriptor
        (
            ElementName = 'UnitGrid'
            FirstElementMargin = TRTTILength2 ( Magnifiable = [10, 0] )
            InterElementMargin = TRTTILength2 ( Magnifiable = [3, 3] )
            MaxElementsPerDimension = [3, 3]
            GridElements = MAP
            [
SLOTS
            ]
        ) ]
    ) ]
)
""".Replace("SLOTS",slots)+"\n");
        Write(AirLayout.Directory+AirLayout.Files[2],"OffMapAirplaneComponentDimension is [111.0, 72.0]\n");
        Write("GameData/Gameplay/Constantes/Airplane.ndf","export AirplaneConstantesWargame is TAirplaneConstantesModernWarfareDescriptor ( EvacuationAltitudeGRU = 3534 TempsEntreDeuxDecollagesEnSecondes = 0.6 UtiliserArmesPendantEvac = true )\n");
    }
    private static async Task PackEditing196()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=CreateTemporaryFixtureCopy("p2-unit-complete");
            try
            {
                WriteStrategicFixture(root,nl);var data=await ReadStrategic(root);var pack=data.Packs["Pack_A"];
                var names=data.Packs.Keys.ToHashSet(StringComparer.Ordinal);var name=StrategicPackEditing.Name(pack.Unit,pack.Transport,2,names);var name2=StrategicPackEditing.Name(pack.Unit,pack.Transport,2,names);Assert(name.Contains("_mod_")&&name2==name+"_01","可读命名及同批去重");
                var op=StrategicPackEditing.Operation(pack,StrategicPackEditing.State(pack) with{Name=name,Xp=2});
                var originalRecord=data.Records[0];var company=originalRecord.Baseline.Companies[0];var grp=company.Groups[0];var local=StrategicCodec.Operation(originalRecord,originalRecord.Baseline with{Companies=[company with{Groups=[grp with{Slots=[grp.Slots[0] with{Count=3}]}]}]});
                var composed=await new UnitTransactionService().PrepareApplyAsync(root,[local,op]);Assert(composed.Files.Any(f=>f.RelativePath.EndsWith("StrategicCombatGroups.ndf")),"SP重命名与编制增量同批规划");
                Assert(composed.Files.Where(f=>f.Kind==FormalTextFileKind.Ndf).Any(f=>Encoding.UTF8.GetString(f.CandidateBytes).Contains("~/"+name)),"组合候选引用新Pack名");
                var extra=Path.Combine(root,"GameData/Extra.ndf");File.WriteAllText(extra,"Extra is TExample ( Reference = ~/Pack_A Note = 'Pack_A' ) // Pack_A"+nl,new UTF8Encoding(false));
                using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(op);var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[op]);
                var deck=preview.Files.Single(f=>f.RelativePath.EndsWith("StrategicDecks.ndf"));Assert(Encoding.UTF8.GetString(deck.CandidateBytes).Contains("~/"+name),"重命名更新Deck引用");
                var extraCandidate=Encoding.UTF8.GetString(preview.Files.Single(f=>f.RelativePath.EndsWith("Extra.ndf")).CandidateBytes);Assert(extraCandidate.Contains("Reference = ~/"+name)&&extraCandidate.Contains("Note = 'Pack_A' ) // Pack_A"),"跨文件引用更新且字符串注释保留");
                Assert(!preview.Files.Any(f=>f.RelativePath.EndsWith("StrategicCombatGroups.ndf")),"仅Pack本体变化不改编制数量和索引");
                var last=preview.Files.Where(f=>f.Kind==FormalTextFileKind.Ndf&&f.Existed).OrderBy(p=>p.RelativePath).Last();File.SetAttributes(last.FullPath,FileAttributes.ReadOnly);
                try{await TestAssert.ThrowsAsync<IOException>(()=>service.CommitApplyAsync(preview,store),"SP失败恢复");}finally{File.SetAttributes(last.FullPath,FileAttributes.Normal);}
                foreach(var f in preview.Files.Where(f=>f.Existed))Assert(File.ReadAllBytes(f.FullPath).SequenceEqual(f.OriginalBytes),"SP失败全部回滚");
                preview=await service.PrepareApplyAsync(root,[op]);await service.CommitApplyAsync(preview,store);var after=await ReadStrategic(root);Assert(!after.Packs.ContainsKey("Pack_A")&&after.Packs[name].Xp==2,"Pack改名及经验回读");Assert(after.Records.All(r=>r.Error is null),"所有共享编制有效");
                Assert(StrategicPackEditing.Resolve(after,op).Status==DraftResolutionStatus.Conflict,"旧草稿不静默覆盖");
                var same=StrategicPackEditing.Operation(after.Packs[name],StrategicPackEditing.State(after.Packs[name]) with{Name="Deck_A"});Assert(StrategicPackEditing.Resolve(after,same).Status==DraftResolutionStatus.Conflict,"跨对象名称冲突拒绝");
            }finally{DeleteTemporaryFixture(root);}
        }
    }
    private static async Task MountedCount196()
    {
        var root=CreateTemporaryFixtureCopy("p4-shared");try
        {
            var path=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf");File.WriteAllText(path,File.ReadAllText(path).Replace("AmmoBoxIndex =","NbWeapons = 1\n                AmmoBoxIndex ="),new UTF8Encoding(false));
            var (_,units,weapons)=await LoadP4Async(root);var weapon=weapons.Weapon("WeaponDescriptor_P4_Shared")!;var field=weapon.Mounts[0].Fields.Single(f=>f.Definition.FieldName=="NbWeapons");Assert(field.Definition.Group=="挂载","数量属于挂载");
            var op=CreateWeaponDraft(field,"3",DraftEditScope.CurrentUnit,["Descriptor_Unit_P4_One"],weapon.Name);var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[op]);Assert(!preview.Files.Any(f=>f.RelativePath.Contains("Ammunition")),"武器数量不克隆弹药");
            using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(op);await service.CommitApplyAsync(preview,store);var (_,_,after)=await LoadP4Async(root);Assert(after.Weapon(weapon.Name)!.Mounts[0].Fields.Single(f=>f.Definition.FieldName=="NbWeapons").RawValue=="1","原共享武器不变");Assert(after.Weapons.Any(w=>w.Name!=weapon.Name&&w.Mounts.Any(m=>m.Fields.Any(f=>f.Definition.FieldName=="NbWeapons"&&f.RawValue=="3"))),"目标隔离武器数量为3");
        }finally{DeleteTemporaryFixture(root);}
    }
}
