using System.Globalization;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Weapons;

public sealed record WeaponBatchTarget(string Unit, string Weapon, int Mount);
// Empty Unit means an explicit edit to the shared source object.
public sealed record WeaponBatchCell(string Unit, string Weapon, int Mount, string Shape,
    string Ammo, string Key, string BaselineRaw, string Value, string Raw)
{
    public string Identity => string.Join("|", Unit, Key.StartsWith("ammo.", StringComparison.Ordinal) && Unit.Length == 0 ? Ammo : Weapon,
        Key.StartsWith("ammo.", StringComparison.Ordinal) && Unit.Length > 0 ? Mount : -1, Key);
}
public sealed record WeaponBatchState(int Version, IReadOnlyList<WeaponBatchCell> Cells);
public sealed record WeaponBatchRequest(IReadOnlyList<WeaponBatchTarget> Targets, string Parameter,
    UnitBatchOperation Operation, string Operand, UnitBatchRounding Rounding = UnitBatchRounding.None,
    string? Minimum = null, string? Maximum = null, bool AllReferences = false, bool ReplaceExisting = false);
public sealed record WeaponBatchRow(string Unit, string Weapon, int Mount, string Parameter, string Current, string Target, string Status, string TotalAmmo = "");
public sealed record WeaponBatchPreview(IReadOnlyList<WeaponBatchRow> Rows, IReadOnlyList<DraftOperation> Upserts,
    IReadOnlyList<string> Removals, IReadOnlyList<string> Errors, IReadOnlyList<string> Impacts, int FieldCount)
{
    public bool CanSave => Errors.Count == 0 && Upserts.Count > 0;
}

