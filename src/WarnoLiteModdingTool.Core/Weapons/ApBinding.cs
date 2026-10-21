using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Weapons;

// Owned by the initiating family draft: removing that draft removes only its automatic changes.
public sealed record ApBindingState(string Ammo, string File, string Body, DraftEditScope Scope,
    IReadOnlyList<string> Units, DamageDistanceState Distance);

public static class ApBinding
{
    public const string FamilyKey = "ammo.damage.family";
    public static string PiercingKey => AmmoProfessional.Key("PiercingWeapon");
    public static bool Has(DraftOperation op) => op.ApBindings is { Count: > 0 };
    private static bool Overlap(ApBindingState b, DraftOperation o) => o.ObjectName == b.Ammo &&
        (o.EditScope == DraftEditScope.AllReferences || b.Scope == DraftEditScope.AllReferences || (o.SelectedUnitNames ?? []).Intersect(b.Units).Any());
    public static DraftOperation DistanceOp(ApBindingState b, string id) => new(id, null, DraftTargetKind.DamageDistance, "ammo", b.File,
        b.Ammo, "TAmmunitionDescriptor", "distance/" + (b.Scope == DraftEditScope.AllReferences ? "shared" : string.Join(",", b.Units.Order(StringComparer.Ordinal))),
        "DamageTypeEvolutionOverRangeDescriptor", "DamageDistanceV2", b.Distance.BeforeReference, b.Distance.BeforeReference,
        b.Distance.Distance + " GRU / " + b.Distance.AP, JsonSerializer.Serialize(b.Distance), "AP联动 · " + b.Ammo, null, false,
        DateTimeOffset.UtcNow, EditScope: b.Scope, SelectedUnitNames: b.Units);

    public static DraftOperation Prepare(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> drafts, DraftOperation op)
    {
        if (!WeaponBatch.IsWeaponEdit(op)) return op;
        var cells = WeaponBatch.Cells(data, [op with { ApBindings = null }]);
        if (!cells.Any(c => c.Key == FamilyKey || c.Key == PiercingKey || c.Key == DamageDistance.ReferenceKey)) return op with { ApBindings = null };
        var ap = cells.Where(c => c.Key == FamilyKey && c.Raw == "DamageFamily_ap").ToArray();
        var remaining = drafts.Where(o => o.Id != op.Id).ToArray();
        if (ap.Length == 0)
        {
            ValidateRelated(data, remaining.Append(op with { ApBindings = null }).ToArray());
            return op with { ApBindings = null };
        }
        var damage = data.DamageSource?.Invoke() ?? throw new InvalidDataException("AP联动需要读取当前Mod伤害规则");
        var links = new List<ApBindingState>();
        foreach (var group in ap.GroupBy(c => (c.Ammo, Shared: c.Unit.Length == 0)))
        {
            var ammo = data.Ammo(group.Key.Ammo)!;
            var scope = group.Key.Shared ? DraftEditScope.AllReferences : DraftEditScope.SelectedUnits;
            var units = group.Where(c => c.Unit.Length > 0).Select(c => c.Unit).Distinct().Order(StringComparer.Ordinal).ToArray();
            if (op.TargetKind == DraftTargetKind.AmmoField && op.EditScope == DraftEditScope.CurrentUnit) scope = DraftEditScope.CurrentUnit;
            var field = ammo.Field(DamageDistance.ReferenceKey) ?? throw new InvalidDataException("弹药缺少距离字段定位：" + ammo.Name);
            if (!field.CanEdit) throw new InvalidDataException(field.Reason + "：" + ammo.Name);
            var piercing = ammo.Field(PiercingKey) ?? throw new InvalidDataException("弹药缺少穿甲标记定位：" + ammo.Name);
            if (!piercing.CanEdit) throw new InvalidDataException(piercing.Reason + "：" + ammo.Name);
            var body = damage.Graph.Body(damage.Graph.RequireObject(ammo.Source.RelativeSourceFile, ammo.Name));
            var original = field.IsMissing || field.RawValue == "nil" ? null : damage.ResolveStair(ammo.Source.RelativeSourceFile, field.RawValue)
                ?? throw new InvalidDataException("距离引用无法解析，请先处理原配置：" + ammo.Name);
            if (op.TargetKind == DraftTargetKind.AmmoField && group.All(c => c.BaselineRaw == "DamageFamily_ap") && original is not null && piercing.RawValue == "True") continue;
            var defaults = damage.Stairs.Where(s => DamageWorkspace.Number(s.Distance) == 175 && DamageWorkspace.Number(s.AP) == 1)
                .Where(s => Compatible(damage, ammo, s)).ToArray();
            var stair = original ?? (defaults.Length == 1 ? defaults[0] : defaults.SingleOrDefault(s => s.Source.Name == "DamageTypeEvolutionOverRangeDescriptor_AP1_1Km"));
            var previous = op.ApBindings?.FirstOrDefault(b => b.Ammo == ammo.Name && b.Scope == scope && b.Units.SequenceEqual(units));
            var name = stair?.Source.Name ?? (previous?.Distance.CreateRule == true ? previous.Distance.StairName : "WLMT_AP_" + Guid.NewGuid().ToString("N")[..12]);
            var distance = stair?.Distance ?? "175.0"; var value = stair?.AP ?? "1.0";
            var state = new DamageDistanceState(2, body, field.RawValue, name, stair?.Source.RelativeSourceFile ?? ammo.Source.RelativeSourceFile,
                stair?.Body ?? DamageDistance.NewRuleBody(name, distance, value), distance, value, true, field.IsMissing, stair is null);
            var binding = new ApBindingState(ammo.Name, ammo.Source.RelativeSourceFile, body, scope, units, state);
            var explicitRules = remaining.Where(o => o.TargetKind == DraftTargetKind.DamageDistance && Overlap(binding, o)).ToArray();
            if (explicitRules.Length == 1 && (explicitRules[0].EditScope == scope || scope != DraftEditScope.AllReferences && explicitRules[0].EditScope != DraftEditScope.AllReferences &&
                (explicitRules[0].SelectedUnitNames ?? []).Order().SequenceEqual(units.Order())))
                binding = binding with { Distance = DamageDistance.Read(explicitRules[0]) };
            else if (explicitRules.Length > 0) throw new InvalidDataException("AP联动与距离草稿范围不同，请统一范围：" + ammo.Name);
            links.Add(binding);
        }
        var result = op with { ApBindings = links.Count == 0 ? null : links };
        ValidateRelated(data, remaining.Append(result).ToArray());
        foreach (var b in links) DamageDistance.Validate(damage, data, DistanceOp(b, op.Id + "/ap-distance/" + b.Ammo));
        return result;
    }

