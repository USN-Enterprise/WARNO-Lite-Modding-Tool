using System.Globalization;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Batch;

public sealed record AmmoBatchTarget(string File, string Name);
public sealed record AmmoBatchRequest(IReadOnlyList<AmmoBatchTarget> Targets, string FieldKey, UnitBatchOperation Operation,
    string Operand, UnitBatchRounding Rounding = UnitBatchRounding.None, string? Minimum = null, string? Maximum = null);
public sealed record AmmoBatchRow(string Name, string DisplayName, string Current, string Target, string Status);
public sealed record AmmoBatchReference(string Ammo, string Weapon, string Unit, string DisplayName);
public sealed record AmmoBatchPreview(IReadOnlyList<AmmoBatchRow> Rows, IReadOnlyList<AmmoBatchReference> References,
    IReadOnlyList<DraftOperation> Upserts, IReadOnlyList<string> Removals, IReadOnlyList<string> Errors)
{
    public bool CanSave => Errors.Count == 0 && (Upserts.Count > 0 || Removals.Count > 0);
}

public static class AmmoBatchPlanner
{
    public static bool IsBatch(DraftOperation op) => op.TargetKind == DraftTargetKind.AmmoField && op.GroupId?.StartsWith("ammo-batch:", StringComparison.Ordinal) == true;
    public static bool IsShared(DraftOperation op) => op.TargetKind == DraftTargetKind.AmmoField && op.EditScope == DraftEditScope.AllReferences;
    public static AmmoBatchTarget Identity(AmmoRecord ammo) => new(ammo.Source.RelativeSourceFile.Replace('\\','/'),ammo.Name);
    public static IReadOnlyList<WeaponFieldDefinition> CommonFields(IEnumerable<AmmoRecord> targets)
    {
        var records = targets.ToArray();
        return records.Length == 0 ? [] : WeaponFieldDefinitions.Ammo.Where(d => records.All(a => a.Fields.Count(f => f.Key == d.Key) == 1)).ToArray();
    }
    public static IReadOnlyList<string> Choices(IEnumerable<AmmoRecord> targets,string key)
    {
        var fields = targets.Select(a => a.Field(key)).ToArray();
        if (fields.Length == 0 || fields.Any(f => f is null)) return [];
        if (fields[0]!.Definition.ValueKind == WeaponValueKind.Boolean) return ["是","否"];
        return fields.Select(f => f!.Choices.Select(NdfSyntaxDocument.Leaf).ToHashSet(StringComparer.Ordinal)).Aggregate((a,b) => { a.IntersectWith(b); return a; }).Order(StringComparer.Ordinal).ToArray();
    }
    public static string Current(UnitWorkspaceData units,WeaponWorkspaceData data,IReadOnlyList<DraftOperation> drafts,AmmoRecord ammo,string key)
    {
        var field = ammo.Field(key) ?? throw new TransactionValidationException("弹药缺少可编辑字段："+ammo.Name+" / "+key);
        var existing = drafts.Where(o => o.TargetKind == DraftTargetKind.AmmoField && o.ObjectName == ammo.Name && o.FieldKey == key).ToArray();
        if (existing.Length > 1) throw new TransactionValidationException("弹药字段有重复草稿："+ammo.Name+" / "+key);
        if (existing.FirstOrDefault() is {} op)
        {
            if (!IsShared(op)) throw new TransactionValidationException("存在局部弹药草稿，请先应用或移除，不能覆盖为共享修改："+ammo.Name+" / "+key);
            var resolved = DraftResolver.Resolve(units,data,[op]).Single();
            if (resolved.Status != DraftResolutionStatus.Active) throw new TransactionValidationException(resolved.Reason+"："+ammo.Name);
        }
        // Shared WeaponBatch cells can supply a current value, but cannot silently be replaced by a legacy-ID draft.
        var batch = drafts.Where(o => o.TargetKind == DraftTargetKind.WeaponBatch).SelectMany(WeaponBatch.ReadCells)
            .Where(c => c.Ammo == ammo.Name && c.Key == key && c.Unit.Length == 0).ToArray();
        foreach (var c in batch) WeaponBatch.Validate(data,c);
        var values = batch.Select(c => c.Value).Concat(existing.Select(o => o.TargetValue)).Distinct().ToArray();
        if (values.Length > 1) throw new TransactionValidationException("共享弹药草稿目标冲突："+ammo.Name+" / "+key);
        return values.FirstOrDefault() ?? field.DisplayValue;
    }
    public static AmmoBatchPreview Preview(UnitWorkspaceData units,WeaponWorkspaceData data,IReadOnlyList<DraftOperation> drafts,AmmoBatchRequest request)
    {
        var rows = new List<AmmoBatchRow>(); var errors = new List<string>(); var upserts = new List<DraftOperation>(); var removals = new List<string>();
        var references = new List<AmmoBatchReference>(); var batchId = "ammo-batch:"+Guid.NewGuid().ToString("N");
        var targets = request.Targets.DistinctBy(t => t.File.Replace('\\','/').ToUpperInvariant()+"|"+t.Name).ToArray();
        if (targets.Length == 0) errors.Add("请先选择弹药");
        if (!WeaponFieldDefinitions.Ammo.Any(f => f.Key == request.FieldKey)) errors.Add("请选择已有弹药参数");
        foreach (var target in targets)
        {
            var current = ""; var desired = "";
            var ammo = data.Ammunition.SingleOrDefault(a => a.Name == target.Name && Identity(a).File.Equals(target.File.Replace('\\','/'),StringComparison.OrdinalIgnoreCase));
            try
            {
                if (ammo is null) throw new TransactionValidationException("弹药不存在或来源已变化："+target.Name);
                var field = ammo.Field(request.FieldKey) ?? throw new TransactionValidationException("弹药缺少可编辑字段："+ammo.Name+" / "+request.FieldKey);
                current = Current(units,data,drafts,ammo,field.Key);
                var input = Calculate(current,field.Definition.ValueKind,request);
                if (!WeaponValueConverter.TryFormat(field,input,out desired,out var raw,out var error)) throw new TransactionValidationException(error);
                var id = DraftOperation.CreateId(DraftTargetKind.AmmoField,field.Location.RelativeSourceFile,ammo.Name,field.Key);
                var existing = drafts.SingleOrDefault(o => o.Id == id);
                var status = desired == current ? "不变" : existing is null ? "将修改" : "更新已有共享草稿";
                if (desired != current)
                {
                    if (desired == field.DisplayValue)
                    {
                        if (existing is not null) { removals.Add(id); status = "恢复正式基线"; }
                        else if (drafts.Where(o => o.TargetKind == DraftTargetKind.WeaponBatch).SelectMany(WeaponBatch.ReadCells).Any(c => c.Unit.Length == 0 && c.Ammo == ammo.Name && c.Key == field.Key))
                            throw new TransactionValidationException("存在历史武器批次，请在草稿中心移除冲突项："+ammo.Name+" / "+field.Key);
                    }
                    else upserts.Add(new(id,batchId,DraftTargetKind.AmmoField,"ammo",field.Location.RelativeSourceFile,ammo.Name,ammo.Source.TypeName,
                        field.Key,field.Location.FieldPath,field.Definition.ValueKind.ToString(),field.DisplayValue,field.RawValue,desired,raw,
                        $"{ammo.DisplayName} · {field.Definition.Label}：{current} → {desired}（全部引用）",null,false,DateTimeOffset.UtcNow,
                        EditScope:DraftEditScope.AllReferences,SelectedUnitNames:[]));
                }
                rows.Add(new(ammo.Name,ammo.DisplayName,current,desired,status));
            }
            catch (Exception ex) when (ex is TransactionValidationException or FormatException or OverflowException)
            { errors.Add(target.Name+"："+ex.Message); rows.Add(new(target.Name,ammo?.DisplayName ?? target.Name,current,desired,ex.Message)); }
            if (ammo is not null)
            {
                foreach (var weapon in data.References.AmmoWeapons.GetValueOrDefault(ammo.Name) ?? [])
                foreach (var unit in (data.References.WeaponUnits.GetValueOrDefault(weapon) ?? []).DefaultIfEmpty(""))
                    references.Add(new(ammo.Name,weapon,unit,data.Units.FirstOrDefault(u => u.Name == unit)?.DisplayName ?? unit));
            }
        }
        var updatedIds = upserts.Select(o => o.Id).Concat(removals).ToHashSet();
        var combined = drafts.Where(o => !updatedIds.Contains(o.Id)).Concat(upserts).ToArray();
        try
        {
            var touched = targets.Select(t => t.Name).ToHashSet();
            var seeds = combined.Where(o => o.TargetKind == DraftTargetKind.AmmoField && touched.Contains(o.ObjectName)).ToArray();
            var related = UnitDraftLinks.Expand(seeds,combined).Where(WeaponBatch.IsWeaponEdit).ToArray();
            foreach (var conflict in DraftResolver.Resolve(units,data,related).Where(r => r.Status == DraftResolutionStatus.Conflict))
                errors.Add(conflict.Operation.Summary+"："+conflict.Reason);
            if (related.Any(o => o.TargetKind == DraftTargetKind.WeaponBatch)) WeaponBatchApplyPlanner.ValidateCombination(data,related);
            ValidateFinal(units,data,related,touched);
        }
        catch (TransactionValidationException ex) { errors.Add(ex.Message); }
        return new(rows,references.Distinct().ToArray(),errors.Count == 0 ? upserts : [],errors.Count == 0 ? removals : [],errors.Distinct().ToArray());
    }
    public static void ValidateFinal(UnitWorkspaceData units,WeaponWorkspaceData data,IReadOnlyList<DraftOperation> operations,IReadOnlySet<string>? names = null)
    {
        names ??= operations.Where(IsShared).Select(o => o.ObjectName).ToHashSet();
        foreach (var ammo in data.Ammunition.Where(a => names.Contains(a.Name)))
        {
            var final = ammo.Fields.ToDictionary(f => f.Key,f => f.DisplayValue);
            foreach (var op in operations.Where(o => IsShared(o) && o.ObjectName == ammo.Name)) final[op.FieldKey] = op.TargetValue;
            foreach (var c in operations.Where(o => o.TargetKind == DraftTargetKind.WeaponBatch).SelectMany(WeaponBatch.ReadCells).Where(c => c.Unit.Length == 0 && c.Ammo == ammo.Name && c.Key.StartsWith("ammo.",StringComparison.Ordinal))) final[c.Key] = c.Value;
            foreach (var prefix in new[] { "ammo.range.ground","ammo.range.heli","ammo.range.air","ammo.range.projectile" })
                if (decimal.TryParse(final.GetValueOrDefault(prefix+".min"),NumberStyles.Float,CultureInfo.InvariantCulture,out var min) &&
                    decimal.TryParse(final.GetValueOrDefault(prefix+".max"),NumberStyles.Float,CultureInfo.InvariantCulture,out var max) && min > max)
                    throw new TransactionValidationException("最小射程不能大于最大射程："+ammo.Name+" / "+prefix);
            if (final.TryGetValue("ammo.damage.family",out var familyName) && final.TryGetValue("ammo.damage.index",out var index) &&
                units.DamageResistance.DamageFamilies.FirstOrDefault(f => f.Name == NdfSyntaxDocument.Leaf(familyName)) is {} family &&
                (!int.TryParse(index,out var i) || i < family.MinimumIndex || i > family.MaximumIndex))
                throw new TransactionValidationException("伤害索引超出当前伤害族范围："+ammo.Name);
        }
    }
    private static string Calculate(string current,WeaponValueKind kind,AmmoBatchRequest r)
    {
        if (r.Operation == UnitBatchOperation.Set) return r.Operand;
        if (kind is not (WeaponValueKind.Integer or WeaponValueKind.Decimal)) throw new TransactionValidationException("该字段只能设为固定值");
        decimal Read(string text) => decimal.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture);
        var a = Read(current); var b = Read(r.Operand);
        var result = r.Operation switch { UnitBatchOperation.Multiply => a*b, UnitBatchOperation.Add => a+b, UnitBatchOperation.Subtract => a-b,
            UnitBatchOperation.IncreasePercent => a*(1+b/100), UnitBatchOperation.DecreasePercent => a*(1-b/100), _ => throw new TransactionValidationException("无效运算") };
        decimal? min = string.IsNullOrWhiteSpace(r.Minimum) ? null : Read(r.Minimum), max = string.IsNullOrWhiteSpace(r.Maximum) ? null : Read(r.Maximum);
        if (min > max) throw new TransactionValidationException("最小值不能大于最大值");
        if (min.HasValue) result = Math.Max(result,min.Value); if (max.HasValue) result = Math.Min(result,max.Value);
        result = r.Rounding switch { UnitBatchRounding.Nearest => Math.Round(result,0,MidpointRounding.AwayFromZero), UnitBatchRounding.Floor => Math.Floor(result), UnitBatchRounding.Ceiling => Math.Ceiling(result), _ => result };
        return result.ToString("0.############################",CultureInfo.InvariantCulture);
    }
}
