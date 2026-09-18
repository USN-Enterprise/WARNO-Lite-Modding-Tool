using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private const string Unit199Path = "GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf";
    private static string Fixture199(string nl = "\n")
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        var path = Path.Combine(root, Unit199Path);
        var text = File.ReadAllText(path).Replace("    ModulesDescriptors", "    ClassNameForDebug = 'custom debug'\n    ModulesDescriptors", StringComparison.Ordinal).Replace("\"AllUnits\", \"Char\"", "\"UNITE_Test_Tank_US\", \"AllUnits\", \"Char\"");
        File.WriteAllText(path, text.Replace("\r\n", "\n").Replace("\n", nl), new UTF8Encoding(false));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, UnitCreation.SerializerPath))!);
        File.WriteAllText(Path.Combine(root, UnitCreation.SerializerPath), "unnamed TDeckSerializerEntries\n(\n UnitIds = MAP [ ($/GFX/Unit/Descriptor_Unit_Test_Tank_US, 17), ($/GFX/Unit/Descriptor_Unit_Test_Recon_SOV, 3) /* keep */ ]\n)\n".Replace("\n", nl), new UTF8Encoding(false));
        var abilityPath = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/CapaciteList.ndf");
        var abilities = UnitCapabilities.Traits.SelectMany(t => t.Skills).Append("Unused_Custom").Distinct().Select(n => "export Capacite_" + n + " is TCapaciteDescriptor\n(\n SelfEffect = ~/UnitEffect_Custom\n RangeGRU = 123\n)\n");
        File.WriteAllText(abilityPath, string.Join("\n", abilities) + "\nexport UnitEffect_Custom is TEffectsPackDescriptor\n(\n Effects = []\n)\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "GameData/CapacityAnchors.ndf"), "Anchors is TObject\n(\n Values = [$/Custom/Capacite_Choc, $/Custom/UnitEffect_Custom,]\n)\n", new UTF8Encoding(false));
        return root;
    }
    private static async Task Unit199RegistrationNames()
    {
        var root = Fixture199();
        try
        {
            var (_, units, _) = await LoadP4Async(root); var mother = units.Units.First();
            var state = UnitCreation.New(mother, units, []);
            TestAssert.Equal(mother.Name + "_mod_001", state.Id, "默认可读名称");
            var operation = UnitCreation.Operation(mother, state);
            TestAssert.Equal(mother.Name + "_mod_002", UnitCreation.New(mother, units, [operation]).Id, "草稿序号占用");
            var high = state with { Id = mother.Name + "_mod_999" };
            TestAssert.Equal(mother.Name + "_mod_1000", UnitCreation.New(mother, units, [UnitCreation.Operation(mother, high)]).Id, "序号扩位");
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(operation);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
            TestAssert.True(Encoding.UTF8.GetString(preview.Files.Single(f => f.RelativePath == UnitCreation.SerializerPath).CandidateBytes).Contains("$/GFX/Unit/" + state.Id), "继承完整注册路径");
            await service.CommitApplyAsync(preview, store);
            var (_, created, _) = await LoadP4Async(root);
            var record = created.Units.Single(u => u.Name == state.Id);
            TestAssert.Equal(state.Id, UnitCreationHistory.Require(new(root), record.Source.RelativeSourceFile, state.Id).Name, "创建来源持久化");
            var registry = Path.Combine(root, UnitCreation.SerializerPath);
            File.WriteAllText(registry, File.ReadAllText(registry).Replace("$/GFX/Unit/" + state.Id, state.Id), new UTF8Encoding(false));
            await store.UpsertAsync(UnitIdentityEditing.Operation(record, record.Name, true));
            preview = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(preview, store);
            TestAssert.True(File.ReadAllText(registry).Contains("$/GFX/Unit/" + state.Id), "旧错误定向修复");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task Unit199RenameAbilities()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        {
            var root = Fixture199(nl);
            try
            {
                var (_, units, _) = await LoadP4Async(root); var unit = units.Units.First(); var graph = new UnitProjectGraph(root);
                var original = UnitCreation.Source(unit); var baseline = UnitCapabilities.FromBody(original);
                var state = baseline;
                foreach (var trait in UnitCapabilities.Traits) state = UnitCapabilities.Toggle(state, trait, true, graph, unit.Source.RelativeSourceFile);
                var body = UnitCapabilities.Apply(original, baseline, state);
                foreach (var trait in UnitCapabilities.Traits) TestAssert.True(UnitCapabilities.Status(UnitCapabilities.FromBody(body), trait) is "已配套" or "自定义或无法判断", "配套 " + trait.Key);
                foreach (var trait in UnitCapabilities.Traits) state = UnitCapabilities.Toggle(state, trait, false, graph, unit.Source.RelativeSourceFile);
                TestAssert.Equal(0, state.Skills.Length, "全部配套移除");
                state = UnitCapabilities.Toggle(baseline, UnitCapabilities.Traits.Single(t => t.Key == "choc"), true, graph, unit.Source.RelativeSourceFile);
                state = state with { Skills = state.Skills.Append("$/Custom/Capacite_Unused_Custom").ToArray() };
                using var store = new DraftStore(root); await store.LoadAsync();
                var rename = UnitIdentityEditing.Operation(unit, "Descriptor_Unit_Renamed_Test");
                await store.UpsertAsync(rename); await store.UpsertAsync(UnitCapabilities.Operation(unit, state));
                var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, [rename]);
                TestAssert.Equal(2, preview.Operations.Count, "部分选择自动闭合能力依赖");
                var source = preview.Files.Single(f => f.RelativePath == Unit199Path);
                var candidate = Encoding.UTF8.GetString(source.CandidateBytes);
                TestAssert.True(candidate.Contains("UpgradeFromUnit = Descriptor_Unit_Renamed_Test"), "裸升级引用迁移");
                TestAssert.True(candidate.Contains("Capacite_Unused_Custom"), "未使用自定义能力保留");
                TestAssert.True(candidate.Contains("'custom debug'"), "自定义调试名保留");
                await service.CommitApplyAsync(preview, store);
                var (_, after, _) = await LoadP4Async(root); var renamed = after.Units.Single(u => u.Name == "Descriptor_Unit_Renamed_Test");
                TestAssert.Equal(UnitIdentityEditing.GuidOf(original), UnitIdentityEditing.GuidOf(UnitCreation.Source(renamed)), "GUID保持");
                TestAssert.True(nl == "\n" ? !File.ReadAllText(Path.Combine(root, Unit199Path)).Contains('\r') : !File.ReadAllText(Path.Combine(root, Unit199Path)).Replace("\r\n", "").Contains('\n'), "换行保持");
                var current = UnitCapabilities.FromBody(UnitCreation.Source(renamed));
                await store.UpsertAsync(UnitCapabilities.Operation(renamed, current with { Skills = [] }));
                preview = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(preview, store);
                var (_, cleared, _) = await LoadP4Async(root);
                TestAssert.Equal(0, UnitCapabilities.FromBody(UnitCreation.Source(cleared.Units.Single(u => u.Name == renamed.Name))).Skills.Length, "专业能力清空");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task Unit199DeletionRestore()
    {
        var root = Fixture199();
        try
        {
            var (_, units, _) = await LoadP4Async(root); var mother = units.Units.First();
            using var store = new DraftStore(root); await store.LoadAsync();
            var state = UnitCreation.New(mother, units, []); var operation = UnitCreation.Operation(mother, state);
            await store.UpsertAsync(operation);
            var renamedPending = UnitCreation.Operation(mother, state with { Id = "Descriptor_Unit_Pending_Custom" });
            await UnitDraftLinks.ReplaceCreationAsync(store, operation, renamedPending);
            TestAssert.Equal(1, store.Operations.Count, "待创建改名只有一项");
            await UnitDraftLinks.CancelCreationAsync(store, renamedPending); TestAssert.Equal(0, store.Operations.Count, "取消创建");
            await store.UpsertAsync(operation);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(preview, store);
            var (_, created, _) = await LoadP4Async(root); var unit = created.Units.Single(u => u.Name == state.Id);
            var rename = UnitIdentityEditing.Operation(unit, "Descriptor_Unit_Created_Renamed");
            await store.UpsertAsync(rename); preview = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(preview, store);
            var (_, named, _) = await LoadP4Async(root); unit = named.Units.Single(u => u.Name == "Descriptor_Unit_Created_Renamed");
            _ = UnitCreationHistory.Require(new(root), unit.Source.RelativeSourceFile, unit.Name);
            var before = File.ReadAllBytes(Path.Combine(root, Unit199Path));
            await store.UpsertAsync(UnitDeletion.Operation(unit));
            var registryPath = Path.Combine(root, UnitCreation.SerializerPath); var registry = File.ReadAllText(registryPath);
            File.WriteAllText(registryPath, registry.Replace("$/GFX/Unit/" + unit.Name, "$/Unknown/" + unit.Name));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "不明限定注册路径不得遗漏后删除");
            File.WriteAllText(registryPath, registry.Replace("$/GFX/Unit/" + unit.Name, unit.Name));
            preview = await service.PrepareApplyAsync(root, store.Operations);
            TestAssert.True(!Encoding.UTF8.GetString(preview.Files.Single(f => f.RelativePath == UnitCreation.SerializerPath).CandidateBytes).Contains(unit.Name), "旧裸名错误注册可随删除清理");
            File.WriteAllText(registryPath, registry.Replace("($/GFX/Unit/" + unit.Name + ", " + state.SerializerId + "),", ""));
            _ = await service.PrepareApplyAsync(root, store.Operations);
            File.WriteAllText(registryPath, registry);
            preview = await service.PrepareApplyAsync(root, store.Operations);
            var csv = preview.Files.Single(f => f.Kind == FormalTextFileKind.Csv).FullPath;
            File.SetAttributes(csv, FileAttributes.ReadOnly);
            try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "删除跨文件失败恢复"); }
            finally { File.SetAttributes(csv, FileAttributes.Normal); }
            TestAssert.True(before.SequenceEqual(File.ReadAllBytes(Path.Combine(root, Unit199Path))), "回滚恢复单位原文");
            preview = await service.PrepareApplyAsync(root, store.Operations); var deleted = await service.CommitApplyAsync(preview, store);
            var (_, after, _) = await LoadP4Async(root); TestAssert.True(after.Units.All(u => u.Name != unit.Name), "正式删除单位");
            TestAssert.True(UnitCreation.New(after.Units.First(), after, []).SerializerId > state.SerializerId, "删除最高编号不复用");
            var restore = service.PrepareRestore(root, deleted.BackupId); await service.CommitRestoreAsync(restore);
            TestAssert.True(before.SequenceEqual(File.ReadAllBytes(Path.Combine(root, Unit199Path))), "备份恢复删除单位");
            _ = UnitCreationHistory.Require(new(root), unit.Source.RelativeSourceFile, unit.Name);
            try { UnitCreationHistory.Require(new(root), mother.Source.RelativeSourceFile, mother.Name); throw new Exception("原有单位必须拒删"); } catch (TransactionValidationException) { }
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task Unit199PreviewGuards()
    {
        var root = Fixture199();
        try
        {
            var (_, units, _) = await LoadP4Async(root); var unit = units.Units.First();
            using var store = new DraftStore(root); await store.LoadAsync();
            await store.UpsertAsync(UnitIdentityEditing.Operation(unit, "Descriptor_Unit_Rename_Guard"));
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
            File.WriteAllText(Path.Combine(root, "GameData/NewReference.ndf"), "NewUse is TObject\n(\n Unit = $/GFX/Unit/" + unit.Name + "\n)\n");
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "预览后新增引用检出");
            var source = UnitCreation.Source(unit); var state = UnitCapabilities.FromBody(source) with { Skills = ["$/Custom/Capacite_Missing"] };
            await store.ClearAsync(); await store.UpsertAsync(UnitCapabilities.Operation(unit, state));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "新增悬空能力拒写");
            TestAssert.Equal(source, UnitCreation.Source(unit), "失败不修改单位");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static void Verify199Ui(Window owner)
    {
        var root = Fixture199(); var wasAdvanced = WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced;
        try
        {
            var loading = LoadP4Async(root); RunWithDispatcher(loading, owner.Dispatcher);
            var (_, units, weapons) = loading.GetAwaiter().GetResult(); var unit = units.Units.First(); var graph = new UnitProjectGraph(root);
            foreach (var language in new[] { "zh-CN", "en" })
            foreach (var advanced in new[] { false, true })
            {
                UiText.Current.SetLanguage(language); WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced = advanced;
                if (language == "en") TestAssert.Equal("Creation record", UiText.T("创建来源"), "删除来源英文文案");
                var state = UnitCapabilities.FromBody(UnitCreation.Source(unit));
                var window = new UnitCapabilitiesWindow(unit, graph, state) { Owner = owner };
                try
                {
                    window.Show(); window.UpdateLayout(); DrainDispatcher(window.Dispatcher);
                    var buttons = Descendants198(window).OfType<Button>().ToArray();
                    TestAssert.Equal(17, buttons.Count(b => Equals(b.Content, UiText.T("添加/补齐"))), "完整15类及两个角色分支");
                    buttons.First(b => Equals(b.Content, UiText.T("添加/补齐"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    TestAssert.True(window.State.Skills.Contains("$/Custom/Capacite_resolute") && window.State.Specialties.Contains("_resolute"), "配套按钮联动实际能力和显示");
                    TestAssert.Equal(advanced, buttons.Any(b => Equals(b.Content, UiText.T("清空能力"))), "原始列表仅专业入口");
                    if (advanced)
                    {
                        buttons.Single(b => Equals(b.Content, UiText.T("清空能力"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        TestAssert.Equal(0, window.State.Skills.Length, "专业清空保留图标独立语义");
                        TestAssert.True(window.State.Specialties.Contains("_resolute"), "专业清空不强制同步图标");
                    }
                    Snapshot199(window, $"abilities-{language}-{(advanced ? "professional" : "basic")}.png");
                    if (advanced)
                    {
                        Descendants198(window).OfType<ScrollViewer>().First().ScrollToEnd(); window.UpdateLayout(); DrainDispatcher(window.Dispatcher);
                        Snapshot199(window, $"abilities-{language}-raw.png");
                    }
                }
                finally { window.Close(); }
                var wizard = new UnitCreationWindow(units, weapons, null, []) { Owner = owner };
                try
                {
                    wizard.Show(); Descendants198(wizard).OfType<TabControl>().Single().SelectedIndex = 1; wizard.UpdateLayout(); DrainDispatcher(wizard.Dispatcher);
                    var name = Descendants198(wizard).OfType<TextBox>().Single(t => t.Text.StartsWith(unit.Name + "_mod_", StringComparison.Ordinal));
                    TestAssert.Equal(!advanced, name.IsReadOnly, "创建变量名模式资格");
                    Snapshot199(wizard, $"creation-{language}-{(advanced ? "professional" : "basic")}.png");
                }
                finally { wizard.Close(); }
            }
            using var store = new DraftStore(root);
            var load = store.LoadAsync(); RunWithDispatcher(load, owner.Dispatcher);
            var pendingAbility = UnitCapabilities.Operation(unit, UnitCapabilities.FromBody(UnitCreation.Source(unit)));
            RunWithDispatcher(store.UpsertAsync(pendingAbility), owner.Dispatcher);
            RunWithDispatcher(store.UpsertAsync(UnitDeletion.Operation(unit)), owner.Dispatcher);
            var vm = new WarnoLiteModdingTool.App.ViewModels.Units.UnitWorkspaceViewModel(units, weapons, null, store, load.Result, _ => { }, () => Task.CompletedTask);
            vm.SelectedUnit = vm.Units.Single(u => u.InternalName == unit.Name);
            TestAssert.True(vm.SelectedPendingDelete && !vm.CanEditSelectedLifecycle, "待删除单位编辑锁定");
            RunWithDispatcher(vm.DeleteNewUnitAsync(owner), owner.Dispatcher);
            TestAssert.True(!vm.SelectedPendingDelete && vm.CanEditSelectedLifecycle && store.Operations.Single().Id == pendingAbility.Id, "撤销删除恢复原能力草稿及编辑状态");
        }
        finally { WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced = wasAdvanced; UiText.Current.SetLanguage("zh-CN"); DeleteTemporaryFixture(root); }
    }
    private static async Task Unit199ReferenceBoundaries()
    {
        var root=Fixture199();
        try
        {
            var (_,units,_) = await LoadP4Async(root);var unit=units.Units.First();
            var unrelated="export OtherMarker is TObject()\nexport "+unit.Name+" is TEntityDescriptor\n(\n DescriptorId = GUID:{11111111-2222-3333-4444-555555555555}\n ModulesDescriptors = []\n)\nOtherUse is TObject(Unit = "+unit.Name+")\n";
            File.WriteAllText(Path.Combine(root,"GameData/OtherNamespace.ndf"),unrelated,new UTF8Encoding(false));
            var comments="// "+unit.Name+"\n/*\nexport "+unit.Name+" is TEntityDescriptor()\n*/\nMixed is TObject\n(\nOther = $/Other/OtherMarker\nValue = $/Other/"+unit.Name+"\nText = '"+unit.Name+"'\n)\n";
            File.WriteAllText(Path.Combine(root,"GameData/Mixed.ndf"),comments,new UTF8Encoding(false));
            using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(UnitIdentityEditing.Operation(unit,"Descriptor_Unit_Scoped_Renamed"));
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            TestAssert.Equal(unrelated,File.ReadAllText(Path.Combine(root,"GameData/OtherNamespace.ndf")),"其他命名空间同叶名保留");
            TestAssert.Equal(comments,File.ReadAllText(Path.Combine(root,"GameData/Mixed.ndf")),"注释、普通字符串和不同完整路径保持");
            var (_,after,_) = await LoadP4Async(root);unit=after.Units.Single(u=>u.Name=="Descriptor_Unit_Scoped_Renamed");
            await store.UpsertAsync(UnitIdentityEditing.Operation(unit,"Descriptor_Unit_Scoped_Final"));
            preview=await service.PrepareApplyAsync(root,store.Operations);
            await store.UpsertAsync(UnitCapabilities.Operation(unit,UnitCapabilities.FromBody(UnitCreation.Source(unit))));
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.CommitApplyAsync(preview,store),"新增依赖草稿检出");
            await store.ClearAsync();
            File.WriteAllText(Path.Combine(root,"GameData/Unclear.ndf"),"Use is TObject(Value = "+unit.Name+"+1)\n");
            await store.UpsertAsync(UnitIdentityEditing.Operation(unit,"Descriptor_Unit_Scoped_Final"));
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.PrepareApplyAsync(root,store.Operations),"未知单位表达式拒绝猜写");
        }
        finally{DeleteTemporaryFixture(root);}
    }
    private static async Task Unit199DeleteReferences()
    {
        var root=Fixture199();
        try
        {
            var (_,units,weapons)=await LoadP4Async(root);var mother=units.Units.First();
            var state=UnitCreation.New(mother,units,[]) with {IndependentWeapons=true};
            foreach(var name in mother.Weapons){var weapon=weapons.Weapon(name)!;state.WeaponBaselines[name]=File.ReadAllText(weapon.Source.SourceFile).Substring(weapon.Source.CharacterOffset,weapon.Source.CharacterLength);}
            using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(UnitCreation.Operation(mother,state));
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);
            var (_,created,createdWeapons)=await LoadP4Async(root);var unit=created.Units.Single(u=>u.Name==state.Id);
            var copied=createdWeapons.Weapon(unit.Weapons.Single())!;var weaponBytes=File.ReadAllBytes(copied.Source.SourceFile);
            var references="Rules is TDeckDivisionRule\n(\n UnitRuleList = [\n TDeckUniteRule(UnitDescriptor = $/GFX/Unit/"+unit.Name+" AvailableWithoutTransport = True),\n TDeckUniteRule(UnitDescriptor = $/GFX/Unit/"+mother.Name+" AvailableWithoutTransport = True AvailableTransportList = [$/GFX/Unit/"+unit.Name+"]),\n ]\n)\nPack is DeckPackDescriptor\n(\n Unit = $/GFX/Unit/"+unit.Name+"\n)\n";
            File.WriteAllText(Path.Combine(root,"GameData/DeleteReferences.ndf"),references,new UTF8Encoding(false));
            var graph=new UnitProjectGraph(root);var uses=UnitDeletion.Uses(graph,graph.RequireObject(unit.Source.RelativeSourceFile,unit.Name));
            TestAssert.True(uses.Count(u=>u.Automatic)>=3,"注册、师成员和运输使用者完整列出");
            await store.UpsertAsync(UnitDeletion.Operation(unit));
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.PrepareApplyAsync(root,store.Operations),"未处置Pack禁止删除");
            var replacement=uses.Single(u=>u.Field=="Unit");
            var delete=UnitDeletion.Operation(unit,new(){{replacement.Key,"$/GFX/Unit/"+mother.Name}});
            await store.UpsertAsync(delete);
            await store.UpsertAsync(UnitCapabilities.Operation(unit,UnitCapabilities.FromBody(UnitCreation.Source(unit))));
            preview=await service.PrepareApplyAsync(root,[delete]);
            TestAssert.Equal(2,preview.Operations.Count,"删除收尾包括同单位能力草稿");
            await service.CommitApplyAsync(preview,store);
            var result=File.ReadAllText(Path.Combine(root,"GameData/DeleteReferences.ndf"));
            TestAssert.True(!result.Contains(unit.Name)&&result.Contains("Unit = $/GFX/Unit/"+mother.Name),"删除引用清理及Pack替代");
            TestAssert.True(weaponBytes.SequenceEqual(File.ReadAllBytes(copied.Source.SourceFile)),"删除保留独立武器及共享弹药");
            TestAssert.Equal(0,store.Operations.Count,"删除组一次清理");
        }
        finally{DeleteTemporaryFixture(root);}
    }
    private static async Task Unit199AbilityBoundaries()
    {
        var root=Fixture199();
        try
        {
            var (_,units,_) = await LoadP4Async(root);var unit=units.Units.First();var graph=new UnitProjectGraph(root);
            var original=UnitCreation.Source(unit);var baseline=UnitCapabilities.FromBody(original);
            var sigint=UnitCapabilities.Traits.Single(t=>t.Key=="sigint");var state=UnitCapabilities.Toggle(baseline,sigint,true,graph,unit.Source.RelativeSourceFile);
            state=state with {Skills=state.Skills.Where(s=>!s.EndsWith("Capacite_sigint_feedback")).ToArray()};
            TestAssert.Equal("已配套",UnitCapabilities.Status(state,sigint),"SIGINT无反馈合法变体");
            var changed=UnitCapabilities.Toggle(state,UnitCapabilities.Traits.Single(t=>t.Key=="resolute"),true,graph,unit.Source.RelativeSourceFile);
            TestAssert.True(changed.Skills.All(s=>!s.EndsWith("sigint_feedback")),"修改其他特性不自动补SIGINT反馈");
            using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(UnitCapabilities.Operation(unit,changed));
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);
            var dependencies=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/CapaciteList.ndf");
            File.WriteAllText(dependencies,File.ReadAllText(dependencies).Replace("RangeGRU = 123","RangeGRU = 124"));
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.CommitApplyAsync(preview,store),"能力依赖外部变化检出");
            var savedDefinitions = File.ReadAllText(dependencies);
            File.WriteAllText(dependencies, savedDefinitions.Replace("SelfEffect = ~/UnitEffect_Custom", "SelfEffect = ~/ArbitraryMissingEffect"));
            TestAssert.True(UnitCapabilities.Choices(new(root), unit.Source.RelativeSourceFile).All(c => c.Error is not null), "任意命名的缺失效果引用也拒绝");
            File.WriteAllText(dependencies, savedDefinitions);
            var duplicated=original.Replace("ModulesDescriptors = [","ModulesDescriptors = [\nTCapaciteModuleDescriptor(DefaultSkillList = []),\nTCapaciteModuleDescriptor(DefaultSkillList = []),");
            try{UnitCapabilities.FromBody(duplicated);throw new Exception("重复能力模块应拒绝");}catch(InvalidDataException){}
            var unknown=baseline with {Skills=["~/Existing_Unknown"]};
            var body=UnitCapabilities.Apply(original,baseline,unknown);
            var added=UnitCapabilities.Apply(body,unknown,unknown with {Specialties=unknown.Specialties.Append("_custom").ToArray()});
            TestAssert.True(added.Contains("~/Existing_Unknown"),"未改未知能力原样保留");
            var legacy=UnitCreation.New(unit,units,[]) with {Id="Descriptor_Unit_WL_ABCDEF1234"};
            TestAssert.Equal(legacy.Id,UnitCreation.Read(UnitCreation.Operation(unit,legacy)).Id,"旧随机名不迁移");
            await Task.CompletedTask;
        }
        finally{DeleteTemporaryFixture(root);}
    }
    private static async Task Unit199Combined()
    {
        var root = Fixture199();
        try
        {
            WriteExperience198(root);
            var path = Path.Combine(root, Unit199Path);
            File.WriteAllText(path, File.ReadAllText(path).Replace("ModulesDescriptors = [", $"ModulesDescriptors = [TExperienceModuleDescriptor(ExperienceLevelsPackDescriptor = ~/{Xp198Name(1)}),"));
            File.AppendAllText(path, "\n" + File.ReadAllText(Path.Combine(root, "GameData/Custom/Routes.ndf")));
            File.Delete(Path.Combine(root, "GameData/Custom/Routes.ndf"));
            var (_, units, _) = await LoadP4Async(root); var mother = units.Units.First();
            using var store = new DraftStore(root); await store.LoadAsync();
            var creation = UnitCreation.New(mother, units, []);
            var graph = new UnitProjectGraph(root);
            creation = creation with { Capabilities = UnitCapabilities.Toggle(UnitCapabilities.FromBody(UnitCreation.Source(mother)), UnitCapabilities.Traits.Single(t => t.Key == "ifv_receiver"), true, graph, mother.Source.RelativeSourceFile) };
            var projected = UnitCreation.Project(mother, creation, UnitCreation.Source(mother));
            creation = creation with { Capabilities = UnitCapabilities.FromBody(UnitCreation.Source(projected)), Id = "Descriptor_Unit_Pending_Combined" };
            await store.UpsertAsync(UnitCreation.Operation(mother, creation));
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(preview, store);
            (_, units, _) = await LoadP4Async(root); var created = units.Units.Single(u => u.Name == creation.Id); mother = units.Units.Single(u => u.Name == mother.Name);
            TestAssert.True(UnitCapabilities.FromBody(UnitCreation.Source(created)).Tags.Contains("Infanterie_IFV"), "新建IFV必要标签生效");
            TestAssert.Equal(creation.NamingRoot, UnitCreation.New(created, units, []).NamingRoot, "再次复制保留命名根");
            var field = mother.Field("experience.type")!; var choice = field.Choices.Single(c => c.RawValue == "~/" + Xp198Name(0));
            await store.UpsertAsync(CreateFieldDraft(mother, field, choice.Display, choice.RawValue));
            await store.UpsertAsync(Edit198(units.Rules!.Experience));
            var rename = UnitIdentityEditing.Operation(mother, "Descriptor_Unit_Combined_Renamed"); await store.UpsertAsync(rename);
            var cap = UnitCapabilities.Toggle(UnitCapabilities.FromBody(UnitCreation.Source(mother)), UnitCapabilities.Traits[0], true, new(root), mother.Source.RelativeSourceFile);
            await store.UpsertAsync(UnitCapabilities.Operation(mother, cap));
            await store.UpsertAsync(UnitDeletion.Operation(created));
            preview = await service.PrepareApplyAsync(root, store.Operations);
            TestAssert.True(preview.ExperienceReview is not null && preview.UnitReadDependencies is not null, "经验与生命周期同时审查");
            TestAssert.Equal(preview.Files.Count, preview.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "同文件合成一次写入");
            await service.CommitApplyAsync(preview, store);
            (_, units, _) = await LoadP4Async(root);
            TestAssert.True(units.Units.All(u => u.Name != created.Name), "同批删除A");
            var renamed = units.Units.Single(u => u.Name == "Descriptor_Unit_Combined_Renamed");
            TestAssert.True(UnitCapabilities.FromBody(UnitCreation.Source(renamed)).Skills.Any(s => s.EndsWith("Capacite_resolute")), "同批保留B能力");
            TestAssert.Equal("3.6", units.Rules!.Experience.Routes.Single(r => r.Name == Xp198Name(0)).Levels[1].Cells.Single(c => c.Key == "threshold.price").Raw, "同批保留经验编辑");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static void Snapshot199(Window window, string name)
    {
        window.UpdateLayout(); var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, surface.ActualWidth, surface.ActualHeight);
            drawing.DrawRectangle(window.Background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(surface), null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("publish/qa-1.9.9"); using var stream = File.Create(Path.Combine("publish/qa-1.9.9", name)); encoder.Save(stream);
    }
}
