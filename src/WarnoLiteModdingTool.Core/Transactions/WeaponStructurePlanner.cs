using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Transactions;

public static class WeaponStructurePlanner
{
    public static IReadOnlyList<WeaponStructureState> States(IEnumerable<DraftOperation> operations) => operations
        .Where(o => o.TargetKind == DraftTargetKind.WeaponStructure).Select(WeaponStructure.Read)
        .Concat(operations.Where(o => o.TargetKind == DraftTargetKind.UnitCreate).SelectMany(o => UnitCreation.Read(o).WeaponStructures
            .Select(s => s with { Unit = o.ObjectName, CreationId = o.ObjectName }))).ToArray();

    public static void ValidateCombination(UnitWorkspaceData units, WeaponWorkspaceData data, IReadOnlyList<DraftOperation> operations)
    {
        var states = States(operations);
        foreach (var state in states)
        {
            if (operations.Any(o => o.TargetKind == DraftTargetKind.UnitDelete && o.ObjectName == state.Unit)) throw new TransactionValidationException("待删除单位存在武器槽草稿：" + state.Unit);
            if (states.Any(s => s.Id != state.Id && s.Weapon.Name == state.Weapon.Name && (s.Shared || state.Shared || s.Unit == state.Unit)))
                throw new TransactionValidationException("同一武器存在重叠结构计划，请分别处理共享与局部计划");
            if (state.Initialize && states.Any(s => s.Id != state.Id && s.Initialize && s.Unit == state.Unit))
                throw new TransactionValidationException("无武器单位只能选择一套初始化参考配置");
            if (operations.Any(o => o.TargetKind == DraftTargetKind.UnitWeaponReference && (o.ContextWeaponName == state.Weapon.Name || o.ObjectName == state.Unit)))
                throw new TransactionValidationException("请先处理整套武器替换草稿");
            var resolved = WeaponStructure.Resolve(units, data, WeaponStructure.Operation(state), operations);
            if (resolved.Status != DraftResolutionStatus.Active) throw new TransactionValidationException(resolved.Reason);
        }
    }

    private static IReadOnlyList<(NdfValueSpan Span, NdfObjectInfo Object)> WeaponRefs(UnitProjectGraph graph, string file, string body)
    {
        var doc = new NdfSyntaxDocument(body); var root = WeaponStructureSyntax.Constructor(doc, new(0, doc.Tokens.Count - 1), "TEntityDescriptor", true);
        var modules = WeaponStructureSyntax.Array(doc, WeaponStructureSyntax.Assignment(doc, root, "ModulesDescriptors"));
        var found = new List<(NdfValueSpan, NdfObjectInfo)>();
        foreach (var element in modules)
        {
            var target = graph.Resolve(file, doc.Raw(element));
            if (target?.TypeName == "TWeaponManagerModuleDescriptor") found.Add((element, target));
        }
        return found;
    }