public static class WeaponBatch
{
    public static bool IsWeaponEdit(DraftOperation o) => o.TargetKind is DraftTargetKind.WeaponBatch or DraftTargetKind.WeaponField
        or DraftTargetKind.MountedWeaponAmmo or DraftTargetKind.AmmoField or DraftTargetKind.UnitWeaponReference;
    public static IReadOnlySet<string> Dependencies(DraftOperation o) => (o.TargetKind == DraftTargetKind.WeaponBatch
        ? Read(o).Cells.SelectMany(c => new[] { c.Weapon, c.Ammo, c.Key.EndsWith(".ammo", StringComparison.Ordinal) ? c.Value : "" })
        : new[] { o.ObjectName, o.ContextWeaponName ?? "", o.TargetKind is DraftTargetKind.MountedWeaponAmmo or DraftTargetKind.UnitWeaponReference ? NdfSyntaxDocument.Leaf(o.TargetRaw) : "" })
        .Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
    public static bool Blocks(IEnumerable<DraftOperation> operations, Func<WeaponBatchCell,bool> matches)
    {
        foreach (var o in operations.Where(o => o.TargetKind == DraftTargetKind.WeaponBatch))
        {
            try { if (Read(o).Cells.Any(matches)) return true; }
            catch (TransactionValidationException) { return true; }
        }
        return false;
    }
    public static string Shape(WeaponRecord w) => JsonSerializer.Serialize(new { w.Source.RelativeSourceFile, w.Source.TypeName,
        Mounts = w.Mounts.Select(m => new { m.Index, m.TurretIndex, m.AmmoBoxIndex, m.AmmoName, m.TurretType, m.EffectTag, m.WeaponAlternative, m.AnimationKeys, m.PresentationReferences }),
        Fields = w.Fields.Concat(w.Mounts.SelectMany(m => m.Fields)).Select(f => new { f.Key, f.Location.FieldPath }) });
    public static IReadOnlyList<WeaponBatchCell> ReadCells(DraftOperation o) => Read(o).Cells;
    public static WeaponBatchState Read(DraftOperation o)
    {
        try
        {
            var state = JsonSerializer.Deserialize<WeaponBatchState>(o.TargetRaw);
            if (state is null || state.Version != 1 || state.Cells is null || state.Cells.Count == 0) throw new JsonException();
            return state;
        }
        catch (JsonException) { throw new TransactionValidationException("武器批量草稿格式无效：" + o.Summary); }
    }
    public static WeaponFieldValue? Field(WeaponWorkspaceData data, WeaponBatchCell c) => c.Key.StartsWith("ammo.", StringComparison.Ordinal)
        ? data.Ammo(c.Ammo)?.Field(c.Key)
        : data.Weapon(c.Weapon)?.Fields.Concat(data.Weapon(c.Weapon)!.Mounts.SelectMany(m => m.Fields)).FirstOrDefault(f => f.Key == c.Key);
    public static void Validate(WeaponWorkspaceData data, WeaponBatchCell c)
    {
        var w = data.Weapon(c.Weapon);
        if (!(c.Unit.Length == 0 && c.Weapon.Length == 0 && c.Key.StartsWith("ammo.", StringComparison.Ordinal)) &&
            (w is null || c.Shape != Shape(w) || !w.Mounts.Any(m => m.Index == c.Mount))) throw new TransactionValidationException("挂载结构已变化，请重新建立批量草稿：" + c.Weapon);
        if (c.Unit.Length > 0 && !data.Units.Any(u => u.Name == c.Unit && u.Weapons.Contains(c.Weapon))) throw new TransactionValidationException("单位引用已变化：" + c.Unit);
        var f = Field(data, c) ?? throw new TransactionValidationException("批量字段已不存在：" + c.Key);
        if (f.RawValue != c.BaselineRaw) throw new TransactionValidationException("批量字段基线已变化：" + c.Key);
        if (!WeaponValueConverter.TryFormat(f, c.Value, out _, out var raw, out var error) || raw != c.Raw)
            throw new TransactionValidationException("批量字段值无效：" + c.Key + " " + error);
    }
    public static ResolvedDraftOperation Resolve(WeaponWorkspaceData? data, DraftOperation o)
    {
        try { if (data is null) throw new TransactionValidationException("武器模块未加载"); foreach (var c in Read(o).Cells) Validate(data, c); return new(o, DraftResolutionStatus.Active, ""); }
        catch (Exception e) when (e is TransactionValidationException or ArgumentException) { return new(o, DraftResolutionStatus.Conflict, e.Message); }
    }
    public static DraftOperation Operation(IReadOnlyList<WeaponBatchCell> cells, WeaponWorkspaceData data, string? id = null)
    {
        id ??= Guid.NewGuid().ToString("N");
        var path = data.Weapon(cells[0].Weapon)!.Source.RelativeSourceFile;
        var json = JsonSerializer.Serialize(new WeaponBatchState(1, cells));
        return new(DraftOperation.CreateId(DraftTargetKind.WeaponBatch, path, id, "weapon.batch"), "weapon-batch:" + id,
            DraftTargetKind.WeaponBatch, "weapons", path, id, "WeaponBatchPlan", "weapon.batch", "weapon.batch", "WeaponBatchV1",
            "", "", $"{cells.Count} 个字段", json, $"批量修改武器：{cells.Count} 个字段", null, false, DateTimeOffset.UtcNow,
            SelectedUnitNames: cells.Where(c => c.Unit.Length > 0).Select(c => c.Unit).Distinct().ToArray());
    }
    public static string Key(WeaponRecord w, MountedWeaponRecord m, string parameter) => parameter switch
    {
        "weapon.salves" => "weapon.salves." + m.AmmoBoxIndex,
        "mount.count" => $"mount.{m.Index}.count", "mount.hidden" => $"mount.{m.Index}.hidden", "mount.ammo" => $"mount.{m.Index}.ammo",
        _ when parameter.StartsWith("turret.", StringComparison.Ordinal) => $"turret.{m.TurretIndex}.{parameter[7..]}",
        _ => parameter
    };
    public static IReadOnlyList<(string Key, WeaponFieldDefinition Definition)> Parameters(WeaponWorkspaceData data) => data.Weapons
        .SelectMany(w => w.Fields.Concat(w.Mounts.SelectMany(m => m.Fields))).Concat(data.Ammunition.SelectMany(a => a.Fields))
        .Select(f => (Key: Semantic(f.Key), Definition: f.Definition)).DistinctBy(p => p.Key).ToArray();
    private static string Semantic(string key)
    {
        var parts = key.Split('.');
        return parts[0] switch { "weapon" => "weapon.salves", "mount" or "turret" => parts[0] + "." + string.Join('.', parts.Skip(2)), _ => key };
    }
    public static IReadOnlyList<WeaponBatchCell> Cells(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> operations)
    {
        var result = new List<WeaponBatchCell>();
        foreach (var o in operations.Where(IsWeaponEdit))
        {
            if (o.TargetKind == DraftTargetKind.WeaponBatch) { foreach (var c in Read(o).Cells) { Validate(data, c); result.Add(c); } continue; }
            if (o.TargetKind == DraftTargetKind.UnitWeaponReference) throw new TransactionValidationException("请先应用或移除整套武器替换草稿，再批量修改武器");
            var global = o.EditScope == DraftEditScope.AllReferences;
            if (global && o.TargetKind == DraftTargetKind.AmmoField)
            {
                var c = new WeaponBatchCell("", "", -1, "", o.ObjectName, o.FieldKey, o.BaselineRaw, o.TargetValue, o.TargetRaw);
                Validate(data, c); result.Add(c); continue;
            }
            var before = result.Count;
            foreach (var w in data.Weapons.Where(w => o.TargetKind == DraftTargetKind.AmmoField ? w.Mounts.Any(m => m.AmmoName == o.ObjectName) : w.Name == o.ObjectName))
            {
                var names = global ? new[] { "" } : (o.SelectedUnitNames ?? []).Where(n => data.References.WeaponUnits.GetValueOrDefault(w.Name)?.Contains(n) == true);
                foreach (var name in names)
                foreach (var m in w.Mounts.Where(m => o.TargetKind == DraftTargetKind.AmmoField ? m.AmmoName == o.ObjectName
                    : o.FieldKey.StartsWith("mount.", StringComparison.Ordinal) ? m.Fields.Any(f => f.Key == o.FieldKey) : true).Take(o.TargetKind == DraftTargetKind.AmmoField ? int.MaxValue : 1))
                {
                    var c = new WeaponBatchCell(name, w.Name, m.Index, Shape(w), m.AmmoName, o.FieldKey, o.BaselineRaw, o.TargetValue, o.TargetRaw);
                    Validate(data, c); result.Add(c);
                }
            }
            if (result.Count == before) throw new TransactionValidationException("武器草稿没有有效作用域：" + o.Summary);
        }
        var unique = new Dictionary<string, WeaponBatchCell>();
        foreach (var c in result)
        {
            if (unique.TryGetValue(c.Identity, out var prior) && prior.Raw != c.Raw) throw new TransactionValidationException("武器草稿目标冲突：" + c.Identity);
            unique[c.Identity] = c;
        }
        return unique.Values.ToArray();
    }
    public static string AmmoAt(WeaponWorkspaceData data, IEnumerable<WeaponBatchCell> cells, string unit, string weapon, int mount)
    {
        var key = $"mount.{mount}.ammo";
        var edits = cells.Where(c => c.Weapon == weapon && c.Key == key && (c.Unit == unit || c.Unit.Length == 0)).ToArray();
        if (edits.Select(c => c.Raw).Distinct().Count() > 1) throw new TransactionValidationException("局部与共享弹药替换冲突：" + weapon);
        return edits.FirstOrDefault()?.Value ?? data.Weapon(weapon)!.Mounts.Single(m => m.Index == mount).AmmoName;
    }
    public static string TotalAmmo(WeaponWorkspaceData data, IReadOnlyList<WeaponBatchCell> cells, WeaponBatchTarget t)
    {
        var w = data.Weapon(t.Weapon)!; var m = w.Mounts.Single(m => m.Index == t.Mount);
        var ammo = AmmoAt(data,cells,t.Unit,t.Weapon,t.Mount); var a = data.Ammo(ammo);
        string? Value(string key) => cells.FirstOrDefault(c => c.Key == key && (c.Unit == t.Unit || c.Unit.Length == 0) &&
            (key.StartsWith("ammo.",StringComparison.Ordinal) ? c.Ammo == ammo && (c.Unit.Length == 0 || c.Weapon == w.Name && c.Mount == m.Index) : c.Weapon == w.Name))?.Value
            ?? (key.StartsWith("ammo.",StringComparison.Ordinal) ? a?.Field(key) : w.Field(key))?.DisplayValue;
        return long.TryParse(Value("weapon.salves."+m.AmmoBoxIndex),out var salves) && long.TryParse(Value("ammo.shotsPerSalvo"),out var shots)
            ? $"{salves} × {shots} = {checked(salves*shots)}" : "—";
    }
    public static WeaponBatchPreview Preview(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> drafts, WeaponBatchRequest request)
    {
        var rows = new List<WeaponBatchRow>(); var errors = new List<string>(); var impacts = new HashSet<string>();
        var upserts = new List<DraftOperation>(); var removals = new List<string>(); var changes = new Dictionary<string, WeaponBatchCell>();
        IReadOnlyList<WeaponBatchCell> existing;
        try { existing = Cells(data, drafts); } catch (TransactionValidationException e) { return new([], [], [], [e.Message], [], 0); }
        var seen = new HashSet<string>();
        foreach (var t in request.Targets.Distinct())
        {
            try
            {
                if (drafts.Any(o => o.TargetKind == DraftTargetKind.UnitDelete && o.ObjectName == t.Unit)) throw new TransactionValidationException("单位待删除：" + t.Unit);
                var w = data.Weapon(t.Weapon) ?? throw new TransactionValidationException("武器不存在：" + t.Weapon);
                if (!data.Units.Any(u => u.Name == t.Unit && u.Weapons.Contains(w.Name))) throw new TransactionValidationException("单位未引用武器：" + t.Unit);
                var m = w.Mounts.Single(m => m.Index == t.Mount);
                var ammo = AmmoAt(data, existing, t.Unit, t.Weapon, t.Mount);
                var c = new WeaponBatchCell(request.AllReferences ? "" : t.Unit, w.Name, m.Index, Shape(w), ammo, Key(w, m, request.Parameter), "", "", "");
                var f = Field(data, c);
                if (f is null) { rows.Add(new(t.Unit, w.Name, m.Index, request.Parameter, "", "", "跳过：字段不存在")); continue; }
                c = c with { BaselineRaw = f.RawValue };
                var effective = existing.Where(e => e.Key == c.Key && (e.Unit == c.Unit || e.Unit.Length == 0) &&
                    (c.Key.StartsWith("ammo.", StringComparison.Ordinal) ? e.Ammo == c.Ammo && (e.Unit.Length == 0 || e.Weapon == c.Weapon && e.Mount == c.Mount) : e.Weapon == c.Weapon)).ToArray();
                if (effective.Select(e => e.Raw).Distinct().Count() > 1) throw new TransactionValidationException("局部与全部引用草稿冲突：" + c.Key);
                var current = effective.FirstOrDefault()?.Value ?? f.DisplayValue;
                var input = Calculate(current, f.Definition.ValueKind, request);
                if (!WeaponValueConverter.TryFormat(f, input, out var value, out var raw, out var error)) throw new TransactionValidationException(error);
                c = c with { Value = value, Raw = raw };
                var changed = value != current;
                rows.Add(new(t.Unit, w.Name, m.Index, f.Definition.Label, current, value, changed ? "将修改" : "不变"));
                seen.Add(c.Identity);
                if (changed) changes.TryAdd(c.Identity, c);
                IEnumerable<MountedWeaponRecord> siblings = f.Definition.Owner switch
                {
                    WeaponFieldOwner.Weapon => w.Mounts.Where(x => x.AmmoBoxIndex == m.AmmoBoxIndex),
                    WeaponFieldOwner.Turret => w.Mounts.Where(x => x.TurretIndex == m.TurretIndex), _ => [m]
                };
                foreach (var s in siblings.Where(s => !request.Targets.Contains(new(t.Unit, w.Name, s.Index)))) impacts.Add($"{t.Unit} / {w.Name} / #{s.Index}：同箱或同炮塔连带影响");
                if (request.AllReferences)
                {
                    var ws = c.Key.StartsWith("ammo.", StringComparison.Ordinal) ? data.Weapons.Where(x => x.Mounts.Any(y => y.AmmoName == c.Ammo)) : [w];
                    foreach (var aw in ws) foreach (var u in data.References.WeaponUnits.GetValueOrDefault(aw.Name) ?? [])
                        foreach (var am in aw.Mounts.Where(x => c.Key.StartsWith("ammo.", StringComparison.Ordinal) ? x.AmmoName == c.Ammo : siblings.Any(s => s.Index == x.Index)))
                            impacts.Add($"{u} / {aw.Name} / #{am.Index}：全部引用");
                }
            }
            catch (Exception e) when (e is TransactionValidationException or OverflowException or FormatException or InvalidOperationException) { errors.Add(t.Unit + " / #" + t.Mount + "：" + e.Message); }
        }
        foreach (var o in drafts.Where(IsWeaponEdit))
        {
            var cells = o.TargetKind == DraftTargetKind.WeaponBatch ? Read(o).Cells : Cells(data, [o]);
            var overlaps = cells.Where(c => changes.TryGetValue(c.Identity, out var n) && c.Raw != n.Raw).ToArray();
            if (overlaps.Length == 0) continue;
            if (!request.ReplaceExisting || o.TargetKind != DraftTargetKind.WeaponBatch) { errors.Add("已存在重叠草稿，请明确替换批量草稿，或在草稿中心移除单字段草稿：" + o.Summary); continue; }
            var keep = cells.Where(c => !overlaps.Contains(c)).ToArray(); removals.Add(o.Id);
            if (keep.Length > 0) upserts.Add(Operation(keep, data, o.ObjectName));
            impacts.Add("替换草稿范围：" + o.Summary + " / " + overlaps.Length);
        }
        if (request.Targets.Count == 0) errors.Add("请先勾选武器挂载");
        if (changes.Count > 0) upserts.Add(Operation(changes.Values.ToArray(), data));
        var combined = drafts.Where(o => !removals.Contains(o.Id)).Concat(upserts).ToArray();
        try
        {
            var finalCells = WeaponBatchApplyPlanner.ValidateCombination(data, combined);
            for (var i = 0; i < rows.Count; i++) rows[i] = rows[i] with { TotalAmmo = TotalAmmo(data,finalCells,new(rows[i].Unit,rows[i].Weapon,rows[i].Mount)) };
        }
        catch (TransactionValidationException e) { errors.Add(e.Message); }
        return new(rows, upserts, removals, errors.Distinct().ToArray(), impacts.ToArray(), seen.Count);
    }
    private static string Calculate(string current, WeaponValueKind kind, WeaponBatchRequest r)
    {
        if (r.Operation == UnitBatchOperation.Set) return r.Operand;
        if (kind is not (WeaponValueKind.Integer or WeaponValueKind.Decimal or WeaponValueKind.Degrees)) throw new TransactionValidationException("该字段只能设为固定值");
        decimal Read(string s) => decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        var a = Read(current); var b = Read(r.Operand);
        var n = r.Operation switch { UnitBatchOperation.Multiply => a*b, UnitBatchOperation.Add => a+b, UnitBatchOperation.Subtract => a-b,
            UnitBatchOperation.IncreasePercent => a*(1+b/100), UnitBatchOperation.DecreasePercent => a*(1-b/100), _ => throw new TransactionValidationException("无效运算") };
        decimal? min = string.IsNullOrWhiteSpace(r.Minimum) ? null : Read(r.Minimum), max = string.IsNullOrWhiteSpace(r.Maximum) ? null : Read(r.Maximum);
        if (min > max) throw new TransactionValidationException("最小值不能大于最大值");
        if (min.HasValue) n = Math.Max(n,min.Value); if (max.HasValue) n = Math.Min(n,max.Value);
        n = r.Rounding switch { UnitBatchRounding.Nearest => Math.Round(n,0,MidpointRounding.AwayFromZero), UnitBatchRounding.Floor => Math.Floor(n), UnitBatchRounding.Ceiling => Math.Ceiling(n), _ => n };
        return n.ToString("0.############################",CultureInfo.InvariantCulture);
    }
}
