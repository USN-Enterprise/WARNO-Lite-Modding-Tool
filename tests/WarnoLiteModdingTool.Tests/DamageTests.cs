using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task DamageOldReader(string path)
    {
        var root=DamageFixture();var context=new System.Runtime.Loader.AssemblyLoadContext("old-damage-reader",true);
        try
        {
            var data=new DamageWorkspace(root);using var store=new DraftStore(root);await store.LoadAsync();var c=data.Matrix!.Cells[0];
            await store.UpsertAsync(data.MatrixOperation([new(c.Key,c.Raw,"1")]));var bytes=File.ReadAllBytes(store.DraftPath);
            var assembly=context.LoadFromAssemblyPath(Path.GetFullPath(path));var type=assembly.GetType("WarnoLiteModdingTool.Core.Drafts.DraftStore")!;
            using var old=(IDisposable)Activator.CreateInstance(type,root)!;
            var task=(Task)type.GetMethod("LoadAsync")!.Invoke(old,[CancellationToken.None])!;await task;var result=task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert((bool)result.GetType().GetProperty("IsBlocked")!.GetValue(result)!&&File.ReadAllBytes(store.DraftPath).SequenceEqual(bytes),"旧版拒绝schema4且保留原草稿");
            Console.WriteLine("PASS previous release rejects damage schema 4 without modifying it");
        }
        finally{context.Unload();DeleteTemporaryFixture(root);}
    }
    private static void DamageStock(string root)
    {
        var data=new DamageWorkspace(root);Assert(data.Matrix is not null,data.MatrixError);var matrix=data.Matrix!;
        Console.WriteLine($"Families={matrix.Rows.Select(r=>r.Family).Distinct().Count()}; resistance families={matrix.Columns.Select(r=>r.Family).Distinct().Count()}; rows={matrix.Rows.Count}; columns={matrix.Columns.Count}; cells={matrix.Cells.Count}; stairs={data.Stairs.Count}");
        foreach(var stair in data.Stairs)Console.WriteLine(stair.Source.Name+" · "+stair.Distance+" / "+stair.AP+" · direct="+data.References(stair.Source.Name).Count+" · chain="+data.FullReferences(stair.Source.Name).Count);
        foreach(var diagnostic in data.Diagnostics)Console.WriteLine(diagnostic);
        Assert(matrix.Rows.Count==145&&matrix.Columns.Count==49&&matrix.Cells.Count==7105&&data.Stairs.Count==4,"当前只读官方样本结构与研究相符");
    }
    private static string DamageFixture(string nl = "\n")
    {
        var root = StructureFixture(nl); var dir = Path.Combine(root, "GameData/Generated/Gameplay/Gfx");
        var ammo = Path.Combine(dir, "Ammunition.ndf");
        File.WriteAllText(ammo, File.ReadAllText(ammo).Replace("    Arme =", "    DamageTypeEvolutionOverRangeDescriptor = ~/TestStair" + nl + "    PiercingWeapon = True" + nl + "    Arme ="), new UTF8Encoding(false));
        var unitPath=Path.Combine(dir,"UniteDescriptor.ndf");
        File.WriteAllText(unitPath,File.ReadAllText(unitPath).Replace("ModulesDescriptors = [", "ModulesDescriptors = [ TDamageModuleDescriptor(BlindageProperties=TBlindageProperties(ResistanceFront=TResistanceTypeRTTI(Family=ResistanceFamily_blindage Index=18) ResistanceSides=TResistanceTypeRTTI(Family=ResistanceFamily_blindage Index=8) ResistanceRear=TResistanceTypeRTTI(Family=ResistanceFamily_blindage Index=5) ResistanceTop=TResistanceTypeRTTI(Family=ResistanceFamily_blindage Index=3))),"),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "DamageResistance.ndf"), "private Matrix is TGameplayDamageResistanceContainer\n(\n DamageFamilyCounts=[(DamageFamily_ap,40),(DamageFamily_he,3),(DamageFamily_suppress,1)]\n ResistanceFamilyCounts=[(ResistanceFamily_blindage,30),(ResistanceFamily_infanterie,3)]\n Values=[\n" + string.Join(",\n", Enumerable.Range(0,44).Select(r => "  [" + string.Join(", ", Enumerable.Range(0,33).Select(c => r==0 && c==0 ? "0" : (r+c+1).ToString()+".0")) + "]")) + "\n ]\n)\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "FamilyIDs.ndf"), "DamageFamily_ap is 0\nDamageFamily_he is 1\nDamageFamily_suppress is 2\nResistanceFamily_blindage is 0\nResistanceFamily_infanterie is 1\nDNames is TDamageFamilyList(Values=['DamageFamily_ap','DamageFamily_he','DamageFamily_suppress'])\nRNames is TResistanceFamilyList(Values=['ResistanceFamily_blindage','ResistanceFamily_infanterie'])\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "Stairs.ndf"), "// shared rule preserved\nexport TestStair is TStairsDamageTypeEvolutionOverRangeDescriptor\n(\n DistanceGRU = 175.0 // step\n AP = 1.0\n)\nexport TestOtherStair is TStairsDamageTypeEvolutionOverRangeDescriptor(DistanceGRU=350.0 AP=2.0)\n", new UTF8Encoding(false));
        foreach(var path in new[]{"DamageResistance.ndf","FamilyIDs.ndf","Stairs.ndf"}) File.WriteAllText(Path.Combine(dir,path), File.ReadAllText(Path.Combine(dir,path)).Replace("\n",nl),new UTF8Encoding(false));
        return root;
    }
    private static async Task DamageContracts()
    {
        var root=DamageFixture();
        try
        {
            var data=new DamageWorkspace(root); Assert(data.Matrix is not null,data.MatrixError); var m=data.Matrix!;
            Assert(m.Rows.Count==44 && m.Columns.Count==33 && m.Cells.Count==1452,"矩阵按家族声明顺序展开");
            Assert(m.Rows[40].Key=="DamageFamily_he/1" && m.Columns[31].Key=="ResistanceFamily_infanterie/2","家族局部Index从1开始且保留步兵特殊列");
            var cell=m.Cells.Single(c=>c.Attack.Key=="DamageFamily_suppress/1"&&c.Resistance.Key=="ResistanceFamily_blindage/30");
            var op=data.MatrixOperation([new(cell.Key,cell.Raw,"2.5")]); var files=new List<PlannedFileChange>(); data.Plan([op],files);
            Assert(new DamageWorkspace(root,files).Matrix!.Cells.Single(c=>c.Key==cell.Key).Raw=="2.5","压制家族和大于1系数可写回");
            Assert(Encoding.UTF8.GetString(files.Single().CandidateBytes)==File.ReadAllText(Path.Combine(root,m.File)).Remove(cell.Offset,cell.Length).Insert(cell.Offset,"2.5"),"矩阵仅替换所选系数");
            var (_,_,weapons)=await LoadP4Async(root); var a=weapons.Ammo(A1911)!;
            Assert(a.Field(DamageDistance.ReferenceKey)?.CanEdit==true,"读取内部距离引用");
            Assert(DamageDistance.Basic(a),"动能普通模式资格");
            Assert(!AmmoProfessional.Visible(a.Field(DamageDistance.ReferenceKey)!,true),"内部引用不泄露为通用字段");
            foreach(var raw in new[]{"NaN","Infinity","-1","~/Custom"})
                await TestAssert.ThrowsAsync<InvalidDataException>(()=>Task.Run(()=>data.MatrixOperation([new(cell.Key,cell.Raw,raw)])),"拒绝无效系数");
            var path=Path.Combine(root,m.File); var original=File.ReadAllText(path); File.WriteAllText(path,original.Remove(cell.Offset,cell.Length).Insert(cell.Offset,"~/Coefficient"));
            var unknown=new DamageWorkspace(root); Assert(unknown.Matrix is not null,"未知表达式不阻塞其他单元");
            await TestAssert.ThrowsAsync<InvalidDataException>(()=>Task.Run(()=>unknown.MatrixOperation([new(cell.Key,"~/Coefficient","2")])),"未知表达式只读");
            File.WriteAllText(path,original); var ids=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/FamilyIDs.ndf");
            File.WriteAllText(ids,File.ReadAllText(ids).Replace("DamageFamily_he is 1","DamageFamily_he is 0"));
            Assert(new DamageWorkspace(root).Matrix is null,"ID/顺序不一致禁写矩阵");
            Assert(new DamageWorkspace(root).Stairs.Count==2,"矩阵异常不阻断独立距离规则");
        }
        finally{DeleteTemporaryFixture(root);}
    }
    private static async Task DamageTransactions()
    {
        foreach(var local in new[]{false,true})
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=DamageFixture(nl);
            try
            {
                var (_,_,weapons)=await LoadP4Async(root); var data=new DamageWorkspace(root); var a=weapons.Ammo(A1911)!; var stair=data.Stairs.Single(s=>s.Source.Name=="TestStair");
                var before=Directory.GetFiles(Path.Combine(root,"GameData"),"*.ndf",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
                using var store=new DraftStore(root);await store.LoadAsync();
                var distance=DamageDistance.Operation(data,a,stair,"500","1.0",local?DraftEditScope.CurrentUnit:DraftEditScope.AllReferences,local?[U1911]:[]);
                await store.UpsertAsync(distance);
                var cell=data.Matrix!.Cells.First(); await store.UpsertAsync(data.MatrixOperation([new(cell.Key,cell.Raw,"0.5")]));
                using(var reopen=new DraftStore(root)){await reopen.LoadAsync();Assert(!reopen.IsBlocked && reopen.Operations.Count==2,"schema4草稿重开");}
                Assert(before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"距离草稿不写正式文件");
                var service=new UnitTransactionService(); var preview=await service.PrepareApplyAsync(root,store.Operations); var result=await service.CommitApplyAsync(preview,store);
                Assert(result.Succeeded,"距离与矩阵组合提交");
                var (_,_,after)=await LoadP4Async(root);var final=new DamageWorkspace(root);
                var actual=local?after.Ammo(after.Weapon(after.Units.Single(u=>u.Name==U1911).Weapons.Single())!.Mounts[0].AmmoName)!:after.Ammo(A1911)!;
                Assert(final.ResolveStair(actual.Source.RelativeSourceFile,actual.Field(DamageDistance.ReferenceKey)!.RawValue)?.Distance=="500","目标Ammo使用独立500阶梯");
                Assert(final.Stairs.Single(s=>s.Source.Name=="TestStair").Body==stair.Body,"原共享阶梯逐字保留");
                if(local) Assert(after.Ammo(A1911)!.Field(DamageDistance.ReferenceKey)!.RawValue=="~/TestStair","局部编辑保留未选单位原Ammo");
                Assert(final.Matrix!.Cells.First().Raw=="0.5","矩阵组合值回读");
                await service.CommitRestoreAsync(service.PrepareRestore(root,result.BackupId));
                Assert(before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"备份恢复整个引用链及矩阵");
            }
            finally{DeleteTemporaryFixture(root);}
        }
    }
    private static async Task DamageGuards()
    {
        var root=DamageFixture();
        try
        {
            var data=new DamageWorkspace(root);var (_,_,weapons)=await LoadP4Async(root);var a=weapons.Ammo(A1911)!;var stair=data.Stairs.First();
            var distance=DamageDistance.Operation(data,a,stair,"500","1",DraftEditScope.CurrentUnit,[U1911]);
            var shared=data.StairOperation(stair,"1000","2");
            var field=CreateWeaponDraft(a.Field("ammo.damage.index")!,"9",DraftEditScope.AllReferences,[],null);
            using var store=new DraftStore(root);await store.LoadAsync();await store.ApplyBatchAsync([distance,shared,field],[]);
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[distance]);
            Assert(preview.Operations.Count==3,"局部距离与同Ammo字段及其共享阶梯草稿依赖不拆分");
            var newPath=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/NewExternal.ndf");
            File.WriteAllText(newPath,"export External is TEntityDescriptor(Value=~/TestStair)");
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.CommitApplyAsync(preview,store),"预览后新增引用必须阻止提交");File.Delete(newPath);
            preview=await service.PrepareApplyAsync(root,store.Operations);
            var originals=preview.Files.Where(f=>f.Kind==FormalTextFileKind.Ndf).ToDictionary(f=>f.FullPath,f=>File.ReadAllBytes(f.FullPath));
            var path=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/Stairs.ndf");File.SetAttributes(path,FileAttributes.ReadOnly);
            try{await TestAssert.ThrowsAsync<IOException>(()=>service.CommitApplyAsync(preview,store),"多文件失败回滚");}
            finally{File.SetAttributes(path,FileAttributes.Normal);}
            Assert(originals.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)) && store.Operations.Count==3,"失败时保留全部原文与草稿");
            preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            var (_,_,after)=await LoadP4Async(root);var final=new DamageWorkspace(root);
            var current=after.Ammo(after.Weapon(after.Units.Single(u=>u.Name==U1911).Weapons.Single())!.Mounts[0].AmmoName)!;
            Assert(current.Field("ammo.damage.index")!.DisplayValue=="9" && after.Ammo(A1911)!.Field("ammo.damage.index")!.DisplayValue=="9","原索引编辑与隔离组合");
            Assert(final.ResolveStair(current.Source.RelativeSourceFile,current.Field(DamageDistance.ReferenceKey)!.RawValue)!.Distance=="500" && final.Stairs.Single(s=>s.Source.Name==stair.Source.Name).Distance=="1000","局部和共享阶梯分别保持明确值");
            Assert(DamageDistance.Resolve(final,after,distance).Status==DraftResolutionStatus.Conflict,"过期Ammo及阶梯草稿拒绝");
        }
        finally{DeleteTemporaryFixture(root);}
    }
    private static async Task DamageMathAndStandalone()
    {
        var root=DamageFixture();
        try
        {
            var data=new DamageWorkspace(root);var cells=data.Matrix!.Cells.Take(2).ToArray();
            var result=DamageMatrixBatch.Preview(cells,new Dictionary<string,string>(),DamageMath.Set,"3","1","2",true);
            Assert(result[0].After=="0"&&result[1].After=="2","保零优先于上下限");
            result=DamageMatrixBatch.Preview(cells,result.ToDictionary(c=>c.Key,c=>c.After),DamageMath.Add,"-1","0","",false);
            Assert(result[0].After=="0"&&result[1].After=="1","从当前草稿继续加减和截限");
            foreach(var operand in new[]{"NaN","Infinity","-1"})await TestAssert.ThrowsAsync<InvalidDataException>(()=>Task.Run(()=>DamageMatrixBatch.Preview(cells,new Dictionary<string,string>(),DamageMath.Set,operand,"","",false)),"矩阵目标必须非负有限");
            var families=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/FamilyIDs.ndf");
            var registration=File.ReadAllText(families);File.WriteAllText(families,registration+"\nDamageFamily_ap is 0\n");
            Assert(new DamageWorkspace(root).Matrix is null,"重复ID声明不猜测");File.WriteAllText(families,registration);
            File.WriteAllText(families,registration.Replace("DamageFamily_he is 1","DamageFamily_he is 1 + 2"));
            Assert(new DamageWorkspace(root).Matrix is null,"族ID表达式不截取首个数字");File.WriteAllText(families,registration);
            foreach(var path in new[]{"UniteDescriptor.ndf","WeaponDescriptor.ndf","Ammunition.ndf"}) File.Delete(Path.Combine(root,"GameData/Generated/Gameplay/Gfx",path));
            using var store=new DraftStore(root);await store.LoadAsync();var m=data.Matrix!;await store.UpsertAsync(data.MatrixOperation([new(cells[1].Key,cells[1].Raw,"3")]));
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            Assert(new DamageWorkspace(root).Matrix!.Cells[1].Raw=="3","缺单位与弹药模块仍支持独立伤害规则");
        }
        finally{DeleteTemporaryFixture(root);}
    }
    private static async Task DamageRefresh()
    {
        var root=DamageFixture();
        try
        {
            var before=await Read1915(root);var first=before.Units.Rules!.Damage;var cell=first.Matrix!.Cells[0];
            using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(first.MatrixOperation([new(cell.Key,cell.Raw,"2")]));
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            var next=await Core.Projects.ProjectWorkspaceSnapshot.LoadRootAsync(root,before.Cache.Next(),previous:before,committed:preview.Files);
            Assert(next.Units.Rules!.Damage.Matrix!.Cells[0].Raw=="2" && first.Matrix.Cells[0].Raw=="0","刷新后独立伤害图且不污染旧快照");
            var op=next.Units.Rules.Damage.MatrixOperation([new(cell.Key,"2","3")]);
            Assert(DraftResolver.Resolve(next.Units,[op]).Single().Status==DraftResolutionStatus.Active,"第二次编辑使用已应用的新基线");
        }
        finally{DeleteTemporaryFixture(root);}
    }
    private static async Task DamageReferenceSelection()
    {
        var root=DamageFixture();
        try
        {
            var path=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
            File.WriteAllText(path,File.ReadAllText(path).Replace("DamageTypeEvolutionOverRangeDescriptor = ~/TestStair","DamageTypeEvolutionOverRangeDescriptor = nil"));
            var (_,_,weapons)=await LoadP4Async(root);var data=new DamageWorkspace(root);var a=weapons.Ammo(A1911)!;var stair=data.Stairs.Single(s=>s.Source.Name=="TestOtherStair");
            var op=DamageDistance.Operation(data,a,stair,stair.Distance,stair.AP,DraftEditScope.CurrentUnit,[U1911],true);
            using var store=new DraftStore(root);await store.LoadAsync();await store.ApplyBatchAsync([op,data.StairOperation(stair,"800","3")],[]);
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[op]);await service.CommitApplyAsync(preview,store);
            var (_,_,after)=await LoadP4Async(root);var final=new DamageWorkspace(root);var actual=after.Ammo(after.Weapon(after.Units.Single(u=>u.Name==U1911).Weapons.Single())!.Mounts[0].AmmoName)!;
            var target=final.ResolveStair(actual.Source.RelativeSourceFile,actual.Field(DamageDistance.ReferenceKey)!.RawValue);
            Assert(final.Stairs.Count==2&&target?.Source.Name==stair.Source.Name&&target.Distance=="800"&&target.AP=="3","专业显式替换nil引用并遵守同批共享规则最终值，不创建多余阶梯");
            Assert(after.Ammo(A1911)!.Field(DamageDistance.ReferenceKey)!.RawValue=="nil","未选单位保留nil");
        }
        finally{DeleteTemporaryFixture(root);}
    }
}