    public static IReadOnlyList<string> Plan(string root, UnitWorkspaceData units, WeaponWorkspaceData weapons, IReadOnlyList<DraftOperation> operations, List<PlannedFileChange> files)
    {
        var states = States(operations); if (states.Count == 0) return [];
        ValidateCombination(units, weapons, operations);
        var originalGraph = new UnitProjectGraph(root); var messages = new List<string>();
        foreach (var state in states)
        {
            WeaponStructurePresentation.ValidateBaselines(originalGraph, state);
            var source = originalGraph.RequireObject(state.Weapon.File, state.Weapon.Name);
            var sourceRefs = originalGraph.References(source);
            if (state.Shared && sourceRefs.Any(r => r.Owner?.TypeName != "TEntityDescriptor")) throw new TransactionValidationException("共享结构包含非单位使用者，暂不支持修改");
            var targets = state.Shared ? sourceRefs.Select(r => r.Owner!.Name).Distinct().ToArray() : [state.Unit];
            foreach (var unitName in targets)
            {
                var graph = new UnitProjectGraph(root, files);
                var unit = graph.FindObjects(unitName).SingleOrDefault(o => o.TypeName == "TEntityDescriptor") ?? throw new TransactionValidationException("目标单位不存在：" + unitName);
                var baseline = state.Shared ? UnitCreation.Source(units.Units.Single(u => u.Name == unitName)) : state.UnitBaseline;
                var originalUnit = state.CreationId is null ? units.Units.Single(u => u.Name == unitName) : units.Units.Single(u => u.Name == UnitCreation.Read(operations.Single(o => o.TargetKind == DraftTargetKind.UnitCreate && o.ObjectName == unitName)).Mother);
                var originals = WeaponRefs(originalGraph, originalUnit.Source.RelativeSourceFile, baseline);
                if (originals.Count(r => UnitProjectGraph.Same(r.Object, source)) > 1) throw new TransactionValidationException("单位重复引用同一武器配置，不能唯一选择模块");
                var ordinal = originals.ToList().FindIndex(r => UnitProjectGraph.Same(r.Object, source));
                var ub = graph.Body(unit); var currentRefs = WeaponRefs(graph, unit.RelativeSourceFile, ub);
                if ((!state.Initialize && ordinal < 0) || currentRefs.Count != originals.Count || state.Initialize && currentRefs.Count != 0) throw new TransactionValidationException("单位武器模块结构已变化");
                var actualWeapon = state.Initialize ? graph.RequireObject(source.RelativeSourceFile, source.Name) : currentRefs[ordinal].Object;
                var currentBody = graph.Body(actualWeapon); var currentSyntax = new WeaponStructureSyntax(currentBody); var originalSyntax = new WeaponStructureSyntax(state.Weapon.Body);
                foreach (var id in state.Removed)
                    if (currentSyntax.Mounts.Single(m => m.Id == id).Body != originalSyntax.Mounts.Single(m => m.Id == id).Body)
                        throw new TransactionValidationException("待删除槽位有参数修改，请先丢弃关联修改或撤销删除：" + id);
                var local = state with { Unit = unitName, UnitBaseline = baseline };
                WeaponStructurePresentation.Validate(graph, local);
                if (state.Initialize && state.Added.Count == 0) throw new TransactionValidationException("初始化武器配置至少需要一个新增槽位");
                foreach (var edit in local.AmmoFields.Where(p => p.Value.Count > 0 && !local.Removed.Contains(p.Key) && !p.Key.StartsWith("new:", StringComparison.Ordinal)))
                    if (currentSyntax.Mounts.Single(m => m.Id == edit.Key).Ammo != originalSyntax.Mounts.Single(m => m.Id == edit.Key).Ammo)
                        throw new TransactionValidationException("该槽位已有弹药替换或局部弹药草稿，请先应用这些修改");
                // Resolve all donor references in the destination scope before materialising the new node.
                local = local with { Added = local.Added.Select(addition =>
                {
                    var chosenAmmo = WeaponStructureSyntax.Values(addition.Body, "TMountedWeaponDescriptor", ["Ammunition"]);
                    var sourceAmmo = new WeaponStructureSyntax(addition.Source.Body).Mounts.Single(m => m.Id == addition.SourceMount).Ammo;
                    var originalAmmoBody = WeaponStructureSyntax.Set(addition.Body, "TMountedWeaponDescriptor", new Dictionary<string, string> { ["Ammunition"] = sourceAmmo });
                    var rebased = WeaponStructurePresentation.RebindReferences(graph, addition.Source.File, actualWeapon.RelativeSourceFile, originalAmmoBody);
                    return addition with { Body = WeaponStructureSyntax.Set(rebased, "TMountedWeaponDescriptor", chosenAmmo) };
                }).ToList() };
                var ammoOverrides = PlanAmmo(root, graph, local, weapons, files, units.Localisation);
                var rendered = WeaponStructureRenderer.Render(local, currentBody, ammoOverrides);
                var final = new WeaponStructureSyntax(rendered); var updatedUnit = ub;
                if (!final.Mounts.Any())
                {
                    var ud = new NdfSyntaxDocument(ub); var un = ud.FindConstructors("TEntityDescriptor").Single();
                    updatedUnit = UnitProjectGraph.Patch(ub, [UnitProjectGraph.RemoveElement(ud, WeaponStructureSyntax.Assignment(ud, un, "ModulesDescriptors"), currentRefs[ordinal].Span, "解除武装")]);
                    messages.Add(unitName + "：移除武器管理模块引用，保留原配置与表现资源");
                }
                else
                {
                    var uses = graph.References(actualWeapon);
                    var privateWeapon = !state.Initialize && uses.All(r => r.Owner?.Name == unitName && r.Owner.TypeName == "TEntityDescriptor");
                    if (privateWeapon)
                    {
                        var text = graph.FileOf(actualWeapon).Text;
                        UnitProjectGraph.Put(root, actualWeapon.RelativeSourceFile, text[..actualWeapon.CharacterOffset] + rendered + text[(actualWeapon.CharacterOffset + actualWeapon.CharacterLength)..], files, "武器槽结构与参数");
                    }
                    else
                    {
                        var name = state.Weapon.Name + "_WLMT_slots_" + state.Id[..10] + "_" + Math.Max(0, ordinal) + "_" + Array.IndexOf(targets, unitName);
                        if (graph.FindObjects(name).Count > 0) throw new TransactionValidationException("武器副本名称已占用");
                        rendered = WeaponStructureSyntax.Rename(rendered, name);
                        var rd = new NdfSyntaxDocument(rendered); var rn = rd.FindConstructors("TWeaponManagerModuleDescriptor").Single();
                        var guid = rd.FindDirectAssignments(rn, "DescriptorId");
                        if (guid.Count == 1) rendered = WeaponStructureSyntax.Set(rendered, "TWeaponManagerModuleDescriptor", new Dictionary<string, string> { ["DescriptorId"] = "GUID:{" + Guid.NewGuid() + "}" });
                        var text = graph.FileOf(actualWeapon).Text; var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                        UnitProjectGraph.Put(root, actualWeapon.RelativeSourceFile, text + nl + rendered + nl, files, "隔离武器槽配置");
                        var ud = new NdfSyntaxDocument(ub);
                        if (state.Initialize)
                        {
                            var un = ud.FindConstructors("TEntityDescriptor").Single();
                            var reference = graph.ReferenceTo(unit.RelativeSourceFile, actualWeapon);
                            reference = reference[..(reference.LastIndexOf('/') + 1)] + name;
                            updatedUnit = WeaponStructureSyntax.EditList(ub, WeaponStructureSyntax.Assignment(ud, un, "ModulesDescriptors"), new HashSet<int>(), new Dictionary<int, string>(), [reference]);
                        }
                        else
                        {
                            var span = currentRefs[ordinal].Span; var raw = ud.Raw(span);
                            var targetRaw = raw[..(raw.LastIndexOf('/') + 1)] + name;
                            updatedUnit = UnitProjectGraph.Patch(ub, [new(ud.StartOffset(span), ud.Length(span), raw, targetRaw, "武器槽独立引用")]);
                        }
                    }
                }
                if (updatedUnit != ub)
                {
                    var next = new UnitProjectGraph(root, files); var targetUnit = next.FindObjects(unitName).Single(); var text = next.FileOf(targetUnit).Text;
                    UnitProjectGraph.Put(root, targetUnit.RelativeSourceFile, text[..targetUnit.CharacterOffset] + updatedUnit + text[(targetUnit.CharacterOffset + targetUnit.CharacterLength)..], files, "单位武器槽引用");
                }
                var presentationGraph = new UnitProjectGraph(root, files);
                WeaponStructurePresentation.Plan(root, presentationGraph, local, presentationGraph.FindObjects(unitName).Single(), files);
                messages.Add(unitName + $"：新增 {state.Added.Count} 槽，删除 {state.Removed.Count} 槽；按完整使用者重映射弹药箱");
            }
        }
        return messages;
    }

