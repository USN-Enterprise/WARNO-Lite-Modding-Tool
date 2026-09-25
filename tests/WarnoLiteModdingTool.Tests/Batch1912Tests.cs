using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.App.ViewModels.Weapons;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static AmmoBatchPreview P1912(UnitWorkspaceData units,WeaponWorkspaceData data,IReadOnlyList<DraftOperation> drafts,string key,string operand,
        UnitBatchOperation operation=UnitBatchOperation.Set,UnitBatchRounding rounding=UnitBatchRounding.None,IReadOnlyList<AmmoBatchTarget>? targets=null,string? min=null,string? max=null) =>
        AmmoBatchPlanner.Preview(units,data,drafts,new(targets ?? data.Ammunition.Where(a=>a.Field("ammo.damage.physical") is not null).Select(AmmoBatchPlanner.Identity).ToArray(),key,operation,operand,rounding,min,max));
    private static async Task Save1912(DraftStore store,AmmoBatchPreview preview)
    { Assert(preview.CanSave,"Ammo批改应可保存："+string.Join(" / ",preview.Errors));await store.ApplyBatchAsync(preview.Upserts,preview.Removals); }
    private static async Task Ammo1912Formulas()
    {
        var (_,units,data)=await LoadP4Async(Fixture("p4-shared"));var one=new[]{AmmoBatchPlanner.Identity(data.Ammo(A1911)!)};
        var request=new AmmoBatchRequest(data.Ammunition.Where(a=>a.Field("ammo.damage.physical") is not null).Select(AmmoBatchPlanner.Identity).Concat(one).ToArray(),"ammo.damage.physical",UnitBatchOperation.IncreasePercent,"10");
        var p=AmmoBatchPlanner.Preview(units,data,[],request);
        Assert(p.CanSave && p.Rows.Count==2 && p.Rows.Single(r=>r.Name==A1911).Target=="11" && p.Rows.Single(r=>r.Name!=A1911).Target=="3.3","每Ammo一次且分别计算小数");
        Assert(p.Upserts.Select(o=>o.GroupId).Distinct().Count()==1 && p.Upserts.All(AmmoBatchPlanner.IsShared),"同批共享AmmoField而非挂载草稿");
        Assert(p.References.Select(r=>r.Unit).Distinct().Count()==3,"全部共享使用者，不被目标Ammo数量截断");
        Assert(AmmoBatchPlanner.CommonFields(data.Ammunition.Where(a=>a.Field("ammo.damage.physical") is not null)).Any(f=>f.Key=="ammo.damage.physical") && !AmmoBatchPlanner.CommonFields(data.Ammunition.Where(a=>a.Field("ammo.damage.physical") is not null)).Any(f=>f.Key=="ammo.supply"),"字段交集");
        Assert(P1912(units,data,[],"ammo.supply","7").Upserts.Count==0,"缺字段整批拒绝");
        Assert(P1912(units,data,[],"ammo.shotsPerSalvo","0.6",UnitBatchOperation.Multiply).Errors.Count>0,"整数不隐式截断");
        Assert(P1912(units,data,[],"ammo.shotsPerSalvo","0.6",UnitBatchOperation.Multiply,UnitBatchRounding.Ceiling).Rows.Single(r=>r.Name!=A1911).Target=="1","显式整数取整");
        Assert(P1912(units,data,[],"ammo.damage.physical","2",UnitBatchOperation.Multiply,targets:one,min:"23.1",max:"24").Rows.Single().Target=="23.1","公式后上下限且保留小数");
        foreach(var value in new[]{"NaN","Infinity","-1"}) Assert(P1912(units,data,[],"ammo.damage.physical",value).Errors.Count>0,"非法数值拒绝");
        Assert(P1912(units,data,[],"ammo.shotsPerSalvo","2147483648").Errors.Count>0,"int溢出拒绝");
        Assert(P1912(units,data,[],"ammo.range.ground.min","4000",targets:one).Errors.Count>0,"最终射程组合验证");
        Assert(P1912(units,data,[],"ammo.damage.family","NoSuchFamily",targets:one).Errors.Count>0,"候选不猜写");
        Assert(P1912(units,data,[],"ammo.behavior.moving","否",targets:one).CanSave,"布尔设值");
        Assert(P1912(units,data,[],"ammo.behavior.moving","2",UnitBatchOperation.Multiply,targets:one).Errors.Count>0,"布尔不走运算");
        Assert(AmmoBatchPlanner.Preview(units,data,[],request).Rows.Select(r=>r.Target).SequenceEqual(p.Rows.Select(r=>r.Target)),"重复预览不累积");
        var next=P1912(units,data,p.Upserts,"ammo.damage.physical","2",UnitBatchOperation.Multiply);
        Assert(next.Rows.Single(r=>r.Name!=A1911).Current=="3.3" && next.Rows.Single(r=>r.Name!=A1911).Target=="6.6","使用有效共享草稿");
        var restore=P1912(units,data,p.Upserts,"ammo.damage.physical","10",targets:one);
        Assert(restore.CanSave && restore.Removals.Count==1 && restore.Upserts.Count==0,"恢复基线移除草稿");
    }
    private static async Task Ammo1912DraftComposition()
    {
        var root=CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_,units,data)=await LoadP4Async(root);var one=new[]{AmmoBatchPlanner.Identity(data.Ammo(A1911)!)};using var store=new DraftStore(root);await store.LoadAsync();
            var local=CreateWeaponDraft(data.Ammo(A1911)!.Field("ammo.damage.physical")!,"17",DraftEditScope.CurrentUnit,[U1911],W1911);
            var conflict=P1912(units,data,[local],"ammo.damage.physical","20",targets:one);Assert(!conflict.CanSave && conflict.Upserts.Count==0,"局部同ID不能覆盖");
            var oldBatch=P1911(data,[],"ammo.damage.physical","17");
            Assert(!P1912(units,data,oldBatch.Upserts,"ammo.damage.physical","20",targets:one).CanSave,"历史挂载批次冲突");
            await store.UpsertAsync(local);await Save1912(store,P1912(units,data,store.Operations,"ammo.supply","77",targets:one));
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[store.Operations.Last()]);Assert(preview.Operations.Count==2,"旧局部和共享不同字段依赖组合");
            await service.CommitApplyAsync(preview,store);var (_,_,after)=await LoadP4Async(root);
            var cloned=after.Ammo(after.Weapon(after.Units.Single(u=>u.Name==U1911).Weapons.Single())!.Mounts[0].AmmoName)!;
            Assert(cloned.Field("ammo.supply")!.DisplayValue=="77" && cloned.Field("ammo.damage.physical")!.DisplayValue=="17","局部副本继承共享值");
            Assert(after.Ammo(A1911)!.Field("ammo.damage.physical")!.DisplayValue=="10" && after.Ammo(A1911)!.Field("ammo.supply")!.DisplayValue=="77","全局字段生效且保留局部伤害");
            await service.CommitRestoreAsync(service.PrepareRestore(root,preview.BackupId));
            await Save1912(store,P1912(units,data,[],"ammo.damage.physical","2",UnitBatchOperation.Multiply));
            var oldGroup=store.Operations.First().GroupId;var other=store.Operations.Single(o=>o.ObjectName!=A1911);
            await Save1912(store,P1912(units,data,store.Operations,"ammo.damage.physical","30",targets:one));
            Assert(store.Operations.Single(o=>o.ObjectName==A1911).GroupId!=oldGroup && store.Operations.Contains(other),"部分更新重新归组且保留旧批次其余草稿");
            preview=await service.PrepareApplyAsync(root,[other]);await service.CommitApplyAsync(preview,store);Assert(store.Operations.Count==1,"无共享依赖的Ammo可独立部分应用");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task Ammo1912StandaloneAndGuards()
    {
        var root=CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            File.Delete(Path.Combine(root,"GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf"));File.Delete(Path.Combine(root,"GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf"));
            var (_,units,data)=await LoadP4Async(root);Assert(units.Units.Count==0 && data.Weapons.Count==0,"独立Ammo样例");using var store=new DraftStore(root);await store.LoadAsync();
            await Save1912(store,P1912(units,data,[],"ammo.damage.physical","25"));var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);
            Assert(preview.UnitReadDependencies is not null,"共享Ammo也携带输入引用集合保护");
            var extra=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/NewWeapon.ndf");File.WriteAllText(extra,"export WeaponDescriptor_New is TWeaponManagerModuleDescriptor ( Salves = [1,] )",new UTF8Encoding(false));
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.CommitApplyAsync(preview,store),"预览后新增文件使影响过期");File.Delete(extra);
            var op=store.Operations[0];await store.UpsertAsync(op with { Summary=op.Summary+"changed" });
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.CommitApplyAsync(preview,store),"预览后草稿变化拒绝");
            preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            var (_,_,after)=await LoadP4Async(root);Assert(after.Ammunition.Where(a=>a.Field("ammo.damage.physical") is not null).All(a=>a.Field("ammo.damage.physical")!.DisplayValue=="25"),"无Unit/Weapon也可正式应用Ammo");
            await service.CommitRestoreAsync(service.PrepareRestore(root,preview.BackupId));
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task Ammo1912PreserveRecovery()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=CreateTemporaryFixtureCopy("p4-shared");string? locked=null;
            try
            {
                var source=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/Ammunition.ndf");var text=File.ReadAllText(source).Replace("\r\n","\n");var split=text.IndexOf("Ammo_P4_Alt",StringComparison.Ordinal);
                File.WriteAllText(source,("// keep head\n"+text[..split]+"// keep tail\n").Replace("\n",nl),new UTF8Encoding(false));
                var second=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/AmmunitionMissiles.ndf");File.WriteAllText(second,(File.ReadAllText(second).Replace("\r\n","\n")+"\n"+text[split..]).Replace("\n",nl),new UTF8Encoding(false));
                var (_,units,data)=await LoadP4Async(root);using var store=new DraftStore(root);await store.LoadAsync();await Save1912(store,P1912(units,data,[],"ammo.damage.physical","2",UnitBatchOperation.Multiply));
                var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);var formal=preview.Files.Where(f=>f.Kind==FormalTextFileKind.Ndf).ToArray();Assert(formal.Length==2,"跨两文件Ammo批次");
                locked=formal[1].FullPath;File.SetAttributes(locked,FileAttributes.ReadOnly);await TestAssert.ThrowsAsync<IOException>(()=>service.CommitApplyAsync(preview,store),"中途写入失败");
                Assert(formal.All(f=>File.ReadAllBytes(f.FullPath).SequenceEqual(f.OriginalBytes)) && store.Operations.Count==2,"原件恢复且两条草稿保留");
                File.SetAttributes(locked,FileAttributes.Normal);locked=null;preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
                foreach(var f in formal) Assert(File.ReadAllText(f.FullPath)==Encoding.UTF8.GetString(f.OriginalBytes).Replace("PhysicalDamages = 10.0","PhysicalDamages = 20").Replace("PhysicalDamages = 3.0","PhysicalDamages = 6"),"只改目标文本保留换行与注释");
                await service.CommitRestoreAsync(service.PrepareRestore(root,preview.BackupId));Assert(formal.All(f=>File.ReadAllBytes(f.FullPath).SequenceEqual(f.OriginalBytes)),"恢复字节一致");
            }
            finally { if(locked is not null && File.Exists(locked))File.SetAttributes(locked,FileAttributes.Normal);DeleteTemporaryFixture(root); }
        }
    }
    private static void Verify1912Ui(MainViewModel main,Window window,string returnRoot)
    {
        var root=CreateTemporaryFixtureCopy("p4-shared");var oldTheme=WarnoLiteModdingTool.App.Theming.ThemeManager.CurrentTheme;
        try
        {
            foreach(var advanced in new[]{false,true}) foreach(var language in new[]{"zh-CN","en"})
            {
                main.AdvancedMode=advanced;UiText.Current.SetLanguage(language);
                WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(advanced?WarnoLiteModdingTool.App.Theming.AppTheme.DarkBlue:WarnoLiteModdingTool.App.Theming.AppTheme.LightBlue,false);
                RunWithDispatcher(main.OpenProjectAsync(root),window.Dispatcher);main.SelectedModule=main.Modules.Single(m=>m.Key=="ammo");DrainDispatcher(window.Dispatcher);
                var vm=main.AmmoWorkspace!;Assert(!vm.HasBatchInspector,"默认仍是单条弹药检查器");
                var picked=vm.Ammunition.Where(a=>a.Ammo.Field("ammo.damage.physical") is not null).ToArray();foreach(var a in picked)a.IsBatchSelected=true;
                Assert(vm.HasBatchInspector && vm.BatchSelectedCount==2,"勾选两项在原右栏切换");
                vm.SelectedAmmo=vm.Ammunition.Single(a=>a.Ammo.Field("ammo.damage.physical") is null);Assert(vm.BatchSelectedCount==2,"浏览焦点与勾选独立");
                var mixed=vm.CommonBatchFields.Single(f=>f.Definition.Key=="ammo.damage.physical");Assert(mixed.IsMixed && mixed.EditValue=="","混合值留空，未被0替代");
                vm.BatchField=mixed;vm.BatchOperation=vm.BatchOperations.Single(o=>o.Operation==UnitBatchOperation.IncreasePercent);
                DrainDispatcher(window.Dispatcher);var inspector=FindVisualChildren<AmmoBatchInspector>(window).Single();Assert(inspector.Visibility==Visibility.Visible,"批量检查器可见");
                var input=FindVisualChildren<TextBox>(inspector).Single(t=>Equals(t.Tag,"ammo-batch-operand"));input.Text="10";DrainDispatcher(window.Dispatcher);
                FindVisualChildren<Button>(inspector).Single(b=>Equals(b.Tag,"ammo-batch-preview")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var until=DateTime.UtcNow.AddSeconds(15);while((!vm.CanEditBatch || vm.BatchRows.Count!=2) && DateTime.UtcNow<until){DrainDispatcher(window.Dispatcher);Thread.Sleep(10);}
                Assert(vm.BatchRows.Count==2 && vm.BatchRows.Single(r=>r.Name!=A1911).Target=="3.3","实际按钮预览逐项百分比");
                using(var disk=new DraftStore(root)){RunWithDispatcher(disk.LoadAsync(),window.Dispatcher);Assert(disk.Operations.Count==0,"预览不保存");}
                window.Width=advanced?1360:1160;window.Height=840;SaveUiSnapshot(window,$"1912-ammo-{language}-{advanced}.png");
                FindVisualChildren<Button>(inspector).Single(b=>Equals(b.Tag,"ammo-batch-save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                until=DateTime.UtcNow.AddSeconds(15);while(!vm.CanEditBatch && DateTime.UtcNow<until){DrainDispatcher(window.Dispatcher);Thread.Sleep(10);}
                using(var disk=new DraftStore(root)){RunWithDispatcher(disk.LoadAsync(),window.Dispatcher);Assert(disk.Operations.Count==2 && disk.Operations.All(AmmoBatchPlanner.IsBatch),"实际加入保存共享Ammo草稿");}
                var common=vm.CommonBatchFields.Single(f=>f.Definition.Key=="ammo.damage.physical");common.EditValue="19";DrainDispatcher(window.Dispatcher);
                FindVisualChildren<Button>(inspector).Single(b=>Equals(b.Tag,"ammo-common-save") && ReferenceEquals(b.DataContext,common)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                until=DateTime.UtcNow.AddSeconds(15);while(!vm.CanEditBatch && DateTime.UtcNow<until){DrainDispatcher(window.Dispatcher);Thread.Sleep(10);}
                using(var disk=new DraftStore(root)){RunWithDispatcher(disk.LoadAsync(),window.Dispatcher);Assert(disk.Operations.All(o=>o.TargetValue=="19"),"共有字段实际按钮更新已有共享草稿");}
                vm.SearchText="Ammo_P4_Shared";Assert(vm.BatchSelectedCount==2 && vm.CommonBatchFields.Single(f=>f.Definition.Key=="ammo.damage.physical").EditValue=="19","隐藏选择保留且共有值读取草稿");
                vm.BatchScope="当前筛选结果";Assert(vm.CommonBatchFields.Any(f=>f.Definition.Key=="ammo.supply"),"共有字段按完整筛选目标切换");
                vm.SelectFilteredBatch(false);vm.SelectFilteredBatch(true);Assert(vm.BatchSelectedCount==1,"筛选选择按数据集合处理");
                // Clear through the live draft center, so no separate store races with the VM.
                RunWithDispatcher(main.UnitWorkspace!.ClearDraftsAsync(),window.Dispatcher);
            }
        }
        finally
        {
            UiText.Current.SetLanguage("zh-CN");main.AdvancedMode=false;WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(oldTheme,false);
            RunWithDispatcher(main.OpenProjectAsync(returnRoot),window.Dispatcher);DeleteTemporaryFixture(root);
        }
    }
}
