using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static void WeaponStructureWindowChecks(System.Windows.Window owner)
    {
        var root = StructureFixture();
        var language = WarnoLiteModdingTool.App.Localisation.UiText.Current.Language;
        var mode = WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced;
        var theme = WarnoLiteModdingTool.App.Theming.ThemeManager.CurrentTheme;
        try
        {
            var load = LoadP4Async(root); RunWithDispatcher(load, owner.Dispatcher); var data = load.Result.Item3; var graph = new UnitProjectGraph(root);
            foreach (var lang in new[] { "zh-CN", "en" })
            foreach (var t in Enum.GetValues<WarnoLiteModdingTool.App.Theming.AppTheme>())
            {
                WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(lang);
                WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced = lang == "en";
                WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(t, false);
                var editor = new WarnoLiteModdingTool.App.Controls.WeaponStructureWindow(data, graph, [], U1911) { Owner = owner, Width = 820, Height = 620 };
                editor.Show(); DrainDispatcher(owner.Dispatcher);
                Assert(editor.CurrentState?.Unit == U1911, "结构编辑器初始目标绑定");
                Assert(FindVisualChildren<System.Windows.Controls.ListBox>(editor).Any(l => l.Items.Count == 2), "已有两槽显示");
                Assert(FindVisualChildren<WarnoLiteModdingTool.App.Controls.FieldRow>(editor).Any(f => f.Header?.ToString() == WarnoLiteModdingTool.App.Localisation.UiText.T(WeaponFieldDefinitions.MountedAmmo(0).Label)), "槽位参数绑定生成");
                var add = new WarnoLiteModdingTool.App.Controls.WeaponSlotAddWindow(data, editor.CurrentState!, graph, "t:0/m:0") { Owner = editor };
                add.Show(); DrainDispatcher(owner.Dispatcher);
                Assert(FindVisualChildren<System.Windows.Controls.Button>(add).Any(b => b.Content?.ToString() == WarnoLiteModdingTool.App.Localisation.UiText.T("加入草稿")), "新增窗口本地化操作入口");
                add.Close(); editor.Close(); DrainDispatcher(owner.Dispatcher);
                Assert(!editor.IsVisible && !add.IsVisible, "关闭编辑器完成保存屏障并退出窗口");
            }
        }
        finally
        {
            WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(language);
            WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced = mode;
            WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(theme, false);
            DeleteTemporaryFixture(root);
        }
    }
    private static async Task WeaponStructureOldReader(string oldCore)
    {
        var root = StructureFixture(); var load = new System.Runtime.Loader.AssemblyLoadContext("old-slot-reader", isCollectible: true);
        try
        {
            var (_, _, data) = await LoadP4Async(root); var state = StructureState(data); AddStructure(state);
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(WeaponStructure.Operation(state));
            var bytes = File.ReadAllBytes(store.DraftPath);
            var assembly = load.LoadFromAssemblyPath(Path.GetFullPath(oldCore));
            var type = assembly.GetType("WarnoLiteModdingTool.Core.Drafts.DraftStore")!;
            using var old = (IDisposable)Activator.CreateInstance(type, root)!;
            var task = (Task)type.GetMethod("LoadAsync")!.Invoke(old, [System.Threading.CancellationToken.None])!; await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert((bool)result.GetType().GetProperty("IsBlocked")!.GetValue(result)!, "旧程序必须拒绝结构草稿");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(store.DraftPath)), "旧程序不修改结构草稿");
            Console.WriteLine("PASS old reader blocks schema 2 and preserves the draft: " + oldCore);
        }
        finally { load.Unload(); DeleteTemporaryFixture(root); }
    }
    private static async Task WeaponStructureCreationTest()
    {
        var root = Fixture199();
        try
        {
            var ap = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
            File.WriteAllText(ap, Regex.Replace(File.ReadAllText(ap), @"(?m)^(Ammo_\w+ is)", "export $1"), new UTF8Encoding(false));
            var (_, units, data) = await LoadP4Async(root); var mother = units.Units.First();
            var create = UnitCreation.New(mother, units, []); var weapon = data.Weapon(mother.Weapons.First())!;
            var state = WeaponStructure.New(mother, weapon, creationId: create.Id);
            var syntax = new WeaponStructureSyntax(state.Weapon.Body); var mount = syntax.Mounts.First();
            WeaponStructure.Add(state, state.Weapon, mount.Id, mother.Name, mount.TurretId, mount.Id, mount.Ammo, 6);
            create = create with { WeaponStructures = [state] };
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(UnitCreation.Operation(mother, create));
            var service = new UnitTransactionService(); var p = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(p, store);
            var (_, _, after) = await LoadP4Async(root);
            Assert(after.Weapon(after.Units.Single(u => u.Name == create.Id).Weapons.Single())!.Mounts.Count == 2, "待创建单位携带结构变更");
            Assert(after.Weapon(weapon.Name)!.Mounts.Count == 1, "母版武器保持");
        }
        finally { DeleteTemporaryFixture(root); }
        root = StructureFixture();
        try
        {
            var up = Path.Combine(root, Unit199Path); var text = File.ReadAllText(up);
            text = text.Replace("ModulesDescriptors = [ $/GFX/Weapon/WeaponDescriptor_P4_Shared, ]", "ModulesDescriptors = [ ]");
            File.WriteAllText(up, text, new UTF8Encoding(false));
            var (_, _, data) = await LoadP4Async(root); var state = StructureState(data); AddStructure(state);
            Assert(state.Initialize, "无武器单位进入初始化模式");
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(WeaponStructure.Operation(state));
            var service = new UnitTransactionService(); var p = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(p, store);
            var (_, _, after) = await LoadP4Async(root);
            Assert(after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!.Mounts.Count == 1, "仅初始化选中的新槽，不带模板原槽");
            Assert(after.Units.Single(u => u.Name == "Descriptor_Unit_P4_Two").Weapons.Count == 0, "其他无武器单位保持");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task WeaponStructureBoundaryTest()
    {
        var root = StructureFixture();
        try
        {
            var (_, units, data) = await LoadP4Async(root); var s = StructureState(data); AddStructure(s);
            var second = StructureState(data, "Descriptor_Unit_P4_Two"); WeaponStructure.Remove(second, "t:0/m:1");
            using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync([WeaponStructure.Operation(s), WeaponStructure.Operation(second)], []);
            var service = new UnitTransactionService(); var p = await service.PrepareApplyAsync(root, store.Operations);
            var before = p.Files.Where(f => File.Exists(f.FullPath)).ToDictionary(f => f.FullPath, f => File.ReadAllBytes(f.FullPath));
            var locked = before.Keys.Last(); File.SetAttributes(locked, FileAttributes.ReadOnly);
            try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(p, store), "跨文件写失败必须回滚"); }
            finally { File.SetAttributes(locked, FileAttributes.Normal); }
            Assert(before.All(kv => File.ReadAllBytes(kv.Key).SequenceEqual(kv.Value)), "失败后所有正式文件保持原文");
            Assert(store.Operations.Count == 2, "失败后草稿保留");
            p = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(p, store);
            var (_, _, after) = await LoadP4Async(root);
            Assert(after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!.Mounts.Count == 3, "批量目标一加槽");
            Assert(after.Weapon(after.Units.Single(u => u.Name == second.Unit).Weapons.Single())!.Mounts.Count == 1, "批量目标二明确删槽");
            await service.CommitRestoreAsync(service.PrepareRestore(root, p.BackupId));
            await store.UpsertAsync(WeaponStructure.Operation(s)); p = await service.PrepareApplyAsync(root, store.Operations);
            var path = Path.Combine(root, s.Weapon.File); File.AppendAllText(path, "\n// changed after preview\n");
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(p, store), "预览后源文件变化阻止覆盖");
            var altered = s with { Weapon = s.Weapon with { Body = WeaponStructureSyntax.Set(s.Weapon.Body, "TWeaponManagerModuleDescriptor", new Dictionary<string, string> { ["Salves"] = "[4, 6, 999,]" }) }, Boxes = new(s.Boxes) };
            altered.Boxes["b:2"] = 999;
            var unused = new WeaponStructureSyntax(WeaponStructureRenderer.Render(altered, altered.Weapon.Body));
            Assert(unused.Boxes.Count == 4 && unused.Document.Raw(unused.Boxes[2]) == "999", "原先未用库存保持");
            // Explicit unknown index consumers block compaction.
            try { _ = new WeaponStructureSyntax(s.Weapon.Body.Replace("Salves =", "Custom = TObject(AmmoBoxIndex = 0)\n Salves =")); throw new Exception("未知索引未阻止"); } catch (TransactionValidationException) { }
            var conflict = s with { Weapon = s.Weapon with { Body = s.Weapon.Body + " " } };
            Assert(WeaponStructure.Resolve(units, data, WeaponStructure.Operation(conflict), []).Status == DraftResolutionStatus.Conflict, "草稿基线漂移冲突");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task WeaponStructureUi(string language, string theme)
    {
        var root = StructureFixture();
        try
        {
            var (_, _, data) = await LoadP4Async(root); var graph = new UnitProjectGraph(root);
            Console.WriteLine("UI fixture loaded");
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    var app = new WarnoLiteModdingTool.App.App(launchWorkspace: false); app.InitializeComponent();
                    Console.WriteLine("WPF resources loaded");
                    app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                    WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(language);
                    WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(Enum.Parse<WarnoLiteModdingTool.App.Theming.AppTheme>(theme), false);
                    WarnoLiteModdingTool.App.Advanced.EditorMode.IsAdvanced = language == "en";
                    System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
                    using var store = new DraftStore(root); RunWithDispatcher(store.LoadAsync(), System.Windows.Threading.Dispatcher.CurrentDispatcher);
                    Console.WriteLine("Draft store loaded");
                    var window = new WarnoLiteModdingTool.App.Controls.WeaponStructureWindow(data, graph, [], U1911, store);
                    Console.WriteLine("Window constructed");
                    window.ShowDialog(); app.Shutdown(); completion.SetResult();
                }
                catch (Exception e) { completion.SetException(e); }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA); thread.Start(); await completion.Task;
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static string StructureFixture(string nl = "\n")
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        var path = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf");
        var text = File.ReadAllText(path);
        text = Regex.Replace(text, @"(?m)^\s*(EffectTag|HandheldEquipmentKey|WeaponActiveAndCanShootPropertyName)\s*=\s*[^\r\n]+\r?\n", "");
        text = text.Replace("TMountedWeaponDescriptor\r\n                (", "TMountedWeaponDescriptor\r\n                (\r\n                    NbWeapons = 1")
            .Replace("TMountedWeaponDescriptor\n                (", "TMountedWeaponDescriptor\n                (\n                    NbWeapons = 1");
        File.WriteAllText(path, text.Replace("\r\n", "\n").Replace("\n", nl), new UTF8Encoding(false));
        var ammoPath = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
        File.WriteAllText(ammoPath, Regex.Replace(File.ReadAllText(ammoPath), @"(?m)^(Ammo_\w+ is)", "export $1"), new UTF8Encoding(false));
        return root;
    }
    private static WeaponStructureState StructureState(WeaponWorkspaceData data, string unit = U1911) => WeaponStructure.New(data.Units.Single(u => u.Name == unit), data.Weapon(W1911)!);
    private static string AddStructure(WeaponStructureState state, string? box = null, bool turret = false) => WeaponStructure.Add(state, state.Weapon, "t:0/m:0", U1911, "t:0", "t:0/m:0", "$/GFX/Weapon/" + A1911, 7, box, turret);
    private static async Task WeaponStructureSyntaxTest()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        {
            var root = StructureFixture(nl);
            try
            {
                var (_, _, data) = await LoadP4Async(root); var state = StructureState(data);
                var id = AddStructure(state); state.Fields[id] = new() { ["NbWeapons"] = "3" };
                WeaponStructure.Remove(state, "t:0/m:0");
                var rendered = WeaponStructureRenderer.Render(state, state.Weapon.Body); var syntax = new WeaponStructureSyntax(rendered);
                Assert(syntax.Mounts.Count() == 2 && syntax.Boxes.Count == 3, "共箱另一使用者和原有未用箱保持");
                Assert(syntax.Mounts.Last().Box == 2 && syntax.Document.Raw(syntax.Boxes[2]) == "7", "新增箱不占用原有未用箱");
                Assert(WeaponStructureSyntax.Values(syntax.Mounts.Last().Body, "TMountedWeaponDescriptor", ["NbWeapons"])["NbWeapons"] == "3", "新槽字段按稳定身份编辑");
                WeaponStructure.UndoRemove(state, "t:0/m:0"); WeaponStructure.Remove(state, id);
                Assert(!state.HasChanges, "撤销新增/删除后无残余箱及字段");
                var cloned = AddStructure(state, "b:0", true); var same = AddStructure(state, state.Added.Single().Box);
                WeaponStructure.Remove(state, cloned);
                Assert(state.Added.Single().Id == same && state.Boxes.ContainsKey("b:0"), "删除一个使用者不删共享箱");
                var body = state.Weapon.Body.Replace("[\r\n", "[\r\n// kept comment ( [", StringComparison.Ordinal);
                Assert(new WeaponStructureSyntax(WeaponStructureRenderer.Render(state, state.Weapon.Body)).Mounts.Count() == 3, "新槽共原箱且无重复库存");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task WeaponStructureTransactionTest()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        {
            var root = StructureFixture(nl);
            try
            {
                var (_, units, data) = await LoadP4Async(root); var state = StructureState(data); AddStructure(state);
                using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(WeaponStructure.Operation(state));
                Assert(JsonDocument.Parse(File.ReadAllText(store.DraftPath)).RootElement.GetProperty("schemaVersion").GetInt32() == 2, "结构草稿外层版本2");
                var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
                Assert(preview.Files.Count >= 2, "结构隔离包含单位与武器");
                await service.CommitApplyAsync(preview, store);
                var (_, _, after) = await LoadP4Async(root); var one = after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!;
                Assert(one.Mounts.Count == 3 && one.Name != W1911, "局部新增隔离共享Weapon");
                Assert(after.Units.Single(u => u.Name == "Descriptor_Unit_P4_Two").Weapons.Single() == W1911 && after.Weapon(W1911)!.Mounts.Count == 2, "未选单位保持");
                await service.CommitRestoreAsync(service.PrepareRestore(root, preview.BackupId));
                Assert((await LoadP4Async(root)).Item3.Weapon(W1911)!.Mounts.Count == 2, "恢复原结构");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task WeaponStructureCompositionTest()
    {
        var root = StructureFixture();
        try
        {
            var (_, _, data) = await LoadP4Async(root); var state = StructureState(data); var id = AddStructure(state);
            using var store = new DraftStore(root); await store.LoadAsync();
            await Save1911(store, P1911(data, [], "weapon.salves", "9"));
            var oldDraft = File.ReadAllBytes(store.DraftPath); await store.UpsertAsync(WeaponStructure.Operation(state));
            Assert(Directory.GetFiles(store.EditorDirectory, "draft-before-structure-*.json").Any(p => File.ReadAllBytes(p).SequenceEqual(oldDraft)), "升级前草稿原样备份");
            var service = new UnitTransactionService(); var p = await service.PrepareApplyAsync(root, [store.Operations.Last()]);
            Assert(p.Operations.Count == 2, "结构计划关联已有参数草稿"); await service.CommitApplyAsync(p, store);
            var (_, _, after) = await LoadP4Async(root); var w = after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!;
            Assert(w.Mounts.Count == 3 && w.Field("weapon.salves.0")!.DisplayValue == "9", "普通字段候选与结构最终组合");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task WeaponStructureLocalAmmoTest()
    {
        var root = StructureFixture();
        try
        {
            var (_, _, data) = await LoadP4Async(root); var s = StructureState(data); var id = AddStructure(s);
            s.AmmoFields[id] = new() { ["ammo.damage.physical"] = "13" }; var a = data.Ammo(A1911)!;
            s.AmmoBaselines[a.Name] = File.ReadAllText(a.Source.SourceFile).Substring(a.Source.CharacterOffset, a.Source.CharacterLength);
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(WeaponStructure.Operation(s));
            var service = new UnitTransactionService(); var p = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(p, store);
            var (_, _, after) = await LoadP4Async(root); var w = after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!;
            Assert(w.Mounts.Last().AmmoName != A1911 && after.Ammo(w.Mounts.Last().AmmoName)!.Field("ammo.damage.physical")!.DisplayValue == "13", "新槽局部Ammo可继续编辑");
            Assert(w.Mounts.First().AmmoName == A1911, "原挂载不随新槽Ammo性能变化");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static void AddStructurePresentationFixture(string root)
    {
        var wp = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/WeaponDescriptor.ndf");
        var text = File.ReadAllText(wp); var i = 0;
        text = Regex.Replace(text, @"AmmoBoxIndex\s*=\s*\d+", m => m.Value + "\n                    WeaponShootDataPropertyName = ['shoot_" + (++i) + "']\n                    WeaponActiveAndCanShootPropertyName = 'active_" + i + "'\n                    HandheldEquipmentKey = 'alternative_" + i + "'");
        File.WriteAllText(wp, text, new UTF8Encoding(false));
        var up = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");
        text = File.ReadAllText(up); i = 0;
        text = Regex.Replace(text, @"ModulesDescriptors\s*=\s*\[", m => m.Value + " VehicleApparenceModuleDescriptor(BlackHoleKey='visual_" + (++i) + "'), ");
        File.WriteAllText(up, text, new UTF8Encoding(false));
        var visual = "EffectAction is TObject()\n";
        for (i = 1; i <= 3; i++)
            visual += $"Op_{i} is DepictionOperator_WeaponInstantFire\n(\n FireEffectTag='effect_{i}'\n Anchors=['existing_bone_{i}']\n WeaponShootDataPropertyName=['shoot_{i}']\n NbProj=1\n)\n";
        for (i = 1; i <= 3; i++)
            visual += $"unnamed TacticVehicleDepictionRegistration\n(\n BlackHoleKey='visual_{i}'\n Operators=[Op_1, Op_2, Op_3,]\n Actions=MAP[('effect_1',EffectAction),('effect_2',EffectAction),('effect_3',EffectAction),] + OtherActions\n)\n";
        File.WriteAllText(Path.Combine(root, "GameData/Generated/Gameplay/Gfx/SlotDepiction.ndf"), visual, new UTF8Encoding(false));
    }
    private static async Task WeaponStructurePresentationTest()
    {
        var root = StructureFixture();
        try
        {
            AddStructurePresentationFixture(root); var (_, _, data) = await LoadP4Async(root);
            var s = StructureState(data); var donor = WeaponStructure.Source(data.Weapon("WeaponDescriptor_P4_Other")!);
            WeaponStructure.Add(s, donor, "t:0/m:0", "Descriptor_Unit_P4_Three", "t:0", "t:0/m:0", "$/GFX/Weapon/" + A1911, 7);
            WeaponStructurePresentation.Capture(new UnitProjectGraph(root), s);
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(WeaponStructure.Operation(s));
            var service = new UnitTransactionService(); var p = await service.PrepareApplyAsync(root, store.Operations);
            Assert(p.Files.Count >= 3, "表现、单位和武器进入同一事务"); await service.CommitApplyAsync(p, store);
            var graph = new UnitProjectGraph(root); var unit = graph.FindObjects(U1911).Single(); var reg = WeaponStructurePresentation.Find(graph, graph.Body(unit))!;
            Assert(reg.Key.StartsWith("visual_1_WLMT_", StringComparison.Ordinal), "局部独立表现注册");
            var op = graph.Objects.Single(o => o.Name.StartsWith("Op_3_WLMT_", StringComparison.Ordinal));
            Assert(graph.Body(op).Contains("existing_bone_1", StringComparison.Ordinal) && !graph.Body(op).Contains("existing_bone_3", StringComparison.Ordinal), "移植沿用目标明确挂点");
            Assert(graph.Body(op).Contains(s.Added[0].PropertyMap["shoot_3"], StringComparison.Ordinal), "挂载与表现属性成对独立");
            var untouched = graph.FindObjects("Descriptor_Unit_P4_Two").Single(); Assert(WeaponStructurePresentation.Key(graph.Body(untouched)) == "visual_2", "未选单位表现保持");
            await service.CommitRestoreAsync(service.PrepareRestore(root, p.BackupId));
            await store.UpsertAsync(WeaponStructure.Operation(s));
            var dp = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/SlotDepiction.ndf");
            File.WriteAllText(dp, File.ReadAllText(dp).Replace("existing_bone_3", "externally_changed_bone"), new UTF8Encoding(false));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "移植表现基线变化必须阻止应用");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task WeaponStructureSharedDeleteTest()
    {
        var root = StructureFixture();
        try
        {
            var (_, _, data) = await LoadP4Async(root); var s = StructureState(data) with { Shared = true };
            foreach (var row in WeaponStructure.Rows(s)) WeaponStructure.Remove(s, row.Id);
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(WeaponStructure.Operation(s));
            var service = new UnitTransactionService(); var p = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(p, store);
            var (_, _, after) = await LoadP4Async(root);
            Assert(after.Units.Where(u => u.Name is U1911 or "Descriptor_Unit_P4_Two").All(u => u.Weapons.Count == 0), "共享解除武装按所有单位移除模块引用");
            Assert(after.Units.Single(u => u.Name == "Descriptor_Unit_P4_Three").Weapons.Count == 1, "其他配置保持");
            await service.CommitRestoreAsync(service.PrepareRestore(root, p.BackupId));
            (_, _, data) = await LoadP4Async(root); s = StructureState(data) with { Shared = true }; AddStructure(s);
            await store.UpsertAsync(WeaponStructure.Operation(s)); p = await service.PrepareApplyAsync(root, store.Operations); await service.CommitApplyAsync(p, store);
            (_, _, after) = await LoadP4Async(root);
            Assert(after.Units.Where(u => u.Name is U1911 or "Descriptor_Unit_P4_Two").All(u => after.Weapon(u.Weapons.Single())!.Mounts.Count == 3), "共享新增覆盖全部引用单位");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