    private static Dictionary<string, string> PlanAmmo(string root, UnitProjectGraph graph, WeaponStructureState state, WeaponWorkspaceData data, List<PlannedFileChange> files, Localisation.UnitLocalisationCatalog localisation)
    {
        var result = new Dictionary<string, string>();
        foreach (var edit in state.AmmoFields.Where(p => p.Value.Count > 0))
        {
            var row = WeaponStructure.Rows(state).Single(r => r.Id == edit.Key); if (row.Removed) continue;
            var ammo = data.Ammo(row.Ammo) ?? throw new TransactionValidationException("弹药已不存在");
            var obj = graph.RequireObject(ammo.Source.RelativeSourceFile, ammo.Name); var body = graph.Body(obj);
            var baseline = Projects.ProjectReadScope.ReadAllText(ammo.Source.SourceFile).Substring(ammo.Source.CharacterOffset, ammo.Source.CharacterLength);
            if (!state.AmmoBaselines.TryGetValue(ammo.Name, out var expected) || expected != baseline) throw new TransactionValidationException("局部弹药基线已变化");
            // Locate supported field spans again in the final candidate rather than reusing shifted offsets.
            var candidate = WeaponProjectLoader.ParseWeaponAmmo(obj with { CharacterOffset = 0, CharacterLength = body.Length }, body, data);
            var replacements = new List<TextReplacement>(); var finalValues = candidate.Fields.ToDictionary(f => f.Key, f => f.DisplayValue);
            var rawChanges = new Dictionary<string, string>();
            foreach (var change in edit.Value)
            {
                var field = candidate.Field(change.Key) ?? throw new TransactionValidationException("弹药字段已不存在");
                var old = ammo.Field(change.Key)!;
                if (!WeaponValueConverter.TryFormat(field, change.Value, out var value, out var raw, out var error)) throw new TransactionValidationException(error);
                if (field.RawValue != old.RawValue && field.RawValue != raw) throw new TransactionValidationException("共享与局部弹药目标冲突");
                finalValues[field.Key] = value;
                rawChanges[field.Key] = raw;
                replacements.Add(AmmoProfessional.Replacement(field, raw));
            }
            AmmoRangeValidator.Validate(candidate, finalValues);
            var name = ammo.Name + "_WLMT_slots_" + state.Id[..10] + "_" + string.Concat(row.Id.Where(char.IsLetterOrDigit)) + "_" + state.Unit;
            if (graph.FindObjects(name).Count > 0) throw new TransactionValidationException("弹药副本名称已占用");
            body = UnitProjectGraph.Patch(body, AmmoProfessional.MergeInsertions(replacements));
            AmmoProfessionalValidation.Candidate(candidate, body, rawChanges, () => graph, localisation);
            body = WeaponStructureSyntax.Rename(body, name);
            body = WeaponStructureSyntax.Set(body, obj.TypeName, new Dictionary<string, string> { ["DescriptorId"] = "GUID:{" + Guid.NewGuid() + "}" });
            var changes = new CandidateTextFiles(root, files); var text = changes.Get(obj.RelativeSourceFile); var nl = changes.NewLine(obj.RelativeSourceFile);
            changes.Set(obj.RelativeSourceFile, text + nl + body + nl); changes.Complete("新槽局部弹药参数");
            var rawAmmo = WeaponStructureSyntax.Values(row.Body, "TMountedWeaponDescriptor", ["Ammunition"])["Ammunition"];
            result[row.Id] = rawAmmo[..(rawAmmo.LastIndexOf('/') + 1)] + name;
        }
        return result;
    }
}