    private static bool Compatible(DamageWorkspace data, AmmoRecord ammo, DamageStair stair)
    {
        try { _ = data.Reference(ammo.Source.RelativeSourceFile, stair); return true; }
        catch (Exception e) when (e is InvalidDataException or TransactionValidationException) { return false; }
    }
    public static IReadOnlyList<DraftOperation> PrepareBatch(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> drafts,
        IReadOnlyList<DraftOperation> upserts, IReadOnlyList<string> removals)
    {
        var ids = upserts.Select(o => o.Id).Concat(removals).ToHashSet();
        var combined = drafts.Where(o => !ids.Contains(o.Id)).ToList();
        var result = new List<DraftOperation>();
        foreach (var op in upserts) { var prepared = Prepare(data, combined, op); result.Add(prepared); combined.Add(prepared); }
        ValidateRelated(data, combined);
        return result;
    }
    public static void ValidateRelated(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> ops)
    {
        var cells = WeaponBatch.Cells(data, ops);
        foreach (var c in cells.Where(c => c.Key == PiercingKey && c.Raw != "True" || c.Key == DamageDistance.ReferenceKey && c.Raw == "nil"))
        {
            var family = cells.Where(f => f.Key == FamilyKey && f.Ammo == c.Ammo &&
                (f.Unit.Length == 0 || c.Unit.Length == 0 || f.Unit == c.Unit)).Select(f => f.Raw).Distinct().ToArray();
            if (family.Contains("DamageFamily_ap") || family.Length == 0 && data.Ammo(c.Ammo)?.Field(FamilyKey)?.RawValue == "DamageFamily_ap")
                throw new InvalidDataException("AP家族需要启用距离规则及穿甲标记：" + c.Ammo);
        }
    }
    public static void Validate(WeaponWorkspaceData data, DamageWorkspace damage, DraftOperation op)
    {
        if (!Has(op)) return;
        var cells = WeaponBatch.Cells(data, [op with { ApBindings = null }]);
        foreach (var b in op.ApBindings!)
        {
            if (b.Units is null || b.Distance is null || b.Scope is not (DraftEditScope.AllReferences or DraftEditScope.CurrentUnit or DraftEditScope.SelectedUnits) ||
                !cells.Any(c => c.Key == FamilyKey && c.Raw == "DamageFamily_ap" && c.Ammo == b.Ammo &&
                    (b.Scope == DraftEditScope.AllReferences ? c.Unit.Length == 0 : b.Units.Contains(c.Unit))) ||
                b.Body != damage.Graph.Body(damage.Graph.RequireObject(b.File, b.Ammo)) || b.Distance.AmmoBody != b.Body)
                throw new InvalidDataException("AP联动基线或范围已变化：" + b.Ammo);
            var expectedUnits = cells.Where(c => c.Key == FamilyKey && c.Raw == "DamageFamily_ap" && c.Ammo == b.Ammo && c.Unit.Length > 0).Select(c => c.Unit).Distinct().Order(StringComparer.Ordinal);
            if (b.Scope != DraftEditScope.AllReferences && !b.Units.Order(StringComparer.Ordinal).SequenceEqual(expectedUnits)) throw new InvalidDataException("AP联动单位范围不一致");
            DamageDistance.Validate(damage, data, DistanceOp(b, op.Id + "/ap-distance/" + b.Ammo));
        }
        if (cells.Where(c => c.Key == FamilyKey && c.Raw == "DamageFamily_ap").Any(c => !op.ApBindings!.Any(b => b.Ammo == c.Ammo &&
            (c.Unit.Length == 0 ? b.Scope == DraftEditScope.AllReferences : b.Units.Contains(c.Unit))))) throw new InvalidDataException("AP联动配套不完整");
    }
    public static IReadOnlyList<DraftOperation> Expand(WeaponWorkspaceData data, DamageWorkspace damage, IReadOnlyList<DraftOperation> ops)
    {
        ValidateRelated(data, ops);
        var result = ops.Select(o => o with { ApBindings = null }).ToList();
        foreach (var owner in ops.Where(Has))
        {
            Validate(data, damage, owner);
            foreach (var b in owner.ApBindings!)
            {
                var distances = ops.Where(o => o.TargetKind == DraftTargetKind.DamageDistance && Overlap(b, o)).ToArray();
                if (distances.Any(o => o.EditScope != b.Scope && !(o.EditScope != DraftEditScope.AllReferences && b.Scope != DraftEditScope.AllReferences && (o.SelectedUnitNames ?? []).Order().SequenceEqual(b.Units.Order()))))
                    throw new InvalidDataException("AP联动与距离草稿范围不同，请统一范围：" + b.Ammo);
                if (distances.Length == 0) result.Add(DistanceOp(b, owner.Id + "/ap-distance/" + b.Ammo + "/" + b.Scope));
                var field = data.Ammo(b.Ammo)!.Field(PiercingKey)!;
                result.Add(new(owner.Id + "/ap-piercing/" + b.Ammo + "/" + b.Scope, null, DraftTargetKind.AmmoField, "ammo", b.File,
                    b.Ammo, "TAmmunitionDescriptor", PiercingKey, "TAmmunitionDescriptor.PiercingWeapon", "Boolean", field.DisplayValue,
                    field.RawValue, "是", "True", "AP联动 · PiercingWeapon=True · " + b.Ammo, null, false, owner.UpdatedUtc,
                    EditScope: b.Scope, SelectedUnitNames: b.Units, InsertAmmoField: field.IsMissing));
            }
        }
        return result;
    }
    public static string Details(DraftOperation op) => string.Join("\n", (op.ApBindings ?? []).Select(b =>
        $"AP联动 · {b.Ammo}\nPiercingWeapon = True\nDistanceGRU = {b.Distance.Distance} · AP = {b.Distance.AP}\n{b.Distance.StairName}" +
        (b.Distance.CreateRule ? " · 新建" : "") + "\n" + (b.Scope == DraftEditScope.AllReferences ? "全部引用" : string.Join(", ", b.Units))));

