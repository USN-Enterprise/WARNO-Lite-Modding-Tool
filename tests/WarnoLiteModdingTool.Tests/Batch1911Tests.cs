using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private const string W1911 = "WeaponDescriptor_P4_Shared", A1911 = "Ammo_P4_Shared", U1911 = "Descriptor_Unit_P4_One";
    private static WeaponBatchTarget T1911(string unit = U1911,int mount = 0,string weapon = W1911) => new(unit,weapon,mount);
    private static WeaponBatchPreview P1911(WeaponWorkspaceData data,IReadOnlyList<DraftOperation> drafts,string key,string value,
        WeaponBatchTarget[]? targets = null,UnitBatchOperation op = UnitBatchOperation.Set,bool global = false,bool replace = false,UnitBatchRounding rounding = UnitBatchRounding.None) =>
        WeaponBatch.Preview(data,drafts,new(targets ?? [T1911()],key,op,value,rounding,AllReferences:global,ReplaceExisting:replace));
    private static async Task Save1911(DraftStore store,WeaponBatchPreview p)
    { Assert(p.CanSave,"批改应可保存："+string.Join(" / ",p.Errors)); await store.ApplyBatchAsync(p.Upserts,p.Removals); }
    private static async Task Weapon1911Isolation()
    {
        foreach (var nl in new[] { "\n","\r\n" })
        {
            var root=CreateTemporaryFixtureCopy("p4-shared");
            try
            {
                var wp=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf");
                var body=File.ReadAllText(wp).Replace("Ammo_P4_Alt","Ammo_P4_Shared").Replace("\r\n","\n").Replace("\n",nl);
                File.WriteAllText(wp,body,new UTF8Encoding(false));
                var (_,_,data)=await LoadP4Async(root);using var store=new DraftStore(root);await store.LoadAsync();
                await Save1911(store,P1911(data,[],"ammo.damage.physical","17"));
                await Save1911(store,P1911(data,store.Operations,"weapon.salves","2",[T1911(),T1911(mount:1)],UnitBatchOperation.Multiply));
                await Save1911(store,P1911(data,store.Operations,"weapon.salves","9",[T1911("Descriptor_Unit_P4_Two")]));
                var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[store.Operations[0]]);
                Assert(preview.Operations.Count==3,"部分应用展开所有武器批次依赖");await service.CommitApplyAsync(preview,store);
                var (_,_,after)=await LoadP4Async(root);var one=after.Weapon(after.Units.Single(u=>u.Name==U1911).Weapons.Single())!;
                var two=after.Weapon(after.Units.Single(u=>u.Name=="Descriptor_Unit_P4_Two").Weapons.Single())!;
                Assert(one.Name!=two.Name && one.Field("weapon.salves.0")!.DisplayValue=="8" && two.Field("weapon.salves.0")!.DisplayValue=="9","不同作用域值隔离且共箱不重复乘");
                Assert(after.Ammo(one.Mounts[0].AmmoName)!.Field("ammo.damage.physical")!.DisplayValue=="17","选中挂载弹药隔离");
                Assert(one.Mounts[1].AmmoName==A1911 && two.Mounts.All(m=>m.AmmoName==A1911),"同Ammo未选挂载与另一单位保持引用");
                Assert(File.ReadAllText(wp).StartsWith(body,StringComparison.Ordinal),"原Weapon与注释换行完全保留");
                await service.CommitRestoreAsync(service.PrepareRestore(root,preview.BackupId));Assert(File.ReadAllText(wp)==body,"恢复整个多文件批次");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task Weapon1911Formulas()
    {
        var (_,_,data)=await LoadP4Async(Fixture("p4-shared"));
        var targets=new[]{T1911(),T1911(mount:1),T1911("Descriptor_Unit_P4_Three",0,"WeaponDescriptor_P4_Other")};
        var p=P1911(data,[],"weapon.salves","2",targets,UnitBatchOperation.Multiply);
        Assert(p.CanSave && p.FieldCount==2 && p.Rows.Select(r=>r.Target).SequenceEqual(new[]{"8","8","6"}),"跨Weapon语义定位与同箱去重");
        Assert(P1911(data,[],"weapon.salves","0.1",op:UnitBatchOperation.Multiply).Errors.Count>0,"整数不静默截断");
        Assert(P1911(data,[],"weapon.salves","0.1",op:UnitBatchOperation.Multiply,rounding:UnitBatchRounding.Ceiling).Rows[0].Target=="1","显式取整");
        Assert(P1911(data,[],"ammo.time.shot","25",op:UnitBatchOperation.IncreasePercent).Rows[0].Target=="1.25","小数不默认取整");
        Assert(P1911(data,[],"weapon.salves","2147483648").Errors.Count>0,"整数溢出拒绝");
        Assert(P1911(data,[],"ammo.damage.physical","NaN").Errors.Count>0,"非法浮点拒绝");
        Assert(P1911(data,[],"mount.ammo","missing").Errors.Count>0,"候选引用边界");
        Assert(P1911(data,[],"ammo.range.ground.min","5000").Errors.Count>0,"最终范围成对验证");
        Assert(P1911(data,[],"mount.count","2").Rows[0].Status.StartsWith("跳过"),"缺字段明确跳过");
        var angle=P1911(data,[],"turret.AngleRotationMax","90");
        Assert(angle.CanSave && Math.Abs(double.Parse(WeaponBatch.Read(angle.Upserts.Single()).Cells.Single().Raw,System.Globalization.CultureInfo.InvariantCulture)-Math.PI/2)<1e-10,"角度换算弧度");
        var same=P1911(data,[],"weapon.salves","2",op:UnitBatchOperation.Multiply);
        var repeat=P1911(data,[],"weapon.salves","2",op:UnitBatchOperation.Multiply);
        Assert(same.Rows[0].Target==repeat.Rows[0].Target,"重复预览不累乘");
        var blocked=P1911(data,same.Upserts,"weapon.salves","2",op:UnitBatchOperation.Multiply);
        Assert(!blocked.CanSave && blocked.Errors.Count>0,"重叠更改要求明确替换");
        var next=P1911(data,same.Upserts,"weapon.salves","2",op:UnitBatchOperation.Multiply,replace:true);
        Assert(next.CanSave && next.Rows[0].Current=="8" && next.Rows[0].Target=="16" && next.Removals.Count==1,"从有效草稿继续计算并仅替换明确范围");
        Assert(P1911(data,[],"weapon.salves","8",[T1911()]).Impacts.Any(s=>s.Contains("#1")),"未选同箱挂载列入影响");
    }
    private static async Task Weapon1911Composition()
    {
        var root=CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_,_,data)=await LoadP4Async(root);using var store=new DraftStore(root);await store.LoadAsync();
            var global=CreateWeaponDraft(data.Ammo(A1911)!.Field("ammo.supply")!,"77",DraftEditScope.AllReferences,[],null);await store.UpsertAsync(global);
            await Save1911(store,P1911(data,store.Operations,"ammo.damage.physical","20"));
            await Save1911(store,P1911(data,store.Operations,"ammo.damage.physical","20",[T1911("Descriptor_Unit_P4_Two")]));
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            var (_,_,after)=await LoadP4Async(root);var w1=after.Units.Single(u=>u.Name==U1911).Weapons.Single();var w2=after.Units.Single(u=>u.Name=="Descriptor_Unit_P4_Two").Weapons.Single();
            Assert(w1==w2,"最终相同结果复用一个Weapon副本");var a=after.Ammo(after.Weapon(w1)!.Mounts[0].AmmoName)!;
            Assert(a.Field("ammo.supply")!.DisplayValue=="77" && a.Field("ammo.damage.physical")!.DisplayValue=="20","局部Ammo继承共享不同字段修改");
            Assert(after.Ammo(A1911)!.Field("ammo.damage.physical")!.DisplayValue=="10","其他单位保留共享伤害原值");
            await service.CommitRestoreAsync(service.PrepareRestore(root,preview.BackupId));
            var replacement=P1911(data,[],"mount.ammo","Ammo_P4_Alt");await Save1911(store,replacement);
            var parameter=P1911(data,store.Operations,"ammo.damage.physical","12");
            Assert(parameter.Rows[0].Current=="3","替换后参数以最终Ammo为基线");await Save1911(store,parameter);
            preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            (_,_,after)=await LoadP4Async(root);a=after.Ammo(after.Weapon(after.Units.Single(u=>u.Name==U1911).Weapons.Single())!.Mounts[0].AmmoName)!;
            Assert(a.Field("ammo.damage.physical")!.DisplayValue=="12" && a.Field("ammo.supply") is null,"最终Ammo引用隔离且不污染旧Ammo");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task Weapon1911Guards()
    {
        var root=CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_,_,data)=await LoadP4Async(root);using var store=new DraftStore(root);await store.LoadAsync();
            await Save1911(store,P1911(data,[],"weapon.salves","8"));
            using(var reloaded=new DraftStore(root)){await reloaded.LoadAsync();Assert(WeaponBatch.Read(reloaded.Operations.Single()).Cells.Single().Unit==U1911,"语义草稿重开保留范围");}
            var global=P1911(data,store.Operations,"weapon.salves","9",global:true);
            Assert(!global.CanSave,"共享与局部同字段冲突拒绝");
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);
            var path=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/NewUser.ndf");File.WriteAllText(path,"export Descriptor_Unit_New is TEntityDescriptor ( ModulesDescriptors = [ $/GFX/Weapon/"+W1911+", ] )",new UTF8Encoding(false));
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.CommitApplyAsync(preview,store),"新增引用使旧预览失效");File.Delete(path);
            var wp=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf");var body=File.ReadAllText(wp);File.WriteAllText(wp,body.Replace("AmmoBoxIndex = 0","AmmoBoxIndex = 1"));
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.PrepareApplyAsync(root,store.Operations),"挂载锚点变化拒绝");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task Weapon1911LifecycleRecovery()
    {
        var root=Fixture199();string? locked=null;
        try
        {
            var (_,units,data)=await LoadP4Async(root);var unit=units.Units.Single(u=>u.Name=="Descriptor_Unit_Test_Tank_US");
            using var store=new DraftStore(root);await store.LoadAsync();
            var t=new WeaponBatchTarget(unit.Name,unit.Weapons.Single(),0);
            await Save1911(store,P1911(data,[],"weapon.salves","8",[t]));
            var unrelated=data.Units.Single(u=>u.Name!=unit.Name);
            var other=P1911(data,store.Operations,"weapon.salves","9",[new(unrelated.Name,unrelated.Weapons.Single(),0)]);
            await Save1911(store,other);
            var rename=Core.Units.UnitIdentityEditing.Operation(unit,"Descriptor_Unit_Batch_Renamed");await store.UpsertAsync(rename);
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[store.Operations.First()]);
            Assert(preview.Operations.Count==2,"关联改名闭合而独立武器批次不被夹带");
            var formal=preview.Files.Where(f=>f.Kind==FormalTextFileKind.Ndf).ToArray();Assert(formal.Length>=2,"多文件批量事务");
            locked=formal.Last().FullPath;File.SetAttributes(locked,FileAttributes.ReadOnly);
            await TestAssert.ThrowsAsync<IOException>(()=>service.CommitApplyAsync(preview,store),"批量多文件中途失败");
            Assert(formal.All(f=>File.ReadAllBytes(f.FullPath).SequenceEqual(f.OriginalBytes)) && store.Operations.Count==3,"所有原件恢复且草稿保留");
            File.SetAttributes(locked,FileAttributes.Normal);locked=null;
            preview=await service.PrepareApplyAsync(root,[store.Operations.First()]);await service.CommitApplyAsync(preview,store);
            var (_,reloaded,weapons)=await LoadP4Async(root);var renamed=reloaded.Units.Single(u=>u.Name=="Descriptor_Unit_Batch_Renamed");
            Assert(weapons.Weapon(renamed.Weapons.Single())!.Field("weapon.salves.0")!.DisplayValue=="8","改名与武器重定向完整组合");
            Assert(store.Operations.Count==1 && store.Operations.Single().Id==other.Upserts.Single().Id,"部分应用保留独立批次");
        }
        finally { if(locked is not null && File.Exists(locked))File.SetAttributes(locked,FileAttributes.Normal);DeleteTemporaryFixture(root); }
    }
    private static void Verify1911Ui(Window main)
    {
        var root=CreateTemporaryFixtureCopy("p4-shared");var oldMode=WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced;
        var oldTheme=WarnoLiteModdingTool.App.Theming.ThemeManager.CurrentTheme;
        try
        {
            var load=LoadP4Async(root);RunWithDispatcher(load,main.Dispatcher);var data=load.Result.Item3;
            using var store=new DraftStore(root);RunWithDispatcher(store.LoadAsync(),main.Dispatcher);
            foreach(var advanced in new[]{false,true}) foreach(var lang in new[]{"zh-CN","en"})
            {
                WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced=advanced;
                WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(lang);
                WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(advanced?WarnoLiteModdingTool.App.Theming.AppTheme.DarkBlue:WarnoLiteModdingTool.App.Theming.AppTheme.LightBlue,false);
                var window=new WeaponBatchWindow(data,store,[U1911],[U1911],()=>{}) { Owner=main, Width=advanced?1240:900, Height=advanced?870:750 };
                window.Show();DrainDispatcher(main.Dispatcher);
                Assert(window.Title==(lang=="en"?"Batch edit weapons":"批量修改武器"),"批改标题实际翻译");
                var table=FindVisualChildren<DataGrid>(window).Single(g=>Equals(g.Tag,"batch-targets"));
                var rows=table.ItemsSource.Cast<WeaponBatchWindow.TargetRow>().ToArray();Assert(rows.Length==5 && rows.All(r=>!r.Selected),"独立选择且无默认全选");
                table.SelectedItem=rows[0];Assert(rows.All(r=>!r.Selected),"浏览焦点不改变勾选");
                FindVisualChildren<Button>(window).Single(b=>Equals(b.Tag,"使用单位筛选全部结果")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(rows.Count(r=>r.Selected)==2,"从完整单位筛选取挂载");
                FindVisualChildren<TextBox>(window).Single(t=>Equals(t.Tag,"batch-search")).Text="P4_One";
                FindVisualChildren<Button>(window).Single(b=>Equals(b.Tag,"全选武器筛选结果")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(rows.Count(r=>r.Selected)==2,"全选真正筛选结果");
                var parameter=FindVisualChildren<SearchPicker>(window).Single(p=>Equals(p.Tag,"batch-parameter"));
                parameter.SelectedItem=parameter.ItemsSource!.Cast<WeaponBatchWindow.Choice>().Single(p=>p.Key=="ammo.damage.physical");
                FindVisualChildren<TextBox>(window).Single(t=>Equals(t.Tag,"batch-operand")).Text="19";
                FindVisualChildren<Button>(window).Single(b=>Equals(b.Tag,"预览")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var result=FindVisualChildren<DataGrid>(window).Single(g=>Equals(g.Tag,"batch-preview"));
                var until=DateTime.UtcNow.AddSeconds(15);while(result.Items.Count!=2 && DateTime.UtcNow<until){DrainDispatcher(main.Dispatcher);Thread.Sleep(10);}
                Assert(result.Items.Count==2,"实际预览事件完成并显示全部目标");
                SaveUiSnapshot(window,$"1911-batch-{lang}-{advanced}.png");
                FindVisualChildren<Button>(window).Single(b=>Equals(b.Tag,"batch-save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                until=DateTime.UtcNow.AddSeconds(15);while(store.Operations.Count!=1 && DateTime.UtcNow<until){DrainDispatcher(main.Dispatcher);Thread.Sleep(10);}
                Assert(store.Operations.Count==1 && WeaponBatch.Read(store.Operations[0]).Cells.Count==2,"实际保存按钮写入一个批次的两个挂载");
                window.Close();RunWithDispatcher(store.ApplyBatchAsync([],store.Operations.Select(o=>o.Id).ToArray()),main.Dispatcher);
            }
        }
        finally
        {
            WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced=oldMode;WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh-CN");
            WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(oldTheme,false);DeleteTemporaryFixture(root);
        }
    }
}
