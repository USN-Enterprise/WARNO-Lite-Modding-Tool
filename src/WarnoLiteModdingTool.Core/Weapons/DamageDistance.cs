using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public sealed record DamageDistanceState(int Version, string AmmoBody, string BeforeReference,
    string StairName, string StairFile, string StairBody, string Distance, string AP, bool ChangeReference);
public sealed record DistanceExpansion(WeaponWorkspaceData Workspace, IReadOnlyList<DraftOperation> Operations,
    IReadOnlyDictionary<string, IReadOnlyList<string>> AppendedBlocks);

/// <summary>Scoped distance edits become ordinary Ammo reference edits, reusing weapon isolation.</summary>
public static class DamageDistance
{
    public const string ReferenceKey = "ammo.damage.evolution.internal";
    public static WeaponFieldDefinition ReferenceDefinition { get; } = new(ReferenceKey, "伤害", "距离规则引用", "由距离规则编辑器维护",
        WeaponFieldOwner.Ammo, "DamageTypeEvolutionOverRangeDescriptor", WeaponValueKind.CatalogChoice, Professional: true);
    public static DamageDistanceState Read(DraftOperation op)
    {
        try
        {
            var value = JsonSerializer.Deserialize<DamageDistanceState>(op.TargetRaw);
            if (value is null || value.Version != 1 || string.IsNullOrEmpty(value.AmmoBody) || string.IsNullOrEmpty(value.StairBody) ||
                string.IsNullOrEmpty(value.BeforeReference) || string.IsNullOrEmpty(value.StairName) || string.IsNullOrEmpty(value.StairFile) ||
                string.IsNullOrEmpty(value.Distance) || string.IsNullOrEmpty(value.AP)) throw new JsonException();
            return value;
        }
        catch (JsonException) { throw new InvalidDataException("距离草稿格式无效"); }
    }
    public static bool Basic(AmmoRecord ammo) => ammo.Field("ammo.damage.family")?.DisplayValue == "DamageFamily_ap" &&
        ammo.Field(AmmoProfessional.Key("PiercingWeapon"))?.RawValue == "True";
    public static DraftOperation Operation(DamageWorkspace data, AmmoRecord ammo, DamageStair stair, string distance, string ap,
        DraftEditScope scope, IReadOnlyList<string> units, bool changeReference = false)
    {
        DamageWorkspace.Positive(distance); DamageWorkspace.Positive(ap);
        var reference = ammo.Field(ReferenceKey) ?? throw new InvalidDataException("弹药缺少唯一距离规则字段");
        if (!reference.CanEdit || reference.IsMissing) throw new InvalidDataException("弹药距离规则字段不可编辑");
        var state = new DamageDistanceState(1, data.Graph.Body(data.Graph.RequireObject(ammo.Source.RelativeSourceFile, ammo.Name)),
            reference.RawValue, stair.Source.Name, stair.Source.RelativeSourceFile, stair.Body, distance, ap, changeReference);
        _ = data.Reference(ammo.Source.RelativeSourceFile, stair);
        var identity = scope == DraftEditScope.AllReferences ? "shared" : string.Join(",", units.Distinct().Order(StringComparer.Ordinal));
        if (identity.Length == 0) throw new InvalidDataException("距离修改缺少单位作用域");
        var key = "distance/" + identity;
        return new(DraftOperation.CreateId(DraftTargetKind.DamageDistance, ammo.Source.RelativeSourceFile, ammo.Name, key), null,
            DraftTargetKind.DamageDistance, "ammo", ammo.Source.RelativeSourceFile, ammo.Name, ammo.Source.TypeName,
            key, "DamageTypeEvolutionOverRangeDescriptor", "DamageDistanceV1", reference.RawValue, reference.RawValue,
            distance + " GRU / " + ap, JsonSerializer.Serialize(state), "距离规则 · " + ammo.DisplayName + " · " + distance + " GRU / " + ap,
            null, false, DateTimeOffset.UtcNow, EditScope: scope, SelectedUnitNames: units.Distinct().Order(StringComparer.Ordinal).ToArray());
    }
    public static ResolvedDraftOperation Resolve(DamageWorkspace data, WeaponWorkspaceData? weapons, DraftOperation op)
    {
        try { Validate(data, weapons, op); return new(op, DraftResolutionStatus.Active, ""); }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or TransactionValidationException)
        { return new(op, DraftResolutionStatus.Conflict, e.Message); }
    }
    public static void Validate(DamageWorkspace data, WeaponWorkspaceData? weapons, DraftOperation op)
    {
        var state = Read(op); DamageWorkspace.Positive(state.Distance); DamageWorkspace.Positive(state.AP);
        var ammo = weapons?.Ammo(op.ObjectName) ?? throw new InvalidDataException("距离草稿对应弹药不存在");
        if (ammo.Source.RelativeSourceFile != op.RelativeSourceFile || ammo.Source.TypeName != op.ObjectType ||
            data.Graph.Body(data.Graph.RequireObject(op.RelativeSourceFile, op.ObjectName)) != state.AmmoBody ||
            ammo.Field(ReferenceKey)?.RawValue != state.BeforeReference || state.BeforeReference != op.BaselineRaw)
            throw new InvalidDataException("弹药距离规则基线已变化");
        var stair = data.Stairs.Single(s => s.Source.Name == state.StairName && s.Source.RelativeSourceFile == state.StairFile);
        if (stair.Body != state.StairBody) throw new InvalidDataException("引用的距离规则基线已变化");
        if (!state.ChangeReference && data.ResolveStair(op.RelativeSourceFile, state.BeforeReference) != stair)
            throw new InvalidDataException("弹药原距离规则不能唯一解析");
        _ = data.Reference(op.RelativeSourceFile, stair);
        if (state.ChangeReference && (state.Distance != stair.Distance || state.AP != stair.AP))
            throw new InvalidDataException("更换引用和修改共享规则须分别处理");
        if (op.EditScope is not (DraftEditScope.AllReferences or DraftEditScope.CurrentUnit or DraftEditScope.SelectedUnits))
            throw new InvalidDataException("距离修改作用域无效");
        if (op.EditScope != DraftEditScope.AllReferences && (op.SelectedUnitNames is not { Count: > 0 } ||
            op.SelectedUnitNames.Any(n => !weapons!.References.AmmoUnits.GetValueOrDefault(ammo.Name, []).Contains(n))))
            throw new InvalidDataException("距离修改单位引用已变化");
        if(op.EditScope==DraftEditScope.CurrentUnit && op.SelectedUnitNames?.Count!=1)
            throw new InvalidDataException("距离修改作用域无效");
    }
    public static DistanceExpansion Expand(DamageWorkspace data, WeaponWorkspaceData weapons, IReadOnlyList<DraftOperation> operations)
    {
        var result = operations.Where(o => o.TargetKind != DraftTargetKind.DamageDistance).ToList();
        var appends = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<string, List<string>>();
        var variants = new Dictionary<string, string>();
        var names = data.Graph.Objects.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var op in operations.Where(o => o.TargetKind == DraftTargetKind.DamageDistance))
        {
            Validate(data, weapons, op); var state = Read(op);
            var stair = data.Stairs.Single(s => s.Source.Name == state.StairName && s.Source.RelativeSourceFile == state.StairFile);
            var raw = data.Reference(op.RelativeSourceFile, stair);
            if (!state.ChangeReference && (state.Distance != stair.Distance || state.AP != stair.AP))
            {
                // A scoped edit always owns its resulting rule; it must never change unselected Ammo.
                var signature = JsonSerializer.Serialize(new { state.StairFile, state.StairName, state.Distance, state.AP });
                if (!variants.TryGetValue(signature, out var clone))
                {
                    do clone = state.StairName + "_WLMT_" + Guid.NewGuid().ToString("N")[..10]; while (!names.Add(clone));
                    variants[signature] = clone;
                    var doc = new NdfSyntaxDocument(stair.Body); var root = doc.FindConstructors(stair.Source.TypeName).Single();
                    var token = doc.Tokens.Single(t => t.Text == stair.Source.Name);
                    var edits = new List<TextReplacement> { new(token.Start, token.Length, token.Text, clone, "独立距离规则") };
                    foreach (var (field, value) in new[] { ("DistanceGRU", state.Distance), ("AP", state.AP) })
                    {
                        var span = DamageWorkspace.Required(doc, root, field);
                        edits.Add(new(doc.StartOffset(span), doc.Length(span), doc.Raw(span), value, field));
                    }
                    var path = stair.Source.RelativeSourceFile;
                    if (!appends.TryGetValue(path, out var list)) appends[path] = list = [];
                    list.Add(SemicolonCsvDocument.ApplyReplacements(stair.Body, edits).TrimEnd('\r', '\n'));
                }
                raw = raw[..(raw.Length - stair.Source.Name.Length)] + clone;
            }
            if (!targets.TryGetValue(op.ObjectName, out var choices)) targets[op.ObjectName] = choices = [];
            choices.Add(raw);
            result.Add(op with { TargetKind = DraftTargetKind.AmmoField, FieldKey = ReferenceKey,
                FieldPath = "TAmmunitionDescriptor.DamageTypeEvolutionOverRangeDescriptor", TargetRaw = raw, TargetValue = raw,
                BaselineRaw = state.BeforeReference, BaselineValue = state.BeforeReference });
        }
        var ammo = weapons.Ammunition.Select(a => !targets.TryGetValue(a.Name, out var choices) ? a :
            new AmmoRecord(a.Source, a.Fields.Select(f => f.Key == ReferenceKey ? f with { Choices = f.Choices.Concat(choices).Distinct().ToArray() } : f).ToArray())
            { NameToken = a.NameToken, NameRaw = a.NameRaw, NameLocation = a.NameLocation, CanEditName = a.CanEditName, DisplayName = a.DisplayName, ChineseName = a.ChineseName }).ToArray();
        return new(weapons with { Ammunition = ammo }, result, appends.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value));
    }
    public static void Append(string root, DistanceExpansion expansion, List<PlannedFileChange> files)
    {
        var candidate = new CandidateTextFiles(root, files);
        foreach (var (path, blocks) in expansion.AppendedBlocks)
        {
            var text = candidate.Get(path); var nl = candidate.NewLine(path);
            candidate.Set(path, text + (text.EndsWith(nl, StringComparison.Ordinal) ? nl : nl + nl) + string.Join(nl + nl, blocks) + nl);
        }
        candidate.Complete("独立距离规则；未选弹药保留原共享配置");
    }
    public static void ValidateFinal(DamageWorkspace final, IReadOnlyList<DraftOperation> original, DistanceExpansion? expansion)
    {
        if (expansion is null) return;
        var changedReferences = expansion.Operations.Where(o => o.FieldKey == ReferenceKey).Select(o => o.TargetRaw).ToHashSet();
        var resolved=new Dictionary<string,DamageStair>(StringComparer.Ordinal);
        foreach (var obj in final.Graph.Objects.Where(o => o.TypeName == "TAmmunitionDescriptor"))
        {
            var doc = new NdfSyntaxDocument(final.Graph.Body(obj)); var root = doc.FindConstructors(obj.TypeName).Single();
            foreach (var span in doc.FindDirectAssignments(root, "DamageTypeEvolutionOverRangeDescriptor"))
            {
                var raw = doc.Raw(span);
                if(!changedReferences.Contains(raw))continue;
                var stair=final.ResolveStair(obj.RelativeSourceFile,raw);
                if(stair is null)throw new TransactionValidationException("最终距离规则引用无法解析：" + obj.Name);
                if(resolved.TryGetValue(raw,out var prior) && !UnitProjectGraph.Same(prior.Source,stair.Source))
                    throw new TransactionValidationException("最终距离规则引用作用域不一致："+raw);
                resolved[raw]=stair;
            }
        }
        foreach(var op in original.Where(o=>o.TargetKind==DraftTargetKind.DamageDistance))
        {
            var state=Read(op);var reference=expansion.Operations.Single(o=>o.Id==op.Id).TargetRaw;
            var distance=state.Distance;var ap=state.AP;
            var shared=original.SingleOrDefault(o=>o.TargetKind==DraftTargetKind.DamageRule&&o.FieldKey=="stair"&&o.ObjectName==state.StairName);
            if(state.ChangeReference && shared is not null)
            {
                var values=DamageWorkspace.Read(shared).Changes.ToDictionary(c=>c.Key,c=>c.After);distance=values["DistanceGRU"];ap=values["AP"];
            }
            if(!resolved.TryGetValue(reference,out var actual) || DamageWorkspace.Positive(actual.Distance)!=DamageWorkspace.Positive(distance) || DamageWorkspace.Positive(actual.AP)!=DamageWorkspace.Positive(ap))
                throw new TransactionValidationException("最终距离参数与组合草稿不一致："+op.ObjectName);
        }
    }
}