    public static ApBindingState? Pending(IReadOnlyList<DraftOperation> ops, string ammo, DraftEditScope scope, IReadOnlyList<string> units) =>
        ops.SelectMany(o => o.ApBindings ?? []).FirstOrDefault(b => b.Ammo == ammo &&
            (scope == DraftEditScope.AllReferences ? b.Scope == scope : b.Scope != DraftEditScope.AllReferences && b.Units.Order().SequenceEqual(units.Order())));
    public static DraftOperation Repair(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> drafts, AmmoRecord ammo, DraftEditScope scope, IReadOnlyList<string> units)
    {
        var field = ammo.Field(FamilyKey) ?? throw new InvalidDataException("弹药缺少伤害家族");
        var id = DraftOperation.CreateId(DraftTargetKind.AmmoField, ammo.Source.RelativeSourceFile, ammo.Name, FamilyKey);
        var existing = drafts.SingleOrDefault(o => o.Id == id);
        if (existing is not null && (existing.TargetRaw != "DamageFamily_ap" || existing.EditScope != scope || !(existing.SelectedUnitNames ?? []).Order().SequenceEqual(units.Order())))
            throw new InvalidDataException("已有家族草稿与补齐范围不同，请先处理");
        if (existing is null && field.RawValue != "DamageFamily_ap") throw new InvalidDataException("请先将伤害族设为AP");
        var result = Prepare(data, drafts, existing ?? new(id, null, DraftTargetKind.AmmoField, "ammo", ammo.Source.RelativeSourceFile, ammo.Name,
            ammo.Source.TypeName, FamilyKey, field.Location.FieldPath, field.Definition.ValueKind.ToString(), field.DisplayValue, field.RawValue,
            "DamageFamily_ap", "DamageFamily_ap", "补齐AP配套 · " + ammo.DisplayName, null, false, DateTimeOffset.UtcNow, EditScope: scope, SelectedUnitNames: units));
        if (!Has(result)) throw new InvalidDataException("AP配套已完整，无需补齐");
        return result;
    }
}
