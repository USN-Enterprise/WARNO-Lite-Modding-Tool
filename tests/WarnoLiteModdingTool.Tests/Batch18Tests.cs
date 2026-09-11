using WarnoLiteModdingTool.Core.Batch;
using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Projects;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static Task UnitCreationTransaction() => VerifyUnitCreation(false);
    private static Task UnitCreationWithP4() => VerifyUnitCreation(true);
    private static Task UnitCreationFixedSlots() => VerifyUnitCreation(true, false, true);

    private static async Task UnitCreationRejectsMissingAmmo()
    {
        await VerifyUnitCreation(false, true);
        await VerifyUnitCreation(true, true);
    }

    private static async Task VerifyUnitCreation(bool withP4, bool missingAmmo = false, bool fixedSlots = false)
    {
        foreach(var newline in new[]{"\n","\r\n"})
        {
            var root=CreateTemporaryFixtureCopy("p2-unit-complete");var divRoot=CreateTemporaryFixtureCopy("p5-division");
            try{
                var sourcePath=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");var source=File.ReadAllText(sourcePath);var at=source.IndexOf("    ModulesDescriptors",StringComparison.Ordinal);source=source.Insert(at,"    ClassNameForDebug = 'Unit_Test_Tank_US'\n");source+="\n"+File.ReadAllText(Path.Combine(divRoot,"GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf"));source=source.Replace("\r\n","\n").Replace("\n",newline);File.WriteAllText(sourcePath,source,new UTF8Encoding(false));
                foreach(var file in Directory.GetFiles(Path.Combine(divRoot,"GameData/Generated/Gameplay/Decks"))){var target=Path.Combine(root,"GameData/Generated/Gameplay/Decks",Path.GetFileName(file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,true);}
                File.WriteAllText(Path.Combine(root,UnitCreation.SerializerPath),"unnamed TDeckSerializerEntries\n(\n UnitIds = MAP [ (Descriptor_Unit_Test_Tank_US, 17), (Descriptor_Unit_Test_Recon_SOV, 3) /* last entry without comma */ ]\n)\n".Replace("\n",newline),new UTF8Encoding(false));
                if (missingAmmo)
                {
                    var weaponPath = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf");
                    File.WriteAllText(weaponPath, File.ReadAllText(weaponPath).Replace("Ammo_Test_AP", "Ammo_Test_Missing"), new UTF8Encoding(false));
                }
                if(fixedSlots)
                {
                    var path=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf");
                    var weaponText=File.ReadAllText(path).Replace("TMountedWeaponDescriptor(Ammunition = $/GFX/Weapon/Ammo_Test_AP AmmoBoxIndex = 0),", "TMountedWeaponDescriptor(Ammunition = $/GFX/Weapon/Ammo_Test_AP AmmoBoxIndex = 0),\n                TMountedWeaponDescriptor(Ammunition = $/GFX/Weapon/Ammo_Test_AP AmmoBoxIndex = 0),");
                    File.WriteAllText(path,weaponText.Replace("\r\n","\n").Replace("\n",newline),new UTF8Encoding(false));
                }
                var (context,units,weapons)=await LoadP4Async(root);var index=await new ProjectIndexer().IndexAsync(context);var divisions=await new DivisionProjectLoader().LoadAsync(context,index,units);var mother=units.Units.Single(u=>u.Name=="Descriptor_Unit_Test_Tank_US");var division=divisions.Divisions.Single();
                var state=UnitCreation.New(mother,units,[]) with {Name="新坦克 \"测试\"",IndependentWeapons=true,Fields=new(){{"survival.health","15"}}};
                foreach(var w in mother.Weapons){var weapon=weapons.Weapon(w)!;state.WeaponBaselines[w]=File.ReadAllText(weapon.Source.SourceFile).Substring(weapon.Source.CharacterOffset,weapon.Source.CharacterLength);}
                if(fixedSlots)
                {
                    state=state with {IndependentWeapons=false,MountChoices=[new(mother.Weapons.Single(),1,"Ammo_Test_MG")]};
                    foreach(var invalid in new[]{new UnitCreationMountChoice(mother.Weapons.Single(),99,"Ammo_Test_MG"),new UnitCreationMountChoice(mother.Weapons.Single(),1,"Ammo_Missing"),new UnitCreationMountChoice("WeaponDescriptor_Test_Recon_SOV",0,"Ammo_Test_MG")})
                    {
                        try{UnitCreation.ValidateMountChoices(mother,state with {MountChoices=[invalid]},weapons);throw new Exception("无效槽位选择必须拒绝");}catch(TransactionValidationException){}
                    }
                    try{UnitCreation.ValidateMountChoices(mother,state with {MountChoices=[state.MountChoices[0],state.MountChoices[0]]},weapons);throw new Exception("重复槽位必须拒绝");}catch(TransactionValidationException){}
                    var restored=UnitCreation.Read(UnitCreation.Operation(mother,state));TestAssert.Equal("Ammo_Test_MG",restored.MountChoices.Single().AmmoName,"槽位草稿回读");
                }
                state.Divisions[division.Name]=new(state.Id,true,[],2,3,[1,.5,0,0]);state.DivisionBaselines[division.Name]=DivisionDraftCodec.Serialize(division.Baseline);
                var operation=UnitCreation.Operation(mother,state);using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(operation);
                TestAssert.Equal(18,state.SerializerId,"扫描全部既有编号");var next=UnitCreation.New(mother,units,store.Operations);TestAssert.Equal(state.SerializerId+1,next.SerializerId,"多个待创建单位编号不冲突");
                await store.UpsertAsync(CreateDivisionDraft(division,division.Baseline with {UnitRules=division.Baseline.UnitRules.Select(r=>r with {MaxPackNumber=r.MaxPackNumber+1}).ToArray()}));
                if (withP4)
                {
                    var otherWeapon = weapons.Weapon("WeaponDescriptor_Test_Recon_SOV")!;
                    await store.UpsertAsync(CreateWeaponDraft(otherWeapon.Field("weapon.salves.0")!, "7", DraftEditScope.AllReferences, [], otherWeapon.Name));
                }
                if (missingAmmo)
                {
                    var originals = Directory.GetFiles(Path.Combine(root, "GameData"), "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
                    try
                    {
                        await new UnitTransactionService().PrepareApplyAsync(root, store.Operations);
                        throw new Exception("真实悬空Ammo必须拒绝");
                    }
                    catch (TransactionValidationException ex)
                    {
                        TestAssert.True(ex.Message.Contains("悬空 Ammo") && ex.Message.Contains("Ammo_Test_Missing"), "应定位真实缺失的Ammo：" + ex.Message);
                    }
                    foreach (var file in originals) TestAssert.True(file.Value.SequenceEqual(File.ReadAllBytes(file.Key)), "引用失败不写正式文件");
                    continue;
                }
                var before=File.ReadAllBytes(sourcePath);var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);TestAssert.True(before.SequenceEqual(File.ReadAllBytes(sourcePath)),"预览不写正式文件");var csvFile=preview.Files.Single(f=>f.Kind==FormalTextFileKind.Csv).FullPath;
                File.SetAttributes(csvFile,FileAttributes.ReadOnly);try{await TestAssert.ThrowsAsync<IOException>(()=>service.CommitApplyAsync(preview,store),"创建事务中途失败回滚");}finally{File.SetAttributes(csvFile,FileAttributes.Normal);}
                foreach(var file in preview.Files)TestAssert.True(file.Existed?File.ReadAllBytes(file.FullPath).SequenceEqual(file.OriginalBytes):!File.Exists(file.FullPath),"创建回滚逐文件保持");
                TestAssert.True(store.Operations.Any(o=>o.TargetKind==DraftTargetKind.UnitCreate),"回滚保留创建草稿");
                preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
                var (_,after,afterWeapons)=await LoadP4Async(root);var created=after.Units.Single(u=>u.Name==state.Id);TestAssert.Equal(state.Name,created.DisplayName,"创建名称CSV回读");TestAssert.Equal("15",created.Field("survival.health")!.DisplayValue,"创建字段回读");TestAssert.Equal(UnitCreation.Source(mother),UnitCreation.Source(after.Units.Single(u=>u.Name==mother.Name)),"母版逐字保留");TestAssert.True(created.Weapons.Single()!=mother.Weapons.Single(),"独立武器不修改母版引用");
                if(fixedSlots)
                {
                    var original=weapons.Weapon(mother.Weapons.Single())!;var copied=afterWeapons.Weapon(created.Weapons.Single())!;
                    TestAssert.Equal(2,copied.Mounts.Count,"固定槽位数量保持");
                    TestAssert.Equal("Ammo_Test_AP",copied.Mounts[0].AmmoName,"未选择槽位保持");TestAssert.Equal("Ammo_Test_MG",copied.Mounts[1].AmmoName,"只替换所选槽位");
                    TestAssert.True(copied.Mounts.Select(m=>(m.TurretIndex,m.AmmoBoxIndex)).SequenceEqual(original.Mounts.Select(m=>(m.TurretIndex,m.AmmoBoxIndex))),"炮塔与共用弹药箱保持");
                    TestAssert.Equal(state.WeaponBaselines[original.Name],File.ReadAllText(original.Source.SourceFile).Substring(original.Source.CharacterOffset,original.Source.CharacterLength),"母版武器逐字保持");
                    TestAssert.True(!preview.Files.Any(f=>f.RelativePath.Contains("Ammunition")),"不修改共享Ammo");
                }
                if (withP4) TestAssert.Equal("7", afterWeapons.Weapon("WeaponDescriptor_Test_Recon_SOV")!.Field("weapon.salves.0")!.DisplayValue, "新建与P4一起提交均生效");
                var (_,_,afterDivisions)=await LoadP5Async(root);TestAssert.True(afterDivisions.Divisions.Count==1,string.Join(";",afterDivisions.Diagnostics));TestAssert.True(afterDivisions.Divisions.Single().Baseline.UnitRules.Any(r=>r.Unit==state.Id&&r.NumberOfUnitInPack==3),"新单位师内规则同事务写入");TestAssert.True(!File.ReadAllBytes(sourcePath).Take(3).SequenceEqual(new byte[]{239,187,191}),"创建写回无BOM");
                var text=File.ReadAllText(sourcePath);TestAssert.True(newline=="\n"?!text.Contains("\r"):!text.Replace("\r\n","").Contains('\n'),"单位文件换行保持");
                var conflict=UnitCreation.Operation(mother,state);TestAssert.Equal(DraftResolutionStatus.Conflict,UnitCreation.Resolve(after,conflict).Status,"重复创建已存在身份被拒绝");
            }finally{DeleteTemporaryFixture(root);DeleteTemporaryFixture(divRoot);}
        }
    }
    private static void VerifyBatch18Ui(WarnoLiteModdingTool.App.ViewModels.MainViewModel vm,System.Windows.Window main,string root)
    {
        WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh");
        vm.AdvancedMode=false;
        var workspace=vm.UnitWorkspace!;
        Assert(!workspace.Fields.Single(f=>f.Key=="structure.upgradeFrom").IsVisible,"升级来源只在高级模式出现");
        Assert(workspace.Fields.Count(f=>f.Key.EndsWith(".family")&&f.Key.StartsWith("armor.")&&f.IsVisible)==0,"基础模式隐藏护甲类型");
        Assert(workspace.Fields.Single(f=>f.Key=="armor.front.family").DisplayLabel=="护甲类型","四向合并标题");
        Assert(main.Icon is not null,"窗口使用W图标");
        workspace.ClearBatchSelection();workspace.SelectedUnit!.IsBatchSelected=true;
        workspace.SelectedBatchField=workspace.BatchFieldOptions.Single(f=>f.Definition.Key=="survival.health");workspace.SelectedBatchOperation=workspace.BatchOperationOptions.First(o=>o.Operation==UnitBatchOperation.Set);workspace.BatchOperand="19";
        Assert(workspace.CanAddBatchDrafts,"不预览也可加入草稿");var add=workspace.AddBatchToDraftsAsync();while(!add.IsCompleted){DrainDispatcher(main.Dispatcher);Thread.Sleep(5);}add.GetAwaiter().GetResult();Assert(workspace.DraftItems.Any(d=>d.Resolved.Operation.FieldKey=="survival.health"&&d.Resolved.Operation.TargetValue=="19"),"不预览直接加入仍使用当前输入校验");workspace.ClearBatchSelection();
        var (context,units,weapons)=Task.Run(()=>LoadP4Async(root)).GetAwaiter().GetResult();var index=new ProjectIndexer().IndexAsync(context).GetAwaiter().GetResult();var divisions=new DivisionProjectLoader().LoadAsync(context,index,units).GetAwaiter().GetResult();
        var wizard=new WarnoLiteModdingTool.App.Controls.UnitCreationWindow(units,weapons,divisions,[]);
        SaveUiSnapshot(wizard,"creation-1.png");var tabs=FindVisualChildren<System.Windows.Controls.TabControl>((System.Windows.DependencyObject)wizard.Content).Single();
        tabs.SelectedIndex=2;DrainDispatcher(main.Dispatcher);
        var slots=FindVisualChildren<WarnoLiteModdingTool.App.Controls.UnitCreationWeaponPanel>((System.Windows.DependencyObject)wizard.Content).Single();
        var slotPicker=FindVisualChildren<WarnoLiteModdingTool.App.Controls.SearchPicker>(slots).First();
        Assert(slots.Choices.Count==0,"初始槽位沿用母版");
        var originalSelection=slotPicker.SelectedItem;
        slotPicker.SelectedItem=slotPicker.ItemsSource!.Cast<object>().First(a=>!Equals(a,originalSelection));
        Assert(slots.Choices.Count==1,"槽位选择生成草稿目标");
        var selectedChoices=slots.Choices.ToArray();
        var slotMother=units.Units.First();slots.Load(slotMother,weapons,selectedChoices);DrainDispatcher(main.Dispatcher);
        Assert(slots.Choices.SequenceEqual(selectedChoices),"重新加载恢复槽位选择");
        FindVisualChildren<System.Windows.Controls.Button>(slots).First(b=>Equals(b.Content,"恢复母版")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert(slots.Choices.Count==0,"恢复母版清除槽位替换");
        for(var page=1;page<5;page++){tabs.SelectedIndex=page;DrainDispatcher(main.Dispatcher);SaveUiSnapshot(wizard,"creation-"+(page+1)+".png");}wizard.Close();
        var filter=new WarnoLiteModdingTool.App.Controls.FacetFilter{IsExpanded=true,Rows=new[]{
            new WarnoLiteModdingTool.App.Controls.FilterRow("shared",new Dictionary<string,string[]>{{"国家",["DDR"]},{"角色",["infantry"]}}),
            new WarnoLiteModdingTool.App.Controls.FilterRow("shared",new Dictionary<string,string[]>{{"国家",["US"]},{"角色",["armor"]}}),
            new WarnoLiteModdingTool.App.Controls.FilterRow("match",new Dictionary<string,string[]>{{"国家",["DDR"]},{"角色",["armor"]}})}};
        var filterWindow=new System.Windows.Window{Content=filter,Width=520,Height=520};
        var deadline=DateTime.UtcNow.AddSeconds(5);while(filter.Content is null&&DateTime.UtcNow<deadline){DrainDispatcher(main.Dispatcher);Thread.Sleep(10);}
        SaveUiSnapshot(filterWindow,"facets.png");foreach(var e in FindVisualChildren<System.Windows.Controls.Expander>(filter))e.IsExpanded=true;SaveUiSnapshot(filterWindow,"facets.png");
        var boxes=FindVisualChildren<System.Windows.Controls.CheckBox>(filter).ToArray();boxes.Single(c=>Equals(c.Content,"东德")).IsChecked=true;boxes.Single(c=>Equals(c.Content,"装甲")).IsChecked=true;
        Assert(!filter.Matches("shared")&&filter.Matches("match"),"多个使用者筛选必须落在同一单位，不能跨引用者拼条件");SaveUiSnapshot(filterWindow,"facets-selected.png");filterWindow.Close();
        vm.AdvancedMode=true;Assert(workspace.Fields.Count(f=>f.Key.EndsWith(".family")&&f.Key.StartsWith("armor.")&&f.IsVisible)==1,"专业模式统一护甲类型");vm.AdvancedMode=false;
    }

}
