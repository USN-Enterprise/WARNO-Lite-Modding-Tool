using System.IO;
using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task AmmoProfessionalOldReader(string path)
    {
        var root = ProfessionalFixture(); var context = new System.Runtime.Loader.AssemblyLoadContext("old-ammo-reader", true);
        try
        {
            var (_, _, data) = await LoadP4Async(root); using var store = new DraftStore(root); await store.LoadAsync();
            await store.UpsertAsync(ProOp(data.Ammo(A1911)!.Field("ammo.behavior.fireAndForget")!, "False"));
            var original = File.ReadAllBytes(store.DraftPath); var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
            var type = assembly.GetType("WarnoLiteModdingTool.Core.Drafts.DraftStore")!;
            using var old = (IDisposable)Activator.CreateInstance(type, root)!;
            var task = (Task)type.GetMethod("LoadAsync")!.Invoke(old, [CancellationToken.None])!; await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert((bool)result.GetType().GetProperty("IsBlocked")!.GetValue(result)! && File.ReadAllBytes(store.DraftPath).SequenceEqual(original), "旧版拒绝schema3且原文件不变");
            Console.WriteLine("PASS previous release rejects schema 3 without modifying it");
        }
        finally { context.Unload(); DeleteTemporaryFixture(root); }
    }
    private static async Task AmmoProfessionalStock(string root)
    {
        var context = new Core.Projects.ModProjectDetector().Detect(root);
        var index = await new Core.Indexing.ProjectIndexer().IndexAsync(context);
        var units = new UnitWorkspaceData([], new(new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, IReadOnlyList<string>>()),
            new Core.Localisation.UnitLocalisationCatalog(root, [], new Dictionary<string, IReadOnlyList<string>>(), []), new([], [], []), []);
        var data = await new WeaponProjectLoader().LoadAsync(context, index, units); var graph = new UnitProjectGraph(root);
        var values = data.Ammunition.Where(a => a.Field("ammo.behavior.fireAndForget")?.IsMissing == true).ToArray();
        var failures = new List<string>();
        foreach (var a in values)
        {
            try
            {
                var field = a.Field("ammo.behavior.fireAndForget")!;
                var body = File.ReadAllText(a.Source.SourceFile).Substring(a.Source.CharacterOffset, a.Source.CharacterLength);
                body = UnitProjectGraph.Patch(body, [AmmoProfessional.Replacement(field, "True", a.Source.CharacterOffset)]);
                AmmoProfessionalValidation.Candidate(a, body, new Dictionary<string, string> { [field.Key] = "True" }, () => graph, units.Localisation);
            }
            catch (Exception e) { failures.Add(a.Name + ": " + e.Message); }
        }
        Console.WriteLine($"Ammo={data.Ammunition.Count}; missing FF={values.Length}; validated={values.Length - failures.Count}; errors={failures.Count}");
        foreach (var e in failures.Take(5)) Console.WriteLine(e);
        Assert(failures.Count == 0, "只读官方样本缺失射后不理引用链");
    }

    private static async Task AmmoProfessionalUi()
    {
        var root = ProfessionalFixture(); var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            WarnoLiteModdingTool.App.App? app = null;
            try
            {
                app = new(launchWorkspace: false); app.InitializeComponent(); app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
                WarnoLiteModdingTool.App.Theming.ThemeManager.Initialize(new WarnoLiteModdingTool.App.Theming.UiThemeStore(Path.Combine(root, ".qa/theme.txt")));
                var vm = new WarnoLiteModdingTool.App.ViewModels.MainViewModel(new WarnoLiteModdingTool.Core.Projects.RecentProjectStore(Path.Combine(root, ".qa/recent.json")));
                var window = new WarnoLiteModdingTool.App.MainWindow(vm) { Width = 1400, Height = 920 }; window.Show(); DrainDispatcher(dispatcher);
                RunWithDispatcher(vm.OpenProjectAsync(root), dispatcher); vm.SelectedModule = vm.Modules.Single(m => m.Key == "ammo");
                foreach (var lang in new[] { "zh-CN", "en" })
                foreach (var pro in new[] { false, true })
                {
                    vm.AdvancedMode = pro; WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage(lang);
                    WarnoLiteModdingTool.App.Theming.ThemeManager.ApplyTheme(pro ? WarnoLiteModdingTool.App.Theming.AppTheme.DarkBlue : WarnoLiteModdingTool.App.Theming.AppTheme.LightBlue, false);
                    window.Width = lang == "en" ? 1240 : 1480;
                    ((System.Windows.FrameworkElement)window.Content).LayoutTransform = new System.Windows.Media.ScaleTransform(lang == "en" ? 1.15 : 1, lang == "en" ? 1.15 : 1);
                    var ammo = vm.AmmoWorkspace!; ammo.SelectedAmmo = ammo.Ammunition.Single(a => a.Name == A1911); ammo.FieldSearch = "";
                    Assert(pro == ammo.Fields.Any(f => f.Field.Definition.Professional), "专业入口可见性");
                    Assert(pro == ammo.Fields.Any(f => f.Field.IsMissing && f.Field.Key == "ammo.behavior.fireAndForget"), "缺失入口只在专业模式");
                    foreach (var section in ammo.FieldSections) section.IsExpanded = section.Title == "行为设置";
                    DrainDispatcher(dispatcher); SaveProfessionalSnapshot(window, $"ammo-{lang}-{pro}");
                    if (pro)
                    {
                        ammo.FieldSearch = "ShowDamageInUI"; DrainDispatcher(dispatcher);
                        var field = ammo.Fields.Single(f => f.Field.Definition.FieldName == "ShowDamageInUI");
                        Assert(FindVisualChildren<System.Windows.Controls.ComboBox>(window).Any(c => ReferenceEquals(c.DataContext, field) && c.IsVisible), "False控件实际可见");
                        SaveProfessionalSnapshot(window, $"false-{lang}");
                        ammo.FieldSearch = "InterfaceWeaponTexture"; DrainDispatcher(dispatcher);
                        Assert(FindVisualChildren<WarnoLiteModdingTool.App.Controls.AmmoAdvancedInput>(window).Any(c => c.IsVisible), "图片选择器实际可见");
                        SaveProfessionalSnapshot(window, $"reference-{lang}");
                        ammo.FieldSearch = "IsFireAndForget"; DrainDispatcher(dispatcher); SaveProfessionalSnapshot(window, $"missing-{lang}");
                        var ff = ammo.Fields.Single(f => f.Field.Key == "ammo.behavior.fireAndForget"); ff.EditValue = "是"; RunWithDispatcher(ff.FlushAsync(), dispatcher);
                        Assert(ff.Draft?.InsertAmmoField == true && ff.TargetRaw == "True", "UI新增草稿保存");
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            var dialog = app.Windows.OfType<System.Windows.Window>().Single(w => w != window && w.Title == WarnoLiteModdingTool.App.Localisation.UiText.T("核对说明标签"));
                            SaveProfessionalSnapshot(dialog, $"tags-{lang}");
                            var adopt = FindVisualChildren<System.Windows.Controls.Button>(dialog).Single(b => b.Content?.ToString()?.StartsWith(WarnoLiteModdingTool.App.Localisation.UiText.T("采用标签建议")) == true);
                            adopt.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        FindVisualChildren<System.Windows.Controls.Button>(window).Single(b => b.IsVisible && b.Content?.ToString() == WarnoLiteModdingTool.App.Localisation.UiText.T("核对说明标签")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        Assert(ff.LinkedTags!.EditValue.Contains("F\\u0026F") || ff.LinkedTags.EditValue.Contains("F&F"), "显式采用标签建议");
                        RunWithDispatcher(ammo.UndoFieldAsync(ff.LinkedTags), dispatcher);
                        vm.AdvancedMode = false; vm.AdvancedMode = true;
                        Assert(vm.AmmoWorkspace!.Fields.Single(f => f.Field.Key == ff.Field.Key).EditValue == "是", "切模式不丢草稿");
                        RunWithDispatcher(vm.AmmoWorkspace.UndoFieldAsync(vm.AmmoWorkspace.Fields.Single(f => f.Field.Key == ff.Field.Key)), dispatcher);
                        ammo.FieldSearch = "";
                    }
                }
                window.Close(); app.Shutdown(); completion.SetResult();
            }
            catch (Exception e) { app?.Shutdown(); completion.SetException(e); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task; Console.WriteLine("PASS professional Ammo WPF modes, languages, widths and scaling"); }
        finally { DeleteTemporaryFixture(root); }
    }
    private static void SaveProfessionalSnapshot(System.Windows.Window window, string name)
    {
        DrainDispatcher(window.Dispatcher); window.UpdateLayout(); var surface = (System.Windows.FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("publish/qa-1.9.19"); using var output = File.Create("publish/qa-1.9.19/" + name + ".png"); encoder.Save(output);
    }
    private static string ProfessionalFixture(string nl = "\n")
    {
        var root = StructureFixture(nl); var path = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        text = text.Replace("    Arme =", "    // unrelated comment: keep False, 0, ( )\n    ProjectileType = EProjectileType/GuidedMissile\n    MissileDescriptor = ~/TestMissile\n    TraitsToken = [ 'manual', 'unknown-custom', 'HEAT', ]\n    ShowDamageInUI = False\n    Guidance = EGuidanceType/Custom\n    InterfaceWeaponTexture = 'TestWeaponTexture'\n    ImpactHappening = 'TestImpact'\n    MinMaxCategory = MinMax_Test\n    Arme =");
        text += "\nTestMissile is TEntityDescriptor(ModulesDescriptors=[TGuidedMissileModuleDescriptor(), TGuidedMissileMovementModuleDescriptor()])\n";
        text += "export TestMissileUnused is TEntityDescriptor(ModulesDescriptors=[TGuidedMissileModuleDescriptor(), TGuidedMissileMovementModuleDescriptor()])\n";
        text += "Ammo_ProBomb is TAmmunitionDescriptor(DescriptorId=GUID:{40000000-0000-0000-0000-000000000019} ProjectileType=EProjectileType/Bombe TraitsToken=['F&F', 'semiAuto'] FireDescriptor=~/TestFire)\n";
        text += "TestFire is TFireDescriptor()\nMinMax_Test is 22\nMinMax_Other is 23\n";
        text += "Ammo_ProFake is TAmmunitionDescriptor(ProjectileType=EProjectileType/Fake)\n";
        File.WriteAllText(path, text.Replace("\n", nl), new UTF8Encoding(false));
        var assets = Path.Combine(root, "GameData/Generated/UserInterface/Textures"); Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "Professional.ndf"), "TestWeaponTexture is TUIResourceTexture_Common(FileName='GameData:/Assets/2D/Test.png')\nTestWeaponTexture2 is TUIResourceTexture_Common(FileName='GameData:/Assets/2D/Other.png')\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "GameData/Generated/Gameplay/Gfx/ImpactTest.ndf"), "unnamed TMimeticWorldHappeningRegistration(Happenings=MAP[('TestImpact',TImpactHappening()),('TestImpact2',TImpactHappening())])", new UTF8Encoding(false));
        return root;
    }
    private static DraftOperation ProOp(WeaponFieldValue f, string value, bool local = false) => CreateWeaponDraft(f, value,
        local ? DraftEditScope.CurrentUnit : DraftEditScope.AllReferences, local ? [U1911] : [], local ? W1911 : null) with { InsertAmmoField = f.IsMissing };

    private static async Task AmmoProfessionalContracts()
    {
        var root = ProfessionalFixture();
        try
        {
            var (_, units, data) = await LoadP4Async(root); var a = data.Ammo(A1911)!;
            Assert(WeaponFieldDefinitions.Ammo.Count(f => (f.CanInsert || f.Professional) && f.Key != DamageDistance.ReferenceKey) == 36, "35项加TraitsToken关联（不含专用距离编辑器内部引用）");
            foreach (var def in WeaponFieldDefinitions.Ammo.Where(f => f.CanInsert))
            {
                var projectile = def.FieldName == "IsSubAmmunition" ? "Bombe" : def.FieldName is "PitchForParabolic" or "CorrectedShotDispersionMultiplier" ? "Artillerie" : "GuidedMissile";
                var body = A1911 + " is TAmmunitionDescriptor\n(\n    ProjectileType = EProjectileType/" + projectile + "\n    FireDescriptor = ~/TestFire\n    HitRollRuleDescriptor = TDiceHitRollRuleDescriptor( BaseCriticModifier = 0 )\n)\n";
                var obj = a.Source with { CharacterOffset = 0, CharacterLength = body.Length };
                var parsed = WeaponProjectLoader.ParseWeaponAmmo(obj, body, data); var missing = parsed.Field(def.Key)!;
                Assert(missing.IsMissing, "逐项补建契约：" + def.FieldName);
                var input = def.ValueKind switch { WeaponValueKind.Boolean => "False", WeaponValueKind.Integer => "2", WeaponValueKind.CatalogChoice => "Sample", WeaponValueKind.Degrees => "45", _ => "0.5" };
                if (def.ValueKind == WeaponValueKind.CatalogChoice) missing = missing with { Choices = ["'Sample'"] };
                Assert(WeaponValueConverter.TryFormat(missing, input, out _, out var raw, out var error), def.FieldName + ": " + error);
                var edited = UnitProjectGraph.Patch(body, [AmmoProfessional.Replacement(missing, raw)]);
                var reread = WeaponProjectLoader.ParseWeaponAmmo(obj with { CharacterLength = edited.Length }, edited, data).Field(def.Key)!;
                Assert(reread.State == WeaponFieldState.Declared && reread.RawValue == raw, "35项逐个新增后唯一回读：" + def.FieldName);
                if (def.Constructor is not null) Assert(new NdfSyntaxDocument(edited).FindDirectAssignments(new NdfSyntaxDocument(edited).FindConstructors("TAmmunitionDescriptor").Single(), def.FieldName).Count == 0, "嵌套不能误放到根");
            }
            Assert(a.Field("ammo.behavior.fireAndForget") is { IsMissing: true, DisplayValue: "", RawValue: "" }, "缺失不是False");
            Assert(a.Field(AmmoProfessional.Key("ShowDamageInUI")) is { DisplayValue: "否", State: WeaponFieldState.Declared }, "显式False必须显示");
            Assert(a.Field(AmmoProfessional.Key("DistanceToTarget")) is { IsMissing: true }, "嵌套字段可补建");
            Assert(data.Ammo("Ammo_ProFake")!.Fields.All(f => !f.IsMissing), "Fake不提供猜测补建");
            Assert(data.Ammo("Ammo_ProBomb")!.Field("ammo.behavior.fireAndForget")!.CanEdit == false, "普通炸弹不变成导弹");
            Assert(data.Ammo("Ammo_ProBomb")!.Field(AmmoProfessional.Key("IsSubAmmunition"))!.IsMissing, "观察到的炸弹结构支持原标记");
            foreach (var f in a.Fields.Where(f => f.Definition.ValueKind == WeaponValueKind.Boolean && f.CanEdit))
                foreach (var v in new[] { "True", "False" }) Assert(WeaponValueConverter.TryFormat(f, v, out _, out var raw, out _) && raw == v, f.Key + "显式布尔原值");
            Assert(a.Field(AmmoProfessional.Key("InterfaceWeaponTexture"))!.Choices.Contains("'TestWeaponTexture2'"), "当前Mod未使用但已声明图片候选");
            Assert(a.Field(AmmoProfessional.Key("MissileDescriptor"))!.Choices.Contains("~/TestMissileUnused"), "当前Mod未使用但兼容的导弹候选");
            Assert(a.Field(AmmoProfessional.Key("ImpactHappening"))!.Choices.Contains("'TestImpact2'"), "当前Mod表现注册键候选");
            Assert(!WeaponValueConverter.TryFormat(a.Field(AmmoProfessional.Key("InterfaceWeaponTexture"))!, "made-up", out _, out _, out _), "不能手写虚构资源");
            var tags = a.Field(AmmoProfessional.Key("TraitsToken"))!;
            Assert(WeaponValueConverter.TryFormat(tags, "[\"unknown-custom\",\"HEAT\",\"F&F\"]", out _, out var tagRaw, out _), "保留未知标签并显式选已有标签");
            Assert(AmmoProfessional.Tags(tagRaw).SequenceEqual(new[] { "unknown-custom", "HEAT", "F&F" }), "标签顺序不被排序");
            Assert(!WeaponValueConverter.TryFormat(tags, "[\"invented\"]", out _, out _, out _), "不创建未知标签");
            var bomb = data.Ammo("Ammo_ProBomb")!;
            Assert(!WeaponValueConverter.TryFormat(bomb.Field(AmmoProfessional.Key("FireTriggeringProbability"))!, "1.01", out _, out _, out _), "概率范围");
            Assert(WeaponValueConverter.TryFormat(a.Field(AmmoProfessional.Key("NoiseDissimulationMalus"))!, "-2.5", out _, out _, out _), "原始隐蔽参数不虚构非负限制");
            Assert(!AmmoBatchPlanner.Preview(units, data, [], new([AmmoBatchPlanner.Identity(a)], "ammo.speed.acceleration", UnitBatchOperation.Multiply, "2")).CanSave, "缺失不按0计算");
        }
        finally { DeleteTemporaryFixture(root); }
    }

    private static async Task AmmoProfessionalTransactions()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        foreach (var local in new[] { false, true })
        {
            var root = ProfessionalFixture(nl);
            try
            {
                var (_, _, data) = await LoadP4Async(root); var a = data.Ammo(A1911)!; var source = a.Source.SourceFile; var before = File.ReadAllBytes(source);
                using var store = new DraftStore(root); await store.LoadAsync();
                await store.ApplyBatchAsync(new[] {
                    ProOp(a.Field("ammo.behavior.fireAndForget")!, "False", local),
                    ProOp(a.Field("ammo.speed.acceleration")!, "420", local),
                    ProOp(a.Field(AmmoProfessional.Key("DistanceToTarget"))!, "True", local),
                    ProOp(a.Field(AmmoProfessional.Key("ShowDamageInUI"))!, "True", local),
                    ProOp(a.Field(AmmoProfessional.Key("TraitsToken"))!, "[\"unknown-custom\",\"HEAT\"]", local)
                }, []);
                Assert(JsonDocument.Parse(File.ReadAllText(store.DraftPath)).RootElement.GetProperty("schemaVersion").GetInt32() == 3, "新增声明草稿版本3");
                using (var reopen = new DraftStore(root)) { await reopen.LoadAsync(); Assert(reopen.Operations.Count == 5 && reopen.Operations.Count(o => o.InsertAmmoField) == 3, "重开保留插入存在性"); }
                var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
                await service.CommitApplyAsync(preview, store); var (_, _, after) = await LoadP4Async(root);
                var actual = local ? after.Ammo(after.Weapon(after.Units.Single(u => u.Name == U1911).Weapons.Single())!.Mounts[0].AmmoName)! : after.Ammo(A1911)!;
                Assert(actual.Field("ammo.behavior.fireAndForget")!.RawValue == "False", "缺失→False是实际新增");
                Assert(actual.Field("ammo.speed.acceleration")!.RawValue == "420", "同一节点多个插入");
                Assert(actual.Field(AmmoProfessional.Key("DistanceToTarget"))!.RawValue == "True", "子字段插入正确节点");
                Assert(actual.Field(AmmoProfessional.Key("ShowDamageInUI"))!.RawValue == "True", "已有False修改");
                Assert(actual.Field("ammo.damage.physical")!.RawValue == a.Field("ammo.damage.physical")!.RawValue, "不连带改伤害");
                if (local) Assert(File.ReadAllBytes(source).AsSpan(0, before.Length).SequenceEqual(before) && after.Ammo(A1911)!.Field("ammo.behavior.fireAndForget")!.IsMissing, "局部保持共享源字节和缺失");
                else
                {
                    var changed = File.ReadAllText(source);
                    Assert(changed.Contains("// unrelated comment: keep False, 0, ( )"), "注释保留");
                    Assert(nl == "\n" ? !changed.Contains('\r') : changed.Replace("\r\n", "").Contains('\n') == false, "原换行保留");
                }
                await service.CommitRestoreAsync(service.PrepareRestore(root, preview.BackupId));
                Assert(File.ReadAllBytes(source).SequenceEqual(before), "恢复完整字节与原缺失状态");
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }

    private static async Task AmmoProfessionalGuards()
    {
        var root = ProfessionalFixture();
        try
        {
            var (_, units, data) = await LoadP4Async(root); var a = data.Ammo(A1911)!;
            var targets = new[] { AmmoBatchPlanner.Identity(a), AmmoBatchPlanner.Identity(data.Ammo("Ammo_ProBomb")!) };
            var mixed = AmmoBatchPlanner.Preview(units, data, [], new(targets, AmmoProfessional.Key("ShowDamageInUI"), UnitBatchOperation.Set, "True"));
            Assert(mixed.CanSave && mixed.Upserts.Count(o => o.InsertAmmoField) == 1, "已有False和未声明混合批改");
            using var store = new DraftStore(root); await store.LoadAsync(); await store.ApplyBatchAsync(mixed.Upserts, []);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
            var before = preview.Files.Where(f => File.Exists(f.FullPath)).ToDictionary(f => f.FullPath, f => File.ReadAllBytes(f.FullPath));
            var locked = a.Source.SourceFile; File.SetAttributes(locked, FileAttributes.ReadOnly);
            try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "写失败回滚"); }
            finally { File.SetAttributes(locked, FileAttributes.Normal); }
            Assert(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)) && store.Operations.Count == 2, "失败保持正式文件与草稿");
            await store.ApplyBatchAsync([], store.Operations.Select(o => o.Id).ToArray());
            var ff = ProOp(a.Field("ammo.behavior.fireAndForget")!, "True"); await store.UpsertAsync(ff);
            File.WriteAllText(a.Source.SourceFile, File.ReadAllText(a.Source.SourceFile).Replace("    Arme =", "    IsFireAndForget = False\n    Arme ="), new UTF8Encoding(false));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "外部补建产生基线冲突");
        }
        finally { DeleteTemporaryFixture(root); }
        root = ProfessionalFixture();
        try
        {
            var (_, _, data) = await LoadP4Async(root); var a = data.Ammo(A1911)!; var service = new UnitTransactionService();
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, [ProOp(a.Field(AmmoProfessional.Key("NbSalvosShootOnPosition"))!, "2")]), "点地依赖按最终组合");
            var point = new[] { ProOp(a.Field(AmmoProfessional.Key("NbSalvosShootOnPosition"))!, "2"), ProOp(a.Field("ammo.behavior.position")!, "True") };
            Assert((await service.PrepareApplyAsync(root, point)).Files.Count > 0, "同次开启点地并新增次数");
            var picture = ProOp(a.Field(AmmoProfessional.Key("InterfaceWeaponTexture"))!, "TestWeaponTexture2");
            Assert((await service.PrepareApplyAsync(root, [picture])).Files.Count > 0, "图片定义解析");
            var impact = ProOp(a.Field(AmmoProfessional.Key("ImpactHappening"))!, "TestImpact2");
            Assert((await service.PrepareApplyAsync(root, [impact])).Files.Count > 0, "表现键注册解析");
            var state = StructureState(data); var added = AddStructure(state);
            state.AmmoFields[added] = new() { ["ammo.behavior.fireAndForget"] = "是", [AmmoProfessional.Key("DistanceToTarget")] = "否" };
            state.AmmoBaselines[a.Name] = File.ReadAllText(a.Source.SourceFile).Substring(a.Source.CharacterOffset, a.Source.CharacterLength);
            Assert((await service.PrepareApplyAsync(root, [WeaponStructure.Operation(state)])).Files.Any(f => Encoding.UTF8.GetString(f.CandidateBytes).Contains("IsFireAndForget = True")), "新槽局部弹药插入");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
